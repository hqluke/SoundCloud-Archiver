using System.IO;
using System.Linq;
using System.Text.Json;

namespace soundCloudArchiver.Services;

using soundCloudArchiver.Models;

public static class ManifestStore
{
    public const long LikedPlaylistId = -1;

    public static Manifest Load(string path, string likedFolderPath)
    {
        Manifest manifest;
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            manifest = JsonSerializer.Deserialize<Manifest>(json) ?? new Manifest();
        }
        else
        {
            manifest = new Manifest();
        }

        if (!manifest.TrackedPlaylists.ContainsKey(LikedPlaylistId))
        {
            manifest.TrackedPlaylists[LikedPlaylistId] = new TrackedPlaylist
            {
                Id = LikedPlaylistId,
                Title = "Liked_Songs",
                FolderPath = likedFolderPath,
            };
        }

        return manifest;
    }

    public static void Save(Manifest manifest, string path)
    {
        var json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(path, json);
    }
}
