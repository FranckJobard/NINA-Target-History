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
            // Capture N.I.N.A.'s own current 1x1 rectangle before removing ours.
            // Reusing its exact pixel dimensions avoids duplicating N.I.N.A.'s
            // camera/FOV calculation and guarantees identical field size.
            var native = _framing.CameraRectangles.FirstOrDefault(r =>
                r?.Name?.StartsWith(HistoryPrefix, StringComparison.Ordinal) != true);

            RemoveHistoricalRectangles();
            if (!_settings.ShowHistoricalFields) {
                Diagnostic("OFF");
                return;
            }
            if (_framing.FramingAssistantSource != SkySurveySource.SKYATLAS) {
                Diagnostic($"source={_framing.FramingAssistantSource}; historical projection currently requires SKYATLAS");
                return;
            }
            if (_framing.HorizontalPanels != 1 || _framing.VerticalPanels != 1) {
                Diagnostic($"mosaic={_framing.HorizontalPanels}x{_framing.VerticalPanels}; overlay suppressed");
                return;
            }
            if (native is null || native.Width <= 0 || native.Height <= 0) {
                Diagnostic("no usable native CameraRectangle yet");
                return;
            }

            var viewport = _framing.SkyMapAnnotator.ViewportFoV;
            if (viewport is null || viewport.Width <= 0 || viewport.Height <= 0
                || viewport.ArcSecWidth <= 0 || viewport.ArcSecHeight <= 0) {
                Diagnostic("no usable SKYATLAS viewport yet");
                return;
            }

            // Each target row owns its angular field. Convert that stored field
            // to the current SKYATLAS viewport just as a mosaic panel is converted
            // to screen geometry. Fall back to N.I.N.A.'s native rectangle only
            // for older/incomplete rows.
            var currentName = _framing.DSO?.Name ?? string.Empty;
            var parentRotation = _framing.Rectangle?.Rotation ?? 0d;
            var candidates = _targets().Where(t => t.TotalSeconds > 0).ToList();
            var added = 0;

            foreach (var target in candidates) {
                if (!string.IsNullOrWhiteSpace(currentName)
                    && string.Equals(target.Name, currentName, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                var coordinates = new Coordinates(
                    target.RaDegrees, target.DecDegrees, Epoch.J2000, Coordinates.RAType.Degrees);
                var center = coordinates.XYProjection(viewport);
                var width = target.FieldWidthDegrees > 0
                    ? AstroUtil.DegreeToArcsec(target.FieldWidthDegrees) / viewport.ArcSecWidth
                    : native.Width;
                var height = target.FieldHeightDegrees > 0
                    ? AstroUtil.DegreeToArcsec(target.FieldHeightDegrees) / viewport.ArcSecHeight
                    : native.Height;

                if (center.X + width / 2 < 0 || center.Y + height / 2 < 0
                    || center.X - width / 2 > viewport.Width || center.Y - height / 2 > viewport.Height) {
                    continue;
                }

                var screenRotation = AstroUtil.EuclidianModulus(
                    360 - target.PositionAngle - viewport.Rotation, 360);
                var rectangleRotation = AstroUtil.EuclidianModulus(
                    screenRotation - parentRotation, 360);

                _framing.CameraRectangles.Add(new FramingRectangle(
                    viewport.Rotation,
                    center.X - width / 2,
                    center.Y - height / 2,
                    width,
                    height) {
                    Id = 0,
                    Name = HistoryPrefix + target.Name,
                    Rotation = rectangleRotation,
                    Coordinates = coordinates,
                    OriginalCoordinates = coordinates,
                    DSOPositionAngle = target.PositionAngle
                });
                added++;
            }

            Diagnostic($"source=SKYATLAS; historical={candidates.Count}; visible={added}; viewport={viewport.Width:0}x{viewport.Height:0}; native={native.Width:0}x{native.Height:0}");
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
