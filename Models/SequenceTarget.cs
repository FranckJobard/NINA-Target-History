namespace NINA.TargetHistory.Models;

public sealed class SequenceTarget {
    public required string SourceFile { get; init; }
    public required string Name { get; init; }
    public double RaDegrees { get; init; }
    public double DecDegrees { get; init; }
    public double PositionAngle { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public List<ExposureEntry> Exposures { get; init; } = new();
}
