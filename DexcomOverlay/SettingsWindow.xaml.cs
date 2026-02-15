using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DexcomOverlay.Models;
using DexcomOverlay.Services;

namespace DexcomOverlay;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        PopulateFields();
    }

    private void PopulateFields()
    {
        UsernameBox.Text = _settings.Username;
        PasswordBox.Password = _settings.Password;

        // Set region combo
        foreach (ComboBoxItem item in RegionCombo.Items)
        {
            if (item.Tag?.ToString() == _settings.Region)
            {
                RegionCombo.SelectedItem = item;
                break;
            }
        }

        RefreshBox.Text = _settings.RefreshIntervalSeconds.ToString();
        FontSizeBox.Text = _settings.FontSize.ToString();
        OpacityBox.Text = _settings.Opacity.ToString("F1", CultureInfo.InvariantCulture);

        UrgLowBox.Text = _settings.Thresholds.UrgentLow.ToString();
        LowBox.Text = _settings.Thresholds.Low.ToString();
        HighBox.Text = _settings.Thresholds.High.ToString();
        UrgHighBox.Text = _settings.Thresholds.UrgentHigh.ToString();

        AlertsCheckBox.IsChecked = _settings.EnablePredictiveAlerts;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.Username = UsernameBox.Text.Trim();
        _settings.Password = PasswordBox.Password.Trim();
        _settings.Region = (RegionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "us";

        if (int.TryParse(RefreshBox.Text, out var refresh))
            _settings.RefreshIntervalSeconds = Math.Max(30, refresh);

        if (int.TryParse(FontSizeBox.Text, out var fontSize))
            _settings.FontSize = Math.Clamp(fontSize, 12, 120);

        if (double.TryParse(OpacityBox.Text, CultureInfo.InvariantCulture, out var opacity))
            _settings.Opacity = Math.Clamp(opacity, 0.1, 1.0);

        if (int.TryParse(UrgLowBox.Text, out var ul)) _settings.Thresholds.UrgentLow = ul;
        if (int.TryParse(LowBox.Text, out var lo)) _settings.Thresholds.Low = lo;
        if (int.TryParse(HighBox.Text, out var hi)) _settings.Thresholds.High = hi;
        if (int.TryParse(UrgHighBox.Text, out var uh)) _settings.Thresholds.UrgentHigh = uh;

        _settings.EnablePredictiveAlerts = AlertsCheckBox.IsChecked == true;

        SettingsService.Save(_settings);
        DialogResult = true;
        Close();
    }
}
