using System.Collections.ObjectModel;

namespace NINA.TargetHistory.Models;

public sealed class TargetHistoryItem {
    public required string Name { get; init; }
    public double RaDegrees { get; set; }
    public double DecDegrees { get; set; }
    public double PositionAngle { get; set; }
    public DateTime MostRecentSequenceUtc { get; set; }
    public ObservableCollection<FilterTotal> Filters { get; } = new();
    public double TotalSeconds => Filters.Sum(x => x.Seconds);
    public string TotalDisplay => TimeFormat.Format(TotalSeconds);
    public bool Finished { get; set; }
    public string? AstroBinUrl { get; set; }
}

public sealed class FilterTotal {
    public required string Name { get; init; }
    public double Seconds { get; set; }
    public string Display => TimeFormat.Format(Seconds);
}

public static class TimeFormat {
    public static string Format(double seconds) {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h{ts.Minutes:00}";
        return $"{ts.Minutes}min";
    }
}
