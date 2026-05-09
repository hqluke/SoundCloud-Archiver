using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace soundCloudArchiver.Models;

public class Manifest
{
    [JsonPropertyName("app_state")]
    public AppState AppState { get; set; } = new();

    [JsonPropertyName("potentially_deleted_tracks")]
    public Dictionary<long, HashSet<long>> PotentiallyDeletedTracks { get; set; } = new();

    [JsonPropertyName("failed_downloads")]
    public Dictionary<long, TrackManifestEntry> FailedDownloads { get; set; } = new();

    [JsonPropertyName("tracked_playlists")]
    public Dictionary<long, TrackedPlaylist> TrackedPlaylists { get; set; } = new();

    [JsonPropertyName("tracks")]
    public Dictionary<string, TrackManifestEntry> Tracks { get; set; } = new();
}
