using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NewHwInspector.Services;
using System;

namespace NewHwInspector.Views;

public sealed partial class OverviewView : UserControl
{
    private bool _loaded;
    public OverviewView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SensorService.Instance.SensorsUpdated += OnSensorsUpdated;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        if (HardwareService.Instance.Tree.Count == 0)
        {
            try { await HardwareService.Instance.RefreshAsync(); } catch (Exception ex) { AppLogger.LogException(ex, "OverviewRefresh"); }
        }
        Render();
        SensorService.Instance.SensorsUpdated += OnSensorsUpdated;
    }

    private void OnSensorsUpdated(object? s, EventArgs e)
    {
        try
        {
            DispatcherQueue.TryEnqueue(() => UpdateLiveParts());
        }
        catch { }
    }

    private void UpdateLiveParts()
    {
        try
        {
            var ov = HardwareService.Instance.Overview;
            // live mem
            var memSensor = FindSensor("Memory Usage", "Memory");
            if (memSensor != null && memSensor.Current.HasValue)
            {
                MemProgress.Value = memSensor.Current.Value;
                MemUsedText.Text = $"{HardwareService.Instance.Overview.MemoryUsedText} • {memSensor.DisplayCurrent} load";
            }
            else
            {
                MemUsedText.Text = ov.MemoryUsedText;
                MemProgress.Value = ov.MemoryLoad;
            }
            var cpuTemp = FindSensor("CPU Package Temperature", "CPU");
            if (cpuTemp != null) CpuTempText.Text = cpuTemp.IsAvailable ? cpuTemp.DisplayCurrent : "n/a";
            var cpuUsage = FindSensor("CPU Total Usage", "CPU");
            CpuClockText.Text = cpuUsage?.IsAvailable == true ? $"{cpuUsage.DisplayCurrent} load" : ov.CpuBaseClock;

            UpdatedText.Text = $"Updated: {DateTime.Now:HH:mm:ss} • Last sensor {SensorService.Instance.LastSampleTime:HH:mm:ss}";
        }
        catch (Exception ex) { AppLogger.LogException(ex, "UpdateLiveParts"); }
    }

    private Models.SensorEntry? FindSensor(string name, string group)
    {
        foreach (var s in SensorService.Instance.Sensors) if (s.Name == name && s.Group == group) return s;
        return null;
    }

    private void Render()
    {
        try
        {
            var ov = HardwareService.Instance.Overview;
            UpdatedText.Text = $"Updated: {ov.CollectedAt:HH:mm:ss} • {Environment.MachineName}";
            StatusText.Text = ov.Warnings.Count > 0 ? $"{ov.Warnings.Count} note(s)" : "Ready";

            CpuNameText.Text = ov.CpuName;
            CpuCoresText.Text = ov.CpuCores;
            CpuClockText.Text = ov.CpuBaseClock;
            CpuTempText.Text = ov.CpuTempText;
            if (ov.CpuTempC.HasValue && ov.CpuTempC >= 80) CpuTempText.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE8, 0x57, 0x4E));
            else if (ov.CpuTempC.HasValue && ov.CpuTempC >= 70) CpuTempText.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE8, 0xB3, 0x3C));
            else CpuTempText.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x7E, 0xC8, 0xFF));

            MemTotalText.Text = ov.TotalRamText;
            MemTypeText.Text = $"{ov.RamType} • {ov.RamSpeed}";
            MemProgress.Value = ov.MemoryLoad;
            MemUsedText.Text = ov.MemoryUsedText;

            GpuNameText.Text = ov.GpuName;
            GpuMemText.Text = ov.GpuMemory;
            GpuDriverText.Text = $"Driver {ov.GpuDriver}";
            GpuTempText.Text = ov.GpuTempText;

            StorageSummaryText.Text = ov.StorageSummary;
            SystemDriveText.Text = ov.SystemDriveFree;
            DriveCountText.Text = $"{ov.DriveCount} physical drives";

            BuildPanel(BoardPanel, new (string, string)[] { ("Vendor", ov.BoardVendor), ("Model", ov.BoardModel), ("BIOS version", ov.BiosVersion), ("BIOS date", ov.BiosDate) });
            BuildPanel(OsPanel, new (string, string)[] { ("Name", ov.OsName), ("Version", ov.OsVersion), ("Build", ov.OsBuild), ("Architecture", ov.OsArch) });
            BuildPanel(CpuDetailPanel, new (string, string)[] { ("Name", ov.CpuName), ("Cores", ov.CpuCores), ("Base clock", ov.CpuBaseClock), ("Socket", ov.CpuSocket), ("Temperature", ov.CpuTempText) });
            BuildPanel(SecPanel, new (string, string)[] { ("Firmware", ov.FirmwareMode), ("Secure Boot", ov.SecureBoot), ("TPM", ov.TpmText), ("Virtualization", ov.Virtualization) });

            if (ov.Warnings.Count > 0)
            {
                WarningsCard.Visibility = Visibility.Visible;
                WarningsText.Text = string.Join("\n• ", ov.Warnings);
                WarnBar.IsOpen = true;
                WarnBar.Message = string.Join("; ", ov.Warnings);
            }
            else
            {
                WarningsCard.Visibility = Visibility.Collapsed;
                WarnBar.IsOpen = false;
            }

            UpdateLiveParts();
        }
        catch (Exception ex) { AppLogger.LogException(ex, "OverviewRender"); }
    }

    private void BuildPanel(StackPanel panel, (string label, string value)[] items)
    {
        panel.Children.Clear();
        foreach (var (label, value) in items)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lb = new TextBlock { Text = label, Style = (Style)Application.Current.Resources["SubText"], FontSize = 14 };
            var val = new TextBlock { Text = string.IsNullOrWhiteSpace(value) ? "—" : value, Style = (Style)Application.Current.Resources["CardValueText"], FontSize = 14 };
            Grid.SetColumn(lb, 0); Grid.SetColumn(val, 1);
            grid.Children.Add(lb); grid.Children.Add(val);
            panel.Children.Add(grid);
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Refreshing…";
            await HardwareService.Instance.RefreshAsync();
            Render();
            StatusText.Text = "Refreshed at " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex) { AppLogger.LogException(ex, "OverviewOnRefresh"); StatusText.Text = "Refresh failed"; }
    }
}
