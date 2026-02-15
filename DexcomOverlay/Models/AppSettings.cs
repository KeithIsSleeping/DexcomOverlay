using System.Text.Json.Serialization;

namespace DexcomOverlay.Models;

public class AppSettings
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Region { get; set; } = "us"; // "us", "ous", "jp"

    public int? WindowX { get; set; }
    public int? WindowY { get; set; }

    public int RefreshIntervalSeconds { get; set; } = 60;
    public int FontSize { get; set; } = 48;
    public double Opacity { get; set; } = 0.9;
    public bool ShowTrendArrow { get; set; } = true;
    public bool ShowMmol { get; set; } = false;
    public bool EnablePredictiveAlerts { get; set; } = true;
    public int AlertCooldownMinutes { get; set; } = 15;

    public GlucoseThresholds Thresholds { get; set; } = new();
}

public class GlucoseThresholds
{
    public int UrgentLow { get; set; } = 55;
    public int Low { get; set; } = 70;
    public int High { get; set; } = 180;
    public int UrgentHigh { get; set; } = 250;
}
