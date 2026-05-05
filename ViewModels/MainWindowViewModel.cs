namespace soundCloudArchiver.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using SoundCloudExplode.Tracks;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private Track? _currentTrack;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _artworkBitmap;
}
