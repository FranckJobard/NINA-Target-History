using System.Windows;
using NINA.TargetHistory.ViewModels;

namespace NINA.TargetHistory.Views;

public partial class TargetHistoryView : System.Windows.Controls.UserControl {
    private bool _initialized;

    public TargetHistoryView() {
        InitializeComponent();
        DataContextChanged += TargetHistoryView_DataContextChanged;
    }

    private void TargetHistoryView_Loaded(object sender, RoutedEventArgs e) {
        TryInitialize();
    }

    private void TargetHistoryView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (IsLoaded) TryInitialize();
    }

    private void TryInitialize() {
        if (_initialized || DataContext is not TargetHistoryViewModel vm) return;
        _initialized = true;
        vm.RefreshAfterViewLoaded();
    }
}
