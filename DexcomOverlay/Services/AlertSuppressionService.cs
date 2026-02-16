using System.Diagnostics;
using DexcomOverlay.Models;

namespace DexcomOverlay.Services;

/// <summary>
/// Manages alert suppression state — timer-based and schedule-based,
/// both globally and per alert type.
/// </summary>
public class AlertSuppressionService
{
    private readonly AppSettings _settings;

    public AlertSuppressionService(AppSettings settings)
    {
        _settings = settings;
    }

    // ── Query ──────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the given alert type is currently suppressed
    /// (either by global rules or per-type rules).
    /// </summary>
    public bool IsAlertSuppressed(AlertType type)
    {
        var suppression = _settings.Suppression;
        if (suppression is null) return false;

        // Global suppression takes priority
        if (suppression.Global is not null && IsRuleSuppressing(suppression.Global))
            return true;

        // Per-type suppression
        var key = type.ToString();
        if (suppression.PerType is not null &&
            suppression.PerType.TryGetValue(key, out var rule))
            return IsRuleSuppressing(rule);

        return false;
    }

    /// <summary>
    /// Returns true if any alerts are currently suppressed (for the UI indicator).
    /// Also cleans up expired timers automatically.
    /// </summary>
    public bool IsAnySuppressed
    {
        get
        {
            var suppression = _settings.Suppression;
            if (suppression is null) return false;

            // Clean up expired timers so stale data doesn't persist
            CleanupExpiredTimers();

            if (suppression.Global is not null && IsRuleSuppressing(suppression.Global))
                return true;

            if (suppression.PerType is not null)
            {
                foreach (var kvp in suppression.PerType)
                {
                    if (IsRuleSuppressing(kvp.Value))
                        return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Returns a human-readable summary of active suppressions.
    /// </summary>
    public List<string> GetActiveSuppressionSummary()
    {
        var lines = new List<string>();
        var suppression = _settings.Suppression;
        if (suppression is null) return lines;

        if (suppression.Global is not null && IsRuleSuppressing(suppression.Global))
            lines.Add($"All alerts: {DescribeRule(suppression.Global)}");

        if (suppression.PerType is not null)
        {
            foreach (var kvp in suppression.PerType)
            {
                if (IsRuleSuppressing(kvp.Value))
                    lines.Add($"{kvp.Key}: {DescribeRule(kvp.Value)}");
            }
        }

        return lines;
    }

    // ── Global suppression actions ─────────────────────────────

    /// <summary>Suppress all alerts for the given duration. Null = indefinite.</summary>
    public void SuppressAll(TimeSpan? duration)
    {
        _settings.Suppression.Global.SuppressUntil = duration.HasValue
            ? DateTime.UtcNow + duration.Value
            : DateTime.MaxValue;
        Save();
    }

    /// <summary>Clear global timer suppression.</summary>
    public void ClearGlobalSuppression()
    {
        _settings.Suppression.Global.SuppressUntil = null;
        Save();
    }

    // ── Per-type suppression actions ───────────────────────────

    /// <summary>Suppress a specific alert type for the given duration. Null = indefinite.</summary>
    public void SuppressType(AlertType type, TimeSpan? duration)
    {
        var key = type.ToString();
        if (!_settings.Suppression.PerType.TryGetValue(key, out var rule))
        {
            rule = new SuppressionRule();
            _settings.Suppression.PerType[key] = rule;
        }

        rule.SuppressUntil = duration.HasValue
            ? DateTime.UtcNow + duration.Value
            : DateTime.MaxValue;
        Save();
    }

    /// <summary>Clear timer suppression for a specific alert type.</summary>
    public void ClearTypeSuppression(AlertType type)
    {
        var key = type.ToString();
        if (_settings.Suppression.PerType.TryGetValue(key, out var rule))
        {
            rule.SuppressUntil = null;
            // If no schedule either, remove the entry entirely
            if (rule.Schedule is null || !rule.Schedule.Enabled)
                _settings.Suppression.PerType.Remove(key);
            Save();
        }
    }

    /// <summary>Clear all suppressions (global + per-type timers).</summary>
    public void ClearAll()
    {
        _settings.Suppression.Global.SuppressUntil = null;
        foreach (var kvp in _settings.Suppression.PerType.ToList())
        {
            kvp.Value.SuppressUntil = null;
            if (kvp.Value.Schedule is null || !kvp.Value.Schedule.Enabled)
                _settings.Suppression.PerType.Remove(kvp.Key);
        }
        Save();
    }

    // ── Schedule management ────────────────────────────────────

    public void SetGlobalSchedule(ScheduleSuppression schedule)
    {
        _settings.Suppression.Global.Schedule = schedule;
        Save();
    }

    public void SetTypeSchedule(AlertType type, ScheduleSuppression schedule)
    {
        var key = type.ToString();
        if (!_settings.Suppression.PerType.TryGetValue(key, out var rule))
        {
            rule = new SuppressionRule();
            _settings.Suppression.PerType[key] = rule;
        }
        rule.Schedule = schedule;
        Save();
    }

    // ── Internal helpers ───────────────────────────────────────

    /// <summary>
    /// Removes expired timer suppressions from the config so they don't persist forever.
    /// </summary>
    private void CleanupExpiredTimers()
    {
        var suppression = _settings.Suppression;
        if (suppression is null) return;

        bool changed = false;

        // Clean global timer
        if (suppression.Global is not null &&
            suppression.Global.SuppressUntil.HasValue &&
            suppression.Global.SuppressUntil.Value != DateTime.MaxValue &&
            DateTime.UtcNow >= suppression.Global.SuppressUntil.Value)
        {
            suppression.Global.SuppressUntil = null;
            changed = true;
        }

        // Clean per-type timers
        if (suppression.PerType is not null)
        {
            var keysToRemove = new List<string>();
            foreach (var kvp in suppression.PerType)
            {
                var rule = kvp.Value;
                if (rule.SuppressUntil.HasValue &&
                    rule.SuppressUntil.Value != DateTime.MaxValue &&
                    DateTime.UtcNow >= rule.SuppressUntil.Value)
                {
                    rule.SuppressUntil = null;
                    changed = true;

                    // If no schedule either, mark for removal
                    if (rule.Schedule is null || !rule.Schedule.Enabled)
                        keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
                suppression.PerType.Remove(key);
        }

        if (changed) Save();
    }

    internal static bool IsRuleSuppressing(SuppressionRule rule)
    {
        // Timer suppression
        if (rule.SuppressUntil.HasValue)
        {
            if (rule.SuppressUntil.Value == DateTime.MaxValue)
                return true; // indefinite
            if (DateTime.UtcNow < rule.SuppressUntil.Value)
                return true; // still within timer
        }

        // Schedule suppression
        if (rule.Schedule is { Enabled: true })
        {
            if (IsWithinSchedule(rule.Schedule))
                return true;
        }

        return false;
    }

    internal static bool IsWithinSchedule(ScheduleSuppression sched)
    {
        var now = DateTime.Now; // local time for schedule comparison

        // Check day-of-week filter
        if (sched.Days.Count > 0 && !sched.Days.Contains((int)now.DayOfWeek))
            return false;

        if (!TimeSpan.TryParse(sched.StartTime, out var start) ||
            !TimeSpan.TryParse(sched.EndTime, out var end))
            return false;

        var current = now.TimeOfDay;

        // Handle overnight spans (e.g., 22:00 → 07:00)
        if (start <= end)
            return current >= start && current < end;
        else
            return current >= start || current < end;
    }

    private static string DescribeRule(SuppressionRule rule)
    {
        var parts = new List<string>();

        if (rule.SuppressUntil.HasValue)
        {
            if (rule.SuppressUntil.Value == DateTime.MaxValue)
                parts.Add("suppressed indefinitely");
            else
            {
                var remaining = rule.SuppressUntil.Value - DateTime.UtcNow;
                if (remaining.TotalMinutes > 0)
                    parts.Add($"suppressed for {remaining.TotalMinutes:F0} more minutes");
                else
                    parts.Add("timer expired");
            }
        }

        if (rule.Schedule is { Enabled: true })
            parts.Add($"scheduled {rule.Schedule.StartTime}–{rule.Schedule.EndTime}");

        return parts.Count > 0 ? string.Join("; ", parts) : "active";
    }

    private void Save()
    {
        try
        {
            SettingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DexcomOverlay] Failed to save suppression settings: {ex.Message}");
        }
    }
}
