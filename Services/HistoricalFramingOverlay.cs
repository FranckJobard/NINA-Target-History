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
            RemoveHistoricalRectangles();
            if (!_settings.ShowHistoricalFields) return;
            if (_framing.FramingAssistantSource != SkySurveySource.SKYATLAS) return;

            // Do not interfere with a mosaic currently being designed by the user.
            // A 1x1 framing is the safe historical-overlay case.
            if (_framing.HorizontalPanels != 1 || _framing.VerticalPanels != 1) return;

            var viewport = _framing.SkyMapAnnotator.ViewportFoV;
            if (viewport is null || viewport.Width <= 0 || viewport.Height <= 0
                || viewport.ArcSecWidth <= 0 || viewport.ArcSecHeight <= 0) return;
            if (_framing.CameraWidth <= 0 || _framing.CameraHeight <= 0
                || _framing.CameraPixelSize <= 0 || _framing.FocalLength <= 0) return;

            var arcsecPerPixel = AstroUtil.ArcsecPerPixel(_framing.CameraPixelSize, _framing.FocalLength);
            var width = _framing.CameraWidth * arcsecPerPixel / viewport.ArcSecWidth;
            var height = _framing.CameraHeight * arcsecPerPixel / viewport.ArcSecHeight;
            var currentName = _framing.DSO?.Name ?? string.Empty;
            var parentRotation = _framing.Rectangle?.Rotation ?? 0d;

            foreach (var target in _targets().Where(t => t.TotalSeconds > 0)) {
                if (!string.IsNullOrWhiteSpace(currentName)
                    && string.Equals(target.Name, currentName, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                var coordinates = new Coordinates(
                    target.RaDegrees, target.DecDegrees, Epoch.J2000, Coordinates.RAType.Degrees);
                var center = coordinates.XYProjection(viewport);

                if (center.X + width / 2 < 0 || center.Y + height / 2 < 0
                    || center.X - width / 2 > viewport.Width || center.Y - height / 2 > viewport.Height) {
                    continue;
                }

                // CameraRectangles are rendered inside an ItemsControl which N.I.N.A.
                // rotates by Rectangle.Rotation. Compensate that parent rotation and
                // preserve the PositionAngle stored in the historical sequence.
                var screenRotation = AstroUtil.EuclidianModulus(
                    360 - target.PositionAngle - viewport.Rotation, 360);
                var rectangleRotation = AstroUtil.EuclidianModulus(
                    screenRotation - parentRotation, 360);

                var rect = new FramingRectangle(
                    viewport.Rotation,
                    center.X - width / 2,
                    center.Y - height / 2,
                    width,
                    height) {
                    Id = 0, // N.I.N.A. hides the panel number for Id=0
                    Name = HistoryPrefix + target.Name,
                    Rotation = rectangleRotation,
                    Coordinates = coordinates,
                    OriginalCoordinates = coordinates,
                    DSOPositionAngle = target.PositionAngle
                };

                _framing.CameraRectangles.Add(rect);
            }
        } finally {
            _updating = false;
        }
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
