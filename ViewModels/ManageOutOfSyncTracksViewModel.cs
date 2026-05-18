using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace soundCloudArchiver.ViewModels;

using soundCloudArchiver.Models;

public partial class ManageOutOfSyncTracksViewModel : ViewModelBase
{
    [ObservableProperty]
    private long _trackId;

    [ObservableProperty]
    private string _artist = "";

    [ObservableProperty]
    private string _trackFilePath = "";

    [ObservableProperty]
    private string _artworkPath = "";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _trackFileName = "";

    [ObservableProperty]
    private Bitmap? _artworkBitmap;

    [ObservableProperty]
    private bool _isKept;

    public HashSet<long> OriginalPlaylistIds { get; set; } = new();

    public ObservableCollection<PotentiallyDeletedPlaylistItem> Playlists { get; } = new();

    [RelayCommand]
    private void CheckAll()
    {
        foreach (var p in Playlists)
            p.KeepInPlaylist = true;
    }

    [RelayCommand]
    private void UncheckAll()
    {
        foreach (var p in Playlists)
            p.KeepInPlaylist = false;
    }
}
