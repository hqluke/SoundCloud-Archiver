using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace soundCloudArchiver.ViewModels;

public partial class ConfirmationDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private bool _showCheckbox;

    [ObservableProperty]
    private string _checkboxLabel = "";

    [ObservableProperty]
    private bool _isChecked;

    public TaskCompletionSource<bool>? Result { get; set; }

    [RelayCommand]
    private void Ok() => Result?.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => Result?.TrySetResult(false);
}
