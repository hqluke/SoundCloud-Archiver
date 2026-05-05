namespace soundCloudArchiver.Models;

public class TrackedPlaylist
{
    public long Id { get; set; }
    public string Permalink { get; set; } = "";
    public string Title { get; set; } = "";
    public string PermalinkUrl { get; set; } = "";
    public string ArtworkUrl { get; set; } = "";
    public string FolderPath { get; set; } = "";
}
