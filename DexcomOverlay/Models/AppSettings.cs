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

    public bool EnableNoDataAlert { get; set; } = true;
    public int NoDataAlertMinutes { get; set; } = 30;

    public GlucoseThresholds Thresholds { get; set; } = new();
    public AlertSuppressionSettings Suppression { get; set; } = new();
}

public class GlucoseThresholds
{
    public int UrgentLow { get; set; } = 55;
    public int Low { get; set; } = 70;
    public int High { get; set; } = 180;
    public int UrgentHigh { get; set; } = 250;
}

/// <summary>
/// Defines the types of alerts the overlay can fire.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AlertType
{
    UrgentLow,
    Low,
    PredictedLow,
    High,
    PredictedHigh,
    UrgentHigh,
    NoData,
}

/// <summary>
/// Root settings for alert suppression — both global and per-alert-type.
/// </summary>
public class AlertSuppressionSettings
{
    /// <summary>Global suppression (applies to all alert types).</summary>
    public SuppressionRule Global { get; set; } = new();

    /// <summary>Per-alert-type suppression overrides. Key is the AlertType name.</summary>
    public Dictionary<string, SuppressionRule> PerType { get; set; } = new();
}

/// <summary>
/// A suppression rule with optional timer and/or schedule.
/// </summary>
public class SuppressionRule
{
    /// <summary>If set, alerts are suppressed until this UTC time. DateTime.MaxValue = indefinite.</summary>
    public DateTime? SuppressUntil { get; set; }

    /// <summary>Schedule-based suppression.</summary>
    public ScheduleSuppression? Schedule { get; set; }
}

/// <summary>
/// Suppress alerts during specific hours on specific days.
/// </summary>
public class ScheduleSuppression
{
    public bool Enabled { get; set; }

    /// <summary>Start time of day (local) as "HH:mm".</summary>
    public string StartTime { get; set; } = "22:00";

    /// <summary>End time of day (local) as "HH:mm".</summary>
    public string EndTime { get; set; } = "07:00";

    /// <summary>Days of week (0=Sunday..6=Saturday). Empty = every day.</summary>
    public List<int> Days { get; set; } = new();
}
