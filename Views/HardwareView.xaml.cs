using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewHwInspector.Models;
using NewHwInspector.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace NewHwInspector.Views;

public sealed partial class HardwareView : UserControl
{
    private HardwareNode? _selected;
    private ObservableCollection<HardwareNode> _roots = new();

    public HardwareView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (HardwareService.Instance.Tree.Count == 0) await HardwareService.Instance.RefreshAsync();
            BuildTree();
            InfoBar.IsOpen = true;
        }
        catch (Exception ex) { AppLogger.LogException(ex, "HardwareViewLoaded"); }
    }

    private void BuildTree(string? filter = null)
    {
        try
        {
            HwTree.ItemsSource = null;
            _roots = new ObservableCollection<HardwareNode>();
            foreach (var n in HardwareService.Instance.Tree)
            {
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    if (!MatchesFilter(n, filter)) continue;
                    var clone = CloneFiltered(n, filter);
                    if (clone != null) _roots.Add(clone);
                }
                else _roots.Add(n);
            }
            HwTree.ItemsSource = _roots;
            TreeCountText.Text = $"{_roots.Count} top-level nodes • {CountNodes(_roots)} total";
            if (_roots.Count > 0) SelectNode(_roots[0]);
        }
        catch (Exception ex) { AppLogger.LogException(ex, "BuildTree"); }
    }

    private bool MatchesFilter(HardwareNode node, string filter)
    {
        if (node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
        if (node.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var p in node.Properties) if (p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || p.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var c in node.Children) if (MatchesFilter(c, filter)) return true;
        return false;
    }

    private HardwareNode? CloneFiltered(HardwareNode node, string filter)
    {
        bool selfMatch = node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || node.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase) || node.Properties.Any(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || p.Value.Contains(filter, StringComparison.OrdinalIgnoreCase));
        var clone = new HardwareNode { Name = node.Name, Type = node.Type, Icon = node.Icon, Summary = node.Summary, IsExpanded = true };
        foreach (var p in node.Properties) clone.Properties.Add(p);
        foreach (var c in node.Children)
        {
            if (MatchesFilter(c, filter))
            {
                var cc = CloneFiltered(c, filter);
                if (cc != null) clone.Children.Add(cc);
            }
        }
        if (selfMatch || clone.Children.Count > 0) return clone;
        return null;
    }

    private int CountNodes(ObservableCollection<HardwareNode> nodes)
    {
        int cnt = nodes.Count;
        foreach (var n in nodes) cnt += CountNodes(new ObservableCollection<HardwareNode>(n.Children));
        return cnt;
    }

    private void OnTreeInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        try
        {
            if (args.InvokedItem is HardwareNode node) SelectNode(node);
            else if (sender.SelectedItem is HardwareNode sel) SelectNode(sel);
        }
        catch (Exception ex) { AppLogger.LogException(ex, "OnTreeInvoked"); }
    }

    private void OnExpanding(TreeView sender, TreeViewExpandingEventArgs args) { }

    private void SelectNode(HardwareNode node)
    {
        _selected = node;
        DetailTitle.Text = node.Name;
        DetailIcon.Glyph = node.Icon;
        DetailSummary.Text = string.IsNullOrWhiteSpace(node.Summary) ? node.Type : $"{node.Type} — {node.Summary}";
        DetailType.Text = $"{node.Properties.Count} properties • {node.Children.Count} child nodes";

        PropsStack.Children.Clear();
        if (node.Properties.Count > 0)
        {
            PropertiesCard.Visibility = Visibility.Visible;
            foreach (var p in node.Properties)
            {
                var grid = new Grid { ColumnSpacing = 12, Margin = new Thickness(0,2,0,2) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var name = new TextBlock { Text = p.Name, Style = (Style)Application.Current.Resources["SubText"], FontSize = 14, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
                var val = new TextBlock { Text = string.IsNullOrWhiteSpace(p.Value) ? "—" : p.Value, Style = (Style)Application.Current.Resources["CardValueText"], IsTextSelectionEnabled = true, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };
                if (p.Value == "—" || p.Value == "n/a" || p.Value.Contains("Not")) val.Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SubTextBrush"];
                Grid.SetColumn(name,0); Grid.SetColumn(val,1);
                grid.Children.Add(name); grid.Children.Add(val);
                grid.ContextFlyout = BuildCopyFlyout(p.Value);
                PropsStack.Children.Add(grid);
                PropsStack.Children.Add(new Border { Height = 1, Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerBrush"], Opacity = 0.5 });
            }
        }
        else PropertiesCard.Visibility = Visibility.Collapsed;

        ChildrenStack.Children.Clear();
        if (node.Children.Count > 0)
        {
            ChildrenCard.Visibility = Visibility.Visible;
            foreach (var c in node.Children)
            {
                var btn = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8,6,8,6), Margin = new Thickness(0,2,0,2) };
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                sp.Children.Add(new FontIcon { Glyph = c.Icon, FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SubTextBrush"] });
                sp.Children.Add(new TextBlock { Text = c.Name, FontSize = 14, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center });
                if (!string.IsNullOrWhiteSpace(c.Summary)) sp.Children.Add(new TextBlock { Text = $"— {c.Summary}", Style = (Style)Application.Current.Resources["SubText"], FontSize = 14, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center, TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis });
                btn.Content = sp;
                btn.Click += (s,e)=> SelectNode(c);
                ChildrenStack.Children.Add(btn);
            }
        }
        else ChildrenCard.Visibility = Visibility.Collapsed;
    }

    private MenuFlyout BuildCopyFlyout(string value)
    {
        var fly = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = "Copy value" };
        copy.Click += (s,e)=> { var dp = new Windows.ApplicationModel.DataTransfer.DataPackage(); dp.SetText(value); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp); };
        fly.Items.Add(copy);
        return fly;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            string q = SearchBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(q)) BuildTree();
            else BuildTree(q);
        }
        catch (Exception ex) { AppLogger.LogException(ex, "OnSearchChanged"); }
    }

    private void OnExpandAll(object sender, RoutedEventArgs e)
    {
        try { foreach (var n in _roots) n.IsExpanded = true; HwTree.ItemsSource = null; HwTree.ItemsSource = _roots; } catch { }
    }

    private async void OnRefreshHw(object sender, RoutedEventArgs e)
    {
        try { await HardwareService.Instance.RefreshAsync(); BuildTree(SearchBox.Text); } catch (Exception ex) { AppLogger.LogException(ex, "OnRefreshHw"); }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_selected == null) return;
            var text = $"{_selected.Name} [{_selected.Type}] { _selected.Summary}\n" + string.Join("\n", _selected.Properties.Select(p=> $"{p.Name}: {p.Value}")) + "\n" + string.Join("\n", _selected.Children.Select(c=> $"- {c.Name}: {c.Summary}"));
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage(); dp.SetText(text); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        }
        catch (Exception ex) { AppLogger.LogException(ex, "OnCopy"); }
    }
}
