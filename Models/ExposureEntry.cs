namespace NINA.TargetHistory.Models;

public sealed record ExposureEntry(
    string Filter,
    int Count,
    double ExposureSeconds,
    double Gain,
    double Offset,
    int BinningX,
    int BinningY) {
    public double TotalSeconds => Count * ExposureSeconds;
}
