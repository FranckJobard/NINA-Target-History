using System.Collections.ObjectModel;

namespace NINA.TargetHistory.Models;

public sealed class TargetHistoryItem : System.ComponentModel.INotifyPropertyChanged {
    public required string Name { get; init; }
    public double RaDegrees { get; set; }
    public double DecDegrees { get; set; }
    public double PositionAngle { get; set; }
    // Hidden framing data carried by each row. These properties are intentionally
    // not displayed as DataGrid columns; they make each target self-contained.
    public double ProfileSensorWidthPixels { get; set; }
    public double ProfileSensorHeightPixels { get; set; }
    public double ProfilePixelSizeMicrons { get; set; }
    public double ProfileFocalLengthMm { get; set; }
    public double FieldWidthDegrees { get; set; }
    public double FieldHeightDegrees { get; set; }
    public DateTime MostRecentSequenceUtc { get; set; }
    public ObservableCollection<FilterTotal> Filters { get; } = new();
    public double TotalSeconds => Filters.Sum(x => x.Seconds);
    public string TotalDisplay => TimeFormat.Format(TotalSeconds);
    private bool _finished;
    public bool Finished {
        get => _finished;
        set {
            if (_finished == value) return;
            _finished = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Finished)));
        }
    }
    public string? AstroBinUrl { get; set; }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
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
