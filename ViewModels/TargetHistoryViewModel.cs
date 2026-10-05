using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Forms;
using NINA.TargetHistory.Models;
using NINA.TargetHistory.Services;

namespace NINA.TargetHistory.ViewModels;

public sealed class TargetHistoryViewModel : INotifyPropertyChanged, IDisposable {
    private readonly PluginSettings _settings = new();
    private HistoryStore? _store;
    private SequenceWatcher? _watcher;
    private string _search = "";
    private string _status = "All";
    private readonly object _targetsSync = new();
    private ICollectionView? _view;

    public ObservableCollection<TargetHistoryItem> Targets { get; } = new();
    public ICollectionView View {
        get {
            if (_view is null) {
                _view = CreateView();
                if (_store is not null) {
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher is not null)
                        dispatcher.BeginInvoke(new Action(Rebuild), System.Windows.Threading.DispatcherPriority.Loaded);
                    else
                        Rebuild();
                }
            }
            return _view;
        }
    }

    public string Search {
        get => _search;
        set { if (Set(ref _search, value)) View.Refresh(); }
    }

    public string Status {
        get => _status;
        set { if (Set(ref _status, value)) View.Refresh(); }
    }

    public string SequenceFolder {
        get => _settings.SequenceFolder;
        private set { _settings.SequenceFolder = value; OnPropertyChanged(); }
    }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenAstroBinCommand { get; }
    public RelayCommand SaveMetadataCommand { get; }

    public TargetHistoryViewModel() {
        _settings.Load();
        BrowseCommand = new RelayCommand(_ => Browse());
        RefreshCommand = new RelayCommand(_ => Rebuild());
        OpenAstroBinCommand = new RelayCommand(OpenAstroBin);
        SaveMetadataCommand = new RelayCommand(_ => _store?.SaveMetadata(Targets));

        if (Directory.Exists(SequenceFolder))
            Attach(SequenceFolder);
    }

    private ICollectionView CreateView() {
        BindingOperations.EnableCollectionSynchronization(Targets, _targetsSync);
        var view = CollectionViewSource.GetDefaultView(Targets);
        view.Filter = Filter;
        view.SortDescriptions.Add(new SortDescription(nameof(TargetHistoryItem.Name), ListSortDirection.Ascending));
        return view;
    }

    private bool Filter(object obj) {
        if (obj is not TargetHistoryItem t) return false;
        if (!string.IsNullOrWhiteSpace(Search)
            && !t.Name.Contains(Search, StringComparison.OrdinalIgnoreCase))
            return false;
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
            SelectedPath = Directory.Exists(SequenceFolder) ? SequenceFolder : ""
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        SequenceFolder = dialog.SelectedPath;
        _settings.Save();
        Attach(SequenceFolder);
    }

    private void Attach(string folder) {
        _watcher?.Dispose();
        _store = new HistoryStore(folder);
        _watcher = new SequenceWatcher(folder);
        _watcher.Changed += (_, _) =>
            System.Windows.Application.Current.Dispatcher.Invoke(Rebuild);
        Rebuild();
    }

    private void Rebuild() {
        if (_store is null) return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) {
            dispatcher.Invoke(Rebuild);
            return;
        }

        var data = _store.Rebuild();
        lock (_targetsSync) {
            Targets.Clear();
            foreach (var item in data) Targets.Add(item);
        }
        _view?.Refresh();
    }

    private static void OpenAstroBin(object? parameter) {
        if (parameter is not TargetHistoryItem t || string.IsNullOrWhiteSpace(t.AstroBinUrl)) return;
        if (!Uri.TryCreate(t.AstroBinUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    public void Dispose() {
        _store?.SaveMetadata(Targets);
        _watcher?.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
