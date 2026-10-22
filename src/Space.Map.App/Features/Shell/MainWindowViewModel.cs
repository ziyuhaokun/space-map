using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Space.Map.App.Services;
using Space.Map.Core.Abstractions;
using Space.Map.Core.Scanning;
using Space.Map.Infrastructure.Logging;

namespace Space.Map.App.ViewModels;

/// <summary>
/// Shell coordinator: composes scan/navigation/drive-home VMs, theme, and workspace view mode.
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IThemeService _themeService;
    private bool _isDisposed;

    public MainWindowViewModel(
        IDiskScanner scanner,
        IFolderPicker folderPicker,
        IShellActions shellActions,
        IThemeService themeService,
        IDriveService driveService)
    {
        _themeService = themeService;

        Navigation = new WorkspaceNavigationViewModel(shellActions);

        Scan = new ScanSessionViewModel(
            scanner,
            folderPicker,
            driveService,
            Navigation,
            hideDriveHome: () => IsDriveHomeVisible = false,
            showDriveHome: () => IsDriveHomeVisible = true,
            isHostDisposed: () => _isDisposed);

        DriveHome = new DriveHomeViewModel(
            Scan,
            driveService,
            showDriveHome: () => IsDriveHomeVisible = true,
            isHostDisposed: () => _isDisposed);

        UpdateThemeToggleState();
        DriveHome.RefreshDrivesCommand.Execute(null);
    }

    public ScanSessionViewModel Scan { get; }

    public WorkspaceNavigationViewModel Navigation { get; }

    public DriveHomeViewModel DriveHome { get; }

    [ObservableProperty]
    private string _languageButtonLabel = "中/EN";

    [ObservableProperty]
    private string _themeToggleToolTip = "切换到深色模式";

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private WorkspaceViewMode _activeView = WorkspaceViewMode.Treemap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkspaceContentVisible))]
    private bool _isDriveHomeVisible = true;

    public bool IsWorkspaceContentVisible => !IsDriveHomeVisible;

    public bool IsTreemapView => ActiveView == WorkspaceViewMode.Treemap;

    public bool IsListView => ActiveView == WorkspaceViewMode.List;

    public bool IsStatisticsView => ActiveView == WorkspaceViewMode.Statistics;

    partial void OnActiveViewChanged(WorkspaceViewMode value)
    {
        OnPropertyChanged(nameof(IsTreemapView));
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsStatisticsView));
        AppLog.Debug($"Workspace view changed: {value}.");
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        _themeService.ToggleTheme();
        UpdateThemeToggleState();
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        var next = LocalizationService.Instance.ToggleLanguage();
        LanguageButtonLabel = next == "zh-CN" ? "EN" : "中";
        UpdateThemeToggleState();
    }

    [RelayCommand]
    private void ShowTreemapView()
    {
        ActiveView = WorkspaceViewMode.Treemap;
        if (Navigation.HasScanResult)
        {
            IsDriveHomeVisible = false;
        }
    }

    [RelayCommand]
    private void ShowListView()
    {
        ActiveView = WorkspaceViewMode.List;
        if (Navigation.HasScanResult)
        {
            IsDriveHomeVisible = false;
        }
    }

    [RelayCommand]
    private void ShowStatisticsView()
    {
        ActiveView = WorkspaceViewMode.Statistics;
        if (Navigation.HasScanResult)
        {
            IsDriveHomeVisible = false;
        }
    }

    public void UpdateThemeToggleState()
    {
        if (_isDisposed)
        {
            return;
        }

        var isDark = _themeService.IsDarkTheme;
        IsDarkTheme = isDark;
        ThemeToggleToolTip = isDark
            ? LocalizationService.Instance.GetString("TitleBar_Theme_Light")
            : LocalizationService.Instance.GetString("TitleBar_Theme_Dark");
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Scan.Dispose();
        AppLog.Information("MainWindowViewModel disposed.");
    }
}
