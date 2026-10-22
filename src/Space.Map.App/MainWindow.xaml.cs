using Space.Map.App.Logging;
using Space.Map.App.ViewModels;
using System.Windows.Interop;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Space.Map.App;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        AppLog.Information("Creating main window.");

        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        AppLog.Information("Main window loaded.");
        ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: true);
        _viewModel.UpdateThemeToggleState();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        AppLog.Information("Main window closed.");
        UnwatchSystemTheme();
        ApplicationThemeManager.Changed -= OnApplicationThemeChanged;
        _viewModel.Dispose();
    }

    private void OnApplicationThemeChanged(ApplicationTheme applicationTheme, System.Windows.Media.Color systemAccent)
    {
        AppLog.Debug($"Application theme changed: {applicationTheme}.");
        _viewModel.UpdateThemeToggleState();
    }

    private void UnwatchSystemTheme()
    {
        if (HwndSource.FromVisual(this) is not HwndSource hwndSource ||
            hwndSource.Handle == IntPtr.Zero)
        {
            return;
        }

        SystemThemeWatcher.UnWatch(this);
    }
}
