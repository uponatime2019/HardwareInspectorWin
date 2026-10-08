using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NewHwInspector.Helpers;
using NewHwInspector.Models;
using NewHwInspector.Services;
using NewHwInspector.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NewHwInspector;

public sealed partial class MainWindow : Window
{
    private bool _isWindowReady;
    private bool _startupLoading = true;
    private readonly ObservableCollection<NavItem> _navItems = new();
    private readonly Dictionary<string, UserControl> _pageCache = new();
    private string _currentTag = "Overview";

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            _isWindowReady = true;
        }
        catch (Exception ex) { Services.AppLogger.LogException(ex, "MainWindow_InitializeComponent"); throw; }

        try
        {
            // Window helper with hardening
            try { WindowHelper.SetAppIcon(this); } catch (Exception ex) { Services.AppLogger.LogException(ex, "SetAppIcon"); }
            try { WindowHelper.Maximize(this); } catch (Exception ex) { Services.AppLogger.LogException(ex, "Maximize"); }

            // Nav
            BuildNav();
            NavList.ItemsSource = _navItems;
            NavList.SelectedIndex = 0;

            // Theme radios
            UpdateThemeRadios();

            VersionText.Text = $"New HwInspector v{ReportService.AppVersion} • {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}";
            StatusText.Text = "Initializing local hardware scan…";
            UptimeText.Text = "Uptime —";

            RootGrid.Loaded += OnWindowLoaded;
            Closed += OnWindowClosed;

            // Sensor monitoring
            try
            {
                SensorService.Instance.Initialize(DispatcherQueue);
                SensorService.Instance.SensorsUpdated += OnSensorsUpdated;
                SensorService.Instance.PausedChanged += OnPausedChanged;
                SensorService.Instance.SensorAlert += OnSensorAlert;
            }
            catch (Exception ex) { Services.AppLogger.LogException(ex, "InitSensorService"); }

            // Theme initial
            ApplyTheme(AppSettings.Current.Theme);
        }
        catch (Exception ex) { Services.AppLogger.LogException(ex, "MainWindow_ctor"); }
    }

    private void BuildNav()
    {
        _navItems.Clear();
        Brush accent = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x4C, 0xC3, 0xFF));
        try { if (Application.Current.Resources.TryGetValue("AccentCyanBrush", out var v) && v is Brush b) accent = b; } catch {}
        _navItems.Add(new NavItem { Tag="Overview", Title="Overview", Glyph="\uE80F", IconBrush = accent });
        _navItems.Add(new NavItem { Tag="Hardware", Title="Hardware", Glyph="\uE770" });
        _navItems.Add(new NavItem { Tag="Sensors", Title="Sensors", Glyph="\uE9D9" });
        _navItems.Add(new NavItem { Tag="Charts", Title="Charts", Glyph="\uE9D2" });
        _navItems.Add(new NavItem { Tag="Reports", Title="Reports", Glyph="\uE74E" });
        _navItems.Add(new NavItem { Tag="Alerts", Title="Alerts", Glyph="\uE7BA" });
        _navItems.Add(new NavItem { Tag="Settings", Title="Settings", Glyph="\uE713" });
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isWindowReady) return;
            // Async hardware collect
            SetStartupProgress(20, "Collecting hardware information…");
            try { await HardwareService.Instance.RefreshAsync(); } catch (Exception ex) { Services.AppLogger.LogException(ex, "WindowLoaded_Hardware"); StatusText.Text = "Hardware collection encountered warnings — see Overview."; }

            SetStartupProgress(65, "Starting sensor monitoring…");
            await SensorService.Instance.StartAsync();
            SetStartupProgress(90, "Preparing system overview…");
            NavigateTo("Overview");
            OnSensorsUpdated(null, EventArgs.Empty);
            UpdateStatusBar();
            // status timer
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (a,b)=> UpdateStatusBar();
            timer.Start();
        }
        catch (Exception ex) { Services.AppLogger.LogException(ex, "OnWindowLoaded"); }
        finally { CompleteStartupLoading(); }
    }

    private void SetStartupProgress(double value, string message)
    {
        if (!_startupLoading) return;
        StartupProgressBar.Value = value;
        StartupProgressText.Text = message;
    }

    private void CompleteStartupLoading()
    {
        if (!_startupLoading) return;
        _startupLoading = false;
        StartupProgressBar.Value = 100;
        StartupProgressText.Text = "Ready";
        StartupLoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        try { SensorService.Instance.Stop(); } catch{}
        try { _ = AppSettings.SaveAsync(); } catch{}
    }

    private void OnSensorsUpdated(object? s, EventArgs e)
    {
        try
        {
            if (!_isWindowReady) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    UpdateStatusBar();
                    UpdateAlertBadge();
                    // update pause state
                    bool paused = SensorService.Instance.IsPaused;
                    NavPauseBtn.Content = paused ? "Resume" : "Pause";
                    MonitorStateText.Text = paused ? "Paused" : "Active";
                    MonitorDot.Fill = paused ? new SolidColorBrush(Microsoft.UI.Colors.Gray) : (Brush)Application.Current.Resources["AccentGreenBrush"];
                    MonitorDetailText.Text = $"Last: {SensorService.Instance.LastSampleTime:HH:mm:ss} • {SensorService.Instance.Sensors.Count(x=>x.IsAvailable)} available";
                }
                catch (Exception ex) { Services.AppLogger.LogException(ex, "OnSensorsUpdated_UI"); }
            });
        }
        catch (Exception ex) { Services.AppLogger.LogException(ex, "OnSensorsUpdated"); }
    }

    private void OnPausedChanged(object? s, EventArgs e) => OnSensorsUpdated(s, e);
    private void OnSensorAlert(object? s, SensorEntry sensor)
    {
        try
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    // show brief teaching tip? Use InfoBar in status
                    StatusText.Text = $"Alert: {sensor.Name} {sensor.DisplayCurrent} — threshold exceeded";
                    UpdateAlertBadge();
                }
                catch{}
            });
        }
        catch{}
    }

    private void UpdateStatusBar()
    {
        try
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            UptimeText.Text = $"Uptime { (int)uptime.TotalDays}d {uptime.Hours:00}h {uptime.Minutes:00}m";
            // active alerts count in nav
            var active = AppSettings.Current.AlertRules.Count(r=>r.IsActive);
            var alertsNav = _navItems.FirstOrDefault(x=>x.Tag=="Alerts");
            if (alertsNav!=null) { alertsNav.BadgeText = active>0? active.ToString(): ""; alertsNav.BadgeVisibility = active>0? Visibility.Visible: Visibility.Collapsed; alertsNav.BadgeBrush = (Brush)Application.Current.Resources["AccentRedBrush"]; }
            // sensors nav badge with unavailable count? optional
        }
        catch{}
    }

    private void UpdateAlertBadge()
    {
        try
        {
            int active = AppSettings.Current.AlertRules.Count(r=>r.IsActive);
            if (active>0) { AlertBadge.Visibility = Visibility.Visible; AlertBadgeText.Text = $"{active} ALERT{(active>1?"S":"")}"; }
            else AlertBadge.Visibility = Visibility.Collapsed;
        }
        catch{}
    }

    private void OnNavItemClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            if (!_isWindowReady || NavList == null) return;
            if (e.ClickedItem is NavItem item) NavigateTo(item.Tag);
        }
        catch (Exception ex) { Services.AppLogger.LogException(ex, "OnNavItemClick"); }
    }

    public void NavigateTo(string tag, object? param=null)
    {
        try
        {
            if (!_isWindowReady) return;
            _currentTag = tag;
            // highlight nav
            foreach (var ni in _navItems) if (ni.Tag==tag) NavList.SelectedItem = ni;
            UserControl page = tag switch
            {
                "Hardware" => GetOrCreate("Hardware", ()=> new HardwareView()),
                "Sensors" => GetOrCreate("Sensors", ()=> new SensorsView()),
                "Charts" => GetOrCreate("Charts", ()=> new ChartsView()),
                "Reports" => GetOrCreate("Reports", ()=> new ReportsView()),
                "Alerts" => GetOrCreate("Alerts", ()=> {
                    var v = new AlertsView();
                    if (param is SensorEntry se) v.PreselectSensor(se);
                    return v;
                }, forceNew: param!=null),
                "Settings" => GetOrCreate("Settings", ()=> new SettingsView()),
                _ => GetOrCreate("Overview", ()=> new OverviewView())
            };
            ContentFrame.Content = page;
            StatusText.Text = $"{tag} • local data • {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { Services.AppLogger.LogException(ex, $"NavigateTo_{tag}"); }
    }

    private UserControl GetOrCreate(string key, Func<UserControl> factory, bool forceNew=false)
    {
        if (!forceNew && _pageCache.TryGetValue(key, out var existing)) return existing;
        var page = factory();
        _pageCache[key]=page;
        return page;
    }

    private async void OnGlobalRefresh(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isWindowReady) return;
            StatusText.Text = "Refreshing hardware…";
            await HardwareService.Instance.RefreshAsync();
            // rebuild current page if overview/hardware
            if (_currentTag=="Overview" || _currentTag=="Hardware")
            {
                _pageCache.Remove(_currentTag);
                NavigateTo(_currentTag);
            }
            StatusText.Text = $"Refreshed at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { Services.AppLogger.LogException(ex, "OnGlobalRefresh"); StatusText.Text = "Refresh failed: " + ex.Message; }
    }

    private async void OnGlobalExport(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isWindowReady) return;
            var opts = new ReportOptions { IncludeOverview=true, IncludeHardware=true, IncludeSensors=true, IncludeAlerts=true, Format="TXT" };
            string path = await ReportService.GenerateAsync(opts, HardwareService.Instance.Overview, HardwareService.Instance.Tree, SensorService.Instance.Sensors, AppSettings.Current.AlertRules);
            StatusText.Text = $"Report saved: {path}";
            var dlg = new ContentDialog { Title="Export complete", Content=path, CloseButtonText="OK", XamlRoot = Content.XamlRoot };
            await dlg.ShowAsync();
        }
        catch (Exception ex) { Services.AppLogger.LogException(ex, "OnGlobalExport"); }
    }

    private void OnThemeDark(object sender, RoutedEventArgs e) => SetTheme("Dark");
    private void OnThemeLight(object sender, RoutedEventArgs e) => SetTheme("Light");
    private void OnThemeSystem(object sender, RoutedEventArgs e) => SetTheme("System");

    private void SetTheme(string theme)
    {
        try
        {
            if (!_isWindowReady) return;
            AppSettings.Current.Theme = theme;
            _ = AppSettings.SaveAsync();
            ApplyTheme(theme);
            UpdateThemeRadios();
        }
        catch (Exception ex) { Services.AppLogger.LogException(ex, "SetTheme"); }
    }

    private void ApplyTheme(string theme)
    {
        try
        {
            if (Content is FrameworkElement fe)
            {
                fe.RequestedTheme = theme switch { "Light"=>ElementTheme.Light, "Dark"=>ElementTheme.Dark, _=>ElementTheme.Default };
            }
            if (RootGrid != null) RootGrid.RequestedTheme = theme switch { "Light"=>ElementTheme.Light, "Dark"=>ElementTheme.Dark, _=>ElementTheme.Default };
        }
        catch (Exception ex) { Services.AppLogger.LogException(ex, "ApplyTheme"); }
    }

    private void UpdateThemeRadios()
    {
        try
        {
            string t = AppSettings.Current.Theme;
            ThemeDarkItem.IsChecked = t=="Dark";
            ThemeLightItem.IsChecked = t=="Light";
            ThemeSystemItem.IsChecked = t=="System";
        }
        catch{}
    }

    private void OnNavPause(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isWindowReady) return;
            if (SensorService.Instance.IsPaused) SensorService.Instance.Resume(); else SensorService.Instance.Pause();
        }
        catch (Exception ex) { Services.AppLogger.LogException(ex, "OnNavPause"); }
    }
    private void OnNavRefreshSensors(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isWindowReady) return;
            SensorService.Instance.ClearHistory();
            StatusText.Text = "Sensor history cleared";
        }
        catch{}
    }
}

public sealed class NavItem : System.ComponentModel.INotifyPropertyChanged
{
    public string Tag { get; init; } = "";
    public string Title { get; init; } = "";
    public string Glyph { get; init; } = "";
    public Brush IconBrush { get; init; } = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x8B, 0x9A, 0xB5));
    private string _badgeText = "";
    public string BadgeText { get=>_badgeText; set{ if(_badgeText!=value){ _badgeText=value; OnPropertyChanged(nameof(BadgeText)); OnPropertyChanged(nameof(BadgeVisibility)); } } }
    private Visibility _badgeVis = Visibility.Collapsed;
    public Visibility BadgeVisibility { get=>_badgeVis; set{ if(_badgeVis!=value){ _badgeVis=value; OnPropertyChanged(nameof(BadgeVisibility)); } } }
    public Brush BadgeBrush { get; set; } = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE8, 0x57, 0x4E));
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string n)=> PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
}
