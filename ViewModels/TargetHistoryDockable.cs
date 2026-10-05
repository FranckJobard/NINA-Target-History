using System;
using System.ComponentModel.Composition;
using System.Windows;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;

namespace NINA.TargetHistory.ViewModels;

[Export(typeof(IDockableVM))]
public sealed class TargetHistoryDockable : DockableVM, IDisposable {
    public override bool IsTool { get; } = true;
    public TargetHistoryViewModel History { get; }

    [ImportingConstructor]
    public TargetHistoryDockable(IProfileService profileService) : base(profileService) {
        Title = "Target History";
        History = new TargetHistoryViewModel();

        // Use N.I.N.A.'s standard puzzle-piece geometry so the panel never depends
        // on a plugin-specific icon resource during startup.
        if (Application.Current?.Resources["PuzzlePieceSVG"] is System.Windows.Media.GeometryGroup icon) {
            ImageGeometry = icon;
        }
    }

    public void Dispose() => History.Dispose();
}
