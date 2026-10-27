using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Space.Map.App.Services;
using Space.Map.Core.Abstractions;
using Space.Map.Core.Formatting;
using Space.Map.Core.Scanning;
using Space.Map.Infrastructure.Logging;
using System.Collections.ObjectModel;
using System.IO;

namespace Space.Map.App.ViewModels;

/// <summary>
/// Scan path selection, async scan lifecycle, progress/result metrics, and issues/large-files projection.
/// </summary>
public partial class ScanSessionViewModel : ObservableObject, IDisposable
{
    private readonly IDiskScanner _scanner;
    private readonly IFolderPicker _folderPicker;
    private readonly IDriveService _driveService;
    private readonly WorkspaceNavigationViewModel _navigation;
    private readonly Action _hideDriveHome;
    private readonly Action _showDriveHome;
    private readonly Func<bool> _isHostDisposed;
    private readonly object _scanGate = new();
    private CancellationTokenSource? _scanCancellation;
    private bool _isDisposed;

    public ScanSessionViewModel(
        IDiskScanner scanner,
        IFolderPicker folderPicker,
        IDriveService driveService,
        WorkspaceNavigationViewModel navigation,
        Action hideDriveHome,
        Action showDriveHome,
        Func<bool> isHostDisposed)
    {
        _scanner = scanner;
        _folderPicker = folderPicker;
        _driveService = driveService;
        _navigation = navigation;
        _hideDriveHome = hideDriveHome;
        _showDriveHome = showDriveHome;
        _isHostDisposed = isHostDisposed;
        SystemDriveLabel = _driveService.ResolveSystemDriveRoot();
        StatusText = "选择磁盘开始扫描，或通过工具栏选择目录";

        WeakReferenceMessenger.Default.Register<ScanSessionViewModel, Space.Map.App.Messages.StatusChangedMessage>(
            this,
            (recipient, message) => recipient.SetStatus(message.Status));
    }

    public ObservableCollection<ScanNodeSummary> LargeFiles { get; } = [];

    public ObservableCollection<ScanIssueSummary> Issues { get; } = [];

