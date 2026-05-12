using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Configuration;
using soundCloudArchiver.Models;
using soundCloudArchiver.ViewModels;
using SoundCloudExplode;
using SoundCloudExplode.Playlists;
using SoundCloudExplode.Tracks;

namespace soundCloudArchiver.Views;

public partial class MainWindow : Window
{
    const long LikedPlaylistId = -1;
    private HashSet<long> _seenTrackIds = new();
    private List<TrackedPlaylist> _TrackedPlaylists = new();

    private SoundCloudClient? _soundcloud;

    private string _profileUrl = "";
    private string _archivePath = "";
    private string _tracksPath => Path.Combine(_archivePath, "tracks");
    private string _playlistsPath => Path.Combine(_archivePath, "playlists");
    private string _artworkPath => Path.Combine(_archivePath, "artwork");
    private string _likedTracksPath => Path.Combine(_archivePath, "liked");

    private bool _IsLikedPlaylistSelected = false;

    private Manifest _manifest = new();
    private readonly string _manifestPath = "manifest.json";

    private TaskCompletionSource<bool> _playlistSelectionComplete = new();
    private TaskCompletionSource<bool> _createSyncedPlaylistsComplete = new();

    private Bitmap? _fallbackBitmap;
    private static readonly Uri FallbackArtwork = new(
        "https://i1.sndcdn.com/avatars-4oLYo9NB9XBQQ11s-2Kdzzg-t500x500.jpg"
    );

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

