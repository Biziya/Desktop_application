using System.Windows.Controls;
using Frontend.Api.Measurements;
using Frontend.Shared.Infrastructure;

namespace Frontend.Pages.DataViewer;

// View only: opens the drillhole list while typing, keeping the caret where it was. Filtering belongs to the VM.
public partial class DataViewerPage : UserControl
{
    public DataViewerPage() => InitializeComponent();

    // A pick sets the text to its Hole ID: that is not typing, so the list stays closed.
    private void DrillholeTyped(object sender, TextChangedEventArgs e) =>
        ComboBoxTyping.OpenListKeepingCaret(sender, (box, text) => box.SelectedItem is UploadedDrillhole picked && picked.HoleId == text);
}
