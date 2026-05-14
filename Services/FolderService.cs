using System;
using System.IO;
using System.Linq;
using soundCloudArchiver.Models;

namespace soundCloudArchiver.Services;

public class FolderService
{
    private readonly string _tracksPath;
    private readonly string _playlistsPath;
    private readonly string _artworkPath;
    private readonly string _likedTracksPath;
    private readonly string _archivePath;

    public FolderService(string archivePath, string likedTracksPath)
    {
        _archivePath = archivePath;
        _tracksPath = Path.Combine(archivePath, "tracks");
        _playlistsPath = Path.Combine(archivePath, "playlists");
        _artworkPath = Path.Combine(archivePath, "artwork");
        _likedTracksPath = likedTracksPath;
    }

    public void CreateAllFolders()
    {
        Console.WriteLine("Creating folders");
        CreateIfMissing(_tracksPath, "base tracks folder");
        CreateIfMissing(_likedTracksPath, "base liked tracks folder");
        CreateIfMissing(_playlistsPath, "base playlists folder");
        CreateIfMissing(_artworkPath, "base artwork folder");
    }

    public void RemoveOrphanedFolders(Manifest manifest)
    {
        Console.WriteLine("Deleting playlist folders not in manifest");
        var playlistPaths = Directory.GetDirectories(_archivePath + "/playlists");
        foreach (var playlist in playlistPaths)
        {
            if (manifest.TrackedPlaylists.Values.Any(x => x.FolderPath == playlist))
            {
                Console.WriteLine($"Playlist {playlist} already exists in folder");
                continue;
            }
            Directory.Delete(playlist, true);
            Console.WriteLine($"Deleted playlist {playlist}");
        }
    }

    public void CreateTrackedPlaylistFolders(Manifest manifest)
    {
        Console.WriteLine("Creating playlist folders");
        foreach (var playlist in manifest.TrackedPlaylists.Values)
        {
            Console.WriteLine($"Creating playlist folder {playlist.FolderPath}");
            if (!Directory.Exists(playlist.FolderPath))
                Directory.CreateDirectory(playlist.FolderPath);
        }
        Console.WriteLine("Playlist folders handled");
    }

    private static void CreateIfMissing(string path, string label)
    {
        Console.WriteLine($"Checking {label}");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            Console.WriteLine($"Created {label}");
        }
        else
            Console.WriteLine($"{label} already exists");
    }
}
