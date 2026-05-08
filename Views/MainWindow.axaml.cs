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
    private List<Track> potentiallyDeletedTracks = new();
    private readonly HashSet<long> _seenTrackIds = new();

    private SoundCloudClient? _soundcloud;

    private string _profileUrl = "";
    private string _archivePath = "";
    private string _tracksPath => Path.Combine(_archivePath, "tracks");
    private string _playlistsPath => Path.Combine(_archivePath, "playlists");
    private string _artworkPath => Path.Combine(_archivePath, "artwork");

    private Manifest _manifest = new();
    private readonly string _manifestPath = "manifest.json";

    private TaskCompletionSource<bool> _playlistSelectionComplete = new();

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
            await _playlistSelectionComplete.Task;
            await SyncTracksAsync(http);
        }
        else
        {
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
        try
        {
            var vm = (MainWindowViewModel)DataContext!;
            vm.PlaylistSelectionItems.Clear();

            if (!Directory.Exists(_artworkPath))
                Directory.CreateDirectory(_artworkPath);

            Console.WriteLine("Fetching playlists for selection...");
            var allPlaylists = new List<Playlist>();
            await foreach (var playlist in _soundcloud!.Users.GetPlaylistsAsync(_profileUrl))
            {
                allPlaylists.Add(playlist);
                Console.WriteLine($"Fetched playlist: {playlist.Title} (ID: {playlist.Id})");
            }

            Console.WriteLine($"Total playlists fetched: {allPlaylists.Count}");

            Console.WriteLine("Fetching artwork for playlists...");
            foreach (var playlist in allPlaylists)
            {
                var isTracked = _manifest.TrackedPlaylists.ContainsKey(playlist.Id ?? 0);
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
            var folderName = $"{item.Playlist.Title}_{item.Playlist.Permalink}".Replace("/", "_");
            var folderPath = Path.Combine(_archivePath, "playlists", folderName);

            var artworkPath = Path.Combine(_artworkPath, item.Playlist.Title + ".jpg");

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
            _manifest.TrackedPlaylists.Remove(item.Playlist.Id ?? 0);
        }

        _manifest.AppState.IsInitialSetupComplete = true;
        SaveManifest();
        Console.WriteLine("Creating playlist folders");
        CreatePlaylistFolders();

        vm.IsPlaylistSelectionVisible = false;
        _playlistSelectionComplete.TrySetResult(true);
        return true;
    }

    private void OnCancelPlaylistSelection()
    {
        Console.WriteLine("Cancelling playlist selection");
        var vm = (MainWindowViewModel)DataContext!;
        if (!_manifest.AppState.IsInitialSetupComplete)
        {
            return;
        }
        vm.IsPlaylistSelectionVisible = false;
        _playlistSelectionComplete.TrySetResult(false);
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
        await DownloadLikedSongsAsync(http);

        await foreach (var track in GetTracksFromPlaylist())
        {
            if (!_seenTrackIds.Add(track.Id))
                continue;
            _seenTrackIds.Add(track.Id);
            Console.WriteLine($"Track {_seenTrackIds.Count}: {track.Title} - {track.Duration}");
            var vm = (MainWindowViewModel)DataContext!;
            vm.CurrentTrack = track;
            vm.ArtworkBitmap = await FetchBitmap(http, track.ArtworkUrl);
        }
        Console.WriteLine($"Stored {_seenTrackIds.Count} tracks");
    }

    private async Task DownloadLikedSongsAsync(HttpClient http)
    {
        await foreach (var track in GetLikedSongs())
        {
            if (!_seenTrackIds.Add(track.Id))
                continue;

            _seenTrackIds.Add(track.Id);

            var trackId = track.Id.ToString();
            var fileName =
                string.Concat(track.Title!.Split(Path.GetInvalidFileNameChars())) + ".mp3";
            var filePath = Path.Combine(_tracksPath, fileName);

            var artworkPath = await FetchAndSaveArtwork(http, track.Title, track.ArtworkUrl);

            if (!_manifest.Tracks.ContainsKey(trackId))
            {
                _manifest.Tracks[trackId] = new TrackManifestEntry
                {
                    Id = track.Id,
                    Title = track.Title,
                    InLikes = true,
                    ArtworkPath = Path.Combine(_artworkPath, track.Title + ".jpg"),
                };
            }
            else
            {
                _manifest.Tracks[trackId].InLikes = true;
            }

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Downloading: {track.Title}");
                await _soundcloud!.DownloadAsync(track, filePath);
                SaveManifest();
            }
            else
            {
                Console.WriteLine($"Already exists, skipping: {track.Title}");
            }

            var vm = (MainWindowViewModel)DataContext!;
            vm.CurrentTrack = track;
            vm.ArtworkBitmap = File.Exists(artworkPath)
                ? new Bitmap(artworkPath)
                : await FetchBitmap(http, track.ArtworkUrl);
        }

        Console.WriteLine($"Liked songs done. Total: {_seenTrackIds.Count}");
    }

    private IAsyncEnumerable<Track> GetTracksFromPlaylist(
        string playlistUrl = "https://soundcloud.com/user-144755027/sets/let-em-know"
    )
    {
        Console.WriteLine("Fetching tracks from playlist...");
        return _soundcloud!.Playlists.GetTracksAsync(playlistUrl);
    }

    private IAsyncEnumerable<Track> GetLikedSongs()
    {
        Console.WriteLine("Fetching liked songs...");
        return _soundcloud!.Users.GetLikedTracksAsync(_profileUrl);
    }

    private async Task<string> FetchAndSaveArtwork(HttpClient http, string trackTitle, Uri? url)
    {
        var artworkPath = Path.Combine(_artworkPath, trackTitle + ".jpg");

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
