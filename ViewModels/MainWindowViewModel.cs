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
    private bool _showSyncedPlaylists;

    [ObservableProperty]
    private bool _isPlaylistSelectionVisible;

    [ObservableProperty]
    private bool _showTracksSyncing;

    [ObservableProperty]
    private bool _isInitialSetupComplete;

    public ObservableCollection<PlaylistViewModel> AllPlaylists { get; } = new();

    public ObservableCollection<PlaylistViewModel> TrackedPlaylists { get; } = new();

    public event Func<Task<bool>>? OnSavePlaylistSelection;
    public event Action? OnCancelPlaylistSelection;
    public event Func<Task>? OnCreateSyncedPlaylists;
    public event Func<Task>? OnSyncNow;
    public event Func<Task>? OnShowPlaylistSelection;
    public event Action? OnShowSyncedPlaylistView;

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

    [RelayCommand]
    private async Task CreateSyncedPlaylists()
    {
        if (OnCreateSyncedPlaylists != null)
            await OnCreateSyncedPlaylists.Invoke();
    }

    [RelayCommand]
    private async Task ShowSyncedPlaylistView()
    {
        if (OnCreateSyncedPlaylists != null)
            await OnCreateSyncedPlaylists.Invoke();

        OnShowSyncedPlaylistView?.Invoke();
    }

    [RelayCommand]
    private async Task ShowPlaylistSelection()
    {
        if (OnShowPlaylistSelection != null)
            await OnShowPlaylistSelection.Invoke();
    }

    [RelayCommand]
    private async Task SyncNow()
    {
        if (OnSyncNow != null)
            await OnSyncNow.Invoke();
    }
}
