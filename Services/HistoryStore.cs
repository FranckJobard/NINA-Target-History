using System.IO;
using System.Text.Json;
using NINA.TargetHistory.Models;

namespace NINA.TargetHistory.Services;

public sealed class HistoryStore {
    private readonly string _folder;
    private readonly string _settingsFile;
    private readonly SequenceParser _parser = new();

    public HistoryStore(string folder) {
        _folder = folder;
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "Plugins", "3.0.0", "TargetHistory");
        Directory.CreateDirectory(appData);
        _settingsFile = Path.Combine(appData, "targets.json");
    }

    public IReadOnlyList<TargetHistoryItem> Rebuild() {
        var metadata = LoadMetadata();
        var parsed = new List<SequenceTarget>();

        if (Directory.Exists(_folder)) {
            foreach (var file in Directory.EnumerateFiles(_folder, "*.json", SearchOption.TopDirectoryOnly)) {
                try { parsed.AddRange(_parser.Parse(file)); }
                catch { /* one malformed/in-use sequence must not kill the catalogue */ }
            }
        }

        var items = parsed
            .GroupBy(x => Normalize(x.Name), StringComparer.OrdinalIgnoreCase)
            .Select(group => {
                var latest = group.OrderByDescending(x => x.LastWriteUtc).First();
                var item = new TargetHistoryItem {
                    Name = latest.Name,
                    RaDegrees = latest.RaDegrees,
                    DecDegrees = latest.DecDegrees,
                    PositionAngle = latest.PositionAngle,
                    MostRecentSequenceUtc = latest.LastWriteUtc
                };

                foreach (var filter in group.SelectMany(x => x.Exposures)
                             .GroupBy(x => x.Filter, StringComparer.OrdinalIgnoreCase)
                             .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) {
                    item.Filters.Add(new FilterTotal {
                        Name = filter.Key,
                        Seconds = filter.Sum(x => x.TotalSeconds)
                    });
                }

                if (metadata.Targets.TryGetValue(group.Key, out var m)) {
                    item.Finished = m.Finished;
                    item.AstroBinUrl = m.AstroBinUrl;
                }
                return item;
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return items;
    }

    public void SaveMetadata(IEnumerable<TargetHistoryItem> items) {
        var data = new UserMetadata();
        foreach (var item in items) {
            data.Targets[Normalize(item.Name)] = new TargetMetadata {
                Finished = item.Finished,
                AstroBinUrl = item.AstroBinUrl
            };
        }
        File.WriteAllText(_settingsFile,
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private UserMetadata LoadMetadata() {
        try {
            if (!File.Exists(_settingsFile)) return new UserMetadata();
            return JsonSerializer.Deserialize<UserMetadata>(File.ReadAllText(_settingsFile))
                   ?? new UserMetadata();
        } catch { return new UserMetadata(); }
    }

    public static string Normalize(string name)
        => string.Join(" ", name.Trim().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
