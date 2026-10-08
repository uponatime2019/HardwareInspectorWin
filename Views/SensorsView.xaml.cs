using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NewHwInspector.Models;
using NewHwInspector.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NewHwInspector.Views;

public sealed partial class SensorsView : UserControl
{
    private bool _isLoaded;
    private string _search = "";
    private string _groupFilter = "";
    private string _sort = "Name";
    private bool _grouped = true;

    public SensorsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded) return;
        _isLoaded = true;
        SensorService.Instance.SensorsUpdated += OnSensorsUpdated;
        SensorService.Instance.PausedChanged += OnPausedChanged;
        // populate group combo
        GroupCombo.Items.Clear();
        GroupCombo.Items.Add(new ComboBoxItem { Content = "All groups" });
        foreach (var g in SensorService.Instance.Groups.Distinct().OrderBy(x => x)) GroupCombo.Items.Add(new ComboBoxItem { Content = g });
        GroupCombo.SelectedIndex = 0;
        GroupCheck.IsChecked = AppSettings.Current.GroupSensors;
        _grouped = AppSettings.Current.GroupSensors;
        Render();
        UpdateStatus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _isLoaded = false;
        SensorService.Instance.SensorsUpdated -= OnSensorsUpdated;
        SensorService.Instance.PausedChanged -= OnPausedChanged;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_isLoaded) Render();
    }

    private void OnSensorsUpdated(object? s, EventArgs e)
    {
        if (!_isLoaded) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isLoaded) return;
            Render();
            UpdateStatus();
        });
    }

    private void OnPausedChanged(object? s, EventArgs e)
    {
        if (_isLoaded) DispatcherQueue.TryEnqueue(UpdatePauseButton);
    }

    private void UpdatePauseButton()
    {
        bool paused = SensorService.Instance.IsPaused;
        PauseText.Text = paused ? "Resume" : "Pause";
        PauseIcon.Glyph = paused ? "\uE768" : "\uE769";
    }

    private void UpdateStatus()
    {
        bool active = !SensorService.Instance.IsPaused && SensorService.Instance.IsMonitoring;
        string last = SensorService.Instance.LastSampleTime == DateTime.MinValue ? "—" : $"{(DateTime.Now - SensorService.Instance.LastSampleTime).TotalSeconds:0}s ago";
        var activeAlerts = AppSettings.Current.AlertRules.Count(r => r.IsActive);
        StatusText.Text = $"{(active ? "Monitoring active" : "Paused")} • Last sample {last} • {SensorService.Instance.Sensors.Count(s=>s.IsAvailable)} / {SensorService.Instance.Sensors.Count} available • Alerts: {(activeAlerts>0? $"{activeAlerts} active":"none")}";
        if (activeAlerts>0) { AlertIcon.Visibility = Visibility.Visible; AlertText.Visibility = Visibility.Visible; AlertText.Text = $"{activeAlerts} alert(s)"; AlertText.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF,0xE8,0x57,0x4E)); }
        else { AlertIcon.Visibility = Visibility.Collapsed; AlertText.Visibility = Visibility.Collapsed; }
        UpdatePauseButton();
    }

    private void Render()
    {
        try
        {
            if (!_isLoaded || SensorsHost is null) return;
            SensorsHost.Children.Clear();
            var query = SensorService.Instance.Sensors.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(_search))
            {
                string q = _search;
                query = query.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Group.Contains(q, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(_groupFilter) && _groupFilter != "All groups") query = query.Where(s => s.Group == _groupFilter);

            query = _sort switch
            {
                "Current" => query.OrderByDescending(s => s.Current ?? double.MinValue),
                "Group" => query.OrderBy(s => s.Group).ThenBy(s => s.Name),
                "Status" => query.OrderByDescending(s => (int)s.Status).ThenBy(s=>s.Name),
                _ => query.OrderBy(s => s.Group).ThenBy(s => s.Name)
            };

            // Header
            var header = CreateHeader();
            SensorsHost.Children.Add(header);

            if (_grouped)
            {
                foreach (var grp in query.GroupBy(s => s.Group).OrderBy(g=>g.Key))
                {
                    var groupHeader = CreateGroupHeader(grp.Key, grp.Count());
                    SensorsHost.Children.Add(groupHeader);
                    foreach (var s in grp) SensorsHost.Children.Add(CreateRow(s));
                }
            }
            else
            {
                foreach (var s in query) SensorsHost.Children.Add(CreateRow(s));
            }
            if (!query.Any())
            {
                SensorsHost.Children.Add(new TextBlock { Text = "No sensors match filter.", Style = (Style)Application.Current.Resources["SubText"], Margin = new Thickness(12,16,0,0) });
            }
        }
        catch (Exception ex) { AppLogger.LogException(ex, "SensorsRender"); }
    }

    private Border CreateHeader()
    {
        var grid = new Grid { Style = (Style)Application.Current.Resources["SensorHeaderGridStyle"], Padding = new Thickness(12,6,12,6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        string[] headers = { "Sensor", "Current", "Min", "Max", "Average", "Status" };
        for (int i=0;i<headers.Length;i++)
        {
            var tb = new TextBlock { Text = headers[i], Style = (Style)Application.Current.Resources["SubText"], FontSize=14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            Grid.SetColumn(tb,i); grid.Children.Add(tb);
        }
        var border = new Border { Child = grid, Style = (Style)Application.Current.Resources["SensorDividerBorderStyle"], BorderThickness = new Thickness(0,0,0,1) };
        return border;
    }

    private Border CreateGroupHeader(string group, int count)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(12,8,12,6) };
        sp.Children.Add(new FontIcon { Glyph = GroupGlyph(group), FontSize=12, Foreground = (Brush)Application.Current.Resources["AccentCyanBrush"] });
        sp.Children.Add(new TextBlock { Text = group, Style = (Style)Application.Current.Resources["CardTitleText"], FontSize=14 });
        sp.Children.Add(new TextBlock { Text = $"({count})", Style=(Style)Application.Current.Resources["SubText"], FontSize=14, VerticalAlignment = VerticalAlignment.Center });
        var border = new Border { Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x22,0x4C,0xC3,0xFF)), Child = sp, BorderThickness = new Thickness(0,1,0,1), Style = (Style)Application.Current.Resources["SensorDividerBorderStyle"] };
        return border;
    }

    private string GroupGlyph(string g) => g switch
    {
        "CPU" => "\uE950", "CPU Cores" => "\uE950", "Memory" => "\uE8F1", "GPU" => "\uEB9F", "Storage" => "\uEDA2", "System" => "\uE770",
        _ => "\uE9D9"
    };

    private Border CreateRow(SensorEntry s)
    {
        var grid = new Grid { Padding = new Thickness(12,7,12,7), ColumnSpacing=8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        var namePanel = new StackPanel { Spacing=1 };
        var nameText = new TextBlock { Text=s.Name, Style = (Style)Application.Current.Resources["CardValueText"], FontSize=14, TextTrimming = TextTrimming.CharacterEllipsis };
        if (!s.IsAvailable) nameText.Style = (Style)Application.Current.Resources["SubText"];
        var desc = new TextBlock { Text=s.Description, Style=(Style)Application.Current.Resources["SubText"], FontSize=14, Visibility = string.IsNullOrWhiteSpace(s.Description)? Visibility.Collapsed: Visibility.Visible, TextTrimming = TextTrimming.CharacterEllipsis };
        namePanel.Children.Add(nameText); if(desc.Visibility==Visibility.Visible) namePanel.Children.Add(desc);
        Grid.SetColumn(namePanel,0); grid.Children.Add(namePanel);

        var cur = new TextBlock { Text=s.DisplayCurrent, FontSize=14, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
        if (s.Status is SensorStatus.Unavailable or SensorStatus.Unknown) cur.Style = (Style)Application.Current.Resources["SubText"];
        else cur.Foreground = StatusBrush(s.Status);
        var min = new TextBlock { Text=s.DisplayMin, Style=(Style)Application.Current.Resources["SubText"], FontSize=14, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
        var max = new TextBlock { Text=s.DisplayMax, Style=(Style)Application.Current.Resources["SubText"], FontSize=14, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
        var avg = new TextBlock { Text=s.DisplayAverage, Style=(Style)Application.Current.Resources["SubText"], FontSize=14, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
        Grid.SetColumn(cur,1); Grid.SetColumn(min,2); Grid.SetColumn(max,3); Grid.SetColumn(avg,4);
        grid.Children.Add(cur); grid.Children.Add(min); grid.Children.Add(max); grid.Children.Add(avg);

        var statusPanel = new StackPanel { Orientation=Orientation.Horizontal, Spacing=4, VerticalAlignment = VerticalAlignment.Center };
        if (s.Status != SensorStatus.Unknown)
        {
            var statusIcon = new FontIcon { Glyph=s.StatusGlyph, FontSize=14 };
            if (s.Status == SensorStatus.Unavailable) statusIcon.Style = (Style)Application.Current.Resources["SensorSubIconStyle"];
            else statusIcon.Foreground = StatusBrush(s.Status);
            statusPanel.Children.Add(statusIcon);
        }
        var statusText = new TextBlock { Text = s.IsAvailable? s.Status.ToString(): "n/a", FontSize=14, VerticalAlignment=VerticalAlignment.Center };
        if (s.IsAvailable) statusText.Foreground = StatusBrush(s.Status);
        else statusText.Style = (Style)Application.Current.Resources["SubText"];
        statusPanel.Children.Add(statusText);
        Grid.SetColumn(statusPanel,5); grid.Children.Add(statusPanel);

        var border = new Border { Child=grid, Style = (Style)Application.Current.Resources["SensorDividerBorderStyle"], BorderThickness=new Thickness(0,0,0,1), Background = s.Status==SensorStatus.Critical? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x18,0xE8,0x57,0x4E)) : s.Status==SensorStatus.Warning? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x18,0xE8,0xB3,0x3C)) : new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        // Context menu
        var fly = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text="Copy value" }; copy.Click+=(a,b)=>CopySensor(s);
        var chart = new MenuFlyoutItem { Text="Show chart" }; chart.Click+=(a,b)=>OnShowChart(s);
        var alert = new MenuFlyoutItem { Text="Configure alert…" }; alert.Click+=(a,b)=>OnConfigureAlert(s);
        fly.Items.Add(copy); fly.Items.Add(chart); fly.Items.Add(alert);
        border.ContextFlyout = fly;
        border.DoubleTapped+=(a,b)=>OnShowChart(s);
        ToolTipService.SetToolTip(border, $"{s.Name} — {s.Group}\nCurrent: {s.DisplayCurrent}\nMin: {s.DisplayMin} Max: {s.DisplayMax} Avg: {s.DisplayAverage}\n{s.Error ?? s.Description}");
        return border;
    }

    private Brush StatusBrush(SensorStatus st) => st switch
    {
        SensorStatus.Normal => (Brush)Application.Current.Resources["AccentGreenBrush"],
        SensorStatus.Warning => (Brush)Application.Current.Resources["AccentOrangeBrush"],
        SensorStatus.Critical => (Brush)Application.Current.Resources["AccentRedBrush"],
        SensorStatus.Unavailable => (Brush)Application.Current.Resources["SubTextBrush"],
        _ => (Brush)Application.Current.Resources["SubTextBrush"]
    };

    private void CopySensor(SensorEntry s)
    {
        try { var dp=new Windows.ApplicationModel.DataTransfer.DataPackage(); dp.SetText($"{s.Group} / {s.Name}: {s.DisplayCurrent} (min {s.DisplayMin}, max {s.DisplayMax}, avg {s.DisplayAverage}) {s.Unit}"); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp); } catch{}
    }

    private void OnShowChart(SensorEntry s)
    {
        try { ChartsView.RequestedSensor = s; var win = FindParentMainWindow(); win?.NavigateTo("Charts"); } catch{}
    }

    private void OnConfigureAlert(SensorEntry s)
    {
        try { var win = FindParentMainWindow(); win?.NavigateTo("Alerts", s); } catch{}
    }

    private MainWindow? FindParentMainWindow()
    {
        try
        {
            return (App.Current as App)?.GetWindow() as MainWindow;
        }
        catch { return null; }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) { _search = SearchBox.Text?.Trim() ?? ""; if (_isLoaded) Render(); }
    private void OnGroupChanged(object sender, SelectionChangedEventArgs e)
    {
        try { if (GroupCombo.SelectedItem is ComboBoxItem ci) { _groupFilter = ci.Content?.ToString() ?? ""; if (_isLoaded) Render(); } } catch{}
    }
    private void OnSortChanged(object sender, SelectionChangedEventArgs e)
    {
        try { if (SortCombo.SelectedItem is ComboBoxItem ci) { _sort = ci.Tag?.ToString() ?? "Name"; if (_isLoaded) Render(); } } catch{}
    }
    private void OnGroupToggle(object sender, RoutedEventArgs e) { if (!_isLoaded) return; _grouped = GroupCheck.IsChecked == true; AppSettings.Current.GroupSensors = _grouped; _ = AppSettings.SaveAsync(); Render(); }
    private void OnPauseResume(object sender, RoutedEventArgs e)
    {
        if (SensorService.Instance.IsPaused) SensorService.Instance.Resume(); else SensorService.Instance.Pause();
        UpdateStatus();
    }
    private void OnClearHistory(object sender, RoutedEventArgs e) { SensorService.Instance.ClearHistory(); Render(); }
    private async void OnExportSensors(object sender, RoutedEventArgs e)
    {
        try
        {
            var opts = new ReportOptions { IncludeOverview=false, IncludeHardware=false, IncludeSensors=true, IncludeAlerts=false, Format="CSV" };
            var path = await ReportService.GenerateAsync(opts, HardwareService.Instance.Overview, HardwareService.Instance.Tree, SensorService.Instance.Sensors, AppSettings.Current.AlertRules);
            var dlg = new ContentDialog { Title="Export", Content=$"Sensors exported to:\n{path}", CloseButtonText="OK", XamlRoot = this.XamlRoot };
            await dlg.ShowAsync();
        } catch (Exception ex) { AppLogger.LogException(ex,"ExportSensors"); }
    }
}
