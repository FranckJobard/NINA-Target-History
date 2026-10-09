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
    private readonly Func<bool> _showPlannedFields;
    private readonly PluginSettings _settings = new();
    private readonly DispatcherTimer _refreshTimer;
    private Canvas? _overlay;
    private StackPanel? _toolbarToggles;
    private System.Windows.Controls.CheckBox? _imagedToggle;
    private System.Windows.Controls.CheckBox? _plannedToggle;
    private bool _syncingToggles;
    private readonly Action<bool> _setImagedFields;
    private readonly Action<bool> _setPlannedFields;
    private INotifyPropertyChanged? _skyMapNotifier;
    private string _lastDiagnostic = "";
    private string _lastToolbarDiagnostic = "";
    private string _lastGeometryDiagnostic = "";

    public HistoricalFramingOverlay(
        IFramingAssistantVM framing,
        Func<IEnumerable<TargetHistoryItem>> targets,
        Func<bool> showPlannedFields,
        Action<bool> setImagedFields,
        Action<bool> setPlannedFields) {
        _framing = framing;
        _targets = targets;
        _showPlannedFields = showPlannedFields;
        _setImagedFields = setImagedFields;
        _setPlannedFields = setPlannedFields;
        _settings.Load();
        PluginSettings.SettingsChanged += SettingsChanged;
        AttachSkyMapRedrawListener();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Render, System.Windows.Application.Current.Dispatcher) {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _refreshTimer.Tick += RefreshTimer_Tick;
        _refreshTimer.Start();
        Logger.Info("Target History toolbar: UI dispatcher timer initialized");
    }

    private void SettingsChanged(object? sender, EventArgs e) {
        _settings.Load();
        Refresh();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e) {
        AttachSkyMapRedrawListener();
        try {
            EnsureToolbarToggles();
            SyncToolbarToggles();
        } catch (Exception ex) {
            ToolbarDiagnostic($"toolbar exception: {ex.GetType().Name}: {ex.Message}");
            Logger.Error(ex);
        }
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
            var showImaged = _settings.ShowHistoricalFields;
            var showPlanned = _showPlannedFields();
            if (!showImaged && !showPlanned) {
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

            var overlay = _overlay;
            if (overlay is null) return;

            // This canvas lives in the exact same native-pixel coordinate space as
            // SkyMapAnnotator.SkyMapOverlay. ImageView/Viewbox performs display scaling.
            var hostWidth = viewport.Width;
            var hostHeight = viewport.Height;
            var scaleX = 1d;
            var scaleY = 1d;
            overlay.Children.Clear();

            var native = _framing.CameraRectangles.FirstOrDefault();
            var candidates = _targets()
                .Where(t => (showImaged && t.TotalSeconds > 0)
                         || (showPlanned && t.TotalSeconds <= 0))
                .ToList();
            var visible = 0;
            var geometry = new System.Collections.Generic.List<string>();

            foreach (var target in candidates) {
                var coordinates = new Coordinates(
                    target.RaDegrees, target.DecDegrees, Epoch.J2000, Coordinates.RAType.Degrees);
                var projected = coordinates.XYProjection(viewport);
                var center = new System.Windows.Point(projected.X * scaleX, projected.Y * scaleY);

                var width = target.FieldWidthDegrees > 0
                    ? (AstroUtil.DegreeToArcsec(target.FieldWidthDegrees) / viewport.ArcSecWidth) * hostWidth * scaleX
                    : (native?.Width ?? 0d) * scaleX;
                var height = target.FieldHeightDegrees > 0
                    ? (AstroUtil.DegreeToArcsec(target.FieldHeightDegrees) / viewport.ArcSecHeight) * hostHeight * scaleY
                    : (native?.Height ?? 0d) * scaleY;

                if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) {
                    geometry.Add($"{target.Name}: invalid size {width:0.##}x{height:0.##} (field {target.FieldWidthDegrees:0.####}x{target.FieldHeightDegrees:0.####} deg)");
                    continue;
                }
                if (center.X + width / 2d < 0 || center.Y + height / 2d < 0
                    || center.X - width / 2d > hostWidth
                    || center.Y - height / 2d > hostHeight) {
                    continue;
                }

                // Keep Target History outlines independent from N.I.N.A.'s native framing rectangle.
                var fieldColor = target.TotalSeconds <= 0
                    ? System.Windows.Media.Colors.Yellow
                    : target.Finished
                        ? System.Windows.Media.Colors.DeepSkyBlue
                        : System.Windows.Media.Colors.Lime;
                var rectangle = new System.Windows.Shapes.Rectangle {
                    Style = null,
                    Width = width,
                    Height = height,
                    Stroke = new SolidColorBrush(fieldColor),
                    StrokeThickness = 2,
                    Fill = System.Windows.Media.Brushes.Transparent,
                    IsHitTestVisible = false,
                    RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(
                        CalculateHistoricalFieldRotation(target, coordinates, center, viewport))
                };

                Canvas.SetLeft(rectangle, center.X - width / 2d);
                Canvas.SetTop(rectangle, center.Y - height / 2d);
                overlay.Children.Add(rectangle);
                visible++;
                geometry.Add($"{target.Name}: field={target.FieldWidthDegrees:0.####}x{target.FieldHeightDegrees:0.####}deg; rect={width:0.#}x{height:0.#}px; center=({center.X:0.#},{center.Y:0.#}); profile={target.ProfileName}");
            }

            Diagnostic($"sky overlay attached; fields={candidates.Count}; visible={visible}; imaged={showImaged}; planned={showPlanned}; viewport={viewport.Width:0}x{viewport.Height:0}");
            // Log only when geometry changes; the 250 ms redraw timer must not flood N.I.N.A. logs.
            var geometryDiagnostic = $"canvas={overlay.Width:0.#}x{overlay.Height:0.#}; actual={overlay.ActualWidth:0.#}x{overlay.ActualHeight:0.#}; children={overlay.Children.Count}; " + string.Join(" | ", geometry);
            if (geometryDiagnostic != _lastGeometryDiagnostic) {
                _lastGeometryDiagnostic = geometryDiagnostic;
                Logger.Info($"Target History geometry: {geometryDiagnostic}");
            }
        } catch (Exception ex) {
            Logger.Error(ex);
        }
    }

    private static double CalculateHistoricalFieldRotation(
        TargetHistoryItem target,
        Coordinates coordinates,
        System.Windows.Point center,
        ViewportFoV viewport) {
        // This follows N.I.N.A. 3.2 FramingDSO.Draw: the apparent orientation
        // changes with position in the projected sky map.
        var panelDeltaX = center.X - viewport.ViewPortCenterPoint.X;
        var panelDeltaY = center.Y - viewport.ViewPortCenterPoint.Y;
        var referenceCenter = viewport.CenterCoordinates.Shift(
            panelDeltaX < 1E-10 ? 1 : 0,
            panelDeltaY,
            viewport.Rotation,
            viewport.ArcSecWidth,
            viewport.ArcSecHeight);

        // Sequence PositionAngle is the camera/frame PA. Unlike N.I.N.A.'s
        // FramingDSO ellipse (whose major axis convention includes a 90° offset),
        // our rectangle is already drawn with Width on X and Height on Y.
        // Therefore PA=0 must remain horizontal at the viewport centre.
        var angle = -target.PositionAngle;
        if (Math.Abs(viewport.CenterCoordinates.RA - coordinates.RA) > 1E-13
            || Math.Abs(viewport.CenterCoordinates.Dec - coordinates.Dec) > 1E-13) {
            angle += AstroUtil.CalculatePositionAngle(
                referenceCenter.RADegrees,
                coordinates.RADegrees,
                referenceCenter.Dec,
                coordinates.Dec) - 90d;
        }

        return angle;
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

    // Controls are hosted in the Framing Assistant toolbar, not in the native
    // CameraRectangles collection. They use the same state as the dockable table.
    private void EnsureToolbarToggles() {
        if (_toolbarToggles?.Parent is System.Windows.Controls.Panel) return;
        // Toolbar controls must be available even when both field categories are OFF.
        // RefreshCore skips attaching the overlay when both are OFF, so attach the
        // transparent sky layer here independently of field visibility.
        if (_overlay?.Parent is null) EnsureOverlayAttached();
        // Reuse the sky overlay to find the actual Framing ImageView ancestor.
        if (_overlay?.Parent is null) {
            ToolbarDiagnostic("waiting for attached sky overlay");
            return;
        }
        DependencyObject? ancestor = _overlay;
        while (ancestor is not null &&
               !string.Equals(ancestor.GetType().Name, "ImageView", StringComparison.Ordinal))
            ancestor = VisualTreeHelper.GetParent(ancestor);
        if (ancestor is null) {
            ToolbarDiagnostic("ImageView ancestor not found above sky overlay");
            return;
        }
        var presenter = FindDescendant(ancestor, d =>
            d is ContentPresenter cp
            && cp.Content is StackPanel
            && System.Windows.Controls.Grid.GetColumn(cp) == 6) as ContentPresenter;
        if (presenter?.Content is not StackPanel panel) {
            ToolbarDiagnostic("ImageView toolbar presenter not found");
            return;
        }
            var index = 0;

            var group = new StackPanel {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 10, 0),
                ToolTip = "Target History sky fields"
            };
            var imaged = new System.Windows.Controls.CheckBox {
                Content = "ON",
                Template = CreateColoredSwitchTemplate(System.Windows.Media.Brushes.Lime),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                ToolTip = "Show or hide imaged Target History fields"
            };
            var planned = new System.Windows.Controls.CheckBox {
                Content = "ON",
                Template = CreateColoredSwitchTemplate(System.Windows.Media.Brushes.Yellow),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Show or hide planned Target History fields"
            };
            imaged.Checked += ToolbarImagedChanged;
            imaged.Unchecked += ToolbarImagedChanged;
            planned.Checked += ToolbarPlannedChanged;
            planned.Unchecked += ToolbarPlannedChanged;
            group.Children.Add(new TextBlock {
                Text = "Imaged targets",
                Foreground = System.Windows.Media.Brushes.Lime,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });
            group.Children.Add(imaged);
            group.Children.Add(new TextBlock {
                Text = "Planned targets",
                Foreground = System.Windows.Media.Brushes.Yellow,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });
            group.Children.Add(planned);
            panel.Children.Insert(index, group);
            _toolbarToggles = group;
            _imagedToggle = imaged;
            _plannedToggle = planned;
            SyncToolbarToggles();
            ToolbarDiagnostic("controls attached next to Opacity");
            group.Loaded += (_, _) => ValidateToolbarSwitches();
            return;
    }

    private void ValidateToolbarSwitches() {
        if (_imagedToggle is null || _plannedToggle is null) return;
        ValidateSwitch("Imaged", _imagedToggle, System.Windows.Media.Brushes.Lime);
        ValidateSwitch("Planned", _plannedToggle, System.Windows.Media.Brushes.Yellow);
    }

    private static void ValidateSwitch(string name, System.Windows.Controls.CheckBox toggle, System.Windows.Media.Brush accent) {
        toggle.ApplyTemplate();
        var border = toggle.Template?.FindName("SwitchBorder", toggle) as Border;
        var label = toggle.Template?.FindName("SwitchLabel", toggle) as TextBlock;
        var expectedBackground = toggle.IsChecked == true ? accent : System.Windows.Media.Brushes.Transparent;
        var backgroundOk = border is not null && Equals(border.Background, expectedBackground);
        var outlineOk = border is not null && Equals(border.BorderBrush, accent);
        var expectedText = toggle.IsChecked == true ? "ON" : "OFF";
        var textOk = label is not null && label.Text == expectedText;
        Logger.Info($"Target History toolbar test: {name} template={(border is not null && label is not null ? "PASS" : "FAIL")}; background={(backgroundOk ? "PASS" : "FAIL")}; border={(outlineOk ? "PASS" : "FAIL")}; text={(textOk ? "PASS" : "FAIL")}; state={expectedText}");
    }

    private static ControlTemplate CreateColoredSwitchTemplate(System.Windows.Media.Brush accent) {
        // A local control template is required: N.I.N.A.'s CheckBox theme
        // draws its own switch and ignores Background/BorderBrush.
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "SwitchBorder";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.PaddingProperty, new Thickness(0));
        border.SetValue(Border.WidthProperty, 40.0);
        border.SetValue(Border.HeightProperty, 20.0);
        border.SetValue(Border.MinWidthProperty, 40.0);
        border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        border.SetValue(Border.BorderBrushProperty, accent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));

        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.Name = "SwitchLabel";
        label.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        label.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        label.SetValue(TextBlock.FontSizeProperty, 11.0);
        label.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        label.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Gainsboro);
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("IsChecked") {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
            Converter = new BooleanOnOffConverter()
        });
        border.AppendChild(label);

        var template = new ControlTemplate(typeof(System.Windows.Controls.CheckBox)) { VisualTree = border };
        var checkedTrigger = new Trigger {
            Property = System.Windows.Controls.CheckBox.IsCheckedProperty,
            Value = true
        };
        checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, accent, "SwitchBorder"));
        checkedTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Black, "SwitchLabel"));
        template.Triggers.Add(checkedTrigger);
        return template;
    }

    private sealed class BooleanOnOffConverter : System.Windows.Data.IValueConverter {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is true ? "ON" : "OFF";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }

    private void ToolbarDiagnostic(string message) {
        if (_lastToolbarDiagnostic == message) return;
        _lastToolbarDiagnostic = message;
        Logger.Info($"Target History toolbar: {message}");
    }

    private void SyncToolbarToggles() {
        if (_imagedToggle is null || _plannedToggle is null) return;
        _syncingToggles = true;
        try {
            _imagedToggle.IsChecked = _settings.ShowHistoricalFields;
            _plannedToggle.IsChecked = _showPlannedFields();
        } finally {
            _syncingToggles = false;
        }
    }

    private void ToolbarImagedChanged(object sender, RoutedEventArgs e) {
        if (!_syncingToggles && _imagedToggle is not null) {
            _setImagedFields(_imagedToggle.IsChecked == true);
            Logger.Info($"Target History toolbar: Imaged clicked; enabled={_imagedToggle.IsChecked == true}");
            _imagedToggle.Dispatcher.BeginInvoke(new Action(() => ValidateSwitch("Imaged", _imagedToggle, System.Windows.Media.Brushes.Lime)), DispatcherPriority.Loaded);
        }
    }

    private void ToolbarPlannedChanged(object sender, RoutedEventArgs e) {
        if (!_syncingToggles && _plannedToggle is not null) {
            _setPlannedFields(_plannedToggle.IsChecked == true);
            Logger.Info($"Target History toolbar: Planned clicked; enabled={_plannedToggle.IsChecked == true}");
            _plannedToggle.Dispatcher.BeginInvoke(new Action(() => ValidateSwitch("Planned", _plannedToggle, System.Windows.Media.Brushes.Yellow)), DispatcherPriority.Loaded);
        }
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
        if (_imagedToggle is not null) {
            _imagedToggle.Checked -= ToolbarImagedChanged;
            _imagedToggle.Unchecked -= ToolbarImagedChanged;
        }
        if (_plannedToggle is not null) {
            _plannedToggle.Checked -= ToolbarPlannedChanged;
            _plannedToggle.Unchecked -= ToolbarPlannedChanged;
        }
        if (_toolbarToggles?.Parent is System.Windows.Controls.Panel toolbarParent) toolbarParent.Children.Remove(_toolbarToggles);
        _toolbarToggles = null;
        _imagedToggle = null;
        _plannedToggle = null;
        _refreshTimer.Tick -= RefreshTimer_Tick;
        if (_overlay?.Parent is System.Windows.Controls.Panel parent) parent.Children.Remove(_overlay);
        _overlay = null;
    }
}
