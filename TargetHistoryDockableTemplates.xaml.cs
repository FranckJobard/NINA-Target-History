using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.TargetHistory;

[Export(typeof(ResourceDictionary))]
public partial class TargetHistoryDockableTemplates : ResourceDictionary {
    public TargetHistoryDockableTemplates() {
        InitializeComponent();
    }
}
