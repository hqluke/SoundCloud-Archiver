using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundCloudExplode.Tracks;

namespace soundCloudArchiver.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private Track? _currentTrack;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _artworkBitmap;

    [ObservableProperty]
    private bool _isPlaylistSelectionVisible = false;

    public ObservableCollection<PlaylistSelectionItem> PlaylistSelectionItems { get; } = new();

    public event Func<Task<bool>>? OnSavePlaylistSelection;
    public event Action? OnCancelPlaylistSelection;

    [RelayCommand]
    private async Task SavePlaylistSelection()
    {
        if (OnSavePlaylistSelection != null)
            await OnSavePlaylistSelection.Invoke();
    }

    [RelayCommand]
    private void CancelPlaylistSelection()
    {
        OnCancelPlaylistSelection?.Invoke();
    }
}
