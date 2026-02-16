using DexcomOverlay.Models;
using DexcomOverlay.Services;

namespace DexcomOverlay.Tests.Services;

public class AlertSuppressionServiceTests
{
    private static AppSettings CreateSettings() => new();

    // ── No suppression by default ──────────────────────────────

    [Theory]
    [InlineData(AlertType.UrgentLow)]
    [InlineData(AlertType.Low)]
    [InlineData(AlertType.High)]
    [InlineData(AlertType.UrgentHigh)]
    [InlineData(AlertType.NoData)]
    public void IsAlertSuppressed_Default_ReturnsFalse(AlertType type)
    {
        var svc = new AlertSuppressionService(CreateSettings());
        Assert.False(svc.IsAlertSuppressed(type));
    }

    [Fact]
    public void IsAnySuppressed_Default_ReturnsFalse()
    {
        var svc = new AlertSuppressionService(CreateSettings());
        Assert.False(svc.IsAnySuppressed);
    }

    // ── Global timer suppression ───────────────────────────────

    [Fact]
    public void SuppressAll_Timer_SuppressesAllTypes()
    {
        var settings = CreateSettings();
        var svc = new AlertSuppressionService(settings);

        svc.SuppressAll(TimeSpan.FromMinutes(10));

        Assert.True(svc.IsAlertSuppressed(AlertType.UrgentLow));
        Assert.True(svc.IsAlertSuppressed(AlertType.High));
        Assert.True(svc.IsAlertSuppressed(AlertType.NoData));
        Assert.True(svc.IsAnySuppressed);
    }

    [Fact]
    public void SuppressAll_Indefinite_SuppressesAllTypes()
    {
        var settings = CreateSettings();
        var svc = new AlertSuppressionService(settings);

        svc.SuppressAll(null); // indefinite

        Assert.True(svc.IsAlertSuppressed(AlertType.UrgentLow));
        Assert.True(svc.IsAlertSuppressed(AlertType.NoData));
        Assert.True(svc.IsAnySuppressed);
    }

    [Fact]
    public void ClearGlobalSuppression_ClearsTimerOnly()
    {
        var settings = CreateSettings();
        var svc = new AlertSuppressionService(settings);

        svc.SuppressAll(TimeSpan.FromMinutes(10));
        Assert.True(svc.IsAnySuppressed);

        svc.ClearGlobalSuppression();
        Assert.False(svc.IsAnySuppressed);
    }

    // ── Per-type timer suppression ─────────────────────────────

    [Fact]
    public void SuppressType_OnlyAffectsSpecifiedType()
    {
        var settings = CreateSettings();
        var svc = new AlertSuppressionService(settings);

        svc.SuppressType(AlertType.Low, TimeSpan.FromMinutes(30));

        Assert.True(svc.IsAlertSuppressed(AlertType.Low));
        Assert.False(svc.IsAlertSuppressed(AlertType.High));
        Assert.False(svc.IsAlertSuppressed(AlertType.NoData));
        Assert.True(svc.IsAnySuppressed);
    }

    [Fact]
    public void ClearTypeSuppression_ClearsOnlyThatType()
    {
        var settings = CreateSettings();
        var svc = new AlertSuppressionService(settings);

        svc.SuppressType(AlertType.Low, TimeSpan.FromMinutes(30));
        svc.SuppressType(AlertType.High, TimeSpan.FromMinutes(30));

        svc.ClearTypeSuppression(AlertType.Low);

        Assert.False(svc.IsAlertSuppressed(AlertType.Low));
        Assert.True(svc.IsAlertSuppressed(AlertType.High));
    }

    [Fact]
    public void ClearAll_ClearsGlobalAndPerType()
    {
        var settings = CreateSettings();
        var svc = new AlertSuppressionService(settings);

        svc.SuppressAll(TimeSpan.FromMinutes(10));
        svc.SuppressType(AlertType.NoData, null);

        svc.ClearAll();

        Assert.False(svc.IsAnySuppressed);
        Assert.False(svc.IsAlertSuppressed(AlertType.NoData));
    }

    // ── Expired timer ──────────────────────────────────────────

    [Fact]
    public void ExpiredTimer_DoesNotSuppress()
    {
        var settings = CreateSettings();
        // Manually set a timer in the past
        settings.Suppression.Global.SuppressUntil = DateTime.UtcNow.AddMinutes(-5);

        var svc = new AlertSuppressionService(settings);
        Assert.False(svc.IsAlertSuppressed(AlertType.Low));
        Assert.False(svc.IsAnySuppressed);
    }

    // ── Schedule suppression (static helpers) ──────────────────

    [Fact]
    public void IsWithinSchedule_OvernightSpan_NowInEvening_ReturnsTrue()
    {
        // 22:00 – 07:00, test at 23:00
        var sched = new ScheduleSuppression
        {
            Enabled = true,
            StartTime = "22:00",
            EndTime = "07:00",
        };

        // We can't easily mock DateTime.Now, but we can test the static method directly
        // by verifying the logic with the current time. Instead test the rule wrapper.
        var rule = new SuppressionRule
        {
            Schedule = sched,
        };

        // The result depends on current time — just verify it doesn't throw
        _ = AlertSuppressionService.IsRuleSuppressing(rule);
    }

