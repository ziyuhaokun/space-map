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
    private readonly ILocalizationService _localization;
    private bool _isDisposed;

    public MainWindowViewModel(
        IDiskScanner scanner,
        IFolderPicker folderPicker,
        IShellActions shellActions,
        IClipboardService clipboardService,
        IThemeService themeService,
        IDriveService driveService,
        ILocalizationService localization)
    {
        _themeService = themeService;
        _localization = localization;

        Navigation = new WorkspaceNavigationViewModel(shellActions, clipboardService, localization);

        Scan = new ScanSessionViewModel(
            scanner,
            folderPicker,
            driveService,
            localization,
            Navigation,
            hideDriveHome: () => IsDriveHomeVisible = false,
            showDriveHome: () => IsDriveHomeVisible = true,
            isHostDisposed: () => _isDisposed);

        DriveHome = new DriveHomeViewModel(
            Scan,
            driveService,
            localization,
            showDriveHome: () => IsDriveHomeVisible = true,
            isHostDisposed: () => _isDisposed);

        UpdateLanguageButtonLabel();
        UpdateThemeToggleState();
        DriveHome.RefreshDrivesCommand.Execute(null);

        _localization.LanguageChanged += OnLanguageChanged;
    }


    /// <summary>
    /// Everything the shell renders from C# (status text, node/issue labels, drive
    /// cards, chart captions) is captured when built, so a language switch has to
    /// rebuild it explicitly. DynamicResource-bound XAML updates on its own.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        UpdateLanguageButtonLabel();
        UpdateThemeToggleState();

        // Drive cards and the scan's row collections refresh themselves: those rows
        // resolve their labels on read and re-notify on this same event.
        Scan.RefreshLocalization();
        Navigation.RefreshLocalization();
    }

    private void UpdateLanguageButtonLabel()
    {
        // Label shows the language you would switch TO, not the current one.
        LanguageButtonLabel = _localization.GetString(
            _localization.CurrentLanguage == "zh-CN" ? "TitleBar_LanguageToEnglish" : "TitleBar_LanguageToChinese");
    }

    public ScanSessionViewModel Scan { get; }

    public WorkspaceNavigationViewModel Navigation { get; }

    public DriveHomeViewModel DriveHome { get; }

    [ObservableProperty]
    private string _languageButtonLabel = string.Empty;

    [ObservableProperty]
    private string _themeToggleToolTip = string.Empty;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private WorkspaceViewMode _activeView = WorkspaceViewMode.Treemap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkspaceContentVisible))]
    private bool _isDriveHomeVisible = true;

    public bool IsWorkspaceContentVisible => !IsDriveHomeVisible;

    /// <summary>
    /// The window uses this to decide whether the system theme watcher may take
    /// over; a saved choice must win over the OS setting.
    /// </summary>
    public bool HasSavedThemePreference => _themeService.HasSavedPreference;

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
    // Cannot be static: [RelayCommand] generates an instance command that calls it.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Command target of the generated RelayCommand; must stay an instance method.")]
    private void ToggleLanguage()
    {
        // SetLanguage raises LanguageChanged, which drives UpdateLanguageButtonLabel
        // and the per-view-model rebuilds.
        _localization.ToggleLanguage();
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
        ThemeToggleToolTip = _localization.GetString(isDark ? "TitleBar_Theme_Light" : "TitleBar_Theme_Dark");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _localization.LanguageChanged -= OnLanguageChanged;
        Scan.Dispose();
        AppLog.Information("MainWindowViewModel disposed.");
    }
}
