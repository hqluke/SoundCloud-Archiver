using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using soundCloudArchiver.ViewModels;

namespace soundCloudArchiver.Views;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    public ConfirmationDialog(string message)
        : this()
    {
        DataContext = new ConfirmationDialogViewModel { Message = message };
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
}
