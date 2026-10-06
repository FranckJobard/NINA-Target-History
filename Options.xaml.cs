using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.TargetHistory;

[Export(typeof(ResourceDictionary))]
public partial class Options : ResourceDictionary {
    public Options() {
        InitializeComponent();
    }
}
