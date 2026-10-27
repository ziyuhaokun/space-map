using Space.Map.App.Services;
using Space.Map.App.ViewModels;
using Space.Map.Core.Scanning;
using Space.Map.Infrastructure.Drives;
using Space.Map.Infrastructure.Logging;
using Space.Map.Infrastructure.Platform;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace Space.Map.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalExceptionHandlers();
        AppLog.Initialize();
        AppLog.Information("Application starting.");
        AppLog.Information($"Log file: {AppLog.CurrentLogFile}");

        try
        {
            ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
            base.OnStartup(e);

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = new MainWindow(CreateMainWindowViewModel());
            MainWindow.Show();

            AppLog.Information("Main window shown.");
        }
        catch (Exception exception)
        {
            AppLog.Critical("Startup failed.", exception);
            ShowFatalError(exception);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Information($"Application exiting. Exit code: {e.ApplicationExitCode}");
        AppLog.Shutdown();
        base.OnExit(e);
    }

    private static MainWindowViewModel CreateMainWindowViewModel()
    {
        var identityResolver = new WindowsDirectoryIdentityResolver();
        return new MainWindowViewModel(
            new FileSystemDiskScanner(identityResolver),
            new WindowsFolderPicker(),
            new WindowsShellActions(),
            new WpfUiThemeService(),
            new SystemDriveService());
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Critical("Unhandled dispatcher exception.", e.Exception);
        ShowFatalError(e.Exception);
        e.Handled = true;
        Shutdown(-1);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        AppLog.Critical($"Unhandled app domain exception. Is terminating: {e.IsTerminating}", exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }

    private static void ShowFatalError(Exception exception)
    {
        var logPath = AppLog.CurrentLogFile ?? AppLog.LogDirectory;
        MessageBox.Show(
            LocalizationService.Instance.Format("Dialog_FatalError", exception.Message, logPath),
            "Space.Map",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
