using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Space.Map.App.Services;
using Space.Map.Core.Abstractions;
using Space.Map.Infrastructure.Logging;
using System.Collections.ObjectModel;

namespace Space.Map.App.ViewModels;

/// <summary>
/// Startup drive cards: enumerate, select path, and kick off scans via <see cref="ScanSessionViewModel"/>.
/// </summary>
public partial class DriveHomeViewModel : ObservableObject
{
    private readonly ScanSessionViewModel _scan;
    private readonly IDriveService _driveService;
    private readonly ILocalizationService _localization;
    private readonly Action _showDriveHome;
    private readonly Func<bool> _isHostDisposed;

    public DriveHomeViewModel(
        ScanSessionViewModel scan,
        IDriveService driveService,
        ILocalizationService localization,
        Action showDriveHome,
        Func<bool> isHostDisposed)
    {
        _scan = scan;
        _localization = localization;
        _driveService = driveService;
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
        _scan.SetStatus(_localization.GetString("Status_SelectPath"));
        AppLog.Debug("Drive home shown.");
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void RefreshDrives()
    {
        if (_isHostDisposed())
        {
            return;
        }

        Drives.DetachAndClear();

        try
        {
            var volumes = _driveService.GetDrives();
            foreach (var volume in volumes)
            {
                Drives.Add(DriveEntry.FromVolume(volume));
            }

            if (Drives.Count == 0)
            {
                _scan.SetStatus(_localization.GetString("Drive_NoDrives"));
            }
        }
        catch (Exception exception)
        {
            AppLog.Warning($"Failed to enumerate drives: {exception.Message}");
            _scan.SetStatus(_localization.GetString("Drive_EnumerateFailed"));
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
            ? _localization.Format("Drive_Selected", drive.DisplayName)
            : _localization.Format("Drive_NotReadyFormat", drive.DisplayName));
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
            _scan.SetStatus(_localization.GetString("Drive_CannotScan"));
            return;
        }

        await _scan.StartScanCommand.ExecuteAsync(null);
    }

    private bool CanInteract() => !_scan.IsScanning && !_isHostDisposed();
}
