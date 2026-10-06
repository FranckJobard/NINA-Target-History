using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.TargetHistory.Services;

namespace NINA.TargetHistory;

[Export(typeof(IPluginManifest))]
public sealed class TargetHistoryPlugin : PluginBase, INotifyPropertyChanged {
    private readonly PluginSettings _settings = new();

    public TargetHistoryPlugin() {
        _settings.Load();
    }

    public bool ShowHistoricalFields {
        get => _settings.ShowHistoricalFields;
        set {
            if (_settings.ShowHistoricalFields == value) return;
            _settings.ShowHistoricalFields = value;
            _settings.Save();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowHistoricalFields)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
