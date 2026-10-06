using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.TargetHistory.Models;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.SkySurvey;

namespace NINA.TargetHistory.Services;

/// <summary>
/// Sky-anchored historical field overlay.
///
/// Historical fields are deliberately NOT inserted into FramingAssistantVM.CameraRectangles.
/// N.I.N.A. owns that collection and rebuilds it for the current framing/mosaic.
/// Instead this class attaches a transparent Canvas next to SkyMapOverlayView and projects
/// every historical target from RA/Dec into the current ViewportFoV on each refresh.
/// </summary>
public sealed class HistoricalFramingOverlay : IDisposable {
    private const string OverlayTag = "__TargetHistorySkyOverlay__";
    private readonly IFramingAssistantVM _framing;
    private readonly Func<IEnumerable<TargetHistoryItem>> _targets;
    private readonly PluginSettings _settings = new();
    private readonly DispatcherTimer _refreshTimer;
    private Canvas? _overlay;
    private INotifyPropertyChanged? _skyMapNotifier;
    private string _lastDiagnostic = "";

    public HistoricalFramingOverlay(IFramingAssistantVM framing, Func<IEnumerable<TargetHistoryItem>> targets) {
        _framing = framing;
        _targets = targets;
        _settings.Load();
        PluginSettings.SettingsChanged += SettingsChanged;
        AttachSkyMapRedrawListener();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Render) {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _refreshTimer.Tick += RefreshTimer_Tick;
        _refreshTimer.Start();
    }

    private void SettingsChanged(object? sender, EventArgs e) {
        _settings.Load();
        Refresh();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e) {
        AttachSkyMapRedrawListener();
        RefreshCore();
    }

    private void AttachSkyMapRedrawListener() {
        if (_skyMapNotifier is not null) return;
        if (_framing.SkyMapAnnotator is INotifyPropertyChanged notifier) {
            _skyMapNotifier = notifier;
            _skyMapNotifier.PropertyChanged += SkyMapAnnotator_PropertyChanged;
        }
    }

