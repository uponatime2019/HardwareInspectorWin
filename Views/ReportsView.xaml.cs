using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewHwInspector.Models;
using NewHwInspector.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace NewHwInspector.Views;

public sealed partial class ReportsView : UserControl
{
    private readonly List<string> _recent = new();
    public ReportsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FolderBox.Text = string.IsNullOrWhiteSpace(AppSettings.Current.ReportFolder) ? DefaultFolder() : AppSettings.Current.ReportFolder;
        FolderInfo.Text = $"Will save to: {FolderBox.Text}";
        RefreshRecent();
    }

    private string DefaultFolder()
    {
        string p = ReportService.DefaultReportFolder;
        try { Directory.CreateDirectory(p); } catch { }
        return p;
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(((App)Application.Current).GetWindow()!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                FolderBox.Text = folder.Path;
                AppSettings.Current.ReportFolder = folder.Path;
                await AppSettings.SaveAsync();
                FolderInfo.Text = $"Will save to: {folder.Path}";
            }
        }
        catch (Exception ex) { AppLogger.LogException(ex, "BrowseReportFolder"); }
    }

    private async void OnGenerate(object sender, RoutedEventArgs e)
    {
        try
        {
            string format = "TXT";
            if (FormatGroup.SelectedItem is RadioButton rb && rb.Tag is string tag) format = tag;
            else if (FormatGroup.SelectedIndex >=0)
            {
                // fallback scan
                var items = FormatGroup.Items.Cast<RadioButton>().ToList();
                if (FormatGroup.SelectedIndex < items.Count) format = items[FormatGroup.SelectedIndex].Tag?.ToString() ?? "TXT";
            }
            var opts = new ReportOptions
            {
                IncludeOverview = IncludeOverview.IsChecked == true,
                IncludeHardware = IncludeHardware.IsChecked == true,
                IncludeSensors = IncludeSensors.IsChecked == true,
                IncludeAlerts = IncludeAlerts.IsChecked == true,
                Format = format
            };
            if (!opts.IncludeOverview && !opts.IncludeHardware && !opts.IncludeSensors && !opts.IncludeAlerts)
            {
                ResultBar.Severity = InfoBarSeverity.Warning;
                ResultBar.Title = "Select content";
                ResultBar.Message = "Choose at least one section to include.";
                ResultBar.IsOpen = true;
                return;
            }
            // ensure folder exists
            string folder = FolderBox.Text;
            if (string.IsNullOrWhiteSpace(folder)) folder = DefaultFolder();
            Directory.CreateDirectory(folder);
            AppSettings.Current.ReportFolder = folder;
            await AppSettings.SaveAsync();
            string path = await ReportService.GenerateAsync(opts, HardwareService.Instance.Overview, HardwareService.Instance.Tree, SensorService.Instance.Sensors, AppSettings.Current.AlertRules);
            _recent.Insert(0, path);
            if (_recent.Count > 10) _recent.RemoveAt(10);
            ResultBar.Severity = InfoBarSeverity.Success;
            ResultBar.Title = "Report saved";
            ResultBar.Message = path;
            ResultBar.IsOpen = true;
            FolderInfo.Text = $"Last saved: {Path.GetFileName(path)} at {DateTime.Now:HH:mm:ss}";
            RefreshRecent();
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "GenerateReport");
            ResultBar.Severity = InfoBarSeverity.Error;
            ResultBar.Title = "Failed";
            ResultBar.Message = ex.Message;
            ResultBar.IsOpen = true;
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = FolderBox.Text;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) folder = DefaultFolder();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex) { AppLogger.LogException(ex, "OpenFolder"); }
    }

    private void OnRefreshRecent(object sender, RoutedEventArgs e) => RefreshRecent();

    private void RefreshRecent()
    {
        RecentPanel.Children.Clear();
        if (_recent.Count == 0)
        {
            NoRecentText.Visibility = Visibility.Visible;
            // also list files on disk
            try
            {
                string folder = FolderBox.Text;
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                {
                    var files = Directory.GetFiles(folder, "*Report_*.*").OrderByDescending(f => File.GetCreationTime(f)).Take(5).ToList();
                    if (files.Count > 0)
                    {
                        NoRecentText.Visibility = Visibility.Collapsed;
                        foreach (var f in files) RecentPanel.Children.Add(BuildRecentRow(f));
                        return;
                    }
                }
            }
            catch { }
            return;
        }
        NoRecentText.Visibility = Visibility.Collapsed;
        foreach (var f in _recent) RecentPanel.Children.Add(BuildRecentRow(f));
        // also show disk files if not in recent
        try
        {
            string folder = FolderBox.Text;
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                var diskFiles = Directory.GetFiles(folder, "*Report_*.*").OrderByDescending(f => File.GetCreationTime(f)).Take(5);
                foreach (var f in diskFiles) if (!_recent.Contains(f)) RecentPanel.Children.Add(BuildRecentRow(f));
            }
        }
        catch { }
    }

    private FrameworkElement BuildRecentRow(string path)
    {
        var grid = new Grid { ColumnSpacing = 8, Padding=new Thickness(6,4,6,4), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var tb = new TextBlock { Text = $"{Path.GetFileName(path)}  —  {new FileInfo(path).Length/1024} KB  —  {File.GetCreationTime(path):yyyy-MM-dd HH:mm}", FontSize=14, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardTextBrush"], TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var btn = new Button { Content = "Open", FontSize=14, Padding=new Thickness(8,4,8,4) };
        btn.Click += (s,e)=> { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{ FileName=path, UseShellExecute=true}); } catch{} };
        Grid.SetColumn(tb,0); Grid.SetColumn(btn,1);
        grid.Children.Add(tb); grid.Children.Add(btn);
        var border = new Border { Child=grid, BorderBrush=(Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerBrush"], BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(6), Margin=new Thickness(0,2,0,2) };
        // make clickable to open folder
        border.Tapped += (s,e)=> { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{ FileName=path, UseShellExecute=true}); } catch{} };
        return border;
    }
}
