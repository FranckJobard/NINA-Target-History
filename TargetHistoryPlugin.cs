using System.ComponentModel.Composition;
using NINA.Plugin;
using NINA.Plugin.Interfaces;

namespace NINA.TargetHistory;

[Export(typeof(IPluginManifest))]
public sealed class TargetHistoryPlugin : PluginBase {
}
