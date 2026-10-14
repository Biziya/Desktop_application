using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Frontend.Pages.CoreInformation;

public partial class CoreInformationPage : UserControl
{
    public CoreInformationPage() => InitializeComponent();

    private static bool Allowed(TextBox box, string input)
    {
        var proposed = box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, input);
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

        if (proposed.Length == 0 || proposed == separator)
        {
            return true;
        }

        return CoreInformationViewModel.TryDepth(proposed, out _);
    }

    private void DepthInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox box)
        {
            e.Handled = !Allowed(box, e.Text);
        }
    }

    private void DepthPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox box ||
            e.DataObject.GetData(DataFormats.UnicodeText) is not string text ||
            !Allowed(box, text))
        {
            e.CancelCommand();
        }
    }
}
