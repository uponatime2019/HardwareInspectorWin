using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NewHwInspector.Models;
using NewHwInspector.Services;
using System;
using System.Linq;

namespace NewHwInspector.Views;

public sealed partial class ChartsView : UserControl
{
    public static SensorEntry? RequestedSensor { get; set; }
    private SensorEntry? _selected;
    private bool _loaded;
    private DispatcherTimer? _refreshTimer;

    public ChartsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        SensorCombo.ItemsSource = SensorService.Instance.Sensors;
        if (RequestedSensor != null)
        {
            SensorCombo.SelectedItem = SensorService.Instance.Sensors.FirstOrDefault(s => s.Id == RequestedSensor.Id) ?? SensorService.Instance.Sensors.FirstOrDefault();
            RequestedSensor = null;
        }
        else SensorCombo.SelectedIndex = SensorService.Instance.Sensors.Count > 0 ? 0 : -1;

        AutoFitCheck.IsChecked = AppSettings.Current.AutoFitCharts;
        RangeCombo.SelectedItem = RangeCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == AppSettings.Current.ChartTimeRangeMinutes.ToString()) ?? RangeCombo.Items[1];

        SensorService.Instance.SensorsUpdated += OnSensorsUpdated;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (a,b)=> Redraw();
        _refreshTimer.Start();
        Redraw();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SensorService.Instance.SensorsUpdated -= OnSensorsUpdated;
        _refreshTimer?.Stop();
    }

    private void OnSensorsUpdated(object? s, EventArgs e) => DispatcherQueue.TryEnqueue(Redraw);

    private void OnSensorChanged(object sender, SelectionChangedEventArgs e) => Redraw();
    private void OnRangeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RangeCombo.SelectedItem is ComboBoxItem ci && int.TryParse(ci.Tag?.ToString(), out int v))
        {
            AppSettings.Current.ChartTimeRangeMinutes = v;
            _ = AppSettings.SaveAsync();
        }
        Redraw();
    }
    private void OnAutoFitChanged(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.AutoFitCharts = AutoFitCheck.IsChecked == true;
        _ = AppSettings.SaveAsync();
        Redraw();
    }
    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        try
        {
            if (SensorCombo.SelectedItem is SensorEntry sel) _selected = sel;
            else if (_selected == null) _selected = SensorService.Instance.Sensors.FirstOrDefault();
            if (_selected == null) { EmptyText.Visibility = Visibility.Visible; ClearCanvas(); return; }

            ChartTitle.Text = _selected.Name;
            ChartSubtitle.Text = $"{_selected.Group} • {_selected.Unit} • {_selected.History.Count} samples • {( _selected.IsAvailable? "available":"n/a")}";
            CurText.Text = _selected.DisplayCurrent;
            MinText.Text = _selected.DisplayMin;
            MaxText.Text = _selected.DisplayMax;
            AvgText.Text = _selected.DisplayAverage;

            CurText.Foreground = BrushFor(_selected.Status);
            // axis times
            int range = AppSettings.Current.ChartTimeRangeMinutes;
            LeftTime.Text = DateTime.Now.AddMinutes(-range).ToString("HH:mm:ss");
            CenterTime.Text = DateTime.Now.AddMinutes(-range/2.0).ToString("HH:mm:ss");

            if (_selected.History.Count < 2 || !_selected.IsAvailable)
            {
                EmptyText.Visibility = Visibility.Visible;
                EmptyText.Text = !_selected.IsAvailable ? "Sensor is currently unavailable — no data to chart. Value will appear when hardware exposes this reading." : _selected.History.Count < 2 ? "Collecting history… need at least 2 samples. Monitoring every " + AppSettings.Current.RefreshIntervalSeconds + "s." : "No data";
                ClearCanvas();
                // still draw placeholder
                return;
            }
            EmptyText.Visibility = Visibility.Collapsed;
            DrawChart(_selected);
        }
        catch (Exception ex) { AppLogger.LogException(ex, "ChartRedraw"); }
    }

    private void ClearCanvas()
    {
        ChartCanvas.Children.Clear();
    }

    private void DrawChart(SensorEntry sensor)
    {
        ClearCanvas();
        double w = ChartCanvas.ActualWidth;
        double h = ChartCanvas.ActualHeight;
        if (w < 10 || h < 10) { w = 600; h = 280; }
        double padLeft = 44, padRight = 12, padTop = 16, padBottom = 24;
        double plotW = w - padLeft - padRight;
        double plotH = h - padTop - padBottom;
        if (plotW <= 0 || plotH <= 0) return;

        var values = sensor.History.Select(x => x.Value).ToList();
        double min = values.Min();
        double max = values.Max();
        if (Math.Abs(max - min) < 0.001) { min -= 1; max += 1; }
        if (!AppSettings.Current.AutoFitCharts)
        {
            // fixed range heuristic: for % 0-100, temps 0-100, MHz broaden
            if (sensor.Unit == "%") { min = 0; max = 100; }
            else if (sensor.Unit == "°C" || sensor.Unit=="°F") { min = Math.Min(min, sensor.Unit=="°F"? 0:0); max = Math.Max(max, sensor.Unit=="°F"?212:100); }
            else { double pad = (max-min)*0.1; min -= pad; max += pad; }
        }
        else
        {
            double pad = (max - min) * 0.12;
            if (pad < 0.01) pad = 0.5;
            min -= pad; max += pad;
            if (sensor.Unit == "%") { min = Math.Max(0, min); max = Math.Min(100, max); }
        }

        // Grid lines + labels
        for (int i=0;i<=4;i++)
        {
            double y = padTop + plotH - (i/4.0)*plotH;
            var line = new Line { X1=padLeft, X2=w-padRight, Y1=y, Y2=y, Stroke=new SolidColorBrush(ColorHelper.FromArgb(0x22,0x8B,0x9A,0xB5)), StrokeThickness=1 };
            ChartCanvas.Children.Add(line);
            double val = min + (max-min)*(i/4.0);
            var label = new TextBlock { Text = sensor.Unit=="MHz"? $"{val:N0}" : $"{val:0.0}", FontSize=14, Foreground=(Brush)Application.Current.Resources["SubTextBrush"] };
            Canvas.SetLeft(label, 4); Canvas.SetTop(label, y-7);
            ChartCanvas.Children.Add(label);
        }

        // vertical time grid
        for (int i=1;i<4;i++)
        {
            double x = padLeft + plotW * (i/4.0);
            var vline = new Line { X1=x, X2=x, Y1=padTop, Y2=h-padBottom, Stroke=new SolidColorBrush(ColorHelper.FromArgb(0x18,0x8B,0x9A,0xB5)), StrokeThickness=1, StrokeDashArray=new DoubleCollection{4,4} };
            ChartCanvas.Children.Add(vline);
        }

        // polyline
        var poly = new Polyline
        {
            Stroke = new SolidColorBrush(ColorHelper.FromArgb(0xFF,0x4C,0xC3,0xFF)),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        var fillPoly = new Polyline
        {
            Fill = new SolidColorBrush(ColorHelper.FromArgb(0x33,0x4C,0xC3,0xFF)),
            StrokeThickness = 0
        };
        var points = new PointCollection();
        var fillPoints = new PointCollection();
        int n = values.Count;
        for(int i=0;i<n;i++)
        {
            double x = padLeft + (n==1?0: (i/(double)(n-1))*plotW);
            double norm = (values[i]-min)/(max-min);
            double y = padTop + plotH - norm*plotH;
            points.Add(new Windows.Foundation.Point(x,y));
            fillPoints.Add(new Windows.Foundation.Point(x,y));
        }
        // close fill to bottom
        if (fillPoints.Count>0)
        {
            fillPoints.Add(new Windows.Foundation.Point(padLeft+plotW, padTop+plotH));
            fillPoints.Add(new Windows.Foundation.Point(padLeft, padTop+plotH));
        }
        poly.Points = points;
        // Fill polygon
        if (fillPoints.Count>2)
        {
            var polygon = new Polygon { Points = fillPoints, Fill=new SolidColorBrush(ColorHelper.FromArgb(0x22,0x4C,0xC3,0xFF)), StrokeThickness=0 };
            ChartCanvas.Children.Add(polygon);
        }
        ChartCanvas.Children.Add(poly);

        // Current dot
        if (points.Count>0)
        {
            var last = points.Last();
            var dot = new Ellipse { Width=8, Height=8, Fill=new SolidColorBrush(Colors.White), Stroke=new SolidColorBrush(ColorHelper.FromArgb(0xFF,0x4C,0xC3,0xFF)), StrokeThickness=2 };
            Canvas.SetLeft(dot, last.X-4); Canvas.SetTop(dot, last.Y-4);
            ChartCanvas.Children.Add(dot);
        }

        // Border
        var border = new Rectangle { Width=plotW, Height=plotH, Stroke=(Brush)Application.Current.Resources["DividerBrush"], StrokeThickness=1, RadiusX=4, RadiusY=4, Fill=new SolidColorBrush(Colors.Transparent) };
        Canvas.SetLeft(border, padLeft); Canvas.SetTop(border, padTop);
        ChartCanvas.Children.Add(border);
    }

    private Brush BrushFor(SensorStatus st) => st switch
    {
        SensorStatus.Normal => (Brush)Application.Current.Resources["AccentGreenBrush"],
        SensorStatus.Warning => (Brush)Application.Current.Resources["AccentOrangeBrush"],
        SensorStatus.Critical => (Brush)Application.Current.Resources["AccentRedBrush"],
        _ => (Brush)Application.Current.Resources["SubTextBrush"]
    };

    private void OnClear(object sender, RoutedEventArgs e) { _selected?.History.Clear(); SensorService.Instance.ClearHistory(); Redraw(); }
    private async void OnExportChart(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_selected == null) return;
            var csv = "Time,Value,Unit\n" + string.Join("\n", _selected.History.Select(h=> $"{h.Time:yyyy-MM-dd HH:mm:ss},{h.Value:0.###},{_selected.Unit}"));
            string dir = AppSettings.Current.ReportFolder;
            if (string.IsNullOrWhiteSpace(dir) || !System.IO.Directory.Exists(dir)) dir = ReportService.DefaultReportFolder;
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"Chart_{_selected.Name.Replace(' ','_')}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            await System.IO.File.WriteAllTextAsync(path, csv);
            var dlg = new ContentDialog { Title="Export", Content=$"Chart exported to:\n{path}", CloseButtonText="OK", XamlRoot=XamlRoot };
            await dlg.ShowAsync();
        } catch (Exception ex){ AppLogger.LogException(ex,"ExportChart"); }
    }
}
