using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SoundCloudExplode.Playlists;

namespace soundCloudArchiver.ViewModels;

public partial class PlaylistSelectionItem : ViewModelBase
{
    [ObservableProperty]
    private Playlist _playlist;

    [ObservableProperty]
    private bool _isTracked;

    [ObservableProperty]
    private Bitmap? _artworkBitmap;

    public PlaylistSelectionItem(Playlist playlist, bool isTracked = false)
    {
        _playlist = playlist;
        _isTracked = isTracked;
    }
}
