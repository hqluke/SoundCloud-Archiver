using System.Collections.Generic;

namespace soundCloudArchiver.Models;

public class TrackManifestEntry
{
    public string Filename { get; set; } = "";
    public bool InLikes { get; set; } = false;
    public List<long> InPlaylists { get; set; } = new();
    public bool IsKept { get; set; } = false;
}
