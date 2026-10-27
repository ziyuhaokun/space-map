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

    // Status/result text is stored as a resource key + args so a language switch can
    // re-render it. CurrentPath may instead hold a literal path during a scan.
    private string _statusKey = "Status_SelectPath";
    private object[] _statusArgs = [];
    private string _resultStatusKey = "Status_NoResult";
    private object[] _resultStatusArgs = [];
    private bool _currentPathIsLiteral;

    private ScanResult? _lastResult;

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
        StatusText = Loc.GetString(_statusKey);
        CurrentPath = Loc.GetString("Status_NotScanned");
        ResultStatus = Loc.GetString(_resultStatusKey);

        WeakReferenceMessenger.Default.Register<ScanSessionViewModel, Space.Map.App.Messages.StatusChangedMessage>(
            this,
            (recipient, message) => recipient.SetStatus(message.Status));
    }

    private static ILocalizationService Loc => LocalizationService.Instance;

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
    private string _statusText;

    [ObservableProperty]
    private string _currentPath;

    [ObservableProperty]
    private string _scannedFiles = "0";

    [ObservableProperty]
    private string _scannedDirectories = "0";

    [ObservableProperty]
    private string _scannedBytes = ByteSizeFormatter.Format(0);

    [ObservableProperty]
    private string _elapsed = "00:00:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IssuesHeader))]
    private string _issueCount = "0";

    public string IssuesHeader => Loc.Format("Issues_HeaderFormat", IssueCount);

    [ObservableProperty]
    private string _skippedCount = "0";

    [ObservableProperty]
    private string _totalSize = ByteSizeFormatter.Format(0);

    [ObservableProperty]
    private string _totalFiles = "0";

    [ObservableProperty]
    private string _totalDirectories = "0";

    [ObservableProperty]
    private string _resultStatus;

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

    private void SetStatusKey(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusText = Loc.Format(key, args);
    }

    private void SetResultStatusKey(string key, params object[] args)
    {
        _resultStatusKey = key;
        _resultStatusArgs = args;
        ResultStatus = Loc.Format(key, args);
    }

    private void SetCurrentPathKey(string key)
    {
        _currentPathIsLiteral = false;
        CurrentPath = Loc.GetString(key);
    }

    /// <summary>
    /// Re-renders every piece of localized text this view model owns. Called after a
    /// language switch, because these strings are computed in C# and are not driven
    /// by DynamicResource.
    /// </summary>
    public void RefreshLocalization()
    {
        if (_isDisposed)
        {
            return;
        }

        StatusText = _statusArgs.Length == 0
            ? Loc.GetString(_statusKey)
            : Loc.Format(_statusKey, _statusArgs);

        ResultStatus = _resultStatusArgs.Length == 0
            ? Loc.GetString(_resultStatusKey)
            : Loc.Format(_resultStatusKey, _resultStatusArgs);

        if (!_currentPathIsLiteral)
        {
            CurrentPath = Loc.GetString(_currentPathKey);
        }

        OnPropertyChanged(nameof(IssuesHeader));

        RebuildIssues();
        RebuildLargeFiles();
    }

    // Tracks which resource key produced the non-literal CurrentPath text.
    private string _currentPathKey = "Status_NotScanned";

    private void SetCurrentPath(string literalPath)
    {
        _currentPathIsLiteral = true;
        CurrentPath = literalPath;
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private void Browse()
    {
        var selected = _folderPicker.PickFolder(SelectedPath, Loc.GetString("Dialog_SelectScanFolder"));
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
            SetStatusKey("Status_PathEmpty");
            return;
        }

        AppLog.Information($"Scan starting. Path: {rootPath}; FollowReparsePoints: {FollowReparsePoints}");
        ResetScanState();
        _hideDriveHome();
        IsScanning = true;
        SetStatusKey("Status_ValidatingPath");
        SetCurrentPath(rootPath);

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
                SetStatusKey("Status_PathNotFound");
                SetResultStatusKey("ResultStatus_Failed");
                _showDriveHome();
                return;
            }

            SetStatusKey("Status_Scanning");
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
            SetStatusKey("Status_Canceled");
            SetResultStatusKey("ResultStatus_Canceled");
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
            SetStatusKey("Status_ScanFailed");
            SetResultStatusKey("ResultStatus_Failed");
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
            SetStatusKey("Status_Canceling");
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
            ? Loc.GetString("Status_Preparing")
            : progress.CurrentPath;
        _currentPathIsLiteral = !string.IsNullOrWhiteSpace(progress.CurrentPath);
        if (!_currentPathIsLiteral)
        {
            _currentPathKey = "Status_Preparing";
        }

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
        SetResultStatusKey("ResultStatus_Scanning");
        SetStatusKey("Status_ScanningFirstLevel", partialRoot.Children.Count);
        _navigation.ApplyPartialRoot(partialRoot);
    }

    private void ApplyResult(ScanResult result)
    {
        _lastResult = result;

        SetResultStatusKey(result.Status switch
        {
            ScanResultStatus.Completed => "ResultStatus_Completed",
            ScanResultStatus.Canceled => "ResultStatus_Canceled",
            ScanResultStatus.Partial => "ResultStatus_Partial",
            ScanResultStatus.Failed => "ResultStatus_Failed",
            _ => "ResultStatus_Failed"
        });

        SetStatusKey(result.Status switch
        {
            ScanResultStatus.Canceled => "Status_CanceledWithResult",
            ScanResultStatus.Partial => "Status_CompletedPartial",
            _ => "Status_Completed"
        });

        IssueCount = result.TotalIssueCount.ToString("N0");
        RebuildIssues();
        IsIssuesPanelExpanded = Issues.Count > 0;

        if (result.TotalIssueCount > result.Issues.Count)
        {
            // Dedicated truncated variants so the whole sentence re-renders on a
            // language switch instead of embedding a pre-formatted fragment.
            SetStatusKey(result.Status switch
            {
                ScanResultStatus.Canceled => "Status_CanceledWithResultTruncated",
                ScanResultStatus.Partial => "Status_CompletedPartialTruncated",
                _ => "Status_CompletedTruncated"
            }, result.Issues.Count);
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

        RebuildLargeFiles();
        _navigation.ApplyResultRoot(result.Root);
    }

    private void RebuildIssues()
    {
        Issues.Clear();

        if (_lastResult is null)
        {
            OnPropertyChanged(nameof(HasIssues));
            return;
        }

        foreach (var issue in _lastResult.Issues)
        {
            Issues.Add(new ScanIssueSummary(issue));
        }

        OnPropertyChanged(nameof(HasIssues));
    }

    private void RebuildLargeFiles()
    {
        LargeFiles.Clear();

        if (_lastResult?.Root is null)
        {
            return;
        }

        var rootSize = _lastResult.Root.SizeBytes;
        var topFiles = _lastResult.TopFiles;
        for (var index = 0; index < topFiles.Count; index++)
        {
            LargeFiles.Add(new ScanNodeSummary(topFiles[index], rootSize, index));
        }
    }

    private void ResetScanState()
    {
        _lastResult = null;
        LargeFiles.Clear();
        Issues.Clear();
        _navigation.Reset();
        IsIssuesPanelExpanded = false;
        TotalSize = ByteSizeFormatter.Format(0);
        TotalFiles = "0";
        TotalDirectories = "0";
        SetResultStatusKey("ResultStatus_Scanning");
        ScannedFiles = "0";
        ScannedDirectories = "0";
        ScannedBytes = ByteSizeFormatter.Format(0);
        SetCurrentPathKey("Status_Preparing");
        IssueCount = "0";
        SkippedCount = "0";
        Elapsed = "00:00:00";
        OnPropertyChanged(nameof(HasIssues));
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

        WeakReferenceMessenger.Default.UnregisterAll(this);
        AppLog.Information("ScanSessionViewModel disposed; active scan cancellation requested.");
    }
}
