using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Configuration;
using soundCloudArchiver.ViewModels;
using SoundCloudExplode;
using SoundCloudExplode.Tracks;

namespace soundCloudArchiver.Views;

public partial class MainWindow : Window
{
    private List<Track> _tracks = new();
    private readonly HashSet<long> _seenTrackIds = new();
    private SoundCloudClient? _soundcloud;
    private string _profileUrl;
    private string _archivePath;

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

        // Subscribe to window opening event so we can refresh the client ID
        // and eventually check what tracks were added/removed from the user's liked songs/playlists everytime the window is opened
        Opened += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var vm = (MainWindowViewModel)DataContext!;
        using var http = new HttpClient();

        // Fetch valid client ID first
        var tempClient = new SoundCloudClient();
        var clientId = await tempClient.GetClientIdAsync();

        // Construct the real client with it
        _soundcloud = new SoundCloudClient(clientId);
        Console.WriteLine("App settings loaded");
        await foreach (var track in GetTracksFromPlaylist())
        {
            //adds or returns false if track already exists
            if (!_seenTrackIds.Add(track.Id))
                continue;
            _tracks.Add(track);
            Console.WriteLine(
                $"Track {_tracks.Count}: {track.Title} - {track.Duration} \n {track.ArtworkUrl}"
            );
            vm.CurrentTrack = track;
            vm.ArtworkBitmap = await FetchBitmap(http, track.ArtworkUrl);
        }
        Console.WriteLine($"Stored {_tracks.Count} tracks");

        await foreach (var track in GetLikedSongs())
        {
            //adds or returns false if track already exists
            if (!_seenTrackIds.Add(track.Id))
                continue;
            _tracks.Add(track);
            Console.WriteLine($"Track {_tracks.Count}: {track.Title} - {track.Duration}");
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
        return _soundcloud.Playlists.GetTracksAsync(playlistUrl);
    }

    private IAsyncEnumerable<Track> GetLikedSongs()
    {
        Console.WriteLine("Fetching liked songs...");
        return _soundcloud.Users.GetLikedTracksAsync(_profileUrl);
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
            // fetch fallback once and reuse it
            _fallbackBitmap ??= await FetchBitmap(http, FallbackArtwork);
            return _fallbackBitmap;
        }
    }

    // if (!TrackExistsOnDisk(track))
    //     await _soundcloud.DownloadAsync(track, _archivePath);
}
