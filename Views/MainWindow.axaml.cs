using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
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
    private TaskCompletionSource<bool> _potentiallyDeletedTracksComplete = new();

    public MainWindow()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.local.json", optional: true)
            .Build();

        Console.WriteLine("Sound cloud client created");
        _profileUrl = config["SoundCloud:ProfileUrl"]!;
        _archivePath = config["Archiver:DownloadPath"]!;

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
            vm.OnPotentiallyDeletedTracksContinue += OnPotentiallyDeletedTracksContinue;
            vm.OnSaveSetup += OnSaveSetup;
            vm.OnShowSetup += OnShowSetup;
            vm.OnShowManageTrackPlaylists += OnShowManageTrackPlaylists;
            vm.OnSaveManageTrackPlaylists += OnSaveManageTrackPlaylists;
            vm.OnCancelManageTrackPlaylists += OnCancelManageTrackPlaylists;
            DataContextChanged -= OnDataContextChanged;
        }
    }

    private async Task InitializeAsync()
    {
        var tempClient = new SoundCloudClient();
        var clientId = await tempClient.GetClientIdAsync();
        _soundcloud = new SoundCloudClient(clientId);
        Console.WriteLine("App settings loaded");

        _manifest = ManifestStore.Load(_manifestPath, _likedTracksPath);

        _artwork = new ArtworkService(_artworkPath);
        _syncService = new SyncService(_soundcloud, _profileUrl, _tracksPath, _artwork);
        _folderService = new FolderService(_archivePath, _likedTracksPath);

        if (!_manifest.AppState.IsInitialSetupComplete)
        {
            var vm = (MainWindowViewModel)DataContext!;
            vm.ShowSetup = true;
        }
        else
        {
            _IsLikedPlaylistSelected =
                _manifest.TrackedPlaylists.TryGetValue(LikedPlaylistId, out var liked)
                && liked.TrackIds.Count > 0;
            await PopulateTrackedPlaylists();
            var vm = (MainWindowViewModel)DataContext!;
            vm.ShowTracksSyncing = true;
            await SyncTracksAsync();
        }
    }

    private async Task PopulateTrackedPlaylists()
    {
        var vm = (MainWindowViewModel)DataContext!;
        vm.TrackedPlaylists.Clear();

        foreach (var (id, tracked) in _manifest.TrackedPlaylists)
        {
            if (id == LikedPlaylistId && !_IsLikedPlaylistSelected)
                continue;

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
                    ? _IsLikedPlaylistSelected
                    : _manifest.TrackedPlaylists.ContainsKey(item.Id);
            }
            vm.IsPlaylistSelectionVisible = true;
            return;
        }

        vm.IsLoadingPlaylists = true;

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
            allPlaylists.Add(new Playlist { Title = "Liked Songs", Permalink = "liked" });

            Console.WriteLine($"Total playlists fetched: {allPlaylists.Count}");

            Console.WriteLine("Fetching artwork for playlists...");
            foreach (var playlist in allPlaylists)
            {
                var item = new PlaylistViewModel(playlist);
                item.IsSelected = item.IsLiked
                    ? _IsLikedPlaylistSelected
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
        finally
        {
            vm.IsLoadingPlaylists = false;
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
                ? _manifest.TrackedPlaylists.TryGetValue(LikedPlaylistId, out var liked)
                    && liked.TrackIds.Count > 0
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

        var dialog = new ConfirmationDialog("Playlist Sync", message);
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
                    ArtworkPath = _artwork!.FallbackFilePath,
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

            var playlistId = item.IsLiked ? LikedPlaylistId : item.Id;
            var playlistPath = item.IsLiked
                ? _likedTracksPath
                : _manifest.TrackedPlaylists[item.Id].FolderPath;

            if (item.IsLiked)
                _IsLikedPlaylistSelected = false;

            var tracksInPlaylist = _manifest
                .Tracks.Values.Where(t => t.InPlaylists.Contains(playlistId))
                .ToList();

            // Remove symlinks, tracks from playlists, and vice versa
            // If there are no remaining playlists, delete the track and its artwork
            foreach (var track in tracksInPlaylist)
            {
                Console.WriteLine($"Untracking: removing {track.Title} from playlist {playlistId}");
                await _syncService!.RemoveSymlinks(track.FileName, playlistPath);
                await _syncService.RemoveTracksFromPlaylists(track.Id, playlistId, _manifest);
                await _syncService.RemovePlaylistsFromTracks(track.Id, playlistId, _manifest);

                if (track.InPlaylists.Count == 0)
                {
                    Console.WriteLine(
                        $"  Track {track.Title} has no remaining playlists — deleting files"
                    );
                    await _syncService.DeleteTrackAndArtwork(track.FilePath, track.ArtworkPath);
                    await _syncService.DeleteTrackFromManifest(track.Id, _manifest);
                }
            }

            var emptyEntries = new List<long>();
            foreach (var (trackId, playlists) in _manifest.PotentiallyDeletedTracks)
            {
                playlists.Remove(playlistId);
                if (playlists.Count == 0)
                    emptyEntries.Add(trackId);
            }
            foreach (var trackId in emptyEntries)
                _manifest.PotentiallyDeletedTracks.Remove(trackId);

            _manifest.TrackedPlaylists.Remove(playlistId);
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
            if (id == LikedPlaylistId && !_IsLikedPlaylistSelected)
                continue;

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
                playlist.IsTracked = _IsLikedPlaylistSelected;
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
                    ? _IsLikedPlaylistSelected
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

    private async void PlaylistSeclectionCompleted(object? sender, EventArgs e)
    {
        var empty = _manifest
            .PotentiallyDeletedTracks.Where(e => e.Value.Count == 0)
            .Select(e => e.Key)
            .ToList();
        foreach (var key in empty)
            _manifest.PotentiallyDeletedTracks.Remove(key);

        var vm = (MainWindowViewModel)DataContext!;
        vm.ShowTracksSyncing = false;
        vm.IsInitialSetupComplete = true;
        if (_manifest.PotentiallyDeletedTracks.Count > 0)
        {
            await CreatePotentiallyDeletedTracks();
            vm.ShowPotentiallyDeletedTracks = true;
            _potentiallyDeletedTracksComplete = new TaskCompletionSource<bool>();
            bool complete = await _potentiallyDeletedTracksComplete.Task;
            if (complete)
                vm.ShowSyncedPlaylists = true;
        }
        else
            vm.ShowSyncedPlaylists = true;
    }

    private async Task CreatePotentiallyDeletedTracks()
    {
        Console.WriteLine("Creating Potentially Deleted Tracks");
        var vm = (MainWindowViewModel)DataContext!;
        vm.PotentiallyDeletedTracks.Clear();
        // trackId key hashset of playlist ids as value
        foreach (var (trackId, playlists) in _manifest.PotentiallyDeletedTracks)
        {
            var trackIdString = trackId.ToString();
            var artist = _manifest.Tracks[trackIdString].Artist;
            var trackPath = _manifest.Tracks[trackIdString].FilePath;
            var trackFileName = _manifest.Tracks[trackIdString].FileName;
            var ListOfPlaylists = new PotentiallyDeletedTrackViewModel()
            {
                TrackId = trackId,
                Title = _manifest.Tracks[trackIdString].Title,
                TrackFilePath = trackPath,
                TrackFileName = trackFileName,
                Artist = artist,
                IsKept = false,
            };

            if (File.Exists(_manifest.Tracks[trackIdString].ArtworkPath))
                ListOfPlaylists.ArtworkBitmap = new Bitmap(
                    _manifest.Tracks[trackIdString].ArtworkPath
                );
            else
                ListOfPlaylists.ArtworkBitmap = await _artwork!.FetchBitmap(null);

            ListOfPlaylists.ArtworkPath = _manifest.Tracks[trackIdString].ArtworkPath;

            // Check if track was removed from ALL its SoundCloud playlists
            var trackEntry = _manifest.Tracks[trackIdString];
            ListOfPlaylists.IsRemovedFromSoundCloud = trackEntry
                .InPlaylists.All(pId => playlists.Contains(pId));

            foreach (var playlistId in playlists)
            {
                string playlistTitle = _manifest.TrackedPlaylists[playlistId].Title;
                string playlistPath = _manifest.TrackedPlaylists[playlistId].FolderPath;
                var PotentiallyDeletedFromPlaylist = new PotentiallyDeletedPlaylistItem
                {
                    PlaylistId = playlistId,
                    PlaylistTitle = playlistTitle,
                    PlaylistPath = playlistPath,
                    KeepInPlaylist = false,
                };
                ListOfPlaylists.Playlists.Add(PotentiallyDeletedFromPlaylist);
            }
            vm.PotentiallyDeletedTracks.Add(ListOfPlaylists);
        }
    }

    private async Task<bool> OnPotentiallyDeletedTracksContinue()
    {
        Console.WriteLine("Continuing Potentially Deleted Tracks");
        var vm = (MainWindowViewModel)DataContext!;

        // Clear deleted playlists
        foreach (var item in vm.PotentiallyDeletedTracks)
            item.DeleteFromPlaylists.Clear();

        string message = "";
        string deletedTrackMessage = "Deleted tracks";
        string messageBody = "";
        var deletedTracks = new List<string>();
        var deletedTracksFromManifest = new HashSet<long>();

        foreach (var item in vm.PotentiallyDeletedTracks)
        {
            string trackTitle = $"{item.Title}";
            string trackInfoMessage = "\n\n";
            string keptInPlaylistsMessage = "Kept in playlists:";
            string removedFromPlaylistsMessage = "Deleted from playlists:";

            foreach (var playlist in item.Playlists)
            {
                if (playlist.KeepInPlaylist)
                {
                    keptInPlaylistsMessage += $"\n  \u2022 {playlist.PlaylistTitle}";
                }
                else
                {
                    item.DeleteFromPlaylists.Add(
                        playlist.PlaylistId.ToString(),
                        playlist.PlaylistPath
                    );
                    removedFromPlaylistsMessage += $"\n  \u2022 {playlist.PlaylistTitle}";
                }
            }

            // Check if track still belongs to any playlists
            var trackIdStr = item.TrackId.ToString();
            if (!_manifest.Tracks.TryGetValue(trackIdStr, out var manifestEntry))
                continue;

            var remainingPlaylists = manifestEntry
                .InPlaylists.Where(pId => !item.DeleteFromPlaylists.ContainsKey(pId.ToString()))
                .ToList();

            // Determine if the track was removed from ALL its SoundCloud playlists
            var potDeletedPlaylistIds = item.Playlists.Select(p => p.PlaylistId).ToHashSet();
            var wasRemovedFromAllPlaylists = manifestEntry
                .InPlaylists.All(pId => potDeletedPlaylistIds.Contains(pId));

            if (remainingPlaylists.Count == 0)
            {
                deletedTracksFromManifest.Add(item.TrackId);
                item.IsKept = false;
                deletedTracks.Add(trackTitle);
            }
            else
            {
                if (wasRemovedFromAllPlaylists)
                {
                    item.IsKept = true;
                    manifestEntry.IsKept = true;
                }
                trackInfoMessage += $"______{trackTitle}______";
                trackInfoMessage += $"\n{keptInPlaylistsMessage}";
                trackInfoMessage += $"\n{removedFromPlaylistsMessage}";
                messageBody += $"{trackInfoMessage}";
            }
        }

        if (deletedTracks.Count > 0)
        {
            message += $"{deletedTrackMessage}:\n";
            foreach (var title in deletedTracks)
                message += $"  \u2022 {title}\n";
            message += messageBody;
        }
        else
        {
            message += messageBody;
        }

        Console.WriteLine("Showing confirmation dialog");
        var dialog = new ConfirmationDialog("Track Updates", message);
        var result = await dialog.ShowDialog<bool>(this);
        Console.WriteLine("\nConfirmation dialog closed");

        if (!result)
            return false;

        Console.WriteLine("Handling Kept tracks");
        foreach (var item in vm.PotentiallyDeletedTracks.Where(i => i.IsKept))
        {
            await _syncService!.RemoveSymlinks(item);
            await _syncService!.RemoveTracksFromPlaylistsAndPlaylistsFromTracks(item, _manifest);

            // Clean up PotentiallyDeletedTracks in manifest
            foreach (var playlistIdStr in item.DeleteFromPlaylists.Keys)
            {
                var playlistId = long.Parse(playlistIdStr);
                if (_manifest.PotentiallyDeletedTracks.TryGetValue(item.TrackId, out var playlists))
                {
                    playlists.Remove(playlistId);
                    if (playlists.Count == 0)
                        _manifest.PotentiallyDeletedTracks.Remove(item.TrackId);
                }
            }
        }

        Console.WriteLine("Handling Deleted tracks");
        var toDelete = vm.PotentiallyDeletedTracks.Where(i => !i.IsKept).ToList();
        foreach (var item in toDelete)
        {
            await _syncService!.RemoveSymlinks(item);
            await _syncService!.RemoveTracksFromPlaylists(item, _manifest);
        }

        if (toDelete.Count > 0)
        {
            await _syncService!.DeleteTrackAndArtwork(vm.PotentiallyDeletedTracks);
            await _syncService!.DeleteTracksFromManifest(deletedTracksFromManifest, _manifest);
        }

        ManifestStore.Save(_manifest, _manifestPath);

        vm.ShowPotentiallyDeletedTracks = false;
        _potentiallyDeletedTracksComplete.TrySetResult(true);
        return true;
    }

    private async Task OnSyncNow()
    {
        var vm = (MainWindowViewModel)DataContext!;
        vm.ShowSyncedPlaylists = false;
        vm.ShowTracksSyncing = true;
        _IsLikedPlaylistSelected =
            _manifest.TrackedPlaylists.TryGetValue(LikedPlaylistId, out var liked)
            && liked.TrackIds.Count > 0;
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
        var vm = (MainWindowViewModel)DataContext!;
        vm.ShowSyncedPlaylists = false;
        vm.ShowTracksSyncing = false;
        _playlistSelectionComplete = new TaskCompletionSource<bool>();

        _artwork = new ArtworkService(_artworkPath);

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
                (track, bitmap, artist) =>
                {
                    var vm = (MainWindowViewModel)DataContext!;
                    vm.CurrentTrack = track;
                    vm.ArtworkBitmap = bitmap;
                    vm.CurrentArtist = artist;
                }
            );
        }

        // Retry any failed downloads using fallback methods
        var vm = (MainWindowViewModel)DataContext!;
        vm.CurrentTrack = null;
        vm.ShowAlternativeDownload = true;
        await _syncService!.DownloadFailedTracksWithFallbackAsync(_manifest, _manifestPath,
            (title, artist, artworkPath, attempt, percent, total) =>
            {
                var vmm = (MainWindowViewModel)DataContext!;
                vmm.FallbackTrackTitle = title;
                vmm.FallbackArtist = artist;
                vmm.AlternativeDownloadStatus =
                    percent > 0
                        ? $"Retrying ({attempt}/2) — {percent}%"
                        : $"Retrying ({attempt}/2) — this may take a while";

                if (attempt == 1)
                    vmm.AlternativeDownloadStatus = $"yt-dlp — {vmm.AlternativeDownloadStatus}";
                else
                    vmm.AlternativeDownloadStatus = $"klickaud — {vmm.AlternativeDownloadStatus}";

                vmm.AlternativeDownloadPercent = percent;

                // Load artwork from saved file
                if (!string.IsNullOrEmpty(artworkPath) && File.Exists(artworkPath))
                {
                    try
                    {
                        vmm.FallbackArtworkBitmap = _artwork!.LoadBitmap(artworkPath);
                    }
                    catch { }
                }
            }
        );
        vm.ShowAlternativeDownload = false;

        PlaylistSeclectionCompleted(this, EventArgs.Empty);
    }

    private void OnManageTrackSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var vm = (MainWindowViewModel)DataContext!;
            vm.SearchManageTracksCommand.Execute(null);
        }
    }

    private async Task OnShowManageTrackPlaylists()
    {
        Console.WriteLine("Showing Manage Out of Sync Tracks");
        var vm = (MainWindowViewModel)DataContext!;

        vm.AllManageableTracks.Clear();
        vm.ManageableTracks.Clear();

        foreach (var (idStr, track) in _manifest.Tracks)
        {
            if (!track.IsKept)
                continue;

            var item = new ManageOutOfSyncTracksViewModel
            {
                TrackId = track.Id,
                Title = track.Title,
                TrackFilePath = track.FilePath,
                TrackFileName = track.FileName,
                Artist = track.Artist,
                ArtworkPath = track.ArtworkPath,
                IsKept = track.IsKept,
                OriginalPlaylistIds = new HashSet<long>(track.InPlaylists),
            };

            if (File.Exists(track.ArtworkPath))
                item.ArtworkBitmap = new Bitmap(track.ArtworkPath);
            else
                item.ArtworkBitmap = await _artwork!.FetchBitmap(null);

            foreach (var (playlistId, trackedPlaylist) in _manifest.TrackedPlaylists)
            {
                var playlistItem = new PotentiallyDeletedPlaylistItem
                {
                    PlaylistId = playlistId,
                    PlaylistTitle = trackedPlaylist.Title,
                    PlaylistPath = trackedPlaylist.FolderPath,
                    KeepInPlaylist = track.InPlaylists.Contains(playlistId),
                };
                item.Playlists.Add(playlistItem);
            }

            vm.AllManageableTracks.Add(item);
            vm.ManageableTracks.Add(item);
        }

        vm.ShowManageTrackPlaylists = true;
        vm.ShowSyncedPlaylists = false;
    }

    private async Task<bool> OnSaveManageTrackPlaylists()
    {
        Console.WriteLine("Saving managed track playlists");
        var vm = (MainWindowViewModel)DataContext!;

        string message = "";
        var willBeDeleted = new List<string>();

        foreach (var item in vm.AllManageableTracks)
        {
            var trackIdStr = item.TrackId.ToString();
            if (!_manifest.Tracks.TryGetValue(trackIdStr, out var manifestEntry))
                continue;

            var added = new List<PotentiallyDeletedPlaylistItem>();
            var removed = new List<PotentiallyDeletedPlaylistItem>();

            foreach (var playlist in item.Playlists)
            {
                var wasInPlaylist = item.OriginalPlaylistIds.Contains(playlist.PlaylistId);
                if (playlist.KeepInPlaylist && !wasInPlaylist)
                    added.Add(playlist);
                else if (!playlist.KeepInPlaylist && wasInPlaylist)
                    removed.Add(playlist);
            }

            if (added.Count == 0 && removed.Count == 0)
                continue;

            var remainingCount =
                manifestEntry.InPlaylists.Count + added.Count - removed.Count;

            message += $"\n{item.Title}";
            if (added.Count > 0)
            {
                message += "\n  Added to playlists:";
                foreach (var p in added)
                    message += $"\n    \u2022 {p.PlaylistTitle}";
            }
            if (removed.Count > 0)
            {
                message += "\n  Removed from playlists:";
                foreach (var p in removed)
                    message += $"\n    \u2022 {p.PlaylistTitle}";
            }
            if (remainingCount == 0)
            {
                message += "\n  \u26A0 This track will be DELETED (no remaining playlists)";
                willBeDeleted.Add(item.Title);
            }
        }

        if (string.IsNullOrEmpty(message))
        {
            vm.ShowManageTrackPlaylists = false;
            vm.ShowSyncedPlaylists = true;
            return true;
        }

        var dialog = new ConfirmationDialog("Track Playlist Changes", message);
        var result = await dialog.ShowDialog<bool>(this);

        if (!result)
            return false;

        // Apply changes
        var toDeleteFromManifest = new HashSet<long>();

        foreach (var item in vm.AllManageableTracks)
        {
            var trackIdStr = item.TrackId.ToString();
            if (!_manifest.Tracks.TryGetValue(trackIdStr, out var manifestEntry))
                continue;

            var added = new List<PotentiallyDeletedPlaylistItem>();
            var removed = new List<PotentiallyDeletedPlaylistItem>();

            foreach (var playlist in item.Playlists)
            {
                var wasInPlaylist = item.OriginalPlaylistIds.Contains(playlist.PlaylistId);
                if (playlist.KeepInPlaylist && !wasInPlaylist)
                    added.Add(playlist);
                else if (!playlist.KeepInPlaylist && wasInPlaylist)
                    removed.Add(playlist);
            }

            if (added.Count == 0 && removed.Count == 0)
                continue;

            // Handle removals
            foreach (var playlist in removed)
            {
                Console.WriteLine(
                    $"Removing {item.Title} from playlist {playlist.PlaylistTitle}"
                );
                await _syncService!.RemoveSymlinks(item.TrackFileName, playlist.PlaylistPath);
                await _syncService!.RemoveTracksFromPlaylists(
                    item.TrackId,
                    playlist.PlaylistId,
                    _manifest
                );
                await _syncService!.RemovePlaylistsFromTracks(
                    item.TrackId,
                    playlist.PlaylistId,
                    _manifest
                );
            }

            // Handle additions
            foreach (var playlist in added)
            {
                Console.WriteLine(
                    $"Adding {item.Title} to playlist {playlist.PlaylistTitle}"
                );
                await _syncService!.AddSymlink(
                    item.TrackFileName,
                    playlist.PlaylistPath,
                    _tracksPath
                );
                await _syncService!.AddTrackToPlaylist(
                    item.TrackId,
                    playlist.PlaylistId,
                    _manifest
                );
            }

            // Check if track is orphaned
            if (manifestEntry.InPlaylists.Count == 0)
            {
                Console.WriteLine(
                    $"Track {item.Title} has no remaining playlists — deleting files"
                );
                toDeleteFromManifest.Add(item.TrackId);
                await _syncService!.DeleteTrackAndArtwork(
                    item.TrackFilePath,
                    item.ArtworkPath
                );
            }
        }

        // Delete orphaned tracks from manifest
        if (toDeleteFromManifest.Count > 0)
        {
            await _syncService!.DeleteTracksFromManifest(toDeleteFromManifest, _manifest);

            foreach (var track in toDeleteFromManifest)
            {
                if (_manifest.PotentiallyDeletedTracks.TryGetValue(track, out var playlists))
                    _manifest.PotentiallyDeletedTracks.Remove(track);
            }
        }

        ManifestStore.Save(_manifest, _manifestPath);

        vm.ShowManageTrackPlaylists = false;
        vm.ShowSyncedPlaylists = true;
        return true;
    }

    private void OnCancelManageTrackPlaylists()
    {
        Console.WriteLine("Cancelling Manage Out of Sync Tracks");
        var vm = (MainWindowViewModel)DataContext!;
        vm.ShowManageTrackPlaylists = false;
        vm.ShowSyncedPlaylists = true;
    }

    private async void OnOpenSoundCloudClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://soundcloud.com/you/library",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open SoundCloud: {ex.Message}");
        }
    }

    private async void OnBrowseDownloadFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "zenity",
                Arguments = "--file-selection --directory --title=\"Select Download Folder\"",
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
            };

            var process = Process.Start(psi);
            if (process == null)
            {
                Console.WriteLine("Failed to start zenity");
                return;
            }

            await process.WaitForExitAsync();
            var path = (await process.StandardOutput.ReadToEndAsync())?.Trim();

            if (!string.IsNullOrEmpty(path))
            {
                var vm = (MainWindowViewModel)DataContext!;
                var suffix = Path.DirectorySeparatorChar + "SoundCloud Archiver";
                var finalPath = path.TrimEnd(Path.DirectorySeparatorChar) + suffix;
                vm.SetupDownloadPath = finalPath;
                Console.WriteLine($"Picked download folder: {finalPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Folder picker failed: {ex.Message}");
        }
    }

    private void OnShowSetup()
    {
        Console.WriteLine("Showing setup");
        var vm = (MainWindowViewModel)DataContext!;

        vm.SetupProfileUrl = _profileUrl;
        vm.SetupDownloadPath = _archivePath;

        vm.ShowSetup = true;
        vm.ShowSyncedPlaylists = false;
    }

    private async Task<bool> OnSaveSetup()
    {
        Console.WriteLine("Saving setup");
        var vm = (MainWindowViewModel)DataContext!;

        var profileUrl = vm.SetupProfileUrl?.Trim() ?? "";
        var downloadPath = vm.SetupDownloadPath?.Trim() ?? "";

        // Validate profile URL
        if (string.IsNullOrEmpty(profileUrl))
        {
            var dialog = new ConfirmationDialog("Invalid URL", "Please enter your SoundCloud profile URL.");
            await dialog.ShowDialog<bool>(this);
            return false;
        }

        // Validate with SoundCloud
        try
        {
            Console.WriteLine($"Validating profile URL: {profileUrl}");
            var user = await _soundcloud!.Users.GetAsync(profileUrl);
            Console.WriteLine($"Found user: {user.Username} ({user.PermalinkUrl})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"URL validation failed: {ex.Message}");
            var dialog = new ConfirmationDialog(
                "Invalid Profile URL",
                $"Could not find a SoundCloud profile at:\n{profileUrl}\n\nPlease check the URL and try again."
            );
            await dialog.ShowDialog<bool>(this);
            return false;
        }

        // Validate download path
        if (string.IsNullOrEmpty(downloadPath))
        {
            var dialog = new ConfirmationDialog("Invalid Path", "Please select a download folder.");
            await dialog.ShowDialog<bool>(this);
            return false;
        }

        // Check if download path changed
        var previousPath = _manifest.AppState.DownloadPath;
        var deleteOldFolder = false;
        if (!string.IsNullOrEmpty(previousPath) && previousPath != downloadPath)
        {
            var confirmDialog = new ConfirmationDialog(
                "Download Path Changed",
                $"Your download path has changed from:\n{previousPath}\n\nto:\n{downloadPath}\n\n"
                    + "Do you want to continue?",
                "Delete old folder and all contents",
                false
            );
            var proceed = await confirmDialog.ShowDialog<bool>(this);
            if (!proceed)
                return false;
            deleteOldFolder = confirmDialog.IsCheckBoxChecked;
        }

        // Delete old folder if requested
        if (deleteOldFolder && !string.IsNullOrEmpty(previousPath) && Directory.Exists(previousPath))
        {
            Console.WriteLine($"Deleting old download folder: {previousPath}");
            try
            {
                Directory.Delete(previousPath, recursive: true);
                Console.WriteLine("Old folder deleted successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to delete old folder: {ex.Message}");
            }
        }

        // Create all necessary directories
        var dirs = new[]
        {
            downloadPath,
            Path.Combine(downloadPath, "tracks"),
            Path.Combine(downloadPath, "artwork"),
            Path.Combine(downloadPath, "playlists"),
            Path.Combine(downloadPath, "playlists", "liked"),
        };
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        var tracksPath = Path.Combine(downloadPath, "tracks");

        // Store the previous config to check if SoundCloudClient needs update
        var prevProfileUrl = _profileUrl;
        var prevArchivePath = _archivePath;

        // Update fields
        _profileUrl = profileUrl;
        _archivePath = downloadPath;

        // Save to manifest
        _manifest.AppState.DownloadPath = downloadPath;

        // Save to appsettings.local.json
        try
        {
            var localSettings = new Dictionary<string, object>
            {
                ["SoundCloud"] = new Dictionary<string, object>
                {
                    ["ProfileUrl"] = profileUrl,
                    ["ClientId"] = _soundcloud?.ClientId ?? "",
                },
                ["Archiver"] = new Dictionary<string, object>
                {
                    ["DownloadPath"] = downloadPath,
                },
            };

            var json = JsonSerializer.Serialize(
                localSettings,
                new JsonSerializerOptions { WriteIndented = true }
            );
            File.WriteAllText("appsettings.local.json", json);
            Console.WriteLine("Saved appsettings.local.json");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save appsettings.local.json: {ex.Message}");
        }

        // Update paths in manifest if archive path changed
        if (prevArchivePath != downloadPath)
        {
            foreach (var (id, tracked) in _manifest.TrackedPlaylists)
            {
                if (id == ManifestStore.LikedPlaylistId)
                {
                    tracked.FolderPath = Path.Combine(downloadPath, "playlists", "liked");
                }
                else
                {
                    var folderName = $"{tracked.Title}_{tracked.Permalink}".Replace("/", "_");
                    tracked.FolderPath = Path.Combine(downloadPath, "playlists", folderName);
                }
            }

            foreach (var (idStr, track) in _manifest.Tracks)
            {
                track.FilePath = Path.Combine(downloadPath, "tracks", track.FileName);
            }

            _manifest.AppState.DownloadPath = downloadPath;
        }

        // Recreate services with new paths if they changed
        if (prevArchivePath != downloadPath || prevProfileUrl != profileUrl)
        {
            _artwork = new ArtworkService(Path.Combine(downloadPath, "artwork"));
            _syncService = new SyncService(_soundcloud!, profileUrl, tracksPath, _artwork);
            _folderService = new FolderService(downloadPath, Path.Combine(downloadPath, "playlists", "liked"));
        }

        // Ensure playlist folders exist at the new path
        if (prevArchivePath != downloadPath)
        {
            _folderService!.CreateAllFolders();
            _folderService.CreateTrackedPlaylistFolders(_manifest);
        }

        // Mark setup as complete so user doesn't have to re-enter if app closes mid-sync
        var isFirstTime = !_manifest.AppState.IsInitialSetupComplete;
        _manifest.AppState.IsInitialSetupComplete = true;
        ManifestStore.Save(_manifest, _manifestPath);

        vm.ShowSetup = false;

        if (isFirstTime)
        {
            // First time setup
            _playlistSelectionComplete = new TaskCompletionSource<bool>();
            await ShowPlaylistSelectionAsync();
            var saved = await _playlistSelectionComplete.Task;
            if (saved)
                await SyncTracksAsync();
        }
        else
        {
            // Came from synced playlists view
            await OnSyncNow();
        }

        return true;
    }
}
