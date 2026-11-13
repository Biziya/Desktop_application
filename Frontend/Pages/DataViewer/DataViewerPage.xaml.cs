using System.Windows.Controls;

namespace Frontend.Pages.DataViewer;

// View only: opens the drillhole list while typing, keeping the caret where it was. Filtering belongs to the VM.
public partial class DataViewerPage : UserControl
{
    public DataViewerPage() => InitializeComponent();

    private void DrillholeTyped(object sender, TextChangedEventArgs e)
    {
        if (sender is not ComboBox { IsKeyboardFocusWithin: true } box ||
            box.Template.FindName("PART_EditableTextBox", box) is not TextBox editor)
        {
            return;
        }

        var caret = editor.CaretIndex;

        if (!box.IsDropDownOpen && editor.Text.Length > 0 && box.Items.Count > 0)
        {
            box.IsDropDownOpen = true;
        }

        editor.SelectionLength = 0;
        editor.CaretIndex = caret;
    }
}
