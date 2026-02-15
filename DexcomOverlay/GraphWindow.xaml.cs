using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DexcomOverlay.Models;
using DexcomOverlay.Services;

namespace DexcomOverlay;

public partial class GraphWindow : Window
{
    private readonly DexcomShareClient _client;
    private readonly AppSettings _settings;
    private List<GlucoseReading> _readings = [];
    private int _currentMinutes = 180; // default 3h
    private readonly Button[] _timeButtons;

    // Graph layout constants
    private const double MarginLeft = 50;
    private const double MarginRight = 20;
    private const double MarginTop = 20;
    private const double MarginBottom = 40;
    private const int MinGlucose = 30;
    private const int MaxGlucose = 400;

    public GraphWindow(DexcomShareClient client, AppSettings settings)
    {
        InitializeComponent();
        _client = client;
        _settings = settings;
        _timeButtons = [Btn1h, Btn3h, Btn6h, Btn12h, Btn24h];

        Loaded += async (_, _) =>
        {
            HighlightTimeButton(180);
            await LoadDataAsync();
        };
    }

    // ── Data Loading ───────────────────────────────────────────

    private async Task LoadDataAsync()
    {
        LoadingLabel.Visibility = Visibility.Visible;
        GraphCanvas.Children.Clear();

        try
        {
            int maxCount = _currentMinutes / 5 + 1; // readings every 5 min
            _readings = await _client.GetGlucoseReadingsAsync(_currentMinutes, maxCount);
            _readings = _readings.OrderBy(r => r.Timestamp).ToList();

            // Update current reading header
            var latest = _readings.LastOrDefault();
            if (latest is not null)
            {
                var color = GetGlucoseColor(latest.Value);
                var brush = new SolidColorBrush(color);

                if (_settings.ShowMmol)
                {
                    CurrentValueLabel.Text = latest.MmolL.ToString("F1");
                    CurrentInfoLabel.Text = $"mmol/L · {latest.TrendDescription}";
                }
                else
                {
                    CurrentValueLabel.Text = latest.Value.ToString();
                    CurrentInfoLabel.Text = $"mg/dL · {latest.TrendDescription}";
                }

                CurrentValueLabel.Foreground = brush;
                CurrentTrendLabel.Text = latest.TrendArrow;
                CurrentTrendLabel.Foreground = brush;
            }

            LoadingLabel.Visibility = Visibility.Collapsed;
            DrawGraph();
        }
        catch (Exception ex)
        {
            LoadingLabel.Text = $"Error: {(ex.Message.Length > 60 ? ex.Message[..60] + "…" : ex.Message)}";
        }
    }

    // ── Graph Drawing ──────────────────────────────────────────

    private void DrawGraph()
    {
        GraphCanvas.Children.Clear();

        double w = GraphCanvas.ActualWidth;
        double h = GraphCanvas.ActualHeight;
        if (w < 100 || h < 100 || _readings.Count == 0) return;

        double plotW = w - MarginLeft - MarginRight;
        double plotH = h - MarginTop - MarginBottom;

        var t = _settings.Thresholds;

        // ── Threshold zone fills ───────────────────────────────
        DrawThresholdZones(plotW, plotH, t);

        // ── Threshold dashed lines + labels ────────────────────
        DrawThresholdLine(t.UrgentLow, "#FF0000", plotW, plotH, $"Urg Low {t.UrgentLow}");
        DrawThresholdLine(t.Low, "#FF8800", plotW, plotH, $"Low {t.Low}");
        DrawThresholdLine(t.High, "#FFCC00", plotW, plotH, $"High {t.High}");
        DrawThresholdLine(t.UrgentHigh, "#FF0000", plotW, plotH, $"Urg Hi {t.UrgentHigh}");

        // ── Time axis ──────────────────────────────────────────
        DrawTimeAxis(plotW, plotH);

        // ── Y axis labels ──────────────────────────────────────
        DrawYAxis(plotH);

        // ── Data points + line ─────────────────────────────────
        DrawDataLine(plotW, plotH);

        // ── Alert markers ──────────────────────────────────────
        DrawAlertMarkers(plotW, plotH, t);
    }

    private void DrawThresholdZones(double plotW, double plotH, GlucoseThresholds t)
    {
        // Urgent low zone (bottom to urgent low)
        DrawZoneRect(MinGlucose, t.UrgentLow, "#18FF0000", plotW, plotH);
        // Low zone
        DrawZoneRect(t.UrgentLow, t.Low, "#18FF8800", plotW, plotH);
        // In-range zone
        DrawZoneRect(t.Low, t.High, "#1000CC66", plotW, plotH);
        // High zone
        DrawZoneRect(t.High, t.UrgentHigh, "#18FFCC00", plotW, plotH);
        // Urgent high zone
        DrawZoneRect(t.UrgentHigh, MaxGlucose, "#18FF0000", plotW, plotH);
    }

