using NINA.TargetHistory.Models;

namespace NINA.TargetHistory.Services;

/// <summary>
/// Host-version boundary for N.I.N.A. Framing Assistant integration.
/// Keep this adapter small: N.I.N.A. internal framing APIs can change independently
/// of the history/parser code.
/// </summary>
public interface IFramingNavigationAdapter {
    bool IsAvailable { get; }
    Task OpenAsync(TargetHistoryItem target, CancellationToken cancellationToken = default);
}

public sealed class PendingFramingNavigationAdapter : IFramingNavigationAdapter {
    public bool IsAvailable => false;
    public Task OpenAsync(TargetHistoryItem target, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
