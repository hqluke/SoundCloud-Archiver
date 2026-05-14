using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Configuration;
using soundCloudArchiver.Models;
using soundCloudArchiver.Services;
using soundCloudArchiver.ViewModels;
using SoundCloudExplode;
using SoundCloudExplode.Playlists;

namespace soundCloudArchiver.Views;

public partial class MainWindow : Window
{
    const long LikedPlaylistId = -1;

    private SoundCloudClient? _soundcloud;
    private SyncService? _syncService;
    private FolderService? _folderService;
    private ArtworkService? _artwork;

    private string _profileUrl = "";
    private string _archivePath = "";
    private string _tracksPath => Path.Combine(_archivePath, "tracks");
    private string _playlistsPath => Path.Combine(_archivePath, "playlists");
    private string _artworkPath => Path.Combine(_archivePath, "artwork");
    private string _likedTracksPath => Path.Combine(_archivePath, "playlists", "liked");

    private bool _IsLikedPlaylistSelected = false;

    private Manifest _manifest = new();
    private readonly string _manifestPath = "manifest.json";

    private TaskCompletionSource<bool> _playlistSelectionComplete = new();

    public MainWindow()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.local.json", optional: true)
            .Build();

        Console.WriteLine("Sound cloud client created");
        _profileUrl = config["SoundCloud:ProfileUrl"]!;
        _archivePath = config["Archiver:DownloadPath"]!;

