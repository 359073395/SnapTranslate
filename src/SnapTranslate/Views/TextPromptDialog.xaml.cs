using System.Windows;

namespace SnapTranslate.Views;

public partial class TextPromptDialog : Window
{
    public TextPromptDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => InputTextBox.Focus();
    }

    public string TextValue => InputTextBox.Text.Trim();

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (TextValue.Length == 0)
        {
            return;
        }

        DialogResult = true;
    }
}
