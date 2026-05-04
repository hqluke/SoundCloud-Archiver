using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.Configuration;
using SoundCloudExplode;
using SoundCloudExplode.Common;

namespace soundCloudArchiver.Views;

public partial class MainWindow : Window
{
    private readonly SoundCloudClient _soundcloud;
    private readonly string _clientId;
    private readonly string _profileUrl;
    private readonly string _archivePath;

    public MainWindow()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.local.json", optional: true)
            .Build();

        _soundcloud = new SoundCloudClient(config["SoundCloud:ClientId"]!);
        _clientId = _soundcloud.ClientId;
        _profileUrl = config["SoundCloud:ProfileUrl"]!;
        _archivePath = config["Archiver:DownloadPath"]!;
        InitializeComponent();
        Start();
    }

    public void Start()
    {
        GetTracksFromPlaylist();
        _ = GetLikedSongs();
    }

    private async void GetTracksFromPlaylist()
    {
        var playlistUrl = "https://soundcloud.com/user-144755027/sets/let-em-know";
        var tracks = await _soundcloud.Playlists.GetTracksAsync(playlistUrl);

        int count = 0;

        foreach (var track in tracks)
        {
            count++;
            Console.WriteLine($"Playlist track {count}/{tracks.Count}: {track.Title}");
        }

        Console.WriteLine($"Total playlist tracks: {count}");
    }

    private async Task GetLikedSongs()
    {
        try
        {
            Console.WriteLine("Fetching liked songs...");
            var count = 0;
            var likes = await _soundcloud.Users.GetLikedTracksAsync(_profileUrl);

            foreach (var track in likes)
            {
                count++;
                Console.WriteLine($"Liked track {count}: {track.Title} - {track.Duration}");
            }

            Console.WriteLine($"Total liked tracks: {count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching liked songs: {ex}");
        }
    }
}
