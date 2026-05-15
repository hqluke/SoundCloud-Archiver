using CommunityToolkit.Mvvm.ComponentModel;

namespace soundCloudArchiver.Models;

public partial class PotentiallyDeletedPlaylistItem : ObservableObject
{
    public long PlaylistId { get; set; }
    public string PlaylistTitle { get; set; } = "";
    public string PlaylistPath { get; set; } = "";

    [ObservableProperty]
    private bool _keepInPlaylist;
}
