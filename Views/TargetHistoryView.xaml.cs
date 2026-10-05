using System.Windows;
using NINA.TargetHistory.ViewModels;

namespace NINA.TargetHistory.Views;

public partial class TargetHistoryView : System.Windows.Controls.UserControl {
    public TargetHistoryView() { InitializeComponent(); }

    private void TargetHistoryView_Loaded(object sender, RoutedEventArgs e) {
        if (DataContext is TargetHistoryViewModel vm) vm.RefreshAfterViewLoaded();
    }
}
