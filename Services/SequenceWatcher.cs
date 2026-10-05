namespace NINA.TargetHistory.Services;

public sealed class SequenceWatcher : IDisposable {
    private readonly FileSystemWatcher _watcher;
    private readonly System.Timers.Timer _debounce;
    public event EventHandler? Changed;

    public SequenceWatcher(string folder) {
        _debounce = new System.Timers.Timer(750) { AutoReset = false };
        _debounce.Elapsed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);

        _watcher = new FileSystemWatcher(folder, "*.json") {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Changed += OnChange;
        _watcher.Created += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnChange;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnChange(object? sender, FileSystemEventArgs e) {
        _debounce.Stop();
        _debounce.Start();
    }

    public void Dispose() {
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
