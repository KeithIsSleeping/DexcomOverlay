using System.Text.Json;
using DexcomOverlay.Models;
using DexcomOverlay.Services;

namespace DexcomOverlay.Tests.Services;

public class DexcomShareClientTests
{
    // ── Region validation ──────────────────────────────────────

    [Theory]
    [InlineData("us")]
    [InlineData("ous")]
    [InlineData("jp")]
    public void Constructor_ValidRegion_DoesNotThrow(string region)
    {
        using var client = new DexcomShareClient("user", "pass", region);
        // no exception expected
    }

    [Theory]
    [InlineData("")]
    [InlineData("eu")]
    [InlineData("invalid")]
    [InlineData("china")]
    public void Constructor_InvalidRegion_ThrowsArgumentException(string region)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new DexcomShareClient("user", "pass", region));

        Assert.Contains("Unknown region", ex.Message);
        Assert.Contains(region, ex.Message);
    }

    // ── ParseReading — valid JSON ──────────────────────────────

    [Fact]
    public void ParseReading_ValidJson_ReturnsReading()
    {
        var json = JsonDocument.Parse("""
        {
            "Value": 120,
            "Trend": "Flat",
            "DT": "Date(1691455258000-0400)"
        }
        """).RootElement;

        var reading = DexcomShareClient.ParseReading(json);

        Assert.NotNull(reading);
        Assert.Equal(120, reading.Value);
        Assert.Equal("Flat", reading.TrendDirection);
        Assert.Equal(4, reading.TrendIndex); // Flat = 4
    }

    [Theory]
    [InlineData("DoubleUp", 1)]
    [InlineData("SingleUp", 2)]
    [InlineData("FortyFiveUp", 3)]
    [InlineData("Flat", 4)]
    [InlineData("FortyFiveDown", 5)]
    [InlineData("SingleDown", 6)]
    [InlineData("DoubleDown", 7)]
    [InlineData("NotComputable", 8)]
    [InlineData("RateOutOfRange", 9)]
    public void ParseReading_AllTrendDirections_MapCorrectly(string trend, int expectedIndex)
    {
        var json = JsonDocument.Parse($$"""
        {
            "Value": 100,
            "Trend": "{{trend}}",
            "DT": "Date(1691455258000+0000)"
        }
        """).RootElement;

        var reading = DexcomShareClient.ParseReading(json);

        Assert.NotNull(reading);
        Assert.Equal(expectedIndex, reading.TrendIndex);
        Assert.Equal(trend, reading.TrendDirection);
    }

    [Fact]
    public void ParseReading_UnknownTrend_DefaultsToFlat()
    {
        var json = JsonDocument.Parse("""
        {
            "Value": 100,
            "Trend": "SomeNewTrend",
            "DT": "Date(1691455258000+0000)"
        }
        """).RootElement;

        var reading = DexcomShareClient.ParseReading(json);

        Assert.NotNull(reading);
        Assert.Equal(4, reading.TrendIndex); // defaults to Flat
    }

    // ── ParseReading — timestamp parsing ───────────────────────

    [Fact]
    public void ParseReading_DateFormat_ParsedCorrectly()
    {
        // 1691455258000 ms since epoch = 2023-08-07 23:00:58 UTC
        var json = JsonDocument.Parse("""
        {
            "Value": 95,
            "Trend": "Flat",
            "DT": "Date(1691455258000+0000)"
        }
        """).RootElement;

        var reading = DexcomShareClient.ParseReading(json);

        Assert.NotNull(reading);
        Assert.Equal(2023, reading.Timestamp.Year);
        Assert.Equal(8, reading.Timestamp.Month);
        // 1691455258000 ms = 2023-08-08 01:00:58 UTC
        Assert.Equal(8, reading.Timestamp.Day);
    }

    [Fact]
    public void ParseReading_DateWithNegativeOffset_ParsedCorrectly()
    {
        // Same epoch ms but UTC-4 → local time should be 4 hours earlier
        var json = JsonDocument.Parse("""
        {
            "Value": 95,
            "Trend": "Flat",
            "DT": "Date(1691455258000-0400)"
        }
        """).RootElement;

        var reading = DexcomShareClient.ParseReading(json);

        Assert.NotNull(reading);
        // 1691455258000 ms = 2023-08-08 00:40:58 UTC → 2023-08-07 20:40:58 UTC-4
        Assert.Equal(20, reading.Timestamp.Hour);
    }

    // ── ParseReading — malformed JSON ──────────────────────────

    [Fact]
    public void ParseReading_MissingValueProperty_ReturnsNull()
    {
        var json = JsonDocument.Parse("""
        {
            "Trend": "Flat",
            "DT": "Date(1691455258000+0000)"
        }
        """).RootElement;

        var reading = DexcomShareClient.ParseReading(json);
        Assert.Null(reading);
    }

    [Fact]
    public void ParseReading_EmptyObject_ReturnsNull()
    {
        var json = JsonDocument.Parse("{}").RootElement;

        var reading = DexcomShareClient.ParseReading(json);
        Assert.Null(reading);
    }

    [Fact]
    public void ParseReading_MissingDT_ReturnsNull()
    {
        // DT property missing → GetProperty throws KeyNotFoundException → caught → null
        var json = JsonDocument.Parse("""
        {
            "Value": 110,
            "Trend": "Flat"
        }
        """).RootElement;

        var reading = DexcomShareClient.ParseReading(json);

        Assert.Null(reading);
    }

    // ── ParseReading — edge cases ──────────────────────────────

    [Theory]
    [InlineData(40)]   // urgent low
    [InlineData(400)]  // very high
    [InlineData(0)]    // edge: zero (sensor error)
    public void ParseReading_ExtremeValues_Parses(int value)
    {
        var json = JsonDocument.Parse($$"""
        {
            "Value": {{value}},
            "Trend": "Flat",
            "DT": "Date(1691455258000+0000)"
        }
        """).RootElement;

        var reading = DexcomShareClient.ParseReading(json);

        Assert.NotNull(reading);
        Assert.Equal(value, reading.Value);
    }

    // ── Dispose ────────────────────────────────────────────────

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var client = new DexcomShareClient("user", "pass", "us");
        client.Dispose();
        // no exception expected
    }
}
