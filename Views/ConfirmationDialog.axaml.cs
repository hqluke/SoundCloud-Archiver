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

    public ConfirmationDialog(string title, string message, string checkboxLabel, bool isChecked = false)
        : this()
    {
        Title = title;
        DataContext = new ConfirmationDialogViewModel
        {
            Title = title,
            Message = message,
            ShowCheckbox = true,
            CheckboxLabel = checkboxLabel,
            IsChecked = isChecked,
        };
    }

    public bool IsCheckBoxChecked =>
        DataContext is ConfirmationDialogViewModel vm && vm.IsChecked;

    private void OnOkClicked(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
}