    private void DrawZoneRect(int fromVal, int toVal, string colorHex, double plotW, double plotH)
    {
        double y1 = MarginTop + plotH * (1 - (double)(toVal - MinGlucose) / (MaxGlucose - MinGlucose));
        double y2 = MarginTop + plotH * (1 - (double)(fromVal - MinGlucose) / (MaxGlucose - MinGlucose));

        var rect = new Rectangle
        {
            Width = plotW,
            Height = Math.Max(0, y2 - y1),
            Fill = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!,
        };
        Canvas.SetLeft(rect, MarginLeft);
        Canvas.SetTop(rect, y1);
        GraphCanvas.Children.Add(rect);
    }

    private void DrawThresholdLine(int value, string colorHex, double plotW, double plotH, string label)
    {
        double y = MarginTop + plotH * (1 - (double)(value - MinGlucose) / (MaxGlucose - MinGlucose));
        var color = (Color)ColorConverter.ConvertFromString(colorHex);

        var line = new Line
        {
            X1 = MarginLeft, Y1 = y,
            X2 = MarginLeft + plotW, Y2 = y,
            Stroke = new SolidColorBrush(Color.FromArgb(100, color.R, color.G, color.B)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection([4, 4]),
        };
        GraphCanvas.Children.Add(line);

        var text = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromArgb(150, color.R, color.G, color.B)),
        };
        Canvas.SetLeft(text, MarginLeft + 4);
        Canvas.SetTop(text, y - 14);
        GraphCanvas.Children.Add(text);
    }

    private void DrawTimeAxis(double plotW, double plotH)
    {
        var now = _readings.Last().Timestamp;
        var start = now.AddMinutes(-_currentMinutes);

        // Determine tick interval based on window
        int tickMinutes = _currentMinutes switch
        {
            <= 60 => 10,
            <= 180 => 30,
            <= 360 => 60,
            <= 720 => 120,
            _ => 180,
        };

        // Round start to next tick
        var tickTime = new DateTime(start.Year, start.Month, start.Day, start.Hour,
            start.Minute / tickMinutes * tickMinutes, 0).AddMinutes(tickMinutes);

        while (tickTime < now)
        {
            double x = MarginLeft + plotW * (tickTime - start).TotalMinutes / _currentMinutes;

            // Tick line
            var tick = new Line
            {
                X1 = x, Y1 = MarginTop,
                X2 = x, Y2 = MarginTop + plotH,
                Stroke = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                StrokeThickness = 1,
            };
            GraphCanvas.Children.Add(tick);

            // Label
            var label = new TextBlock
            {
                Text = tickTime.ToString("h:mm tt", CultureInfo.InvariantCulture),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            };
            Canvas.SetLeft(label, x - 20);
            Canvas.SetTop(label, MarginTop + plotH + 4);
            GraphCanvas.Children.Add(label);

            tickTime = tickTime.AddMinutes(tickMinutes);
        }
    }

    private void DrawYAxis(double plotH)
    {
        int[] yTicks = [40, 70, 100, 150, 200, 250, 300, 350];
        foreach (var val in yTicks)
        {
            if (val < MinGlucose || val > MaxGlucose) continue;
            double y = MarginTop + plotH * (1 - (double)(val - MinGlucose) / (MaxGlucose - MinGlucose));

            var label = new TextBlock
            {
                Text = val.ToString(),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                TextAlignment = TextAlignment.Right,
                Width = MarginLeft - 8,
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 7);
            GraphCanvas.Children.Add(label);
        }
    }

    private void DrawDataLine(double plotW, double plotH)
    {
        if (_readings.Count < 2) return;

        var now = _readings.Last().Timestamp;
        var start = now.AddMinutes(-_currentMinutes);

        // Draw connecting lines with gradient color
        for (int i = 1; i < _readings.Count; i++)
        {
            var prev = _readings[i - 1];
            var curr = _readings[i];

            double x1 = MarginLeft + plotW * (prev.Timestamp - start).TotalMinutes / _currentMinutes;
            double y1 = MarginTop + plotH * (1 - (double)(Math.Clamp(prev.Value, MinGlucose, MaxGlucose) - MinGlucose) / (MaxGlucose - MinGlucose));
            double x2 = MarginLeft + plotW * (curr.Timestamp - start).TotalMinutes / _currentMinutes;
            double y2 = MarginTop + plotH * (1 - (double)(Math.Clamp(curr.Value, MinGlucose, MaxGlucose) - MinGlucose) / (MaxGlucose - MinGlucose));

            var color = GetGlucoseColor(curr.Value);
            var line = new Line
            {
                X1 = x1, Y1 = y1,
                X2 = x2, Y2 = y2,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            GraphCanvas.Children.Add(line);
        }

        // Draw data points
        foreach (var reading in _readings)
        {
            double x = MarginLeft + plotW * (reading.Timestamp - start).TotalMinutes / _currentMinutes;
            double y = MarginTop + plotH * (1 - (double)(Math.Clamp(reading.Value, MinGlucose, MaxGlucose) - MinGlucose) / (MaxGlucose - MinGlucose));
            var color = GetGlucoseColor(reading.Value);

            var dot = new Ellipse
            {
                Width = 6, Height = 6,
                Fill = new SolidColorBrush(color),
                ToolTip = $"{reading.Value} mg/dL ({reading.MmolL:F1} mmol/L)\n{reading.Timestamp:h:mm tt}\n{reading.TrendArrow} {reading.TrendDescription}",
            };
            Canvas.SetLeft(dot, x - 3);
            Canvas.SetTop(dot, y - 3);
            GraphCanvas.Children.Add(dot);
        }
    }

    private void DrawAlertMarkers(double plotW, double plotH, GlucoseThresholds t)
    {
        if (_readings.Count < 2) return;

        var now = _readings.Last().Timestamp;
        var start = now.AddMinutes(-_currentMinutes);

        for (int i = 1; i < _readings.Count; i++)
        {
            var prev = _readings[i - 1];
            var curr = _readings[i];

            bool triggered = false;
            string reason = "";

            // Urgent low/high
            if (curr.Value <= t.UrgentLow)
            { triggered = true; reason = "URGENT LOW"; }
            else if (curr.Value >= t.UrgentHigh)
            { triggered = true; reason = "URGENT HIGH"; }
            // Predictive low: crossed into low zone while falling
            else if (curr.Value <= t.Low && prev.Value > t.Low && curr.TrendIndex >= 5 && curr.TrendIndex <= 7)
            { triggered = true; reason = "Predicted Low"; }
            // Predictive high: crossed into high zone while rising
            else if (curr.Value >= t.High && prev.Value < t.High && curr.TrendIndex >= 1 && curr.TrendIndex <= 3)
            { triggered = true; reason = "Predicted High"; }

            if (!triggered) continue;

            double x = MarginLeft + plotW * (curr.Timestamp - start).TotalMinutes / _currentMinutes;
            double y = MarginTop + plotH * (1 - (double)(Math.Clamp(curr.Value, MinGlucose, MaxGlucose) - MinGlucose) / (MaxGlucose - MinGlucose));

            // Alert triangle marker
            var marker = new TextBlock
            {
                Text = "▲",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)),
                ToolTip = $"⚠ {reason}\n{curr.Value} mg/dL at {curr.Timestamp:h:mm tt}",
            };
            Canvas.SetLeft(marker, x - 7);
            Canvas.SetTop(marker, y - 22);
            GraphCanvas.Children.Add(marker);

            // Vertical alert line
            var alertLine = new Line
            {
                X1 = x, Y1 = MarginTop,
                X2 = x, Y2 = MarginTop + plotH,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 0xFF, 0x44, 0x44)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection([2, 4]),
            };
            GraphCanvas.Children.Add(alertLine);
        }
    }

    // ── Color Coding ───────────────────────────────────────────

    private Color GetGlucoseColor(int value)
    {
        var t = _settings.Thresholds;
        if (value <= t.UrgentLow) return Color.FromRgb(0xFF, 0x00, 0x00);
        if (value <= t.Low) return Color.FromRgb(0xFF, 0x88, 0x00);
        if (value <= t.High) return Color.FromRgb(0x00, 0xCC, 0x66);
        if (value <= t.UrgentHigh) return Color.FromRgb(0xFF, 0xCC, 0x00);
        return Color.FromRgb(0xFF, 0x00, 0x00);
    }

    // ── Event Handlers ─────────────────────────────────────────

    private async void TimeWindow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out var minutes))
        {
            _currentMinutes = minutes;
            HighlightTimeButton(minutes);
            await LoadDataAsync();
        }
    }

    private void HighlightTimeButton(int activeMinutes)
    {
        foreach (var btn in _timeButtons)
        {
            btn.Style = int.TryParse(btn.Tag?.ToString(), out var m) && m == activeMinutes
                ? (Style)FindResource("ActiveTimeBtn")
                : (Style)FindResource("TimeBtn");
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_readings.Count > 0)
            DrawGraph();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