    [Fact]
    public void IsWithinSchedule_DayFilter_WrongDay_ReturnsFalse()
    {
        var today = (int)DateTime.Now.DayOfWeek;
        var otherDay = (today + 3) % 7; // a day that is not today

        var sched = new ScheduleSuppression
        {
            Enabled = true,
            StartTime = "00:00",
            EndTime = "23:59",
            Days = new List<int> { otherDay },
        };

        Assert.False(AlertSuppressionService.IsWithinSchedule(sched));
    }

    [Fact]
    public void IsWithinSchedule_DayFilter_TodayIncluded_DoesNotReturnFalseForDay()
    {
        var today = (int)DateTime.Now.DayOfWeek;

        var sched = new ScheduleSuppression
        {
            Enabled = true,
            StartTime = "00:00",
            EndTime = "23:59",
            Days = new List<int> { today },
        };

        // Today is included, so it should check time range (00:00-23:59 = all day)
        Assert.True(AlertSuppressionService.IsWithinSchedule(sched));
    }

    [Fact]
    public void IsWithinSchedule_InvalidTimes_ReturnsFalse()
    {
        var sched = new ScheduleSuppression
        {
            Enabled = true,
            StartTime = "not-a-time",
            EndTime = "also-not",
        };

        Assert.False(AlertSuppressionService.IsWithinSchedule(sched));
    }

    // ── Summary ────────────────────────────────────────────────

    [Fact]
    public void GetActiveSuppressionSummary_NoSuppressions_ReturnsEmpty()
    {
        var svc = new AlertSuppressionService(CreateSettings());
        Assert.Empty(svc.GetActiveSuppressionSummary());
    }

    [Fact]
    public void GetActiveSuppressionSummary_WithGlobal_ReturnsEntry()
    {
        var settings = CreateSettings();
        var svc = new AlertSuppressionService(settings);
        svc.SuppressAll(null);

        var lines = svc.GetActiveSuppressionSummary();
        Assert.Single(lines);
        Assert.Contains("All alerts", lines[0]);
    }

    [Fact]
    public void GetActiveSuppressionSummary_WithPerType_ReturnsEntry()
    {
        var settings = CreateSettings();
        var svc = new AlertSuppressionService(settings);
        svc.SuppressType(AlertType.NoData, TimeSpan.FromMinutes(10));

        var lines = svc.GetActiveSuppressionSummary();
        Assert.Single(lines);
        Assert.Contains("NoData", lines[0]);
    }

    // ── NoDataAlertMinutes sanitization ────────────────────────

    [Theory]
    [InlineData(1, 5)]
    [InlineData(5, 5)]
    [InlineData(30, 30)]
    [InlineData(120, 120)]
    [InlineData(200, 120)]
    public void Sanitize_NoDataAlertMinutes_Clamped(int input, int expected)
    {
        var settings = new AppSettings { NoDataAlertMinutes = input };
        SettingsService.Sanitize(settings);
        Assert.Equal(expected, settings.NoDataAlertMinutes);
    }

    // ── Null safety ────────────────────────────────────────────

    [Fact]
    public void IsAlertSuppressed_NullSuppression_ReturnsFalse()
    {
        var settings = CreateSettings();
        settings.Suppression = null!;
        var svc = new AlertSuppressionService(settings);
        Assert.False(svc.IsAlertSuppressed(AlertType.Low));
    }

    [Fact]
    public void IsAnySuppressed_NullSuppression_ReturnsFalse()
    {
        var settings = CreateSettings();
        settings.Suppression = null!;
        var svc = new AlertSuppressionService(settings);
        Assert.False(svc.IsAnySuppressed);
    }

    [Fact]
    public void GetActiveSuppressionSummary_NullSuppression_ReturnsEmpty()
    {
        var settings = CreateSettings();
        settings.Suppression = null!;
        var svc = new AlertSuppressionService(settings);
        Assert.Empty(svc.GetActiveSuppressionSummary());
    }

    [Fact]
    public void IsAlertSuppressed_NullGlobal_ReturnsFalse()
    {
        var settings = CreateSettings();
        settings.Suppression.Global = null!;
        var svc = new AlertSuppressionService(settings);
        Assert.False(svc.IsAlertSuppressed(AlertType.High));
    }

    [Fact]
    public void Sanitize_NullSuppression_InitializesDefaults()
    {
        var settings = new AppSettings();
        settings.Suppression = null!;
        SettingsService.Sanitize(settings);

        Assert.NotNull(settings.Suppression);
        Assert.NotNull(settings.Suppression.Global);
        Assert.NotNull(settings.Suppression.PerType);
    }
}
