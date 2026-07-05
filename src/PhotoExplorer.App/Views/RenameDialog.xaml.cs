using System.Windows;
using System.Windows.Input;

namespace PhotoExplorer.App.Views;

public partial class RenameDialog : Window
{
    public string NewName => NameBox.Text;

    public RenameDialog(string currentName)
    {
        InitializeComponent();
        NameBox.Text = currentName;
        Loaded += (_, _) => { NameBox.SelectAll(); NameBox.Focus(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        DialogResult = true;
    }
}
