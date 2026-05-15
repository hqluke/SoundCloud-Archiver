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

    public ConfirmationDialog(string title, string message)
        : this()
    {
        Title = title;
        DataContext = new ConfirmationDialogViewModel { Title = title, Message = message };
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
}
