using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SoundCloudExplode.Playlists;

namespace soundCloudArchiver.ViewModels;
using soundCloudArchiver.Models;

public partial class SyncedPlaylistItem : ViewModelBase
{
    [ObservableProperty]
    private TrackedPlaylist _playlist;

    [ObservableProperty]
    private Bitmap? _artworkBitmap;

    public SyncedPlaylistItem(TrackedPlaylist playlist)
    {
        _playlist = playlist;
    }

}

