using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using HardwareInspectorWin.Models;
using HardwareInspectorWin.Services;
using System;
using System.Linq;

namespace HardwareInspectorWin.Views;

public sealed partial class AlertsView : UserControl
{
    private bool _loaded;
    private AlertRule? _editing;

    public AlertsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void PreselectSensor(SensorEntry sensor)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await System.Threading.Tasks.Task.Delay(200);
            ShowAddDialog(preselected: sensor);
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        SensorService.Instance.SensorsUpdated += OnSensorsUpdated;
        SensorPicker.ItemsSource = SensorService.Instance.Sensors.Where(s=> s.Unit!="").OrderBy(s=>s.Group).ThenBy(s=>s.Name).ToList();
        SensorPicker.SelectionChanged += (a,b)=> UpdateUnitText();
        Render();
    }

    private void OnSensorsUpdated(object? s, EventArgs e) => DispatcherQueue.TryEnqueue(Render);

    private void Render()
    {
        try
        {
            var rules = AppSettings.Current.AlertRules;
            RulesHost.Children.Clear();
            bool has = rules.Count>0;
            EmptyCard.Visibility = has? Visibility.Collapsed: Visibility.Visible;
            int active = rules.Count(r=>r.IsActive);
            StatusText.Text = $"{rules.Count} rule(s) • {active} active • evaluated every {AppSettings.Current.RefreshIntervalSeconds}s";
            if (active>0)
            {
                ActiveBar.IsOpen = true;
                ActiveBar.Message = $"{active} alert(s) active: " + string.Join(", ", rules.Where(r=>r.IsActive).Select(r=>$"{r.SensorName} {r.Condition} {r.Threshold}{r.Unit}"));
            }
            else ActiveBar.IsOpen = false;

            foreach (var rule in rules)
            {
                var card = BuildRuleCard(rule);
                RulesHost.Children.Add(card);
            }
        }
        catch (Exception ex) { AppLogger.LogException(ex, "AlertsRender"); }
    }

    private Border BuildRuleCard(AlertRule rule)
    {
        var outer = new Border { Style=(Style)Application.Current.Resources["CardBorder"], Background = rule.IsActive? new SolidColorBrush(ColorHelper.FromArgb(0x18,0xE8,0x57,0x4E)) : (Brush)Application.Current.Resources["CardBgBrush"], BorderBrush = rule.IsActive? (Brush)Application.Current.Resources["AccentRedBrush"] : (Brush)Application.Current.Resources["CardBorderBrush"] };
        var grid = new Grid { ColumnSpacing=12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition{ Width=new GridLength(1, GridUnitType.Star)});
        grid.ColumnDefinitions.Add(new ColumnDefinition{ Width=GridLength.Auto});

        var left = new StackPanel { Spacing=4 };
        var titleRow = new StackPanel { Orientation=Orientation.Horizontal, Spacing=8 };
        var icon = new FontIcon { Glyph = rule.IsActive? "\uEA39" : "\uE73E", FontSize=14, Foreground = rule.IsActive? (Brush)Application.Current.Resources["AccentRedBrush"] : (Brush)Application.Current.Resources["AccentGreenBrush"], VerticalAlignment=VerticalAlignment.Center };
        titleRow.Children.Add(icon);
        titleRow.Children.Add(new TextBlock{ Text=$"{rule.Group} / {rule.SensorName}", FontWeight=Microsoft.UI.Text.FontWeights.SemiBold, FontSize=14, Foreground=(Brush)Application.Current.Resources["CardTextBrush"], VerticalAlignment=VerticalAlignment.Center });
        if (!rule.Enabled) titleRow.Children.Add(new Border{ Background=(Brush)Application.Current.Resources["ChipBgBrush"], Padding=new Thickness(6,2,6,2), CornerRadius=new CornerRadius(4), Child=new TextBlock{ Text="Disabled", FontSize=14, Foreground=(Brush)Application.Current.Resources["SubTextBrush"] } });
        if (rule.IsActive) titleRow.Children.Add(new Border{ Background=new SolidColorBrush(ColorHelper.FromArgb(0xFF,0xE8,0x57,0x4E)), Padding=new Thickness(6,2,6,2), CornerRadius=new CornerRadius(4), Child=new TextBlock{ Text="ALERT", FontSize=14, Foreground=new SolidColorBrush(Colors.White), FontWeight=Microsoft.UI.Text.FontWeights.Bold }});
        left.Children.Add(titleRow);
        left.Children.Add(new TextBlock{ Text=$"{rule.Condition} {rule.Threshold} {rule.Unit} {(rule.LastTriggered.HasValue? $"• triggered {rule.LastTriggered:HH:mm:ss}":"")}", Style=(Style)Application.Current.Resources["SubText"], FontSize=14 });
        var sensor = SensorService.Instance.Sensors.FirstOrDefault(s=> s.Id==rule.SensorId || s.Name==rule.SensorName);
        if (sensor != null)
        {
            var cur = new TextBlock{ Text=$"Current: {sensor.DisplayCurrent} (min {sensor.DisplayMin}, max {sensor.DisplayMax}) • {sensor.Status}", FontSize=14, Foreground=(Brush)Application.Current.Resources["SubTextBrush"] };
            left.Children.Add(cur);
        }

        var right = new StackPanel{ Orientation=Orientation.Horizontal, Spacing=6, VerticalAlignment=VerticalAlignment.Center };
        var toggle = new ToggleSwitch{ IsOn=rule.Enabled, MinWidth=0, OnContent="On", OffContent="Off" };
        toggle.Toggled += (a,b)=> { rule.Enabled = toggle.IsOn; _=AppSettings.SaveAsync(); Render(); };
        right.Children.Add(toggle);

        var editBtn = new Button{ Content=new FontIcon{ Glyph="\uE70F", FontSize=12 }, Padding=new Thickness(8,6,8,6) };
        ToolTipService.SetToolTip(editBtn, "Edit");
        editBtn.Click+=(a,b)=> ShowEditDialog(rule);
        right.Children.Add(editBtn);
        var delBtn = new Button{ Content=new FontIcon{ Glyph="\uE74D", FontSize=12 }, Padding=new Thickness(8,6,8,6) };
        ToolTipService.SetToolTip(delBtn, "Delete");
        delBtn.Click+=(a,b)=> { AppSettings.Current.AlertRules.Remove(rule); _=AppSettings.SaveAsync(); Render(); };
        right.Children.Add(delBtn);

        Grid.SetColumn(left,0); Grid.SetColumn(right,1);
        grid.Children.Add(left); grid.Children.Add(right);
        outer.Child = grid;
        return outer;
    }

    private async void OnAddAlert(object sender, RoutedEventArgs e) => ShowAddDialog();

    private async void ShowAddDialog(SensorEntry? preselected=null)
    {
        _editing = null;
        SensorPicker.SelectedItem = preselected ?? SensorPicker.Items.FirstOrDefault();
        ConditionPicker.SelectedIndex = 0;
        ThresholdBox.Value = 80;
        EnabledCheck.IsChecked = true;
        UpdateUnitText();
        var result = await AlertDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (SensorPicker.SelectedItem is not SensorEntry sensor) return;
            var rule = new AlertRule
            {
                SensorId = sensor.Id,
                SensorName = sensor.Name,
                Group = sensor.Group,
                Condition = (ConditionPicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Above",
                Threshold = ThresholdBox.Value,
                Unit = sensor.Unit,
                Enabled = EnabledCheck.IsChecked==true
            };
            AppSettings.Current.AlertRules.Add(rule);
            await AppSettings.SaveAsync();
            Render();
        }
    }

    private async void ShowEditDialog(AlertRule rule)
    {
        _editing = rule;
        var sensor = SensorService.Instance.Sensors.FirstOrDefault(s=> s.Id==rule.SensorId || s.Name==rule.SensorName) ?? SensorService.Instance.Sensors.FirstOrDefault();
        SensorPicker.SelectedItem = sensor;
        ConditionPicker.SelectedItem = ConditionPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(i=> (string?)i.Tag==rule.Condition) ?? ConditionPicker.Items[0];
        ThresholdBox.Value = rule.Threshold;
        EnabledCheck.IsChecked = rule.Enabled;
        UpdateUnitText();
        var result = await AlertDialog.ShowAsync();
        if (result==ContentDialogResult.Primary && SensorPicker.SelectedItem is SensorEntry s)
        {
            rule.SensorId = s.Id;
            rule.SensorName = s.Name;
            rule.Group = s.Group;
            rule.Unit = s.Unit;
            rule.Condition = (ConditionPicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Above";
            rule.Threshold = ThresholdBox.Value;
            rule.Enabled = EnabledCheck.IsChecked==true;
            await AppSettings.SaveAsync();
            Render();
        }
    }

    private void UpdateUnitText()
    {
        if (SensorPicker.SelectedItem is SensorEntry s) UnitText.Text = $"Unit: {s.Unit} • Current: {s.DisplayCurrent}";
        else UnitText.Text = "";
    }

    private async void OnClearAll(object sender, RoutedEventArgs e)
    {
        if (AppSettings.Current.AlertRules.Count==0) return;
        var dlg = new ContentDialog{ Title="Clear all alerts?", Content="This will remove all alert rules.", PrimaryButtonText="Clear", CloseButtonText="Cancel", XamlRoot=XamlRoot };
        var r = await dlg.ShowAsync();
        if (r==ContentDialogResult.Primary) { AppSettings.Current.AlertRules.Clear(); await AppSettings.SaveAsync(); Render(); }
    }
}
