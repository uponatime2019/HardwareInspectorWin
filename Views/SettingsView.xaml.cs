using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewHwInspector.Services;
using System;
using System.IO;
using Windows.Storage.Pickers;

namespace NewHwInspector.Views;

public sealed partial class SettingsView : UserControl
{
    private bool _ready;
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_ready) return;
        _ready = true;
        var s = AppSettings.Current;
        SelectByTag(IntervalCombo, s.RefreshIntervalSeconds.ToString());
        SelectByTag(ChartRangeCombo, s.ChartTimeRangeMinutes.ToString());
        SelectByTag(TempUnitCombo, s.TempUnit);
        SelectByTag(ThemeCombo, s.Theme);
        ReportFolderBox.Text = string.IsNullOrWhiteSpace(s.ReportFolder) ? ReportService.DefaultReportFolder : s.ReportFolder;
        GroupSensorsCheck.IsChecked = s.GroupSensors;
        AutoFitCheck.IsChecked = s.AutoFitCharts;
        AppNameText.Text = "New HwInspector";
        VersionText.Text = $"Version {ReportService.AppVersion} • Build {DateTime.Now:yyyy-MM-dd}";
        ArchText.Text = $"Platform {Environment.OSVersion.VersionString} • {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture} • {Environment.ProcessorCount} logical processors";
        ErrorStateText.Text = $"Logs: {AppLogger.LogDirectory}\nSettings: {AppSettings.SettingsFilePath}\nRuns: {s.RunCount} • First run: {s.IsFirstTimeRun}";
    }

    private void SelectByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items) if (item is ComboBoxItem ci && (ci.Tag?.ToString() == tag)) { combo.SelectedItem = item; return; }
        if (combo.Items.Count>0) combo.SelectedIndex = 0;
    }

    private async void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (IntervalCombo.SelectedItem is ComboBoxItem ci && int.TryParse(ci.Tag?.ToString(), out int v))
        {
            AppSettings.Current.RefreshIntervalSeconds = v;
            SensorService.Instance.UpdateInterval();
            await AppSettings.SaveAsync();
            InfoBar.Title = "Refresh interval updated"; InfoBar.Message = $"Now {v} second(s)."; InfoBar.Severity = InfoBarSeverity.Success; InfoBar.IsOpen = true;
        }
    }

    private async void OnChartRangeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (ChartRangeCombo.SelectedItem is ComboBoxItem ci && int.TryParse(ci.Tag?.ToString(), out int v))
        {
            AppSettings.Current.ChartTimeRangeMinutes = v;
            await AppSettings.SaveAsync();
            InfoBar.Title = "Chart range updated"; InfoBar.Message = $"{v} minute(s)."; InfoBar.Severity=InfoBarSeverity.Success; InfoBar.IsOpen=true;
        }
    }

    private async void OnAutoFitChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppSettings.Current.AutoFitCharts = AutoFitCheck.IsChecked==true;
        await AppSettings.SaveAsync();
    }

    private async void OnTempUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (TempUnitCombo.SelectedItem is ComboBoxItem ci)
        {
            AppSettings.Current.TempUnit = ci.Tag?.ToString() ?? "Celsius";
            await AppSettings.SaveAsync();
            InfoBar.Title="Temperature unit"; InfoBar.Message=AppSettings.Current.TempUnit; InfoBar.Severity=InfoBarSeverity.Success; InfoBar.IsOpen=true;
        }
    }

    private async void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (ThemeCombo.SelectedItem is ComboBoxItem ci)
        {
            string theme = ci.Tag?.ToString() ?? "Dark";
            AppSettings.Current.Theme = theme;
            await AppSettings.SaveAsync();
            // Apply immediately
            if (this.XamlRoot?.Content is FrameworkElement fe) fe.RequestedTheme = theme switch { "Light"=>ElementTheme.Light, "Dark"=>ElementTheme.Dark, _=>ElementTheme.Default };
            // Also broadcast to MainWindow
            try { (Application.Current as App)?.GetWindow()?.Content?.TrySetTheme(theme); } catch{}
            InfoBar.Title="Theme"; InfoBar.Message=theme; InfoBar.Severity=InfoBarSeverity.Success; InfoBar.IsOpen=true;
        }
    }

    private async void OnGroupSensorsChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppSettings.Current.GroupSensors = GroupSensorsCheck.IsChecked==true;
        await AppSettings.SaveAsync();
    }

    private async void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); picker.SuggestedStartLocation=PickerLocationId.DocumentsLibrary;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(((App)Application.Current).GetWindow()!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder!=null) { ReportFolderBox.Text = folder.Path; AppSettings.Current.ReportFolder = folder.Path; await AppSettings.SaveAsync(); InfoBar.Title="Folder"; InfoBar.Message=folder.Path; InfoBar.Severity=InfoBarSeverity.Success; InfoBar.IsOpen=true; }
        } catch (Exception ex){ AppLogger.LogException(ex,"BrowseSettingsFolder"); }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = AppLogger.LogDirectory;
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{ FileName=dir, UseShellExecute=true});
        } catch{}
    }

    private void OnClearHistory(object sender, RoutedEventArgs e) { SensorService.Instance.ClearHistory(); InfoBar.Title="History cleared"; InfoBar.Message="Min/max/average reset."; InfoBar.Severity=InfoBarSeverity.Success; InfoBar.IsOpen=true; }
    private void OnOpenReports(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = ReportFolderBox.Text;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { folder = ReportService.DefaultReportFolder; Directory.CreateDirectory(folder); }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{ FileName=folder, UseShellExecute=true});
        } catch{}
    }
}

public static class ThemeExtensions
{
    public static void TrySetTheme(this object? content, string theme)
    {
        if (content is FrameworkElement fe) fe.RequestedTheme = theme switch { "Light"=>ElementTheme.Light, "Dark"=>ElementTheme.Dark, _=>ElementTheme.Default };
    }
}