    private void SkyMapAnnotator_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        // N.I.N.A. publishes a new SkyMapOverlay after it has shifted/rebuilt the
        // viewport. Redraw Target History at that exact point, just like the
        // catalogue/grid render cycle, instead of merely polling the UI.
        if (e.PropertyName == nameof(ISkyMapAnnotator.SkyMapOverlay)) {
            Refresh();
        }
    }

    public void Refresh() {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.CheckAccess()) RefreshCore();
        else dispatcher.BeginInvoke(RefreshCore, DispatcherPriority.Render);
    }

    private void RefreshCore() {
        try {
            if (!_settings.ShowHistoricalFields) {
                ClearOverlay();
                Diagnostic("OFF");
                return;
            }
            if (_framing.FramingAssistantSource != SkySurveySource.SKYATLAS) {
                ClearOverlay();
                Diagnostic($"source={_framing.FramingAssistantSource}; sky overlay requires SKYATLAS");
                return;
            }

            var viewport = _framing.SkyMapAnnotator.ViewportFoV;
            if (viewport is null || viewport.Width <= 0 || viewport.Height <= 0
                || viewport.ArcSecWidth <= 0 || viewport.ArcSecHeight <= 0) {
                ClearOverlay();
                Diagnostic("waiting for SKYATLAS viewport");
                return;
            }

            if (!EnsureOverlayAttached()) {
                Diagnostic("waiting for FramingAssistant SkyMap canvas");
                return;
            }

            // This canvas lives in the exact same native-pixel coordinate space as
            // SkyMapAnnotator.SkyMapOverlay. ImageView/Viewbox performs display scaling.
            var hostWidth = viewport.Width;
            var hostHeight = viewport.Height;
            var scaleX = 1d;
            var scaleY = 1d;
            _overlay.Children.Clear();

            var native = _framing.CameraRectangles.FirstOrDefault();
            var candidates = _targets().Where(t => t.TotalSeconds > 0).ToList();
            var visible = 0;

            foreach (var target in candidates) {
                var coordinates = new Coordinates(
                    target.RaDegrees, target.DecDegrees, Epoch.J2000, Coordinates.RAType.Degrees);
                var projected = coordinates.XYProjection(viewport);
                var center = new System.Windows.Point(projected.X * scaleX, projected.Y * scaleY);

                var width = target.FieldWidthDegrees > 0
                    ? (AstroUtil.DegreeToArcsec(target.FieldWidthDegrees) / viewport.ArcSecWidth) * scaleX
                    : (native?.Width ?? 0d) * scaleX;
                var height = target.FieldHeightDegrees > 0
                    ? (AstroUtil.DegreeToArcsec(target.FieldHeightDegrees) / viewport.ArcSecHeight) * scaleY
                    : (native?.Height ?? 0d) * scaleY;

                if (width <= 0 || height <= 0) continue;
                if (center.X + width / 2d < 0 || center.Y + height / 2d < 0
                    || center.X - width / 2d > hostWidth
                    || center.Y - height / 2d > hostHeight) {
                    continue;
                }

                var rectangle = new System.Windows.Shapes.Rectangle {
                    Width = width,
                    Height = height,
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 2,
                    Fill = System.Windows.Media.Brushes.Transparent,
                    IsHitTestVisible = false,
                    RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                    // First make anchoring identical to N.I.N.A.'s catalogue objects.
                    // Orientation refinement comes after the sky anchoring is verified.
                    RenderTransform = new RotateTransform(
                        AstroUtil.EuclidianModulus(360d - target.PositionAngle - viewport.Rotation, 360d))
                };

                Canvas.SetLeft(rectangle, center.X - width / 2d);
                Canvas.SetTop(rectangle, center.Y - height / 2d);
                _overlay.Children.Add(rectangle);
                visible++;
            }

            Diagnostic($"sky overlay attached; historical={candidates.Count}; visible={visible}; viewport={viewport.Width:0}x{viewport.Height:0}");
        } catch (Exception ex) {
            Logger.Error(ex);
        }
    }

    private bool EnsureOverlayAttached() {
        if (_overlay?.Parent is System.Windows.Controls.Panel) return true;

        foreach (System.Windows.Window window in System.Windows.Application.Current.Windows) {
            var framingView = FindDescendant(window, d =>
                string.Equals(d.GetType().FullName, "NINA.View.FramingAssistantView", StringComparison.Ordinal));
            if (framingView is null) continue;

            var skyMapView = FindDescendant(framingView, d =>
                string.Equals(d.GetType().FullName, "NINA.View.SkyMapOverlayView", StringComparison.Ordinal));
            if (skyMapView is null) continue;

            // N.I.N.A. draws the grid, DSO outlines/names and telescope marker into
            // SkyMapAnnotator.SkyMapOverlay, displayed by the Image inside this Canvas.
            // Put Target History in that exact same visual layer so ImageView applies
            // the same pan/zoom/rotation transform to both.
            var skyCanvas = FindDescendant(skyMapView, d => d is Canvas) as Canvas;
            if (skyCanvas is null) continue;
            var skyImage = skyCanvas.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
            if (skyImage is null) continue;

            foreach (var child in skyCanvas.Children.OfType<Canvas>()) {
                if (Equals(child.Tag, OverlayTag)) {
                    _overlay = child;
                    return true;
                }
            }

            _overlay = new Canvas {
                Tag = OverlayTag,
                IsHitTestVisible = false,
                ClipToBounds = true,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top
            };
            _overlay.Width = _framing.SkyMapAnnotator.ViewportFoV?.Width ?? skyImage.ActualWidth;
            _overlay.Height = _framing.SkyMapAnnotator.ViewportFoV?.Height ?? skyImage.ActualHeight;
            System.Windows.Controls.Panel.SetZIndex(_overlay, 1000);
            skyCanvas.Children.Add(_overlay);
            return true;
        }
        return false;
    }

    private static DependencyObject? FindDescendant(DependencyObject root, Func<DependencyObject, bool> predicate) {
        if (predicate(root)) return root;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++) {
            var child = VisualTreeHelper.GetChild(root, i);
            var match = FindDescendant(child, predicate);
            if (match is not null) return match;
        }
        return null;
    }

    private void ClearOverlay() {
        _overlay?.Children.Clear();
    }

    private void Diagnostic(string message) {
        if (message == _lastDiagnostic) return;
        _lastDiagnostic = message;
        Logger.Info($"Target History framing: {message}");
    }

    public void Dispose() {
        PluginSettings.SettingsChanged -= SettingsChanged;
        if (_skyMapNotifier is not null) {
            _skyMapNotifier.PropertyChanged -= SkyMapAnnotator_PropertyChanged;
            _skyMapNotifier = null;
        }
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;
        if (_overlay?.Parent is System.Windows.Controls.Panel parent) parent.Children.Remove(_overlay);
        _overlay = null;
    }
}
