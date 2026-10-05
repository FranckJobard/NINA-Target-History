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

        // Dedicated Target History icon: clock/history symbol, with no external resource dependency.
        var icon = new System.Windows.Media.GeometryGroup();
        icon.Children.Add(new System.Windows.Media.CombinedGeometry(
            System.Windows.Media.GeometryCombineMode.Exclude,
            new System.Windows.Media.EllipseGeometry(new System.Windows.Point(12, 12), 10, 10),
            new System.Windows.Media.EllipseGeometry(new System.Windows.Point(12, 12), 7.5, 7.5)));
        icon.Children.Add(new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(11, 6, 2, 7)));
        icon.Children.Add(new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(12, 11, 6, 2)));

        // Dockable view models can be composed by N.I.N.A. away from the UI thread.
        // Freeze the Freezable geometry before WPF binds to it on the Imaging UI thread.
        // A frozen Freezable is thread-safe and can be shared across dispatchers.
        icon.Freeze();
        ImageGeometry = icon;

        // Initialize the history from N.I.N.A.'s UI dispatcher instead of relying
        // on the view's Loaded/DataContext event ordering.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null) {
            dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(History.RefreshAfterViewLoaded));
        }
    }

    public void Dispose() => History.Dispose();
}
