namespace NINA.TargetHistory.Models;

public sealed class UserMetadata {
    public Dictionary<string, TargetMetadata> Targets { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TargetMetadata {
    public bool Finished { get; set; }
    public string? AstroBinUrl { get; set; }
}
