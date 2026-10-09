using System.IO;
using System.Xml.Linq;
using System.Globalization;
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
    public ObservableCollection<ProfileChoice> AvailableProfiles { get; } = new();
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
        // Keep the PR #93 overlay geometry; field dimensions now come from
        // the immutable profile snapshot selected for each historical target.
        LoadProfiles();
        foreach (var item in data) {
            ApplyStoredOptics(item);
            item.PropertyChanged += Target_PropertyChanged;
            Targets.Add(item);
        }
        PopulateVisibleLists();
        _historicalOverlay.Refresh();
        Logger.Info($"Target History startup: visible imaged={ImagedTargets.Count}, planned={PlannedTargets.Count}");
    }

    private void Target_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(TargetHistoryItem.ProfileId) && sender is TargetHistoryItem target) {
            var profile = AvailableProfiles.FirstOrDefault(p => p.Id == target.ProfileId);
            if (profile is not null) {
                target.ProfileName = profile.Name;
                target.ProfileSensorWidthPixels = profile.Width;
                target.ProfileSensorHeightPixels = profile.Height;
                target.ProfilePixelSizeMicrons = profile.PixelSize;
                target.ProfileFocalLengthMm = profile.FocalLength;
                ApplyStoredOptics(target);
                _store?.SaveMetadata(Targets);
                _historicalOverlay.Refresh();
                // Refresh read-only columns after the profile selection.
                System.Windows.Data.CollectionViewSource.GetDefaultView(ImagedTargets).Refresh();
            }
        }
        if (e.PropertyName == nameof(TargetHistoryItem.Finished)) {
            _store?.SaveMetadata(Targets);
            if (Status != "All") PopulateVisibleLists();
        }
    }

    private void ApplyStoredOptics(TargetHistoryItem item) {
        var focal = item.ProfileFocalLengthMm;
        var pixel = item.ProfilePixelSizeMicrons;
        var scale = focal > 0 && pixel > 0 ? AstroUtil.ArcsecPerPixel(pixel, focal) : 0d;
        item.FieldWidthDegrees = item.ProfileSensorWidthPixels > 0
            ? AstroUtil.ArcsecToDegree(item.ProfileSensorWidthPixels * scale) : 0d;
        item.FieldHeightDegrees = item.ProfileSensorHeightPixels > 0
            ? AstroUtil.ArcsecToDegree(item.ProfileSensorHeightPixels * scale) : 0d;
        item.NotifyProfileFoVChanged();
    }

    private void LoadProfiles() {
        AvailableProfiles.Clear();
        // N.I.N.A. stores named profiles as XML .profile files.
        var roots = new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "Profiles"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NINA", "Profiles")
        };
        foreach (var directory in roots.Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*.profile", SearchOption.TopDirectoryOnly)) {
                try {
                    using var profileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var root = XDocument.Load(profileStream).Root;
                    if (root is null) continue;
                    string? Read(string section, string key) {
                        var node = root.Elements().FirstOrDefault(e => e.Name.LocalName == section);
                        return node?.Elements().FirstOrDefault(e => e.Name.LocalName == key)?.Value;
                    }
                    string? Top(string key) => root.Elements().FirstOrDefault(e => e.Name.LocalName == key)?.Value;
                    double Number(string section, string key) =>
                        double.TryParse(Read(section, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0d;
                    var id = Top("Id");
                    var name = Top("Name");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)
                        || AvailableProfiles.Any(p => p.Id == id)) continue;
                    AvailableProfiles.Add(new ProfileChoice {
                        Id = id, Name = name,
                        Width = Number("FramingAssistantSettings", "CameraWidth"),
                        Height = Number("FramingAssistantSettings", "CameraHeight"),
                        PixelSize = Number("CameraSettings", "PixelSize"),
                        FocalLength = Number("TelescopeSettings", "FocalLength")
                    });
                } catch (Exception ex) {
                    Logger.Error(ex);
                }
            }
        }
        // Also offer the currently active profile if its file was not found.
        var active = _profileService.ActiveProfile;
        var activeId = active.Id.ToString();
        if (!AvailableProfiles.Any(p => p.Id == activeId)) {
            AvailableProfiles.Add(new ProfileChoice {
                Id = activeId, Name = active.Name,
                Width = active.FramingAssistantSettings.CameraWidth,
                Height = active.FramingAssistantSettings.CameraHeight,
                PixelSize = active.CameraSettings.PixelSize,
                FocalLength = active.TelescopeSettings.FocalLength
            });
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

public sealed class ProfileChoice {
    public required string Id { get; init; }
    public required string Name { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double PixelSize { get; init; }
    public double FocalLength { get; init; }
}
