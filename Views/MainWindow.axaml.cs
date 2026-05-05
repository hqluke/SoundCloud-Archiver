using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Configuration;
using soundCloudArchiver.Models;
using soundCloudArchiver.ViewModels;
using SoundCloudExplode;
using SoundCloudExplode.Tracks;
using SoundCloudExplode.Playlists;

namespace soundCloudArchiver.Views;

public partial class MainWindow : Window
{
    private List<Track> _tracks = new();
    private readonly HashSet<long> _seenTrackIds = new();
    private SoundCloudClient? _soundcloud;
    private string _profileUrl = "";
    private string _archivePath = "";
    private Manifest _manifest = new();
    private readonly string _manifestPath = "manifest.json";

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
            await ShowPlaylistSelectionAsync(http);
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
        var json = JsonSerializer.Serialize(_manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_manifestPath, json);
    }

    private async Task ShowPlaylistSelectionAsync(HttpClient http)
    {
        try
        {
            var vm = (MainWindowViewModel)DataContext!;
            vm.PlaylistSelectionItems.Clear();

            Console.WriteLine("Fetching playlists for selection...");
            var allPlaylists = new List<Playlist>();
            await foreach (var playlist in _soundcloud!.Users.GetPlaylistsAsync(_profileUrl))
            {
                allPlaylists.Add(playlist);
                Console.WriteLine($"Fetched playlist: {playlist.Title} (ID: {playlist.Id})");
            }

            Console.WriteLine($"Total playlists fetched: {allPlaylists.Count}");

            foreach (var playlist in allPlaylists)
            {
                var isTracked = _manifest.TrackedPlaylists.ContainsKey(playlist.Id ?? 0);
                var item = new PlaylistSelectionItem(playlist, isTracked);
                vm.PlaylistSelectionItems.Add(item);
                item.ArtworkBitmap = await FetchBitmap(http, playlist.ArtworkUrl);
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
            if (message.Length > 0) message += "\n";
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
        var result = await dialog.ShowDialog(this);

        if (!result)
            return false;

        using var http = new HttpClient();
        foreach (var item in toTrack)
        {
            var folderName = $"{item.Playlist.Title}_{item.Playlist.Permalink}".Replace("/", "_");
            var folderPath = Path.Combine(_archivePath, "playlists", folderName);

            _manifest.TrackedPlaylists[item.Playlist.Id ?? 0] = new TrackedPlaylist
            {
                Id = item.Playlist.Id ?? 0,
                Permalink = item.Playlist.Permalink!,
                Title = item.Playlist.Title!,
                PermalinkUrl = item.Playlist.PermalinkUrl?.ToString() ?? "",
                ArtworkUrl = item.Playlist.ArtworkUrl?.ToString() ?? "",
                FolderPath = folderPath
            };
        }

        foreach (var item in toUntrack)
        {
            _manifest.TrackedPlaylists.Remove(item.Playlist.Id ?? 0);
        }

        _manifest.AppState.IsInitialSetupComplete = true;
        SaveManifest();

        vm.IsPlaylistSelectionVisible = false;
        return true;
    }

    private void OnCancelPlaylistSelection()
    {
        var vm = (MainWindowViewModel)DataContext!;
        if (!_manifest.AppState.IsInitialSetupComplete)
        {
            return;
        }
        vm.IsPlaylistSelectionVisible = false;
    }

    private async Task SyncTracksAsync(HttpClient http)
    {
        await foreach (var track in GetTracksFromPlaylist())
        {
            if (!_seenTrackIds.Add(track.Id))
                continue;
            _tracks.Add(track);
            Console.WriteLine($"Track {_tracks.Count}: {track.Title} - {track.Duration}");
            var vm = (MainWindowViewModel)DataContext!;
            vm.CurrentTrack = track;
            vm.ArtworkBitmap = await FetchBitmap(http, track.ArtworkUrl);
        }
        Console.WriteLine($"Stored {_tracks.Count} tracks");

        await foreach (var track in GetLikedSongs())
        {
            if (!_seenTrackIds.Add(track.Id))
                continue;
            _tracks.Add(track);
            Console.WriteLine($"Track {_tracks.Count}: {track.Title} - {track.Duration}");
            var vm = (MainWindowViewModel)DataContext!;
            vm.CurrentTrack = track;
            vm.ArtworkBitmap = await FetchBitmap(http, track.ArtworkUrl);
        }
        Console.WriteLine($"Stored {_tracks.Count} tracks");
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
