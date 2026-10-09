namespace NINA.TargetHistory.Models;

public sealed class UserMetadata {
    public Dictionary<string, TargetMetadata> Targets { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TargetMetadata {
    public string? ProfileId { get; set; }
    public string? ProfileName { get; set; }
    public double SensorWidthPixels { get; set; }
    public double SensorHeightPixels { get; set; }
    public double PixelSizeMicrons { get; set; }
    public double FocalLengthMm { get; set; }
    public bool Finished { get; set; }
    public string? AstroBinUrl { get; set; }
}
