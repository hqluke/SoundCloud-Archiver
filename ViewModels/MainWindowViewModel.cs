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
    [NotifyPropertyChangedFor(nameof(HasTrack))]
    [NotifyPropertyChangedFor(nameof(IsNullTrack))]
    private Track? _currentTrack;

    [ObservableProperty]
    private string _currentArtist = "";

    public bool HasTrack => CurrentTrack != null;
    public bool IsNullTrack => CurrentTrack == null;

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

    [ObservableProperty]
    private bool _showPotentiallyDeletedTracks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoadingPlaylists))]
    private bool _isLoadingPlaylists;

    public bool IsNotLoadingPlaylists => !IsLoadingPlaylists;

    public ObservableCollection<PlaylistViewModel> AllPlaylists { get; } = new();

    public ObservableCollection<PlaylistViewModel> TrackedPlaylists { get; } = new();

    public ObservableCollection<PotentiallyDeletedTrackViewModel> PotentiallyDeletedTracks { get; } =
        new();

    public event Func<Task<bool>>? OnSavePlaylistSelection;
    public event Action? OnCancelPlaylistSelection;
    public event Func<Task>? OnCreateSyncedPlaylists;
    public event Func<Task>? OnSyncNow;
    public event Func<Task>? OnShowPlaylistSelection;
    public event Action? OnShowSyncedPlaylistView;
    public event Func<Task<bool>>? OnPotentiallyDeletedTracksContinue;

    [RelayCommand]
    private async Task ContinueFromPotentiallyDeletedTracks()
    {
        if (OnPotentiallyDeletedTracksContinue != null)
            await OnPotentiallyDeletedTracksContinue.Invoke();
    }

    [RelayCommand]
    private async Task KeepAllTracks()
    {
        foreach (var track in PotentiallyDeletedTracks)
        foreach (var playlist in track.Playlists)
            playlist.KeepInPlaylist = true;
        if (OnPotentiallyDeletedTracksContinue != null)
            await OnPotentiallyDeletedTracksContinue.Invoke();
    }

    [RelayCommand]
    private async Task DeleteAllTracks()
    {
        foreach (var track in PotentiallyDeletedTracks)
        foreach (var playlist in track.Playlists)
            playlist.KeepInPlaylist = false;
        if (OnPotentiallyDeletedTracksContinue != null)
            await OnPotentiallyDeletedTracksContinue.Invoke();
    }

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