        if (!Directory.Exists(_archivePath))
            Directory.CreateDirectory(_archivePath);

        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        Opened += async (_, _) => await InitializeAsync();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.OnSavePlaylistSelection += OnSavePlaylistSelection;
            vm.OnCancelPlaylistSelection += OnCancelPlaylistSelection;
            vm.OnSyncNow += OnSyncNow;
            vm.OnCreateSyncedPlaylists += OnCreateSyncedPlaylists;
            vm.OnShowPlaylistSelection += OnShowPlaylistSelection;
            vm.OnShowSyncedPlaylistView += OnShowSyncedPlaylistView;
            DataContextChanged -= OnDataContextChanged;
        }
    }

    private async Task InitializeAsync()
    {
        using var http = new HttpClient();

        var tempClient = new SoundCloudClient();
        var clientId = await tempClient.GetClientIdAsync();
        _soundcloud = new SoundCloudClient(clientId);
        Console.WriteLine("App settings loaded");

        _manifest = ManifestStore.Load(_manifestPath, _likedTracksPath);

        _artwork = new ArtworkService(http, _artworkPath);
        _syncService = new SyncService(_soundcloud, _profileUrl, _tracksPath, _artwork);
        _folderService = new FolderService(_archivePath, _likedTracksPath);

        if (!_manifest.AppState.IsInitialSetupComplete)
        {
            _playlistSelectionComplete = new TaskCompletionSource<bool>();
            await ShowPlaylistSelectionAsync();
            var saved = await _playlistSelectionComplete.Task;
            if (saved)
                await SyncTracksAsync();
        }
        else
        {
            await PopulateTrackedPlaylists();
            _IsLikedPlaylistSelected =
                _manifest.TrackedPlaylists[LikedPlaylistId].TrackIds.Count > 0;
            await SyncTracksAsync();
        }
    }

    private async Task PopulateTrackedPlaylists()
    {
        var vm = (MainWindowViewModel)DataContext!;
        vm.TrackedPlaylists.Clear();

        foreach (var (id, tracked) in _manifest.TrackedPlaylists)
        {
            var item = new PlaylistViewModel(tracked);
            if (File.Exists(tracked.ArtworkPath))
                item.ArtworkBitmap = new Bitmap(tracked.ArtworkPath);
            else
                item.ArtworkBitmap = await _artwork!.FetchBitmap(null);

            vm.TrackedPlaylists.Add(item);
        }

        var liked = vm.TrackedPlaylists.FirstOrDefault(p => p.IsLiked);
        if (liked != null && liked.ArtworkBitmap == null)
            liked.ArtworkBitmap = await _artwork!.FetchBitmap(null);
    }

    private async Task ShowPlaylistSelectionAsync()
    {
        var vm = (MainWindowViewModel)DataContext!;

        vm.IsPlaylistSelectionVisible = true;

        if (vm.AllPlaylists.Count > 0)
        {
            foreach (var item in vm.AllPlaylists)
            {
                item.IsSelected = item.IsLiked
                    ? _manifest.TrackedPlaylists[LikedPlaylistId].TrackIds.Count > 0
                    : _manifest.TrackedPlaylists.ContainsKey(item.Id);
            }
            vm.IsPlaylistSelectionVisible = true;
            return;
        }

        try
        {
            if (!Directory.Exists(_artworkPath))
                Directory.CreateDirectory(_artworkPath);

            Console.WriteLine("Fetching playlists for selection...");
            var allPlaylists = new List<Playlist>();
            await foreach (var playlist in _soundcloud!.Users.GetPlaylistsAsync(_profileUrl))
            {
                allPlaylists.Add(playlist);
                Console.WriteLine($"Fetched playlist: {playlist.Title} (ID: {playlist.Id})");
            }
            allPlaylists.Add(new Playlist { Title = "Liked_Songs", Permalink = "liked" });

            Console.WriteLine($"Total playlists fetched: {allPlaylists.Count}");

            Console.WriteLine("Fetching artwork for playlists...");
            foreach (var playlist in allPlaylists)
            {
                var item = new PlaylistViewModel(playlist);
                item.IsSelected = item.IsLiked
                    ? _manifest.TrackedPlaylists[LikedPlaylistId].TrackIds.Count > 0
                    : _manifest.TrackedPlaylists.ContainsKey(item.Id);

                var artworkPath = await _artwork!.FetchAndSaveArtwork(
                    playlist.Title!,
                    playlist.ArtworkUrl
                );
                if (File.Exists(artworkPath))
                    item.ArtworkBitmap = new Bitmap(artworkPath);

                vm.AllPlaylists.Add(item);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ShowPlaylistSelectionAsync: {ex.Message}");
        }
    }

    private async Task<bool> OnSavePlaylistSelection()
    {
        Console.WriteLine("Saving playlist selection");
        var vm = (MainWindowViewModel)DataContext!;

        var toTrack = new List<PlaylistViewModel>();
        var toUntrack = new List<PlaylistViewModel>();

        foreach (var item in vm.AllPlaylists)
        {
            var tracked = item.IsLiked
                ? _manifest.TrackedPlaylists[LikedPlaylistId].TrackIds.Count > 0
                : _manifest.TrackedPlaylists.ContainsKey(item.Id);
            if (item.IsSelected && !tracked)
                toTrack.Add(item);
            else if (!item.IsSelected && tracked)
                toUntrack.Add(item);
        }

        var message = "";
        if (toTrack.Count > 0)
        {
            message += $"Start tracking {toTrack.Count} playlists:\n";
            foreach (var item in toTrack)
                message += $"  \u2022 {item.Title}_{item.Permalink}\n";
        }
        if (toUntrack.Count > 0)
        {
            if (message.Length > 0)
                message += "\n";
            message += $"Untrack {toUntrack.Count} playlists:\n";
            foreach (var item in toUntrack)
                message += $"  \u2022 {item.Title}_{item.Permalink}\n";
        }

        if (string.IsNullOrEmpty(message))
        {
            vm.IsPlaylistSelectionVisible = false;
            _playlistSelectionComplete.TrySetResult(true);
            return true;
        }

        message += "\nDo you want to proceed?";

        var dialog = new ConfirmationDialog(message);
        var result = await dialog.ShowDialog<bool>(this);

        if (!result)
            return false;

        foreach (var item in toTrack)
        {
            item.IsTracked = true;

            if (item.IsLiked)
            {
                _IsLikedPlaylistSelected = true;
                _manifest.TrackedPlaylists[LikedPlaylistId] = new TrackedPlaylist
                {
                    Id = LikedPlaylistId,
                    Permalink = item.Permalink,
                    Title = item.Title,
                    PermalinkUrl = "",
                    FolderPath = _likedTracksPath,
                };
                continue;
            }

            var folderName = $"{item.Title}_{item.Permalink}".Replace("/", "_");
            var folderPath = Path.Combine(_archivePath, "playlists", folderName);
            var safeTitle = string.Concat(item.Title.Split(Path.GetInvalidFileNameChars()));
            var artworkPath = Path.Combine(_artworkPath, safeTitle + ".jpg");

            item.ArtworkPath = artworkPath;
            item.FolderPath = folderPath;

            _manifest.TrackedPlaylists[item.Id] = new TrackedPlaylist
            {
                Id = item.Id,
                Permalink = item.Permalink,
                Title = item.Title,
                PermalinkUrl = item.PermalinkUrl ?? "",
                ArtworkPath = artworkPath,
                FolderPath = folderPath,
            };
        }

        foreach (var item in toUntrack)
        {
            item.IsTracked = false;

            if (item.IsLiked)
            {
                _IsLikedPlaylistSelected = false;
                _manifest.TrackedPlaylists[LikedPlaylistId].TrackIds.Clear();
                continue;
            }

            _manifest.TrackedPlaylists.Remove(item.Id);
            foreach (var track in _manifest.Tracks.Values)
            {
                if (track.InPlaylists.Contains(item.Id))
                    track.InPlaylists.Remove(item.Id);
            }
        }

        await RebuildTrackedPlaylists();

        _manifest.AppState.IsInitialSetupComplete = true;
        ManifestStore.Save(_manifest, _manifestPath);

        _folderService!.CreateAllFolders();
        _folderService.RemoveOrphanedFolders(_manifest);
        _folderService.CreateTrackedPlaylistFolders(_manifest);

        vm.IsPlaylistSelectionVisible = false;
        _playlistSelectionComplete.TrySetResult(true);
        vm.ShowTracksSyncing = true;
        return true;
    }

    private async Task RebuildTrackedPlaylists()
    {
        var vm = (MainWindowViewModel)DataContext!;
        vm.TrackedPlaylists.Clear();

        foreach (var (id, tracked) in _manifest.TrackedPlaylists)
        {
            var item = new PlaylistViewModel(tracked);

            if (_IsLikedPlaylistSelected && id == LikedPlaylistId)
                item.ArtworkBitmap = await _artwork!.FetchBitmap(null);

            if (id != LikedPlaylistId)
            {
                if (File.Exists(tracked.ArtworkPath))
                    item.ArtworkBitmap = new Bitmap(tracked.ArtworkPath);
                else
                    item.ArtworkBitmap = await _artwork!.FetchBitmap(null);
            }

            vm.TrackedPlaylists.Add(item);
        }

        foreach (var playlist in vm.AllPlaylists)
        {
            if (playlist.IsLiked)
                playlist.IsTracked = _manifest.TrackedPlaylists[LikedPlaylistId].TrackIds.Count > 0;
            else
                playlist.IsTracked = _manifest.TrackedPlaylists.ContainsKey(playlist.Id);
        }
    }

    private void OnCancelPlaylistSelection()
    {
        Console.WriteLine("Cancelling playlist selection");
        var vm = (MainWindowViewModel)DataContext!;
        foreach (var item in vm.AllPlaylists)
        {
            if (!_manifest.AppState.IsInitialSetupComplete)
                item.IsSelected = false;
            else
                item.IsSelected = item.IsLiked
                    ? _manifest.TrackedPlaylists[LikedPlaylistId].TrackIds.Count > 0
                    : _manifest.TrackedPlaylists.ContainsKey(item.Id);
        }
        vm.IsPlaylistSelectionVisible = true;
    }

    private async Task OnCreateSyncedPlaylists()
    {
        Console.WriteLine("Creating Synced Playlists");
        var vm = (MainWindowViewModel)DataContext!;
        vm.ShowSyncedPlaylists = false;
        vm.ShowTracksSyncing = false;

        await RebuildTrackedPlaylists();
    }

    private void PlaylistSeclectionCompleted(object? sender, EventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        vm.ShowTracksSyncing = false;
        vm.ShowSyncedPlaylists = true;
        vm.IsInitialSetupComplete = true;
    }

    private async Task OnSyncNow()
    {
        var vm = (MainWindowViewModel)DataContext!;
        vm.ShowSyncedPlaylists = false;
        vm.ShowTracksSyncing = true;
        _IsLikedPlaylistSelected = _manifest.TrackedPlaylists[LikedPlaylistId].TrackIds.Count > 0;
        await SyncTracksAsync();
    }

    private void OnShowSyncedPlaylistView()
    {
        var vm = (MainWindowViewModel)DataContext!;
        vm.IsPlaylistSelectionVisible = false;
        vm.ShowTracksSyncing = false;
        vm.ShowSyncedPlaylists = true;
        _playlistSelectionComplete.TrySetResult(false);
    }

    private async Task OnShowPlaylistSelection()
    {
        using var http = new HttpClient();
        var vm = (MainWindowViewModel)DataContext!;
        vm.ShowSyncedPlaylists = false;
        vm.ShowTracksSyncing = false;
        _playlistSelectionComplete = new TaskCompletionSource<bool>();

        // recreate artwork service with new http client for this session
        _artwork = new ArtworkService(http, _artworkPath);

        await ShowPlaylistSelectionAsync();
        var saved = await _playlistSelectionComplete.Task;
        if (saved)
            await SyncTracksAsync();
    }

    private async Task SyncTracksAsync()
    {
        foreach (var (id, tracked) in _manifest.TrackedPlaylists)
        {
            if (id == LikedPlaylistId && !_IsLikedPlaylistSelected)
                continue;

            await _syncService!.SyncPlaylistAsync(
                _manifest,
                _manifestPath,
                tracked,
                // have SyncPlaylistAsync set the CurrentTrack and ArtworkBitmap properties every time it finishes handling a track
                (track, bitmap) =>
                {
                    var vm = (MainWindowViewModel)DataContext!;
                    vm.CurrentTrack = track;
                    vm.ArtworkBitmap = bitmap;
                }
            );
        }

        PlaylistSeclectionCompleted(this, EventArgs.Empty);
    }
}
