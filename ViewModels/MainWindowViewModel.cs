using System;
using System.Collections.Generic;
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
    [NotifyPropertyChangedFor(nameof(ShowPreparingText))]
    private Track? _currentTrack;

    [ObservableProperty]
    private string _currentArtist = "";

    public bool HasTrack => CurrentTrack != null;
    public bool IsNullTrack => CurrentTrack == null;
    public bool ShowPreparingText => IsNullTrack && !ShowAlternativeDownload;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _artworkBitmap;

    [ObservableProperty]
    private bool _showSyncedPlaylists;

    [ObservableProperty]
    private bool _isPlaylistSelectionVisible;

    [ObservableProperty]
    private bool _showTracksSyncing;

    [ObservableProperty]
    private string _alternativeDownloadStatus = "";

    [ObservableProperty]
    private int _alternativeDownloadPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPreparingText))]
    private bool _showAlternativeDownload;

    [ObservableProperty]
    private string _fallbackTrackTitle = "";

    [ObservableProperty]
    private string _fallbackArtist = "";

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _fallbackArtworkBitmap;

    [ObservableProperty]
    private bool _isInitialSetupComplete;

    [ObservableProperty]
    private bool _showPotentiallyDeletedTracks;

    [ObservableProperty]
    private bool _showSetup;

    [ObservableProperty]
    private string _setupProfileUrl = "";

    [ObservableProperty]
    private string _setupDownloadPath = "";

    [ObservableProperty]
    private bool _showManageTrackPlaylists;

    [ObservableProperty]
    private string _manageTrackSearch = "";

    public List<ManageOutOfSyncTracksViewModel> AllManageableTracks { get; } = new();

    public ObservableCollection<ManageOutOfSyncTracksViewModel> ManageableTracks { get; } = new();

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
    public event Func<Task<bool>>? OnSaveSetup;
    public event Action? OnShowSetup;
    public event Func<Task>? OnShowManageTrackPlaylists;
    public event Func<Task<bool>>? OnSaveManageTrackPlaylists;
    public event Action? OnCancelManageTrackPlaylists;

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

    [RelayCommand]
    private async Task SaveSetup()
    {
        if (OnSaveSetup != null)
            await OnSaveSetup.Invoke();
    }

    [RelayCommand]
    private void OpenSetup()
    {
        OnShowSetup?.Invoke();
    }

    [RelayCommand]
    private void SearchManageTracks()
    {
        var search = ManageTrackSearch?.Trim().ToLowerInvariant() ?? "";
        ManageableTracks.Clear();
        foreach (var track in AllManageableTracks)
        {
            if (string.IsNullOrEmpty(search) ||
                track.Title.ToLowerInvariant().Contains(search) ||
                track.Artist.ToLowerInvariant().Contains(search))
                ManageableTracks.Add(track);
        }
    }

    [RelayCommand]
    private void ClearManageTrackSearch()
    {
        ManageTrackSearch = "";
        SearchManageTracks();
    }

    [RelayCommand]
    private async Task OpenManageTrackPlaylists()
    {
        if (OnShowManageTrackPlaylists != null)
            await OnShowManageTrackPlaylists.Invoke();
    }

    [RelayCommand]
    private async Task SaveManageTrackPlaylists()
    {
        if (OnSaveManageTrackPlaylists != null)
            await OnSaveManageTrackPlaylists.Invoke();
    }

    [RelayCommand]
    private void CancelManageTrackPlaylists()
    {
        foreach (var track in AllManageableTracks)
        {
            foreach (var playlist in track.Playlists)
            {
                playlist.KeepInPlaylist = track.OriginalPlaylistIds.Contains(playlist.PlaylistId);
            }
        }
    }

    [RelayCommand]
    private void BackManageTrackPlaylists()
    {
        foreach (var track in AllManageableTracks)
        {
            foreach (var playlist in track.Playlists)
            {
                playlist.KeepInPlaylist = track.OriginalPlaylistIds.Contains(playlist.PlaylistId);
            }
        }
        OnCancelManageTrackPlaylists?.Invoke();
    }
}
