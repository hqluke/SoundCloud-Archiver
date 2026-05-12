using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundCloudExplode.Tracks;

namespace soundCloudArchiver.ViewModels;
using soundCloudArchiver.Models;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private Track? _currentTrack;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _artworkBitmap;

    // TODO: DOUBLE BINDING
    [ObservableProperty]
    private bool _showSyncedPlaylists = false;

    [ObservableProperty]
    private bool _isPlaylistSelectionVisible = false;

    [ObservableProperty]
    private bool _showTracksSyncing = false;

    [ObservableProperty]
    private bool _isInitialSetupComplete = false;

    [ObservableProperty]
    private bool _areSyncedPlaylistsCreated = false;

    public ObservableCollection<PlaylistSelectionItem> PlaylistSelectionItems { get; } = new();

    public ObservableCollection<SyncedPlaylistItem> SyncedPlaylistItems { get; } = new();

    public event Func<Task<bool>>? OnSavePlaylistSelection;
    public event Action? OnCancelPlaylistSelection;
    public event Func<Task>? OnCreateSyncedPlaylists;
    public event Func<Task>? OnSyncNow;
    public event Action? OnShowPlaylistSelection;
    public event Func<Task>? OnShowSyncedPlaylistView;

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
        if (!AreSyncedPlaylistsCreated && OnCreateSyncedPlaylists != null)
            await OnCreateSyncedPlaylists.Invoke();

        if (OnShowSyncedPlaylistView != null)
            await OnShowSyncedPlaylistView.Invoke();
    }

    [RelayCommand]
    private void ShowPlaylistSelection()
    {
        OnShowPlaylistSelection?.Invoke();
    }

    [RelayCommand]
    private async Task SyncNow()
    {
        if (OnSyncNow != null)
            await OnSyncNow.Invoke();
    }


}
