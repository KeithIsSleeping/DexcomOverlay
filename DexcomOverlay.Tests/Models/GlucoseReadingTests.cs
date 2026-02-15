using DexcomOverlay.Models;

namespace DexcomOverlay.Tests.Models;

public class GlucoseReadingTests
{
    // ── MmolL conversion ───────────────────────────────────────

    [Theory]
    [InlineData(100, 5.6)]   // 100 mg/dL → 5.6 mmol/L
    [InlineData(180, 10.0)]  // 180 mg/dL → 10.0 mmol/L
    [InlineData(70, 3.9)]    // 70 mg/dL  → 3.9 mmol/L
    [InlineData(55, 3.1)]    // 55 mg/dL  → 3.1 mmol/L
    [InlineData(250, 13.9)]  // 250 mg/dL → 13.9 mmol/L
    [InlineData(0, 0.0)]     // edge: zero
    public void MmolL_ConvertsCorrectly(int mgdl, double expectedMmol)
    {
        var reading = new GlucoseReading { Value = mgdl };
        Assert.Equal(expectedMmol, reading.MmolL);
    }

    // ── Trend arrows ───────────────────────────────────────────

    [Theory]
    [InlineData(0, "")]     // None
    [InlineData(1, "↑↑")]  // DoubleUp
    [InlineData(2, "↑")]   // SingleUp
    [InlineData(3, "↗")]   // FortyFiveUp
    [InlineData(4, "→")]   // Flat
    [InlineData(5, "↘")]   // FortyFiveDown
    [InlineData(6, "↓")]   // SingleDown
    [InlineData(7, "↓↓")]  // DoubleDown
    [InlineData(8, "?")]   // NotComputable
    [InlineData(9, "-")]   // RateOutOfRange
    public void TrendArrow_ReturnsCorrectSymbol(int trendIndex, string expectedArrow)
    {
        var reading = new GlucoseReading { TrendIndex = trendIndex };
        Assert.Equal(expectedArrow, reading.TrendArrow);
    }

    [Fact]
    public void TrendArrow_OutOfRange_ReturnsFallback()
    {
        var reading = new GlucoseReading { TrendIndex = 99 };
        Assert.Equal("→", reading.TrendArrow); // default fallback
    }

    // ── Trend descriptions ─────────────────────────────────────

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "rising quickly")]
    [InlineData(4, "steady")]
    [InlineData(7, "falling quickly")]
    [InlineData(8, "unable to determine")]
    [InlineData(9, "trend unavailable")]
    public void TrendDescription_ReturnsCorrectText(int trendIndex, string expected)
    {
        var reading = new GlucoseReading { TrendIndex = trendIndex };
        Assert.Equal(expected, reading.TrendDescription);
    }

    [Fact]
    public void TrendDescription_OutOfRange_ReturnsFallback()
    {
        var reading = new GlucoseReading { TrendIndex = -1 };
        Assert.Equal("steady", reading.TrendDescription);
    }

    // ── TrendDirectionMap ──────────────────────────────────────

    [Theory]
    [InlineData("None", 0)]
    [InlineData("DoubleUp", 1)]
    [InlineData("SingleUp", 2)]
    [InlineData("FortyFiveUp", 3)]
    [InlineData("Flat", 4)]
    [InlineData("FortyFiveDown", 5)]
    [InlineData("SingleDown", 6)]
    [InlineData("DoubleDown", 7)]
    [InlineData("NotComputable", 8)]
    [InlineData("RateOutOfRange", 9)]
    public void TrendDirectionMap_ContainsAllDirections(string direction, int expectedIndex)
    {
        Assert.True(GlucoseReading.TrendDirectionMap.ContainsKey(direction));
        Assert.Equal(expectedIndex, GlucoseReading.TrendDirectionMap[direction]);
    }

    [Fact]
    public void TrendDirectionMap_IsCaseInsensitive()
    {
        Assert.True(GlucoseReading.TrendDirectionMap.ContainsKey("flat"));
        Assert.True(GlucoseReading.TrendDirectionMap.ContainsKey("FLAT"));
        Assert.True(GlucoseReading.TrendDirectionMap.ContainsKey("Flat"));
    }

    [Fact]
    public void TrendDirectionMap_Has10Entries()
    {
        Assert.Equal(10, GlucoseReading.TrendDirectionMap.Count);
    }

    // ── Default values ─────────────────────────────────────────

    [Fact]
    public void Defaults_AreReasonable()
    {
        var reading = new GlucoseReading();
        Assert.Equal(0, reading.Value);
        Assert.Equal("Flat", reading.TrendDirection);
        Assert.Equal(0, reading.TrendIndex);
    }
}
