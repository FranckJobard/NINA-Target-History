using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using NINA.TargetHistory.Models;
using NINA.TargetHistory.Services;
using NINA.Core.Utility;
using NINA.Core.Enum;
using NINA.Astrometry;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.Interfaces.Mediator;

namespace NINA.TargetHistory.ViewModels;

public sealed class TargetHistoryViewModel : INotifyPropertyChanged, IDisposable {
    private readonly PluginSettings _settings = new();
    private readonly IProfileService _profileService;
    private readonly IFramingAssistantVM _framingAssistantVM;
    private readonly IApplicationMediator _applicationMediator;
    private HistoryStore? _store;
    private SequenceWatcher? _watcher;
    private readonly HistoricalFramingOverlay _historicalOverlay;
    private string _search = "";
    private string _status = "All";
    private bool _showPlannedFields;
    public ObservableCollection<TargetHistoryItem> Targets { get; } = new();
    public ObservableCollection<TargetHistoryItem> ImagedTargets { get; } = new();
    public ObservableCollection<TargetHistoryItem> PlannedTargets { get; } = new();

    public string Search {
        get => _search;
        set { if (Set(ref _search, value)) PopulateVisibleLists(); }
    }

    public string Status {
        get => _status;
        set { if (Set(ref _status, value)) PopulateVisibleLists(); }
    }

    public string SequenceFolder {
        get => _settings.SequenceFolder;
        private set { _settings.SequenceFolder = value; OnPropertyChanged(); }
    }

