using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Frontend.Pages.CoreInformation;

public partial class CoreInformationPage : UserControl
{
    // The user moved the dropdown highlight with the arrow keys since the last keystroke: Enter then picks that item
    // (the ComboBox's own behaviour) instead of the closest match.
    private bool arrowed;
    private CoreInformationViewModel? observed;

    public CoreInformationPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe(DataContext as CoreInformationViewModel);
        Unloaded += (_, _) => Observe(null);
        Loaded += (_, _) => Observe(DataContext as CoreInformationViewModel);
    }

    // Opening the Bluetooth device dropdown reads the device list again, so a newly paired CT007-M appears.
    private void BluetoothDropDownOpened(object sender, EventArgs e) =>
        (DataContext as CoreInformationViewModel)?.RefreshBluetoothDevicesCommand.Execute(null);

    // View-only: while the user types in the Drillhole box, show the filtered list and keep the caret where it was
    // (opening the dropdown would otherwise select the whole text, so the next key would replace it).
    private void DrillholeTyped(object sender, TextChangedEventArgs e)
    {
        if (sender is not ComboBox { IsKeyboardFocusWithin: true } box ||
            box.Template.FindName("PART_EditableTextBox", box) is not TextBox editor)
        {
            return;
        }

        arrowed = false;
        var caret = editor.CaretIndex;

        if (!box.IsDropDownOpen && editor.Text.Length > 0 && box.Items.Count > 0)
        {
            box.IsDropDownOpen = true;
        }

        editor.SelectionLength = 0;
        editor.CaretIndex = caret;
    }

    // Enter picks the highlighted closest match (unless the arrow keys chose another entry); Escape keeps the
    // ComboBox's own behaviour (closes the list).
    private void DrillholeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
        {
            arrowed = true;
            return;
        }

        if (e.Key == Key.Enter && !arrowed && sender is ComboBox box && DataContext is CoreInformationViewModel vm && vm.CommitClosest())
        {
            box.IsDropDownOpen = false;
            e.Handled = true;
        }
    }

    private void Observe(CoreInformationViewModel? vm)
    {
        if (observed is not null)
        {
            observed.PropertyChanged -= OnViewModelChanged;
        }

        observed = vm;

        if (vm is not null)
        {
            vm.PropertyChanged += OnViewModelChanged;
        }
    }

    // Scrolls the (virtualized) list so the closest match is visible.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CoreInformationViewModel.ClosestOption) || observed?.ClosestOption is not { } closest ||
            !DrillholeBox.IsDropDownOpen)
        {
            return;
        }

        var index = DrillholeBox.Items.IndexOf(closest);

        if (index >= 0 && DrillholeBox.Template.FindName("PART_Popup", DrillholeBox) is Popup { Child: { } child } &&
            Find<VirtualizingStackPanel>(child) is { } panel)
        {
            panel.BringIndexIntoViewPublic(index);
        }
    }

    private static T? Find<T>(DependencyObject element) where T : DependencyObject
    {
        if (element is T found)
        {
            return found;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            if (Find<T>(VisualTreeHelper.GetChild(element, i)) is { } result)
            {
                return result;
            }
        }

        return null;
    }
}
