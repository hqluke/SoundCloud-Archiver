using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace soundCloudArchiver.Models;

public class TrackManifestEntry
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public HashSet<long> InPlaylists { get; set; } = new();
    public bool IsKept { get; set; } = false;
    public string ArtworkPath { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string SoundCloudUrl { get; set; } = "";
}
