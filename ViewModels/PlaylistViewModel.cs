using System.Collections.Generic;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SoundCloudExplode.Playlists;

namespace soundCloudArchiver.ViewModels;
using soundCloudArchiver.Models;

public partial class PlaylistViewModel : ViewModelBase
{
    [ObservableProperty]
    private long _id;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _permalink = "";

    [ObservableProperty]
    private string? _permalinkUrl;

    [ObservableProperty]
    private string _artworkPath = "";

    [ObservableProperty]
    private Bitmap? _artworkBitmap;

    [ObservableProperty]
    private string _folderPath = "";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isTracked;

    [ObservableProperty]
    private HashSet<long> _trackIds = new();

    public bool IsLiked => Id == -1;

    public PlaylistViewModel() { }

    public PlaylistViewModel(Playlist playlist)
    {
        Id = playlist.Id ?? -1;
        Title = playlist.Title ?? "";
        Permalink = playlist.Permalink ?? "";
        PermalinkUrl = playlist.PermalinkUrl?.ToString();
    }

    public PlaylistViewModel(TrackedPlaylist tracked)
    {
        Id = tracked.Id;
        Title = tracked.Title;
        Permalink = tracked.Permalink;
        PermalinkUrl = tracked.PermalinkUrl;
        ArtworkPath = tracked.ArtworkPath;
        FolderPath = tracked.FolderPath;
        TrackIds = tracked.TrackIds;
        IsTracked = true;
    }

    public TrackedPlaylist ToTrackedPlaylist()
    {
        return new TrackedPlaylist
        {
            Id = Id,
            Permalink = Permalink,
            Title = Title,
            PermalinkUrl = PermalinkUrl ?? "",
            ArtworkPath = ArtworkPath,
            FolderPath = FolderPath,
            TrackIds = TrackIds,
        };
    }
}
