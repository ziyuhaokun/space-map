using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Space.Map.App.Logging;
using System.Collections.ObjectModel;
using System.IO;

namespace Space.Map.App.ViewModels;

/// <summary>
/// Startup drive cards: enumerate, select path, and kick off scans via <see cref="ScanSessionViewModel"/>.
/// </summary>
public partial class DriveHomeViewModel : ObservableObject
{
    private readonly ScanSessionViewModel _scan;
    private readonly Action _showDriveHome;
    private readonly Func<bool> _isHostDisposed;

    public DriveHomeViewModel(
        ScanSessionViewModel scan,
        Action showDriveHome,
        Func<bool> isHostDisposed)
    {
        _scan = scan;
        _showDriveHome = showDriveHome;
        _isHostDisposed = isHostDisposed;
        _scan.PropertyChanged += OnScanPropertyChanged;
    }

    public ObservableCollection<DriveEntry> Drives { get; } = [];

    private void OnScanPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScanSessionViewModel.IsScanning))
        {
            RefreshDrivesCommand.NotifyCanExecuteChanged();
            SelectDriveCommand.NotifyCanExecuteChanged();
            ScanDriveCommand.NotifyCanExecuteChanged();
            ShowDriveHomeCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void ShowDriveHome()
    {
        if (_isHostDisposed())
        {
            return;
        }

        _showDriveHome();
        RefreshDrives();
        _scan.SetStatus("选择磁盘开始扫描，或通过工具栏选择目录");
        AppLog.Debug("Drive home shown.");
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void RefreshDrives()
    {
        if (_isHostDisposed())
        {
            return;
        }

        Drives.Clear();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                Drives.Add(DriveEntry.FromDriveInfo(drive));
            }

            if (Drives.Count == 0)
            {
                _scan.SetStatus("未发现可用磁盘");
            }
        }
        catch (Exception exception)
        {
            AppLog.Warning($"Failed to enumerate drives: {exception.Message}");
            _scan.SetStatus("无法枚举磁盘");
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void SelectDrive(DriveEntry? drive)
    {
        if (drive is null || string.IsNullOrWhiteSpace(drive.RootPath))
        {
            return;
        }

        _scan.SelectedPath = drive.RootPath;
        _scan.SetStatus(drive.IsReady
            ? $"已选择 {drive.DisplayName}"
            : $"磁盘未就绪：{drive.DisplayName}");
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task ScanDriveAsync(DriveEntry? drive)
    {
        if (drive is null || !drive.CanScan)
        {
            return;
        }

        _scan.SelectedPath = drive.RootPath;
        if (!_scan.StartScanCommand.CanExecute(null))
        {
            _scan.SetStatus("无法扫描所选磁盘");
            return;
        }

        await _scan.StartScanCommand.ExecuteAsync(null);
    }

    private bool CanInteract() => !_scan.IsScanning && !_isHostDisposed();
}
