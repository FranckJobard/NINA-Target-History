using System;
using System.Windows;
using System.Windows.Threading;
using NINA.TargetHistory.ViewModels;
using NINA.TargetHistory.Models;
using System.Windows.Controls;
using System.Windows.Input;

namespace NINA.TargetHistory.Views;

public partial class TargetHistoryView : System.Windows.Controls.UserControl {
    private bool _initialized;

    public TargetHistoryView() {
        InitializeComponent();
        DataContextChanged += TargetHistoryView_DataContextChanged;
    }

    private void TargetHistoryView_Loaded(object sender, RoutedEventArgs e) {
        QueueInitialization();
    }

    private void TargetHistoryView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (IsLoaded) QueueInitialization();
    }

    private void TargetRow_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        if (sender is DataGridRow row
            && row.Item is TargetHistoryItem target
            && DataContext is TargetHistoryViewModel vm) {
            vm.OpenTargetInFraming(target);
            e.Handled = true;
        }
    }

    private void QueueInitialization() {
        if (_initialized) return;

        // Run after N.I.N.A. has completed binding/layout of the dockable.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
            if (_initialized || DataContext is not TargetHistoryViewModel vm) return;
            _initialized = true;
            vm.RefreshAfterViewLoaded();
        }));
    }
}
