using System.IO;
using System.Text.Json;

namespace NINA.TargetHistory.Services;

public sealed class PluginSettings {
    private readonly string _path;
    public string SequenceFolder { get; set; } = "";
    public bool ShowHistoricalFields { get; set; } = false;
    public static event EventHandler? SettingsChanged;

    public PluginSettings() {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "Plugins", "3.0.0", "TargetHistory");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "settings.json");
    }

    public void Load() {
        try {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("SequenceFolder", out var value)
                || doc.RootElement.TryGetProperty("sequenceFolder", out value)) {
                SequenceFolder = value.GetString() ?? "";
            }
            if (doc.RootElement.TryGetProperty("ShowHistoricalFields", out var showValue)
                || doc.RootElement.TryGetProperty("showHistoricalFields", out showValue)) {
                if (showValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    ShowHistoricalFields = showValue.GetBoolean();
            }
        } catch { }
    }

    public void Save() {
        File.WriteAllText(_path, JsonSerializer.Serialize(
            new PluginSettingsDto { SequenceFolder = SequenceFolder, ShowHistoricalFields = ShowHistoricalFields },
            new JsonSerializerOptions { WriteIndented = true }));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class PluginSettingsDto {
        public string SequenceFolder { get; set; } = "";
        public bool ShowHistoricalFields { get; set; } = false;
    }
}
