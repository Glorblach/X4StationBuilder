using System.Windows;

namespace X4StationBuilder.App.Views;

/// <summary>
/// Interaction logic for SettingsWindow.xaml. A modal editor for user preferences (name prefix +
/// default docks). Closes with <c>DialogResult = true</c> on Save; the caller persists the changes.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