        // Subscribe to DataContext changed to wire up events after DataContext is set
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
            DataContextChanged -= OnDataContextChanged; // Unsubscribe after wiring up
        }
    }


    private async Task InitializeAsync()
    {
        using var http = new HttpClient();

        var tempClient = new SoundCloudClient();
        var clientId = await tempClient.GetClientIdAsync();
        _soundcloud = new SoundCloudClient(clientId);
        Console.WriteLine("App settings loaded");

        LoadManifest();

        if (!_manifest.AppState.IsInitialSetupComplete)
        {
            _playlistSelectionComplete = new TaskCompletionSource<bool>();
            await ShowPlaylistSelectionAsync(http);
            // wait for playlist selection to complete
            var saved = await _playlistSelectionComplete.Task;
            if (saved)
                await SyncTracksAsync(http);
        }
        else
        {
            var vm = (MainWindowViewModel)DataContext!;
            foreach (var playlist in _TrackedPlaylists)
            {
                var item = new SyncedPlaylistItem(playlist);
                if (File.Exists(playlist.ArtworkPath))
                    item.ArtworkBitmap = new Bitmap(playlist.ArtworkPath);
                else
                    item.ArtworkBitmap = await FetchBitmap(http, FallbackArtwork);
                vm.SyncedPlaylistItems.Add(item);
            }
            vm.AreSyncedPlaylistsCreated = true;

            _IsLikedPlaylistSelected = _manifest.TrackedPlaylists[LikedPlaylistId].TrackIds.Count > 0;
            await SyncTracksAsync(http);
        }
    }

    private void LoadManifest()
    {
        if (File.Exists(_manifestPath))
        {
            var json = File.ReadAllText(_manifestPath);
            _manifest = JsonSerializer.Deserialize<Manifest>(json) ?? new Manifest();
        }
        else
        {
            _manifest = new Manifest();
        }
        if (!_manifest.TrackedPlaylists.ContainsKey(LikedPlaylistId))
        {
            _manifest.TrackedPlaylists[LikedPlaylistId] = new TrackedPlaylist
            {
                Id = LikedPlaylistId,
                Title = "Liked_Songs",
                FolderPath = _likedTracksPath,
            };
        }

        _TrackedPlaylists = _manifest
            .TrackedPlaylists.Values.Where(x => x.Id != LikedPlaylistId)
            .ToList();
    }

    private void SaveManifest()
    {
        var json = JsonSerializer.Serialize(
            _manifest,
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(_manifestPath, json);
    }

    private async Task ShowPlaylistSelectionAsync(HttpClient http)
    {
        var vm = (MainWindowViewModel)DataContext!;

        if (vm.PlaylistSelectionItems.Count > 0)
        {
            foreach (var item in vm.PlaylistSelectionItems)
            {
                var playlistId = item.Playlist.Permalink?.Equals("liked") == true
                    ? LikedPlaylistId
                    : item.Playlist.Id ?? 0;
                item.IsTracked = _manifest.TrackedPlaylists.ContainsKey(playlistId);
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
            var fakePlaylist = new Playlist
            {
                Title = "Liked_Songs",
                Permalink = "liked",
            };
            allPlaylists.Add(fakePlaylist);

            Console.WriteLine($"Total playlists fetched: {allPlaylists.Count}");

            Console.WriteLine("Fetching artwork for playlists...");
            foreach (var playlist in allPlaylists)
            {
                var isTracked = playlist.Permalink?.Equals("liked") == true
                    ? _manifest.TrackedPlaylists.ContainsKey(LikedPlaylistId)
                    : _manifest.TrackedPlaylists.ContainsKey(playlist.Id ?? 0);
                var item = new PlaylistSelectionItem(playlist, isTracked);

                var artworkPath = await FetchAndSaveArtwork(
                    http,
                    playlist.Title!,
                    playlist.ArtworkUrl
                );
                if (File.Exists(artworkPath))
                    item.ArtworkBitmap = new Bitmap(artworkPath);

                vm.PlaylistSelectionItems.Add(item);
            }

            vm.IsPlaylistSelectionVisible = true;
            Console.WriteLine("Playlist selection UI should now be visible");
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

        var toTrack = new List<PlaylistSelectionItem>();
        var toUntrack = new List<PlaylistSelectionItem>();

        foreach (var item in vm.PlaylistSelectionItems)
        {
            var currentlyTracked = _manifest.TrackedPlaylists.ContainsKey(item.Playlist.Id ?? 0);
            if (item.IsTracked && !currentlyTracked)
                toTrack.Add(item);
            else if (!item.IsTracked && currentlyTracked)
                toUntrack.Add(item);
        }

        var message = "";
        if (toTrack.Count > 0)
        {
            message += $"Start tracking {toTrack.Count} playlists:\n";
            foreach (var item in toTrack)
                message += $"  • {item.Playlist.Title}_{item.Playlist.Permalink}\n";
        }
        if (toUntrack.Count > 0)
        {
            if (message.Length > 0)
                message += "\n";
            message += $"Untrack {toUntrack.Count} playlists:\n";
            foreach (var item in toUntrack)
                message += $"  • {item.Playlist.Title}_{item.Playlist.Permalink}\n";
        }

        if (string.IsNullOrEmpty(message))
        {
            vm.IsPlaylistSelectionVisible = false;
            return true;
        }

        message += "\nDo you want to proceed?";

        var dialog = new ConfirmationDialog(message);
        var result = await dialog.ShowDialog<bool>(this);

        if (!result)
            return false;

        foreach (var item in toTrack)
        {
            if (item.Playlist.Permalink?.Equals("liked") == true)
            {
                _IsLikedPlaylistSelected = true;
                _manifest.TrackedPlaylists[LikedPlaylistId] = new TrackedPlaylist
                {
                    Id = LikedPlaylistId,
                    Permalink = item.Playlist.Permalink ?? "liked",
                    Title = item.Playlist.Title ?? "Liked_Songs",
                    PermalinkUrl = "",
                    FolderPath = _likedTracksPath,
                };
                continue;
            }

            var folderName = $"{item.Playlist.Title}_{item.Playlist.Permalink}".Replace("/", "_");
            var folderPath = Path.Combine(_archivePath, "playlists", folderName);

            var safeTitle = string.Concat(
                item.Playlist.Title!.Split(Path.GetInvalidFileNameChars())
            );
            var artworkPath = Path.Combine(_artworkPath, safeTitle + ".jpg");

            var playlist = new TrackedPlaylist
            {
                Id = item.Playlist.Id ?? 0,
                Permalink = item.Playlist.Permalink!,
                Title = item.Playlist.Title!,
                PermalinkUrl = item.Playlist.PermalinkUrl?.ToString() ?? "",
                ArtworkPath = artworkPath,
                FolderPath = folderPath,
            };

            _TrackedPlaylists.Add(playlist);
            vm.SyncedPlaylistItems.Add(new SyncedPlaylistItem(playlist));

            _manifest.TrackedPlaylists[item.Playlist.Id ?? 0] = new TrackedPlaylist
            {
                Id = item.Playlist.Id ?? 0,
                Permalink = item.Playlist.Permalink!,
                Title = item.Playlist.Title!,
                PermalinkUrl = item.Playlist.PermalinkUrl?.ToString() ?? "",
                ArtworkPath = artworkPath,
                FolderPath = folderPath,
            };
        }

        foreach (var item in toUntrack)
        {
            if (item.Playlist.Permalink?.Equals("liked") == true)
            {
                _IsLikedPlaylistSelected = false;
                _manifest.TrackedPlaylists[LikedPlaylistId].TrackIds.Clear();
                continue;
            }

            var itemToRemove = vm.SyncedPlaylistItems.FirstOrDefault(x => x.Playlist.Id == item.Playlist.Id);
            if (itemToRemove != null)
            {
                vm.SyncedPlaylistItems.Remove(itemToRemove);
                _TrackedPlaylists.Remove(itemToRemove.Playlist);
            }

            var playlistId = item.Playlist.Id ?? 0;
            _manifest.TrackedPlaylists.Remove(playlistId);
            foreach (var track in _manifest.Tracks.Values)
            {
                if (track.InPlaylists.Contains(playlistId))
                {
                    track.InPlaylists.Remove(playlistId);
                }
            }
        }

        _manifest.AppState.IsInitialSetupComplete = true;
        SaveManifest();
        Console.WriteLine("Creating playlist folders");
        CreatePlaylistFolders();

        vm.IsPlaylistSelectionVisible = false;
        _playlistSelectionComplete.TrySetResult(true);
        vm.ShowTracksSyncing = true;
        return true;
    }

    private void OnCancelPlaylistSelection()
    {
        Console.WriteLine("Cancelling playlist selection");
        var vm = (MainWindowViewModel)DataContext!;
        foreach (var item in vm.PlaylistSelectionItems)
        {
            if (!_manifest.AppState.IsInitialSetupComplete)
            {
                item.IsTracked = false;
            }
            else
            {
                var playlistId = item.Playlist.Permalink?.Equals("liked") == true
                    ? LikedPlaylistId
                    : item.Playlist.Id ?? 0;
                item.IsTracked = _manifest.TrackedPlaylists.ContainsKey(playlistId);
            }
        }
        vm.IsPlaylistSelectionVisible = true;
        _playlistSelectionComplete.TrySetResult(false);
    }


    private async Task OnCreateSyncedPlaylists()
    {
        Console.WriteLine("Creating Synced Playlists");
        using var http = new HttpClient();
        var vm = (MainWindowViewModel)DataContext!;
        vm.ShowSyncedPlaylists = false;
        vm.ShowTracksSyncing = false;
            foreach (var playlist in _TrackedPlaylists)
            {
                SyncedPlaylistItem item = new SyncedPlaylistItem(playlist);
                if (File.Exists(item.Playlist.ArtworkPath))
                    item.ArtworkBitmap = new Bitmap(item.Playlist.ArtworkPath);
                else
                    item.ArtworkBitmap = await FetchBitmap(http, FallbackArtwork);

                vm.SyncedPlaylistItems.Add(item);
            }
        vm.AreSyncedPlaylistsCreated = true;
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
        await SyncTracksAsync(new HttpClient());
    }

    private async Task OnShowSyncedPlaylistView()
    {
        var vm = (MainWindowViewModel)DataContext!;
        vm.IsPlaylistSelectionVisible = false;
        vm.ShowTracksSyncing = false;
        vm.ShowSyncedPlaylists = true;
    }

    private async void OnShowPlaylistSelection()
    {

        using var http = new HttpClient();
        var vm = (MainWindowViewModel)DataContext!;
        vm.ShowSyncedPlaylists = false;
        vm.ShowTracksSyncing = false;
        _playlistSelectionComplete = new TaskCompletionSource<bool>();
        await ShowPlaylistSelectionAsync(http);
        // wait for playlist selection to complete
        var saved = await _playlistSelectionComplete.Task;
        if (saved)
            await SyncTracksAsync(http);
    }

    private void CreatePlaylistFolders()
    {
        Console.WriteLine("Creating folders");
        Console.WriteLine("Checking base track folder");
        bool doesBaseTracksFolderExist = Directory.Exists(_tracksPath);
        if (!doesBaseTracksFolderExist)
        {
            Directory.CreateDirectory(_tracksPath);
            Console.WriteLine("Created base tracks folder");
        }
        else
            Console.WriteLine("Base tracks folder already exists");

        Console.WriteLine("Checking base liked tracks folder");
        bool doesBaseLikedTracksFolderExist = Directory.Exists(_likedTracksPath);
        if (!doesBaseLikedTracksFolderExist)
        {
            Directory.CreateDirectory(_likedTracksPath);
            Console.WriteLine("Created base liked tracks folder");
        }

        Console.WriteLine("Checking base playlists folder");
        bool doesBasePlaylistsFolderExist = Directory.Exists(_playlistsPath);
        if (!doesBasePlaylistsFolderExist)
        {
            Directory.CreateDirectory(_playlistsPath);
            Console.WriteLine("Created base playlists folder");
        }
        else
            Console.WriteLine("Base playlists folder already exists");

        Console.WriteLine("Checking base artwork folder");
        bool doesBaseArtworkFolderExist = Directory.Exists(_artworkPath);
        if (!doesBaseArtworkFolderExist)
        {
            Directory.CreateDirectory(_artworkPath);
            Console.WriteLine("Created base artwork folder");
        }
        else
            Console.WriteLine("Base artwork folder already exists");

        // Delete all playlist folders that are not in the manifest
        Console.WriteLine("Deleting playlist folders not in manifest");
        var playlistPaths = Directory.GetDirectories(_archivePath + "/playlists");
        foreach (var playlist in playlistPaths)
        {
            if (_manifest.TrackedPlaylists.Values.Any(x => x.FolderPath == playlist))
            {
                Console.WriteLine($"Playlist {playlist} already exists in folder");
                continue;
            }
            else
            {
                Directory.Delete(playlist, true);
                Console.WriteLine($"Deleted playlist {playlist}");
            }
        }
        // Create all playlist folders that are in the manifest
        Console.WriteLine("Creating playlist folders");
        foreach (var playlist in _manifest.TrackedPlaylists.Values)
        {
            Console.WriteLine($"Creating playlist folder {playlist.FolderPath}");
            if (!Directory.Exists(playlist.FolderPath))
            {
                Directory.CreateDirectory(playlist.FolderPath);
            }
        }
        Console.WriteLine("Playlist folders handled");
    }

    private async Task SyncTracksAsync(HttpClient http)
    {
        if (_IsLikedPlaylistSelected)
            await DownloadLikedSongsAsync(http);

        foreach (var playlist in _TrackedPlaylists)
        {
            await HandleTracksFromPlaylistAsync(http, playlist);
        }

        PlaylistSeclectionCompleted(this, EventArgs.Empty);
    }

    private async Task DownloadLikedSongsAsync(HttpClient http)
    {
        int alreadyDownloaded = 0;
        int downloaded = 0;
        int failed = 0;
        HashSet<string> FailedDownloads = new();
        _seenTrackIds = new();
        var playlist = _manifest.TrackedPlaylists[LikedPlaylistId];
        Console.WriteLine($"\n\nFetching Liked Songs...");
        await foreach (var track in GetLikedSongs())
        {
            _seenTrackIds.Add(track.Id);

            Console.WriteLine($"{_seenTrackIds.Count} : Checking track {track.Title}");

            var trackId = track.Id.ToString();
            var fileName =
                string.Concat(track.Title!.Split(Path.GetInvalidFileNameChars())) + ".mp3";
            var filePath = Path.Combine(_tracksPath, fileName);

            var artworkPath = await FetchAndSaveArtwork(http, track.Title, track.ArtworkUrl);

            TrackManifestEntry currentTrack;

            if (!_manifest.Tracks.ContainsKey(trackId))
            {
                currentTrack = _manifest.Tracks[trackId] = new TrackManifestEntry
                {
                    Id = track.Id,
                    Title = track.Title,
                    InPlaylists = new HashSet<long> { playlist.Id },
                    ArtworkPath = artworkPath,
                };
            }
            else
            {
                Console.WriteLine($"Track entry already exists in {playlist.Title}");
                currentTrack = _manifest.Tracks[trackId];
                _manifest.Tracks[trackId].InPlaylists.Add(playlist.Id);
            }

            if (!_manifest.TrackedPlaylists[playlist.Id].TrackIds.Contains(track.Id))
            {
                _manifest.TrackedPlaylists[playlist.Id].TrackIds.Add(track.Id);
            }

            if (!File.Exists(filePath))
            {
                try
                {
                    Console.WriteLine($"Downloading...");
                    await _soundcloud!.DownloadAsync(track, filePath);
                    if (File.Exists(filePath))
                    {
                        Console.WriteLine($"Downloaded successfully");
                        if (_manifest.FailedDownloads.ContainsKey(track.Id))
                        {
                            _manifest.FailedDownloads[track.Id].InPlaylists.Remove(playlist.Id);
                        }
                        downloaded++;
                    }
                    else
                    {
                        _manifest.FailedDownloads[track.Id] = currentTrack;
                        Console.WriteLine(
                            $"-----------Failed to download {track.Title}-----------\n No file written"
                        );
                        FailedDownloads.Add(track.Title);
                        failed++;
                    }
                }
                catch (HttpRequestException ex)
                {
                    _manifest.FailedDownloads[track.Id] = currentTrack;
                    Console.WriteLine(
                        $"-----------Failed to download {track.Title}-----------\n {ex.Message}"
                    );
                    FailedDownloads.Add(track.Title);
                    failed++;
                }
            }
            else
            {
                Console.WriteLine($"File already exists, skipping...");
                alreadyDownloaded++;
            }

            SaveManifest();

            if (File.Exists(filePath))
            {
                var symlinkName =
                    string.Concat(track.Title!.Split(Path.GetInvalidFileNameChars())) + ".mp3";
                var symlinkPath = Path.Combine(playlist.FolderPath, symlinkName);
                if (!File.Exists(symlinkPath))
                {
                    File.CreateSymbolicLink(symlinkPath, filePath);
                    Console.WriteLine("Symlink created");
                }
                else
                {
                    Console.WriteLine($"Symlink already exists, skipping");
                }
            }

            var vm = (MainWindowViewModel)DataContext!;
            vm.CurrentTrack = track;
            vm.ArtworkBitmap = File.Exists(artworkPath)
                ? new Bitmap(artworkPath)
                : await FetchBitmap(http, track.ArtworkUrl);
        }

        int playlistLength = playlist.Title.Length;
        string bottomLine = new string('-', 21 + playlistLength + 10);
        Console.WriteLine(
            $"\n----------{playlist.Title} completed-----------\n"
                + $"Total Songs Processed: {_seenTrackIds.Count}\n"
                + $"Already on disk: {alreadyDownloaded}\n"
                + $"Downloaded: {downloaded}\n"
                + $"Failed to download: {failed}\n"
                + $"{string.Join(", ", FailedDownloads)}\n"
                + bottomLine
        );
        FindPotentiallyDeletedTracks(_manifest.TrackedPlaylists[playlist.Id]);
    }

    private void FindPotentiallyDeletedTracks(TrackedPlaylist playlist)
    {
        var msg = $"\nFinding potentially deleted tracks in {playlist.Title}";
        Console.WriteLine(msg);
        Console.WriteLine(new string('-', msg.Length));
        int potentialDeleted = 0;
        int restored = 0;
        int noChanges = 0;
        HashSet<long> potentiallyDeletedTracks = new();
        foreach (var track in playlist.TrackIds)
        {
            if (!_seenTrackIds.Contains(track))
            {
                Console.WriteLine($"Track {track} is no longer in {playlist.Title}");
                Console.WriteLine($"Added to potentially deleted tracks");
                if (!_manifest.PotentiallyDeletedTracks.ContainsKey(track))
                    _manifest.PotentiallyDeletedTracks[track] = new HashSet<long>();
                _manifest.PotentiallyDeletedTracks[track].Add(playlist.Id);
                potentialDeleted++;
                potentiallyDeletedTracks.Add(track);
            }
            else if (
                _manifest.PotentiallyDeletedTracks.ContainsKey(track)
                && _manifest.PotentiallyDeletedTracks[track].Contains(playlist.Id)
            )
            {
                Console.WriteLine(
                    $"Track {track} is now in {playlist.Title}.\nRemoved from potentially deleted tracks"
                );
                _manifest.PotentiallyDeletedTracks[track].Remove(playlist.Id);
                restored++;
            }
            else
            {
                noChanges++;
            }
        }
        Console.WriteLine(
            $"Potentially deleted tracks in {playlist.Title}: {potentialDeleted}/{playlist.TrackIds.Count} \n"
                + $"Restored tracks: {restored}/{playlist.TrackIds.Count} \n"
                + $"No changes: {noChanges}/{playlist.TrackIds.Count}"
        );
        if (potentiallyDeletedTracks.Count > 0)
        {
            Console.WriteLine(
                $"\nPotentially deleted tracks: {string.Join(", ", potentiallyDeletedTracks)}"
            );
        }
        Console.WriteLine(new string('-', msg.Length));
    }

    private async Task HandleTracksFromPlaylistAsync(HttpClient http, TrackedPlaylist playlist)
    {
        int alreadyDownloaded = 0;
        int downloaded = 0;
        int failed = 0;
        HashSet<string> FailedDownloads = new();
        _seenTrackIds = new();
        Console.WriteLine($"\n\nFetching tracks from playlist {playlist.Title}");
        await foreach (var track in GetTracksFromPlaylist(playlist.PermalinkUrl))
        {
            Console.WriteLine($"{_seenTrackIds.Count} : Checking track {track.Title}");

            _seenTrackIds.Add(track.Id);

            var trackId = track.Id.ToString();
            var fileName =
                string.Concat(track.Title!.Split(Path.GetInvalidFileNameChars())) + ".mp3";
            var filePath = Path.Combine(_tracksPath, fileName);

            var artworkPath = await FetchAndSaveArtwork(http, track.Title, track.ArtworkUrl);

            TrackManifestEntry currentTrack;

            if (!_manifest.Tracks.ContainsKey(trackId))
            {
                Console.WriteLine($"Creating new track entry");
                currentTrack = _manifest.Tracks[trackId] = new TrackManifestEntry
                {
                    Id = track.Id,
                    Title = track.Title,
                    InPlaylists = new HashSet<long> { playlist.Id },
                    ArtworkPath = artworkPath,
                };
            }
            else
            {
                Console.WriteLine($"Track entry already exists in {playlist.Title}");
                currentTrack = _manifest.Tracks[trackId];
                _manifest.Tracks[trackId].InPlaylists.Add(playlist.Id);
            }

            if (!_manifest.TrackedPlaylists[playlist.Id].TrackIds.Contains(track.Id))
                _manifest.TrackedPlaylists[playlist.Id].TrackIds.Add(track.Id);

            if (!File.Exists(filePath))
            {
                try
                {
                    Console.WriteLine($"Downloading...");
                    await _soundcloud!.DownloadAsync(track, filePath);
                    if (File.Exists(filePath))
                    {
                        Console.WriteLine($"Downloaded successfully");
                        if (_manifest.FailedDownloads.ContainsKey(track.Id))
                        {
                            _manifest.FailedDownloads[track.Id].InPlaylists.Remove(playlist.Id);
                        }
                        downloaded++;
                    }
                    else
                    {
                        _manifest.FailedDownloads[track.Id] = currentTrack;
                        Console.WriteLine(
                            $"-----------Failed to download {track.Title}-----------\n No file written"
                        );
                        FailedDownloads.Add(track.Title);
                        failed++;
                    }
                }
                catch (HttpRequestException ex)
                {
                    _manifest.FailedDownloads[track.Id] = currentTrack;
                    Console.WriteLine(
                        $"-----------Failed to download {track.Title}-----------\n {ex.Message}"
                    );
                    FailedDownloads.Add(track.Title);
                    failed++;
                }
            }
            else
            {
                Console.WriteLine($"File already exists, skipping");
                alreadyDownloaded++;
            }

            SaveManifest();

            if (File.Exists(filePath))
            {
                var symlinkName =
                    string.Concat(track.Title!.Split(Path.GetInvalidFileNameChars())) + ".mp3";
                var symlinkPath = Path.Combine(playlist.FolderPath, symlinkName);
                if (!File.Exists(symlinkPath))
                {
                    File.CreateSymbolicLink(symlinkPath, filePath);
                    Console.WriteLine("Symlink created");
                }
                else
                {
                    Console.WriteLine($"Symlink already exists, skipping");
                }
            }

            var vm = (MainWindowViewModel)DataContext!;
            vm.CurrentTrack = track;
            vm.ArtworkBitmap = File.Exists(artworkPath)
                ? new Bitmap(artworkPath)
                : await FetchBitmap(http, track.ArtworkUrl);
        }

        int playlistLength = playlist.Title.Length;
        string bottomLine = new string('-', 21 + playlistLength + 10);
        Console.WriteLine(
            $"\n----------{playlist.Title} completed-----------\n"
                + $"Total Songs Processed: {_seenTrackIds.Count}\n"
                + $"Already on disk: {alreadyDownloaded}\n"
                + $"Downloaded: {downloaded}\n"
                + $"Failed to download: {failed}\n"
                + $"{string.Join(", ", FailedDownloads)}\n"
                + bottomLine
        );
        FindPotentiallyDeletedTracks(playlist);
    }

    private IAsyncEnumerable<Track> GetTracksFromPlaylist(string playlistUrl = "")
    {
        return _soundcloud!.Playlists.GetTracksAsync(playlistUrl);
    }

    private IAsyncEnumerable<Track> GetLikedSongs()
    {
        return _soundcloud!.Users.GetLikedTracksAsync(_profileUrl).Take(5); // only get first 5
        // return _soundcloud!.Users.GetLikedTracksAsync(_profileUrl);
    }

    private async Task<string> FetchAndSaveArtwork(HttpClient http, string trackTitle, Uri? url)
    {
        var safeName = string.Concat(trackTitle.Split(Path.GetInvalidFileNameChars()));
        var artworkPath = Path.Combine(_artworkPath, safeName + ".jpg");

        if (File.Exists(artworkPath))
            return artworkPath;

        try
        {
            var bytes = await http.GetByteArrayAsync(url ?? FallbackArtwork);
            await File.WriteAllBytesAsync(artworkPath, bytes);
        }
        catch (HttpRequestException)
        {
            // save fallback artwork if track artwork fetch fails
            var bytes = await http.GetByteArrayAsync(FallbackArtwork);
            await File.WriteAllBytesAsync(artworkPath, bytes);
        }

        return artworkPath;
    }

    private async Task<Bitmap?> FetchBitmap(HttpClient http, Uri? url)
    {
        try
        {
            var bytes = await http.GetByteArrayAsync(url ?? FallbackArtwork);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch (HttpRequestException)
        {
            _fallbackBitmap ??= await FetchBitmap(http, FallbackArtwork);
            return _fallbackBitmap;
        }
    }
}