    public string SystemDriveLabel { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectSystemDriveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectUserProfileCommand))]
    private string _selectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [ObservableProperty]
    private bool _followReparsePoints;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectSystemDriveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectUserProfileCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _currentPath = "尚未开始扫描";

    [ObservableProperty]
    private string _scannedFiles = "0";

    [ObservableProperty]
    private string _scannedDirectories = "0";

    [ObservableProperty]
    private string _scannedBytes = ByteSizeFormatter.Format(0);

    [ObservableProperty]
    private string _elapsed = "00:00:00";

    [ObservableProperty]
    private string _issueCount = "0";

    public string IssuesHeader => LocalizationService.Instance.Format("Issues_HeaderFormat", IssueCount);

    [ObservableProperty]
    private string _skippedCount = "0";

    [ObservableProperty]
    private string _totalSize = ByteSizeFormatter.Format(0);

    [ObservableProperty]
    private string _totalFiles = "0";

    [ObservableProperty]
    private string _totalDirectories = "0";

    [ObservableProperty]
    private string _resultStatus = "无结果";

    [ObservableProperty]
    private bool _isIssuesPanelExpanded;

    public bool HasIssues => Issues.Count > 0;

    public void SetStatus(string status)
    {
        if (_isDisposed || _isHostDisposed())
        {
            return;
        }

        StatusText = status;
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private void Browse()
    {
        var selected = _folderPicker.PickFolder(SelectedPath);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SelectedPath = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private void SelectSystemDrive()
    {
        SelectedPath = SystemDriveLabel;
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private void SelectUserProfile()
    {
        SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private bool CanBrowse() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScanAsync()
    {
        if (_isDisposed || _isHostDisposed())
        {
            return;
        }

        var rootPath = SelectedPath.Trim();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            StatusText = "路径不能为空";
            return;
        }

        AppLog.Information($"Scan starting. Path: {rootPath}; FollowReparsePoints: {FollowReparsePoints}");
        ResetScanState();
        _hideDriveHome();
        IsScanning = true;
        StatusText = "正在验证路径";
        CurrentPath = rootPath;

        CancellationTokenSource cts;
        lock (_scanGate)
        {
            if (_isDisposed || _isHostDisposed())
            {
                IsScanning = false;
                return;
            }

            _scanCancellation?.Dispose();
            cts = new CancellationTokenSource();
            _scanCancellation = cts;
        }

        var progress = new Progress<ScanProgress>(ApplyProgress);

        try
        {
            var pathExists = await Task.Run(
                () => Directory.Exists(rootPath) || File.Exists(rootPath),
                cts.Token);

            if (_isDisposed || _isHostDisposed())
            {
                return;
            }

            if (!pathExists)
            {
                AppLog.Warning($"Scan rejected because path does not exist: {rootPath}");
                StatusText = "路径不存在";
                ResultStatus = "失败";
                _showDriveHome();
                return;
            }

            StatusText = "扫描中";
            var result = await _scanner.ScanAsync(
                new ScanOptions(rootPath)
                {
                    FollowReparsePoints = FollowReparsePoints,
                    KeepFileNodes = false,
                    ReadFileTimestamps = false,
                    MaxTopFiles = 100,
                    MaxChildrenPerDirectory = 64
                },
                progress,
                cts.Token);

            if (_isDisposed || _isHostDisposed())
            {
                return;
            }

            ApplyResult(result);
            AppLog.Information(
                $"Scan finished. Status: {result.Status}; Files: {result.Root?.FileCount ?? 0:N0}; Directories: {result.Root?.DirectoryCount ?? 0:N0}; SizeBytes: {result.Root?.SizeBytes ?? 0:N0}; Issues: {result.TotalIssueCount:N0}");
        }
        catch (OperationCanceledException)
        {
            if (_isDisposed || _isHostDisposed())
            {
                return;
            }

            AppLog.Warning($"Scan canceled before any result. Path: {rootPath}");
            StatusText = "扫描已取消";
            ResultStatus = "已取消";
            if (!_navigation.HasScanResult)
            {
                _showDriveHome();
            }
        }
        catch (Exception exception)
        {
            if (_isDisposed || _isHostDisposed())
            {
                return;
            }

            AppLog.Error($"Scan failed. Path: {rootPath}", exception);
            StatusText = "扫描失败";
            ResultStatus = exception.Message;
            if (!_navigation.HasScanResult)
            {
                _showDriveHome();
            }
        }
        finally
        {
            lock (_scanGate)
            {
                if (ReferenceEquals(_scanCancellation, cts))
                {
                    _scanCancellation.Dispose();
                    _scanCancellation = null;
                }
            }

            if (!_isDisposed && !_isHostDisposed())
            {
                IsScanning = false;
            }
        }
    }

    private bool CanStartScan()
    {
        return !IsScanning && !string.IsNullOrWhiteSpace(SelectedPath);
    }

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan()
    {
        AppLog.Information("Scan cancellation requested.");
        lock (_scanGate)
        {
            _scanCancellation?.Cancel();
        }

        if (!_isDisposed && !_isHostDisposed())
        {
            StatusText = "正在取消";
        }
    }

    private bool CanCancelScan() => IsScanning;

    private void ApplyProgress(ScanProgress progress)
    {
        if (_isDisposed || _isHostDisposed())
        {
            return;
        }

        ScannedFiles = progress.ScannedFiles.ToString("N0");
        ScannedDirectories = progress.ScannedDirectories.ToString("N0");
        ScannedBytes = ByteSizeFormatter.Format(progress.TotalBytes);
        CurrentPath = string.IsNullOrWhiteSpace(progress.CurrentPath)
            ? "准备扫描"
            : progress.CurrentPath;
        IssueCount = progress.IssueCount.ToString("N0");
        SkippedCount = progress.SkippedCount.ToString("N0");
        Elapsed = progress.Elapsed.ToString(@"hh\:mm\:ss");

        if (progress.PartialRoot is not null)
        {
            ApplyPartialRoot(progress.PartialRoot);
        }
    }

    private void ApplyPartialRoot(ScanNode partialRoot)
    {
        TotalSize = ByteSizeFormatter.Format(partialRoot.SizeBytes);
        TotalFiles = partialRoot.FileCount.ToString("N0");
        TotalDirectories = partialRoot.DirectoryCount.ToString("N0");
        ResultStatus = "扫描中";
        StatusText = $"扫描中（一级已完成 {partialRoot.Children.Count:N0} 项）";
        _navigation.ApplyPartialRoot(partialRoot);
    }

    private void ApplyResult(ScanResult result)
    {
        ResultStatus = FormatResultStatus(result.Status);
        StatusText = result.Status switch
        {
            ScanResultStatus.Canceled => "扫描已取消，已保留部分结果",
            ScanResultStatus.Partial => "扫描完成，部分路径被跳过",
            _ => "扫描完成"
        };

        Issues.Clear();
        foreach (var issue in result.Issues)
        {
            Issues.Add(new ScanIssueSummary(issue));
        }

        IssueCount = result.TotalIssueCount.ToString("N0");
        OnPropertyChanged(nameof(HasIssues));
        IsIssuesPanelExpanded = Issues.Count > 0;

        if (result.TotalIssueCount > result.Issues.Count)
        {
            StatusText = result.Status switch
            {
                ScanResultStatus.Canceled => $"扫描已取消，已保留部分结果（问题明细前 {result.Issues.Count:N0} 条）",
                ScanResultStatus.Partial => $"扫描完成，部分路径被跳过（问题明细前 {result.Issues.Count:N0} 条）",
                _ => $"扫描完成（问题明细前 {result.Issues.Count:N0} 条）"
            };
        }

        if (result.Root is null)
        {
            if (!_navigation.HasScanResult)
            {
                _showDriveHome();
            }

            return;
        }

        TotalSize = ByteSizeFormatter.Format(result.Root.SizeBytes);
        TotalFiles = result.Root.FileCount.ToString("N0");
        TotalDirectories = result.Root.DirectoryCount.ToString("N0");

        ApplyLargeFiles(result.TopFiles, result.Root.SizeBytes);
        _navigation.ApplyResultRoot(result.Root);
    }

    private void ApplyLargeFiles(IReadOnlyList<ScanNode> topFiles, long rootSizeBytes)
    {
        LargeFiles.Clear();
        for (var index = 0; index < topFiles.Count; index++)
        {
            LargeFiles.Add(new ScanNodeSummary(topFiles[index], rootSizeBytes, index));
        }
    }

    private void ResetScanState()
    {
        LargeFiles.Clear();
        Issues.Clear();
        _navigation.Reset();
        IsIssuesPanelExpanded = false;
        TotalSize = ByteSizeFormatter.Format(0);
        TotalFiles = "0";
        TotalDirectories = "0";
        ResultStatus = "扫描中";
        ScannedFiles = "0";
        ScannedDirectories = "0";
        ScannedBytes = ByteSizeFormatter.Format(0);
        CurrentPath = "准备扫描";
        IssueCount = "0";
        SkippedCount = "0";
        Elapsed = "00:00:00";
        OnPropertyChanged(nameof(HasIssues));
    }

    private static string FormatResultStatus(ScanResultStatus status)
    {
        return status switch
        {
            ScanResultStatus.Completed => "完成",
            ScanResultStatus.Canceled => "已取消",
            ScanResultStatus.Partial => "部分完成",
            ScanResultStatus.Failed => "失败",
            _ => status.ToString()
        };
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        lock (_scanGate)
        {
            try
            {
                _scanCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by a concurrent scan completion path.
            }

            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }

        AppLog.Information("ScanSessionViewModel disposed; active scan cancellation requested.");
    }
}
