using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MultiChatOverlay.App.Settings;

public partial class TextInputDialog : Window
{
    public string ResponseText { get; private set; } = string.Empty;

    public TextInputDialog(string title, string prompt, string defaultText = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputTextBox.Text = defaultText;
        InputTextBox.SelectAll();
        InputTextBox.Focus();
        Loaded += (_, _) => InputTextBox.Focus();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ResponseText = InputTextBox.Text;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OK_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            Cancel_Click(sender, e);
        }
    }
}