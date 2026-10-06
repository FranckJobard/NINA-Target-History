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

    private bool _syncingSelection;

    private void TargetGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (_syncingSelection || sender is not DataGrid grid || grid.SelectedItem is null) return;

        try {
            _syncingSelection = true;
            if (ReferenceEquals(grid, ImagedTargetsGrid)) {
                PlannedTargetsGrid.SelectedItem = null;
            } else if (ReferenceEquals(grid, PlannedTargetsGrid)) {
                ImagedTargetsGrid.SelectedItem = null;
            }
        } finally {
            _syncingSelection = false;
        }
    }


    private void ImagedTargetsGrid_Sorting(object sender, DataGridSortingEventArgs e) {
        if (sender is DataGrid grid) {
            grid.SelectedItem = null;
        }
    }

    private void TargetGrid_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
        if (sender is DataGrid grid && !grid.IsKeyboardFocusWithin) {
            grid.SelectedItem = null;
        }
    }

    private void TargetGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        if (DataContext is not TargetHistoryViewModel vm) return;

        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source is not null && source is not DataGridRow) {
            source = source switch {
                System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    => System.Windows.Media.VisualTreeHelper.GetParent(source),
                FrameworkContentElement contentElement
                    => contentElement.Parent,
                _ => null
            };
        }

        if (source is DataGridRow row && row.Item is TargetHistoryItem target) {
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
