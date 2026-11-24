using System.Windows.Controls;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// Searchable (editable) drillhole ComboBoxes: while the user types, show the filtered list and keep the caret where it
/// was. Opening the dropdown would otherwise select the whole text, so the next key would replace it.
/// </summary>
public static class ComboBoxTyping
{
    /// <summary>
    /// Call from the ComboBox's TextBoxBase.TextChanged handler. Returns false when the change was not typing (the box
    /// does not have the keyboard focus, or <paramref name="isPick"/> says the text came from picking an item).
    /// </summary>
    public static bool OpenListKeepingCaret(object sender, Func<ComboBox, string, bool>? isPick = null)
    {
        if (sender is not ComboBox { IsKeyboardFocusWithin: true } box ||
            box.Template.FindName("PART_EditableTextBox", box) is not TextBox editor ||
            isPick?.Invoke(box, editor.Text) == true)
        {
            return false;
        }

        var caret = editor.CaretIndex;

        if (!box.IsDropDownOpen && editor.Text.Length > 0 && box.Items.Count > 0)
        {
            box.IsDropDownOpen = true;
        }

        editor.SelectionLength = 0;
        editor.CaretIndex = caret;
        return true;
    }
}
