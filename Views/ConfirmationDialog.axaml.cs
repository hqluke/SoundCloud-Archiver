using System.Threading.Tasks;
using Avalonia.Controls;
using soundCloudArchiver.ViewModels;

namespace soundCloudArchiver.Views;

public partial class ConfirmationDialog : Window
{
    private readonly ConfirmationDialogViewModel? _viewModel;

    // Parameterless constructor for Avalonia XAML loader
    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    public ConfirmationDialog(string message) : this()
    {
        _viewModel = new ConfirmationDialogViewModel { Message = message };
        DataContext = _viewModel;

        _viewModel.Result = new TaskCompletionSource<bool>();

        Closed += (_, _) =>
        {
            _viewModel.Result.TrySetResult(false);
        };
    }

    public new Task<bool> ShowDialog(Window owner)
    {
        base.ShowDialog(owner);
        return _viewModel!.Result!.Task;
    }
}
