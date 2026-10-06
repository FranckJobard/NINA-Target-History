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

namespace NINA.TargetHistory.Services;

public sealed class HistoricalFramingOverlay : IDisposable {
    private readonly IFramingAssistantVM _framing;
    private readonly Func<IEnumerable<TargetHistoryItem>> _targets;
    private readonly PluginSettings _settings = new();
    private Canvas? _host;
    private Canvas? _overlay;
    private INotifyPropertyChanged? _annotatorNotifier;

    public HistoricalFramingOverlay(IFramingAssistantVM framing, Func<IEnumerable<TargetHistoryItem>> targets) {
        _framing = framing;
        _targets = targets;
        _settings.Load();
        PluginSettings.SettingsChanged += SettingsChanged;
        HookAnnotator();
    }

    private void HookAnnotator() {
        _annotatorNotifier = _framing.SkyMapAnnotator as INotifyPropertyChanged;
        if (_annotatorNotifier is not null)
            _annotatorNotifier.PropertyChanged += AnnotatorPropertyChanged;
    }

    private void SettingsChanged(object? sender, EventArgs e) {
        _settings.Load();
        Dispatch(Render);
    }

    private void AnnotatorPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(_framing.SkyMapAnnotator.SkyMapOverlay))
            Dispatch(Render);
    }

    public void Refresh() => Dispatch(() => {
        Attach();
        Render();
    });

    private static void Dispatch(Action action) {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    private void Attach() {
        if (_overlay is not null && _host is not null) return;
        var root = System.Windows.Application.Current?.MainWindow;
        if (root is null) return;
        var canvas = FindFramingCanvas(root);
        if (canvas is null) return;

        _host = canvas;
        _overlay = new Canvas {
            IsHitTestVisible = false,
            ClipToBounds = true,
            Width = canvas.Width,
            Height = canvas.Height
        };
        System.Windows.Controls.Panel.SetZIndex(_overlay, 20);
        canvas.Children.Add(_overlay);
    }

    private Canvas? FindFramingCanvas(DependencyObject root) {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++) {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Canvas canvas
                && ReferenceEquals(canvas.DataContext, _framing)
                && canvas.Children.OfType<FrameworkElement>().Any(x => x.GetType().Name == "SkyMapOverlayView")) {
                return canvas;
            }
            var found = FindFramingCanvas(child);
            if (found is not null) return found;
        }
        return null;
    }

    private void Render() {
        if (_overlay is null || _host is null) {
            Attach();
            if (_overlay is null) return;
        }

        _overlay.Children.Clear();
        if (!_settings.ShowHistoricalFields) return;
        if (_framing.FramingAssistantSource != SkySurveySource.SKYATLAS) return;

        var viewport = _framing.SkyMapAnnotator.ViewportFoV;
        if (viewport is null || viewport.ArcSecWidth <= 0 || viewport.ArcSecHeight <= 0) return;
        if (_framing.CameraWidth <= 0 || _framing.CameraHeight <= 0 || _framing.CameraPixelSize <= 0 || _framing.FocalLength <= 0) return;

        _overlay.Width = viewport.Width;
        _overlay.Height = viewport.Height;

        var cameraArcsecPerPixel = AstroUtil.ArcsecPerPixel(_framing.CameraPixelSize, _framing.FocalLength);
        var width = _framing.CameraWidth * cameraArcsecPerPixel / viewport.ArcSecWidth;
        var height = _framing.CameraHeight * cameraArcsecPerPixel / viewport.ArcSecHeight;
        var currentName = _framing.DSO?.Name ?? string.Empty;
        var stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 170, 112));
        stroke.Freeze();

        foreach (var target in _targets().Where(t => t.TotalSeconds > 0)) {
            if (!string.IsNullOrWhiteSpace(currentName)
                && string.Equals(target.Name, currentName, StringComparison.OrdinalIgnoreCase))
                continue;

            var coordinates = new Coordinates(target.RaDegrees, target.DecDegrees, Epoch.J2000, Coordinates.RAType.Degrees);
            var center = coordinates.XYProjection(viewport);
            if (center.X + width / 2 < 0 || center.Y + height / 2 < 0
                || center.X - width / 2 > viewport.Width || center.Y - height / 2 > viewport.Height)
                continue;

            var rectangle = new System.Windows.Shapes.Rectangle {
                Width = width,
                Height = height,
                Stroke = stroke,
                StrokeThickness = 2,
                Fill = System.Windows.Media.Brushes.Transparent,
                IsHitTestVisible = false,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new RotateTransform(
                    AstroUtil.EuclidianModulus(360 - target.PositionAngle - viewport.Rotation, 360))
            };
            Canvas.SetLeft(rectangle, center.X - width / 2);
            Canvas.SetTop(rectangle, center.Y - height / 2);
            _overlay.Children.Add(rectangle);
        }
    }

    public void Dispose() {
        PluginSettings.SettingsChanged -= SettingsChanged;
        if (_annotatorNotifier is not null)
            _annotatorNotifier.PropertyChanged -= AnnotatorPropertyChanged;
        Dispatch(() => {
            if (_host is not null && _overlay is not null)
                _host.Children.Remove(_overlay);
            _host = null;
            _overlay = null;
        });
    }
}
