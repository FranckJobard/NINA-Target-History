using System.Windows.Threading;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.TargetHistory.Models;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.SkySurvey;

namespace NINA.TargetHistory.Services;

/// <summary>
/// Adds historical fields as native N.I.N.A. FramingRectangle instances.
/// N.I.N.A. owns the rendering, so historical fields use the same white
/// rectangle appearance as normal Framing Assistant panels.
/// </summary>
public sealed class HistoricalFramingOverlay : IDisposable {
    private const string HistoryPrefix = "__TargetHistory__:";
    private readonly IFramingAssistantVM _framing;
    private readonly Func<IEnumerable<TargetHistoryItem>> _targets;
    private readonly PluginSettings _settings = new();
    private readonly DispatcherTimer _refreshTimer;
    private bool _updating;
    private string _lastDiagnostic = "";

    public HistoricalFramingOverlay(IFramingAssistantVM framing, Func<IEnumerable<TargetHistoryItem>> targets) {
        _framing = framing;
        _targets = targets;
        _settings.Load();
        PluginSettings.SettingsChanged += SettingsChanged;

        // FramingAssistantVM recalculates (and clears) CameraRectangles when the
        // sky is panned/zoomed. Re-apply our native rectangles after that work.
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += RefreshTimer_Tick;
        _refreshTimer.Start();
    }

    private void SettingsChanged(object? sender, EventArgs e) {
        _settings.Load();
        Refresh();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e) {
        RefreshCore();
    }

    public void Refresh() {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.CheckAccess()) RefreshCore();
        else dispatcher.BeginInvoke(RefreshCore, DispatcherPriority.Background);
    }

    private void RefreshCore() {
        if (_updating) return;
        _updating = true;
        try {
            var native = _framing.CameraRectangles.FirstOrDefault(r =>
                r?.Name?.StartsWith(HistoryPrefix, StringComparison.Ordinal) != true);

            RemoveHistoricalRectangles();
            if (!_settings.ShowHistoricalFields) {
                Diagnostic("OFF");
                return;
            }
            if (_framing.FramingAssistantSource != SkySurveySource.SKYATLAS) {
                Diagnostic($"source={_framing.FramingAssistantSource}; historical projection requires SKYATLAS");
                return;
            }
            if (_framing.HorizontalPanels != 1 || _framing.VerticalPanels != 1) {
                Diagnostic($"mosaic={_framing.HorizontalPanels}x{_framing.VerticalPanels}; overlay suppressed");
                return;
            }

            var viewport = _framing.SkyMapAnnotator.ViewportFoV;
            var parent = _framing.Rectangle;
            if (viewport is null || parent is null || viewport.Width <= 0 || viewport.Height <= 0
                || viewport.ArcSecWidth <= 0 || viewport.ArcSecHeight <= 0) {
                Diagnostic("no usable SKYATLAS viewport/parent rectangle yet");
                return;
            }
            if (native is null || native.Width <= 0 || native.Height <= 0) {
                Diagnostic("no usable native CameraRectangle yet");
                return;
            }

            var currentName = _framing.DSO?.Name ?? string.Empty;
            var candidates = _targets().Where(t => t.TotalSeconds > 0).ToList();
            var added = 0;

            // FramingAssistantView.xaml renders CameraRectangles inside an ItemsControl:
            // 1) ItemsControl.Margin = Rectangle.X/Y
            // 2) ItemsControl.RenderTransform = Rectangle.Rotation around its center
            // 3) each CameraRectangle X/Y is RELATIVE to that parent.
            // Therefore project each historical RA/Dec to the viewport first, then
            // inverse-rotate it around the parent center and finally subtract parent X/Y.
            var parentCenter = new System.Windows.Point(
                parent.X + parent.Width / 2d,
                parent.Y + parent.Height / 2d);
            var parentRadians = -parent.Rotation * Math.PI / 180d;
            var cos = Math.Cos(parentRadians);
            var sin = Math.Sin(parentRadians);

            foreach (var target in candidates) {
                if (!string.IsNullOrWhiteSpace(currentName)
                    && string.Equals(target.Name, currentName, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                var coordinates = new Coordinates(
                    target.RaDegrees, target.DecDegrees, Epoch.J2000, Coordinates.RAType.Degrees);
                var screenCenter = coordinates.XYProjection(viewport);

                var width = target.FieldWidthDegrees > 0
                    ? AstroUtil.DegreeToArcsec(target.FieldWidthDegrees) / viewport.ArcSecWidth
                    : native.Width;
                var height = target.FieldHeightDegrees > 0
                    ? AstroUtil.DegreeToArcsec(target.FieldHeightDegrees) / viewport.ArcSecHeight
                    : native.Height;

                if (screenCenter.X + width / 2d < 0 || screenCenter.Y + height / 2d < 0
                    || screenCenter.X - width / 2d > viewport.Width
                    || screenCenter.Y - height / 2d > viewport.Height) {
                    continue;
                }

                // Undo the parent ItemsControl rotation so the child lands at the
                // requested sky position after N.I.N.A. applies that rotation again.
                var dx = screenCenter.X - parentCenter.X;
                var dy = screenCenter.Y - parentCenter.Y;
                var unrotatedX = parentCenter.X + dx * cos - dy * sin;
                var unrotatedY = parentCenter.Y + dx * sin + dy * cos;

                var relativeX = unrotatedX - parent.X - width / 2d;
                var relativeY = unrotatedY - parent.Y - height / 2d;

                // N.I.N.A. defines DSO PA as 360 - total screen rotation.
                // Child Rotation is relative to the already-rotated parent.
                var desiredScreenRotation = AstroUtil.EuclidianModulus(
                    360d - target.PositionAngle, 360d);
                var childRotation = AstroUtil.EuclidianModulus(
                    desiredScreenRotation - parent.Rotation, 360d);

                _framing.CameraRectangles.Add(new FramingRectangle(
                    viewport.Rotation,
                    relativeX,
                    relativeY,
                    width,
                    height) {
                    Id = 0,
                    Name = HistoryPrefix + target.Name,
                    Rotation = childRotation,
                    Coordinates = coordinates,
                    OriginalCoordinates = coordinates,
                    DSOPositionAngle = AstroUtil.EuclidianModulus(target.PositionAngle, 360d)
                });
                added++;
            }

            Diagnostic($"relative-mosaic geometry; historical={candidates.Count}; visible={added}; parent=({parent.X:0},{parent.Y:0}) {parent.Width:0}x{parent.Height:0} rot={parent.Rotation:0.0}");
        } catch (Exception ex) {
            Logger.Error(ex);
        } finally {
            _updating = false;
        }
    }

    private void Diagnostic(string message) {
        if (message == _lastDiagnostic) return;
        _lastDiagnostic = message;
        Logger.Info($"Target History framing: {message}");
    }

    private void RemoveHistoricalRectangles() {
        for (var i = _framing.CameraRectangles.Count - 1; i >= 0; i--) {
            var rect = _framing.CameraRectangles[i];
            if (rect?.Name?.StartsWith(HistoryPrefix, StringComparison.Ordinal) == true) {
                _framing.CameraRectangles.RemoveAt(i);
            }
        }
    }

    public void Dispose() {
        PluginSettings.SettingsChanged -= SettingsChanged;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher?.CheckAccess() == true) RemoveHistoricalRectangles();
        else dispatcher?.BeginInvoke(RemoveHistoricalRectangles, DispatcherPriority.Background);
    }
}
