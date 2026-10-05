using System.IO;
using System.Text.Json;

namespace NINA.TargetHistory.Services;

public sealed class PluginSettings {
    private readonly string _path;
    public string SequenceFolder { get; set; } = "";

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
            var loaded = JsonSerializer.Deserialize<PluginSettingsDto>(File.ReadAllText(_path));
            SequenceFolder = loaded?.SequenceFolder ?? "";
        } catch { }
    }

    public void Save() {
        File.WriteAllText(_path, JsonSerializer.Serialize(
            new PluginSettingsDto { SequenceFolder = SequenceFolder },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class PluginSettingsDto {
        public string SequenceFolder { get; set; } = "";
    }
}
