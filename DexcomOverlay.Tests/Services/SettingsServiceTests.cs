using DexcomOverlay.Models;
using DexcomOverlay.Services;

namespace DexcomOverlay.Tests.Services;

public class SettingsServiceTests
{
    // ── Region validation ──────────────────────────────────────

    [Theory]
    [InlineData("us")]
    [InlineData("ous")]
    [InlineData("jp")]
    [InlineData("US")]   // case-insensitive
    [InlineData("OUS")]
    [InlineData("Jp")]
    public void Sanitize_ValidRegion_IsPreserved(string region)
    {
        var settings = new AppSettings { Region = region };
        SettingsService.Sanitize(settings);
        Assert.Equal(region, settings.Region);
    }

    [Theory]
    [InlineData("")]
    [InlineData("eu")]
    [InlineData("invalid")]
    [InlineData("china")]
    public void Sanitize_InvalidRegion_DefaultsToUs(string region)
    {
        var settings = new AppSettings { Region = region };
        SettingsService.Sanitize(settings);
        Assert.Equal("us", settings.Region);
    }

    // ── Refresh interval clamping ──────────────────────────────

    [Theory]
    [InlineData(1, 30)]     // below min → clamped to 30
    [InlineData(29, 30)]    // just below min → 30
    [InlineData(30, 30)]    // at min → kept
    [InlineData(60, 60)]    // normal → kept
    [InlineData(600, 600)]  // at max → kept
    [InlineData(601, 600)]  // above max → clamped to 600
    [InlineData(9999, 600)] // way above max → 600
    [InlineData(-5, 30)]    // negative → clamped to 30
    public void Sanitize_RefreshInterval_Clamped(int input, int expected)
    {
        var settings = new AppSettings { RefreshIntervalSeconds = input };
        SettingsService.Sanitize(settings);
        Assert.Equal(expected, settings.RefreshIntervalSeconds);
    }

    // ── Font size clamping ─────────────────────────────────────

    [Theory]
    [InlineData(1, 12)]
    [InlineData(12, 12)]
    [InlineData(48, 48)]
    [InlineData(120, 120)]
    [InlineData(200, 120)]
    public void Sanitize_FontSize_Clamped(int input, int expected)
    {
        var settings = new AppSettings { FontSize = input };
        SettingsService.Sanitize(settings);
        Assert.Equal(expected, settings.FontSize);
    }

    // ── Opacity clamping ───────────────────────────────────────

    [Theory]
    [InlineData(0.0, 0.1)]
    [InlineData(0.05, 0.1)]
    [InlineData(0.1, 0.1)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.0)]
    public void Sanitize_Opacity_Clamped(double input, double expected)
    {
        var settings = new AppSettings { Opacity = input };
        SettingsService.Sanitize(settings);
        Assert.Equal(expected, settings.Opacity, precision: 2);
    }

    // ── Alert cooldown clamping ────────────────────────────────

    [Theory]
    [InlineData(1, 5)]
    [InlineData(5, 5)]
    [InlineData(15, 15)]
    [InlineData(60, 60)]
    [InlineData(100, 60)]
    public void Sanitize_AlertCooldown_Clamped(int input, int expected)
    {
        var settings = new AppSettings { AlertCooldownMinutes = input };
        SettingsService.Sanitize(settings);
        Assert.Equal(expected, settings.AlertCooldownMinutes);
    }

    // ── Threshold sanitization ─────────────────────────────────

    [Fact]
    public void Sanitize_DefaultThresholds_Unchanged()
    {
        var settings = new AppSettings();
        SettingsService.Sanitize(settings);

        Assert.Equal(55, settings.Thresholds.UrgentLow);
        Assert.Equal(70, settings.Thresholds.Low);
        Assert.Equal(180, settings.Thresholds.High);
        Assert.Equal(250, settings.Thresholds.UrgentHigh);
    }

    [Fact]
    public void Sanitize_ThresholdsOutOfPhysiologicalRange_Clamped()
    {
        var settings = new AppSettings
        {
            Thresholds = new GlucoseThresholds
            {
                UrgentLow = 5,    // below min 20
                Low = 10,         // below UrgentLow+1
                High = 500,       // above max 400
                UrgentHigh = 600, // above max 500
            },
        };

        SettingsService.Sanitize(settings);

        // UrgentLow clamped to 20 (min)
        Assert.Equal(20, settings.Thresholds.UrgentLow);
        // Low must be >= UrgentLow+1 = 21
        Assert.True(settings.Thresholds.Low >= settings.Thresholds.UrgentLow + 1);
        // High must be <= 400 and >= Low+1
        Assert.InRange(settings.Thresholds.High, settings.Thresholds.Low + 1, 400);
        // UrgentHigh must be <= 500 and >= High+1
        Assert.InRange(settings.Thresholds.UrgentHigh, settings.Thresholds.High + 1, 500);
    }

    [Fact]
    public void Sanitize_ThresholdsInverted_CorrectedToAscendingOrder()
    {
        var settings = new AppSettings
        {
            Thresholds = new GlucoseThresholds
            {
                UrgentLow = 200,
                Low = 50,
                High = 30,
                UrgentHigh = 20,
            },
        };

        SettingsService.Sanitize(settings);

        // After sanitization, thresholds must be strictly ascending
        Assert.True(settings.Thresholds.UrgentLow < settings.Thresholds.Low,
            $"UrgentLow ({settings.Thresholds.UrgentLow}) should be < Low ({settings.Thresholds.Low})");
        Assert.True(settings.Thresholds.Low < settings.Thresholds.High,
            $"Low ({settings.Thresholds.Low}) should be < High ({settings.Thresholds.High})");
        Assert.True(settings.Thresholds.High < settings.Thresholds.UrgentHigh,
            $"High ({settings.Thresholds.High}) should be < UrgentHigh ({settings.Thresholds.UrgentHigh})");
    }

    // ── Sanitize preserves within-range values ─────────────────

    [Fact]
    public void Sanitize_ValidSettings_NotMutated()
    {
        var settings = new AppSettings
        {
            Region = "ous",
            RefreshIntervalSeconds = 120,
            FontSize = 36,
            Opacity = 0.8,
            AlertCooldownMinutes = 30,
            Thresholds = new GlucoseThresholds
            {
                UrgentLow = 55,
                Low = 70,
                High = 180,
                UrgentHigh = 250,
            },
        };

        SettingsService.Sanitize(settings);

        Assert.Equal("ous", settings.Region);
        Assert.Equal(120, settings.RefreshIntervalSeconds);
        Assert.Equal(36, settings.FontSize);
        Assert.Equal(0.8, settings.Opacity);
        Assert.Equal(30, settings.AlertCooldownMinutes);
        Assert.Equal(55, settings.Thresholds.UrgentLow);
        Assert.Equal(70, settings.Thresholds.Low);
        Assert.Equal(180, settings.Thresholds.High);
        Assert.Equal(250, settings.Thresholds.UrgentHigh);
    }
}
