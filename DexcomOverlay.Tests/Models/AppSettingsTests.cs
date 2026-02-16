using DexcomOverlay.Models;

namespace DexcomOverlay.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_HaveExpectedValues()
    {
        var settings = new AppSettings();

        Assert.Equal("", settings.Username);
        Assert.Equal("", settings.Password);
        Assert.Equal("us", settings.Region);
        Assert.Null(settings.WindowX);
        Assert.Null(settings.WindowY);
        Assert.Equal(60, settings.RefreshIntervalSeconds);
        Assert.Equal(48, settings.FontSize);
        Assert.Equal(0.9, settings.Opacity);
        Assert.True(settings.ShowTrendArrow);
        Assert.False(settings.ShowMmol);
        Assert.True(settings.EnablePredictiveAlerts);
        Assert.Equal(15, settings.AlertCooldownMinutes);
        Assert.True(settings.EnableNoDataAlert);
        Assert.Equal(30, settings.NoDataAlertMinutes);
    }

    [Fact]
    public void Thresholds_HaveDefaults()
    {
        var t = new GlucoseThresholds();

        Assert.Equal(55, t.UrgentLow);
        Assert.Equal(70, t.Low);
        Assert.Equal(180, t.High);
        Assert.Equal(250, t.UrgentHigh);
    }

    [Fact]
    public void Thresholds_AreInOrder()
    {
        var t = new GlucoseThresholds();
        Assert.True(t.UrgentLow < t.Low);
        Assert.True(t.Low < t.High);
        Assert.True(t.High < t.UrgentHigh);
    }

    [Fact]
    public void Suppression_HasEmptyDefaults()
    {
        var s = new AlertSuppressionSettings();
        Assert.NotNull(s.Global);
        Assert.Null(s.Global.SuppressUntil);
        Assert.Null(s.Global.Schedule);
        Assert.Empty(s.PerType);
    }

    [Fact]
    public void ScheduleSuppression_HasSensibleDefaults()
    {
        var sched = new ScheduleSuppression();
        Assert.False(sched.Enabled);
        Assert.Equal("22:00", sched.StartTime);
        Assert.Equal("07:00", sched.EndTime);
        Assert.Empty(sched.Days);
    }
}