    public bool ShowHistoricalFields {
        get => _settings.ShowHistoricalFields;
        set {
            if (_settings.ShowHistoricalFields == value) return;
            _settings.ShowHistoricalFields = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool ShowPlannedFields {
        get => _showPlannedFields;
        set {
            if (!Set(ref _showPlannedFields, value)) return;
            _historicalOverlay.Refresh();
        }
    }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenAstroBinCommand { get; }
    public RelayCommand SaveMetadataCommand { get; }

    public TargetHistoryViewModel(IProfileService profileService, IFramingAssistantVM framingAssistantVM, IApplicationMediator applicationMediator) {
        _profileService = profileService;
        _framingAssistantVM = framingAssistantVM;
        _applicationMediator = applicationMediator;
        _historicalOverlay = new HistoricalFramingOverlay(
            framingAssistantVM,
            () => Targets,
            () => ShowPlannedFields,
            value => ShowHistoricalFields = value,
            value => ShowPlannedFields = value);
        var ninaDefaultSequenceFolder = profileService.ActiveProfile.SequenceSettings.DefaultSequenceFolder;
        _settings.Load();
        if (string.IsNullOrWhiteSpace(SequenceFolder) && Directory.Exists(ninaDefaultSequenceFolder)) {
            SequenceFolder = Path.GetFullPath(ninaDefaultSequenceFolder);
            _settings.Save();
            Logger.Info($"Target History startup: adopted N.I.N.A. profile sequence folder='{SequenceFolder}'");
        }
        BrowseCommand = new RelayCommand(_ => Browse());
        RefreshCommand = new RelayCommand(_ => Rebuild());
        OpenAstroBinCommand = new RelayCommand(OpenAstroBin);
        SaveMetadataCommand = new RelayCommand(_ => _store?.SaveMetadata(Targets));

        // Do not scan or touch WPF collections here. N.I.N.A. can construct
        // dockables off the UI thread. Initialization is deferred until the view is Loaded.
    }

    private bool MatchesSearch(object obj) {
        if (obj is not TargetHistoryItem t) return false;
        if (!string.IsNullOrWhiteSpace(Search)
            && !t.Name.Contains(Search, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private bool MatchesImagedStatus(TargetHistoryItem t) {
        return Status switch {
            "In progress" => !t.Finished,
            "Finished" => t.Finished,
            _ => true
        };
    }

    private void Browse() {
        using var dialog = new FolderBrowserDialog {
            Description = "Select the N.I.N.A. sequences folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(SequenceFolder) ? SequenceFolder : "",
            InitialDirectory = Directory.Exists(SequenceFolder) ? SequenceFolder : ""
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        SequenceFolder = dialog.SelectedPath;
        _settings.Save();
        Attach(SequenceFolder);
    }

    private void Attach(string folder) {
        Logger.Info($"Target History startup: Attach('{folder}')");
        _watcher?.Dispose();
        _store = new HistoryStore(folder);
        _watcher = new SequenceWatcher(folder);
        _watcher.Changed += (_, _) =>
            System.Windows.Application.Current.Dispatcher.Invoke(Rebuild);
        Rebuild();
    }

    public void RefreshAfterViewLoaded() {
        Logger.Info($"Target History startup: RefreshAfterViewLoaded; folder='{SequenceFolder}'; exists={Directory.Exists(SequenceFolder)}");
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) {
            dispatcher.BeginInvoke(new Action(RefreshAfterViewLoaded));
            return;
        }

        if (Directory.Exists(SequenceFolder)) {
            Attach(SequenceFolder);
        } else {
            PopulateVisibleLists();
        }
    }

    private void Rebuild() {
        if (_store is null) return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) {
            dispatcher.Invoke(Rebuild);
            return;
        }

        var data = _store.Rebuild();
        Logger.Info($"Target History startup: Rebuild returned {data.Count} targets");
        foreach (var existing in Targets) existing.PropertyChanged -= Target_PropertyChanged;
        Targets.Clear();
        // Read the optical configuration from the active N.I.N.A. profile,
        // not the Framing Assistant's potentially edited framing values.
        // RA/Dec and PositionAngle still come from the sequence JSON.
        var profile = _profileService.ActiveProfile;
        var focalLength = profile.TelescopeSettings.FocalLength;
        var pixelSize = profile.CameraSettings.PixelSize;
        var sensorWidthPixels = profile.FramingAssistantSettings.CameraWidth;
        var sensorHeightPixels = profile.FramingAssistantSettings.CameraHeight;
        var arcsecPerPixel = focalLength > 0 && pixelSize > 0
            ? AstroUtil.ArcsecPerPixel(pixelSize, focalLength) : 0d;
        var fieldWidthDegrees = sensorWidthPixels > 0
            ? AstroUtil.ArcsecToDegree(sensorWidthPixels * arcsecPerPixel) : 0d;
        var fieldHeightDegrees = sensorHeightPixels > 0
            ? AstroUtil.ArcsecToDegree(sensorHeightPixels * arcsecPerPixel) : 0d;

        foreach (var item in data) {
            item.FieldWidthDegrees = fieldWidthDegrees;
            item.FieldHeightDegrees = fieldHeightDegrees;
            item.PropertyChanged += Target_PropertyChanged;
            Targets.Add(item);
        }
        PopulateVisibleLists();
        _historicalOverlay.Refresh();
        Logger.Info($"Target History startup: visible imaged={ImagedTargets.Count}, planned={PlannedTargets.Count}");
    }

    private void Target_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(TargetHistoryItem.Finished)) {
            _store?.SaveMetadata(Targets);
            if (Status != "All") PopulateVisibleLists();
        }
    }

    private void PopulateVisibleLists() {
        ImagedTargets.Clear();
        PlannedTargets.Clear();
        foreach (var item in Targets.Where(MatchesSearch).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)) {
            if (item.TotalSeconds > 0) {
                if (MatchesImagedStatus(item)) ImagedTargets.Add(item);
            } else {
                // Planned targets have no progress status; only search applies.
                PlannedTargets.Add(item);
            }
        }
    }

    public async void OpenTargetInFraming(TargetHistoryItem target) {
        if (target is null) return;
        try {
            var coordinates = new Coordinates(target.RaDegrees, target.DecDegrees, Epoch.J2000, Coordinates.RAType.Degrees);
            var dso = new DeepSkyObject(target.Name, coordinates, _profileService.ActiveProfile.AstrometrySettings.Horizon) {
                RotationPositionAngle = target.PositionAngle
            };

            // Use N.I.N.A.'s own Framing Assistant pipeline. It already takes its
            // camera width/height from FramingAssistantSettings, pixel size from
            // CameraSettings and focal length from TelescopeSettings.
            _applicationMediator.ChangeTab(ApplicationTab.FRAMINGASSISTANT);
            await _framingAssistantVM.SetCoordinates(dso);
            _historicalOverlay.Refresh();
        } catch (Exception ex) {
            Logger.Error(ex);
        }
    }

    private static void OpenAstroBin(object? parameter) {
        if (parameter is not TargetHistoryItem t || string.IsNullOrWhiteSpace(t.AstroBinUrl)) return;
        if (!Uri.TryCreate(t.AstroBinUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    public void Dispose() {
        _store?.SaveMetadata(Targets);
        foreach (var item in Targets) item.PropertyChanged -= Target_PropertyChanged;
        _watcher?.Dispose();
        _historicalOverlay.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
