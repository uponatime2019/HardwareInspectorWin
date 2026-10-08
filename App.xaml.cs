using Microsoft.UI.Xaml;
using NewHwInspector.Services;
using System;

namespace NewHwInspector;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (s, e) =>
        {
            AppLogger.LogException(e.Exception, "GlobalUnhandledException");
            e.Handled = true;
            if (_window == null) { AppLogger.Log("Fatal during startup; exiting"); Exit(); }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex) AppLogger.LogException(ex, "AppDomainUnhandled");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            AppLogger.LogException(e.Exception, "UnobservedTask");
            e.SetObserved();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLogger.Log($"NewHwInspector launched version={ReportService.AppVersion}");
        await AppSettings.LoadAsync();
        ApplyTheme(AppSettings.Current.Theme);
        _window = new MainWindow();
        _window.Activate();
        AppLogger.Log("MainWindow activated");
    }

    public Window? GetWindow() => _window;

    public static void ApplyTheme(string theme)
    {
        try
        {
            if (Current is App app && app._window != null)
            {
                var root = app._window.Content as FrameworkElement;
                if (root != null)
                {
                    root.RequestedTheme = theme switch
                    {
                        "Light" => ElementTheme.Light,
                        "Dark" => ElementTheme.Dark,
                        _ => ElementTheme.Default
                    };
                }
            }
            else if (Current != null)
            {
                // Window not yet created; will be applied after creation
            }
        }
        catch (Exception ex) { AppLogger.LogException(ex, "ApplyTheme"); }
    }
}
