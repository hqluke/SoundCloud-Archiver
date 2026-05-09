using System.Collections.Generic;
namespace soundCloudArchiver.Models;

public class TrackedPlaylist
{
    public long Id { get; set; }
    public string Permalink { get; set; } = "";
    public string Title { get; set; } = "";
    public string PermalinkUrl { get; set; } = "";
    public string ArtworkPath { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public HashSet<long> TrackIds { get; set; } = new();
}
