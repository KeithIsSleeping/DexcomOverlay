namespace DexcomOverlay.Models;

public class GlucoseReading
{
    public int Value { get; set; }
    public double MmolL => Math.Round(Value * 0.0555, 1);
    public string TrendDirection { get; set; } = "Flat";
    public int TrendIndex { get; set; }
    public string TrendArrow => TrendArrows.ElementAtOrDefault(TrendIndex) ?? "→";
    public string TrendDescription => TrendDescriptions.ElementAtOrDefault(TrendIndex) ?? "steady";
    public DateTime Timestamp { get; set; }

    private static readonly string[] TrendArrows =
        ["", "↑↑", "↑", "↗", "→", "↘", "↓", "↓↓", "?", "-"];

    private static readonly string[] TrendDescriptions =
        ["", "rising quickly", "rising", "rising slightly", "steady",
         "falling slightly", "falling", "falling quickly",
         "unable to determine", "trend unavailable"];

    public static readonly Dictionary<string, int> TrendDirectionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["None"] = 0,
        ["DoubleUp"] = 1,
        ["SingleUp"] = 2,
        ["FortyFiveUp"] = 3,
        ["Flat"] = 4,
        ["FortyFiveDown"] = 5,
        ["SingleDown"] = 6,
        ["DoubleDown"] = 7,
        ["NotComputable"] = 8,
        ["RateOutOfRange"] = 9,
    };
}
