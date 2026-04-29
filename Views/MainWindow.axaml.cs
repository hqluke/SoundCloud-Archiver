using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Microsoft.Extensions.Configuration;
using SoundCloudExplode;
using SoundCloudExplode.Common;

namespace soundCloudArchiver.Views;

public partial class MainWindow : Window
{
    private readonly SoundCloudClient _soundcloud;
    private readonly string _profileUrl;
    private readonly string _archivePath;

    public MainWindow()
    {
        var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
        _soundcloud = new SoundCloudClient(config["SoundCloud:ClientId"]!);
        _profileUrl = config["SoundCloud:ProfileUrl"]!;
        _archivePath = config["Archiver:DownloadPath"]!;

        InitializeComponent();
        Start();
    }

    public void Start()
    {
        GetTracksFromPlaylist();
    }

    private async void GetTracksFromPlaylist()
    {
        var playlistUrl = "https://soundcloud.com/user-144755027/sets/let-em-know";
        var tracks = await _soundcloud.Playlists.GetTracksAsync(playlistUrl);

        var lines = new System.Text.StringBuilder();

        foreach (var track in tracks)
        {
            var safeName = string.Join("_", track.Title.Split(Path.GetInvalidFileNameChars()));
            var filePath = Path.Combine(_archivePath, $"{safeName}.mp3");

            Console.WriteLine($"Downloading: {track.Title}");
            await _soundcloud.DownloadAsync(track, filePath);
        }

        await System.IO.File.WriteAllTextAsync("/tmp/sc-debug.txt", lines.ToString());
    }
}
