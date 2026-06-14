using System.Windows;
using System.Windows.Input;
using X4StationBuilder.App.ViewModels;

namespace X4StationBuilder.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Double-clicking a ware in the picker adds it to the desired factories list.</summary>
    private void PickerItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.AddWareCommand.CanExecute(null))
        {
            vm.AddWareCommand.Execute(null);
        }
    }
}
