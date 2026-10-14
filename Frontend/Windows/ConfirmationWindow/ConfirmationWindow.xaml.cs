using System.Windows;
using System.Windows.Media;

namespace Frontend.Windows.ConfirmationWindow;

public partial class ConfirmationWindow : Window
{
    /// <param name="actionBrush">Background of the action button; null keeps the theme's primary blue.</param>
    public ConfirmationWindow(string title, string question, string detail, string action, Brush? actionBrush = null)
    {
        InitializeComponent();
        Title = title;
        Question.Text = question;
        Detail.Text = detail;
        Action.Content = action;

        if (actionBrush is not null)
        {
            Action.Background = actionBrush;
        }
    }

    private void Confirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
