using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace soundCloudArchiver.Models;

public class Manifest
{
    [JsonPropertyName("app_state")]
    public AppState AppState { get; set; } = new();

    [JsonPropertyName("tracked_playlists")]
    public Dictionary<long, TrackedPlaylist> TrackedPlaylists { get; set; } = new();

    [JsonPropertyName("tracks")]
    public Dictionary<string, TrackManifestEntry> Tracks { get; set; } = new();
}
