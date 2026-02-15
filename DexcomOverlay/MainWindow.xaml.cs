using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DexcomOverlay.Models;
using DexcomOverlay.Services;
using Microsoft.Toolkit.Uwp.Notifications;

namespace DexcomOverlay;

public partial class MainWindow : Window
{
    private AppSettings _settings;
    private DexcomShareClient? _client;
    private DispatcherTimer? _timer;
    private CancellationTokenSource? _cts;

    private GlucoseReading? _lastReading;

    // Notification cooldown
    private DateTime _lastLowAlertTime = DateTime.MinValue;
    private DateTime _lastHighAlertTime = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsService.Load();
        ApplySettings();
        Loaded += OnLoaded;
    }

    // ── Lifecycle ──────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Restore saved window position
        if (_settings.WindowX.HasValue && _settings.WindowY.HasValue)
        {
            Left = _settings.WindowX.Value;
            Top = _settings.WindowY.Value;
        }
        else
        {
            Left = SystemParameters.WorkArea.Width - 250;
            Top = 20;
        }

        // Brief highlight so user can find the window
        Activate();
        Focus();

        StartFetching();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SavePosition();
        _cts?.Cancel();
        _timer?.Stop();
        _client?.Dispose();
        base.OnClosing(e);
    }

    // ── Settings ───────────────────────────────────────────────

    private void ApplySettings()
    {
        Opacity = _settings.Opacity;
        GlucoseLabel.FontSize = _settings.FontSize;
        TrendLabel.FontSize = Math.Max(20, _settings.FontSize / 2);
    }

    private void SavePosition()
    {
        _settings.WindowX = (int)Left;
        _settings.WindowY = (int)Top;
        SettingsService.Save(_settings);
    }

    // ── Glucose Fetching ───────────────────────────────────────

    private void StartFetching()
    {
        _timer?.Stop();
        _cts?.Cancel();
        _client?.Dispose();

        if (string.IsNullOrWhiteSpace(_settings.Username) ||
            string.IsNullOrWhiteSpace(_settings.Password))
        {
            ShowNoCredentials();
            return;
        }

        _client = new DexcomShareClient(_settings.Username, _settings.Password, _settings.Region);
        _cts = new CancellationTokenSource();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds)
        };
        _timer.Tick += async (_, _) => await FetchGlucoseAsync();
        _timer.Start();

        // Immediate first fetch
        _ = FetchGlucoseAsync();
    }

    private async Task FetchGlucoseAsync()
    {
        if (_client is null) return;

        try
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00)); // Yellow = fetching

            var reading = await _client.GetCurrentReadingAsync(_cts?.Token ?? CancellationToken.None);

            if (reading is not null)
                UpdateDisplay(reading);
            else
                ShowError("No data");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message.Length > 30 ? ex.Message[..30] + "…" : ex.Message);
        }
    }

    private void UpdateDisplay(GlucoseReading reading)
    {
        _lastReading = reading;
        var color = GetGlucoseColor(reading.Value);

        if (_settings.ShowMmol)
        {
            GlucoseLabel.Text = reading.MmolL.ToString("F1");
            InfoLabel.Text = $"mmol/L  {reading.TrendDescription}";
        }
        else
        {
            GlucoseLabel.Text = reading.Value.ToString();
            InfoLabel.Text = $"mg/dL  {reading.TrendDescription}";
        }

        GlucoseLabel.Foreground = new SolidColorBrush(color);

        if (_settings.ShowTrendArrow)
        {
            TrendLabel.Text = reading.TrendArrow;
            TrendLabel.Foreground = new SolidColorBrush(color);
        }
        else
        {
            TrendLabel.Text = "";
        }

        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0x66)); // Green = ok

        // Check for predictive alerts
        CheckPredictiveAlerts(reading);
    }

    private void ShowError(string message)
    {
        GlucoseLabel.Text = "ERR";
        GlucoseLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
        TrendLabel.Text = "";
        InfoLabel.Text = message;
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00));
    }

    private void ShowNoCredentials()
    {
        GlucoseLabel.Text = "---";
        GlucoseLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        TrendLabel.Text = "";
        InfoLabel.Text = "Right-click \u2192 Settings";
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x00));
    }

    // ── Color Coding ───────────────────────────────────────────

    private Color GetGlucoseColor(int value)
    {
        var t = _settings.Thresholds;
        if (value <= t.UrgentLow) return Color.FromRgb(0xFF, 0x00, 0x00); // Red
        if (value <= t.Low)       return Color.FromRgb(0xFF, 0x88, 0x00); // Orange
        if (value <= t.High)      return Color.FromRgb(0x00, 0xCC, 0x66); // Green
        if (value <= t.UrgentHigh) return Color.FromRgb(0xFF, 0xCC, 0x00); // Yellow
        return Color.FromRgb(0xFF, 0x00, 0x00); // Red
    }

    // ── Minimize ───────────────────────────────────────────────

    private void Minimize_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        WindowState = WindowState.Minimized;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    // ── Graph ──────────────────────────────────────────────────

    private GraphWindow? _graphWindow;

    private void Graph_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenGraph();
    }

    private void Graph_Click(object sender, RoutedEventArgs e)
    {
        OpenGraph();
    }

    private void OpenGraph()
    {
        if (_client is null)
        {
            MessageBox.Show("Set up credentials first (right-click → Settings).",
                "Dexcom Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_graphWindow is { IsLoaded: true })
        {
            _graphWindow.Activate();
            return;
        }

        _graphWindow = new GraphWindow(_client, _settings);
        _graphWindow.Show();
    }

    // ── Predictive Notifications ───────────────────────────────

    private void CheckPredictiveAlerts(GlucoseReading reading)
    {
        if (!_settings.EnablePredictiveAlerts) return;

        var t = _settings.Thresholds;
        var cooldown = TimeSpan.FromMinutes(_settings.AlertCooldownMinutes);
        var now = DateTime.Now;
        var value = reading.Value;
        var trend = reading.TrendIndex;

        // Falling trends: FortyFiveDown(5), SingleDown(6), DoubleDown(7)
        bool isFalling = trend >= 5 && trend <= 7;
        bool isUrgentLow = value <= t.UrgentLow;
        bool isPredictedLow = value <= t.Low + 20 && value > t.UrgentLow && isFalling;

        if ((isUrgentLow || isPredictedLow) && now - _lastLowAlertTime > cooldown)
        {
            _lastLowAlertTime = now;
            var unit = _settings.ShowMmol ? $"{reading.MmolL:F1} mmol/L" : $"{value} mg/dL";
            SendAlert(
                isUrgentLow ? "\u26a0 URGENT LOW" : "\u26a0 Predicted Low",
                isUrgentLow
                    ? $"Glucose is {unit} — take action immediately"
                    : $"Glucose is {unit} and {reading.TrendDescription}");
        }

        // Rising trends: DoubleUp(1), SingleUp(2), FortyFiveUp(3)
        bool isRising = trend >= 1 && trend <= 3;
        bool isUrgentHigh = value >= t.UrgentHigh;
        bool isPredictedHigh = value >= t.High - 20 && value < t.UrgentHigh && isRising;

        if ((isUrgentHigh || isPredictedHigh) && now - _lastHighAlertTime > cooldown)
        {
            _lastHighAlertTime = now;
            var unit = _settings.ShowMmol ? $"{reading.MmolL:F1} mmol/L" : $"{value} mg/dL";
            SendAlert(
                isUrgentHigh ? "\u26a0 URGENT HIGH" : "\u26a0 Predicted High",
                isUrgentHigh
                    ? $"Glucose is {unit} — take action"
                    : $"Glucose is {unit} and {reading.TrendDescription}");
        }
    }

    private void SendAlert(string title, string body)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show();
        }
        catch { /* Notification failed — non-critical */ }
    }

    // ── Drag ───────────────────────────────────────────────────

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        DragMove();
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SavePosition();
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Context menu is handled by WPF automatically
    }

    // ── Context Menu ───────────────────────────────────────────

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings);

        if (dialog.ShowDialog() == true)
        {
            _settings = SettingsService.Load();
            ApplySettings();
            StartFetching();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await FetchGlucoseAsync();
    }

    private void ToggleUnits_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowMmol = !_settings.ShowMmol;
        SettingsService.Save(_settings);
        _ = FetchGlucoseAsync();
    }

    private void Close_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}