using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SoundCloudExplode;
using SoundCloudExplode.Tracks;

namespace soundCloudArchiver.Services;

using soundCloudArchiver.Models;
using soundCloudArchiver.ViewModels;

public class SyncService
{
    private readonly SoundCloudClient _soundcloud;
    private readonly string _profileUrl;
    private readonly string _tracksPath;
    private readonly ArtworkService _artwork;

    public SyncService(
        SoundCloudClient soundcloud,
        string profileUrl,
        string tracksPath,
        ArtworkService artwork
    )
    {
        _soundcloud = soundcloud;
        _profileUrl = profileUrl;
        _tracksPath = tracksPath;
        _artwork = artwork;
    }

    public async Task SyncPlaylistAsync(
        Manifest manifest,
        string manifestPath,
        TrackedPlaylist playlist,
        Action<Track, Bitmap?, string>? onProgress
    )
    {
        int alreadyDownloaded = 0;
        int downloaded = 0;
        int failed = 0;
        var failedDownloads = new HashSet<string>();
        var seenTrackIds = new HashSet<long>();
        var isLiked = playlist.Id == ManifestStore.LikedPlaylistId;

        Console.WriteLine($"\n\nFetching tracks from {playlist.Title}");
        await foreach (var track in GetTracks(playlist))
        {
            seenTrackIds.Add(track.Id);
            Console.WriteLine($"{seenTrackIds.Count} : Checking track {track.Title}");

            var trackId = track.Id.ToString();
            var fileName =
                string.Concat(track.Title!.Split(Path.GetInvalidFileNameChars())) + ".mp3";
            var filePath = Path.Combine(_tracksPath, fileName);

            var artworkPath = await _artwork.FetchAndSaveArtwork(track.Title, track.ArtworkUrl);

            TrackManifestEntry currentTrack;
            var artist = track.User?.Username?.ToString() ?? "";

            if (!manifest.Tracks.ContainsKey(trackId))
            {
                currentTrack = manifest.Tracks[trackId] = new TrackManifestEntry
                {
                    Id = track.Id,
                    Title = track.Title,
                    Artist = artist,
                    InPlaylists = new HashSet<long> { playlist.Id },
                    ArtworkPath = artworkPath,
                    FilePath = filePath,
                    FileName = fileName,
                };
            }
            else
            {
                Console.WriteLine($"Track entry already exists in {playlist.Title}");
                currentTrack = manifest.Tracks[trackId];
                manifest.Tracks[trackId].InPlaylists.Add(playlist.Id);
            }

            if (!manifest.TrackedPlaylists[playlist.Id].TrackIds.Contains(track.Id))
                manifest.TrackedPlaylists[playlist.Id].TrackIds.Add(track.Id);

            if (!File.Exists(filePath))
            {
                try
                {
                    Console.WriteLine($"Downloading...");
                    await _soundcloud.DownloadAsync(track, filePath);
                    if (File.Exists(filePath))
                    {
                        Console.WriteLine($"Downloaded successfully");
                        if (manifest.FailedDownloads.ContainsKey(track.Id))
                            manifest.FailedDownloads[track.Id].InPlaylists.Remove(playlist.Id);

                        downloaded++;
                    }
                    else
                    {
                        manifest.FailedDownloads[track.Id] = currentTrack;
                        Console.WriteLine(
                            $"-----------Failed to download {track.Title}-----------\n No file written"
                        );
                        failedDownloads.Add(track.Title);
                        failed++;
                    }
                }
                catch (HttpRequestException ex)
                {
                    manifest.FailedDownloads[track.Id] = currentTrack;
                    Console.WriteLine(
                        $"-----------Failed to download {track.Title}-----------\n {ex.Message}"
                    );
                    failedDownloads.Add(track.Title);
                    failed++;
                }
            }
            else
            {
                Console.WriteLine($"File already exists, skipping");
                alreadyDownloaded++;
            }

            if (File.Exists(filePath))
            {
                var symlinkName =
                    string.Concat(track.Title!.Split(Path.GetInvalidFileNameChars())) + ".mp3";
                var symlinkPath = Path.Combine(playlist.FolderPath, symlinkName);
                if (!File.Exists(symlinkPath))
                {
                    File.CreateSymbolicLink(symlinkPath, filePath);
                    Console.WriteLine("Symlink created");
                }
                else
                    Console.WriteLine($"Symlink already exists, skipping");
            }

            var bitmap =
                _artwork.LoadBitmap(artworkPath) ?? await _artwork.FetchBitmap(track.ArtworkUrl);
            onProgress?.Invoke(track, bitmap, artist);
        }

        var bottomLine = new string('-', 21 + playlist.Title.Length + 10);
        Console.WriteLine(
            $"\n----------{playlist.Title} completed-----------\n"
                + $"Total Songs Processed: {seenTrackIds.Count}\n"
                + $"Already on disk: {alreadyDownloaded}\n"
                + $"Downloaded: {downloaded}\n"
                + $"Failed to download: {failed}\n"
                + $"{string.Join(", ", failedDownloads)}"
                + $"\nbottomLine"
        );

        FindPotentiallyDeletedTracks(manifest, playlist, seenTrackIds, manifestPath);
    }

    private IAsyncEnumerable<Track> GetTracks(TrackedPlaylist playlist)
    {
        if (playlist.Id == ManifestStore.LikedPlaylistId)
            return _soundcloud.Users.GetLikedTracksAsync(_profileUrl).Take(5);
        // return _soundcloud.Users.GetLikedTracksAsync(_profileUrl);

        return _soundcloud.Playlists.GetTracksAsync(playlist.PermalinkUrl).Take(5);
        // return _soundcloud.Playlists.GetTracksAsync(playlist.PermalinkUrl);
    }

    public static void FindPotentiallyDeletedTracks(
        Manifest manifest,
        TrackedPlaylist playlist,
        HashSet<long> seenTrackIds,
        string manifestPath
    )
    {
        var msg = $"\nFinding potentially deleted tracks in {playlist.Title}";
        Console.WriteLine(msg);
        Console.WriteLine(new string('-', msg.Length));
        int potentialDeleted = 0;
        int restored = 0;
        var potentialDeletedTracks = new List<string>();
        var restoredTracks = new List<string>();
        // Search each track in the manifest
        foreach (var track in manifest.Tracks.Values)
        {
            // Track says it belongs to this playlist (don't check kept tracks)
            if (track.InPlaylists.Contains(playlist.Id) && track.IsKept == false)
            {
                // Playlist says it doesn't have this track, add to potentially deleted tracks
                if (!seenTrackIds.Contains(track.Id))
                {
                    Console.WriteLine($"Track {track.Title} is no longer in {playlist.Title}");
                    Console.WriteLine($"Added to potentially deleted tracks");
                    if (!manifest.PotentiallyDeletedTracks.ContainsKey(track.Id))
                        manifest.PotentiallyDeletedTracks[track.Id] = new HashSet<long>();
                    manifest.PotentiallyDeletedTracks[track.Id].Add(playlist.Id);
                    potentialDeletedTracks.Add(track.Title);
                    potentialDeleted++;
                }
            }
            else if (
                manifest.PotentiallyDeletedTracks.ContainsKey(track.Id)
                && manifest.PotentiallyDeletedTracks[track.Id].Contains(playlist.Id)
            )
            {
                Console.WriteLine(
                    $"Track {track.Title} is now in {playlist.Title}.\nRemoved from potentially deleted tracks"
                );
                manifest.PotentiallyDeletedTracks[track.Id].Remove(playlist.Id);
                if (manifest.PotentiallyDeletedTracks[track.Id].Count == 0)
                    manifest.PotentiallyDeletedTracks.Remove(track.Id);
                restoredTracks.Add(track.Title);
                restored++;
            }
        }
        Console.WriteLine(
            $"Potentially deleted tracks in {playlist.Title}: {potentialDeleted}/{playlist.TrackIds.Count} \n"
                + $"Restored tracks: {restored}/{playlist.TrackIds.Count} \n"
        );
        if (potentialDeletedTracks.Count > 0 || restoredTracks.Count > 0)
        {
            string initmsg = $"Tracks found out of sync in {playlist.Title}:";
            Console.WriteLine(initmsg);
            Console.WriteLine(new string('-', initmsg.Length));
            string message = "";

            if (potentialDeletedTracks.Count > 0)
            {
                foreach (var item in potentialDeletedTracks)
                    message += $"  \u2022 {item}\n";
            }
            if (restoredTracks.Count > 0)
            {
                if (message.Length > 0)
                    message += "\n";
                message += $"Restored tracks:\n";
                foreach (var item in restoredTracks)
                    message += $"  \u2022 {item}\n";
            }
            Console.WriteLine($"{message}\n");
        }

        ManifestStore.Save(manifest, manifestPath);
    }

    public async Task RemoveSymlinks(PotentiallyDeletedTrackViewModel TrackViewModel)
    {
        Console.WriteLine("\nWas passed a TrackViewModel to remove symlinks");
        var trackFileName = TrackViewModel.TrackFileName;
        foreach (var playlistPath in TrackViewModel.DeleteFromPlaylists.Values)
        {
            await RemoveSymlinks(trackFileName, playlistPath);
        }
        Console.WriteLine("Finished removing symlinks\n");
    }

    public async Task RemoveSymlinks(string trackFileName, string playlistPath)
    {
        var trackPath = Path.Combine(playlistPath, trackFileName);
        Console.WriteLine($"Removing symlinks for {trackPath}");
        if (File.Exists(trackPath))
        {
            File.Delete(trackPath);
            Console.WriteLine($"Deleted {trackPath}");
        }
        else
            Console.WriteLine($"File {trackPath} does not exist");
    }

    public async Task RemoveTracksFromPlaylistsAndPlaylistsFromTracks(
        PotentiallyDeletedTrackViewModel TrackViewModel,
        Manifest manifest
    )
    {
        Console.WriteLine(
            "Was passed a TrackViewModel to remove tracks from playlists and playlists from tracks"
        );
        var trackId = TrackViewModel.TrackId;
        foreach (var playlistIdString in TrackViewModel.DeleteFromPlaylists.Keys)
        {
            var playlistId = long.Parse(playlistIdString);
            await RemoveTracksFromPlaylists(trackId, playlistId, manifest);
            await RemovePlaylistsFromTracks(trackId, playlistId, manifest);
        }
        Console.WriteLine("Finished removing tracks from playlists and playlists from tracks\n");
    }

    public async Task RemoveTracksFromPlaylists(
        PotentiallyDeletedTrackViewModel TrackViewModel,
        Manifest manifest
    )
    {
        Console.WriteLine("Was passed a TrackViewModel to remove tracks from playlists");
        var trackId = TrackViewModel.TrackId;
        foreach (var playlistIdString in TrackViewModel.DeleteFromPlaylists.Keys)
        {
            var playlistId = long.Parse(playlistIdString);
            await RemoveTracksFromPlaylists(trackId, playlistId, manifest);
        }
        Console.WriteLine("Finished removing tracks from playlists\n");
    }

    public async Task RemoveTracksFromPlaylists(long trackId, long playlistId, Manifest manifest)
    {
        manifest.TrackedPlaylists[playlistId].TrackIds.Remove(trackId);
        Console.WriteLine($"Removed {trackId} from playlist {playlistId}");
    }

    public async Task RemovePlaylistsFromTracks(long trackId, long playlistId, Manifest manifest)
    {
        manifest.Tracks[trackId.ToString()].InPlaylists.Remove(playlistId);
        Console.WriteLine($"Removed {playlistId} from track {trackId}");
    }

    public async Task DeleteTrackAndArtwork(
        ObservableCollection<PotentiallyDeletedTrackViewModel> PotentiallyDeletedTracks
    )
    {
        Console.WriteLine(
            "Was passed a collection of PotentiallyDeletedTracks to delete tracks and artwork"
        );
        foreach (var track in PotentiallyDeletedTracks)
        {
            if (track.IsKept)
                continue;
            var trackFilePath = track.TrackFilePath;
            var trackArtworkPath = track.ArtworkPath;
            await DeleteTrackAndArtwork(trackFilePath, trackArtworkPath);
        }
        Console.WriteLine("Finished deleting tracks and artwork\n");
    }

    public async Task DeleteTrackAndArtwork(string trackFilePath, string trackArtworkPath)
    {
        Console.WriteLine($"Deleting {trackFilePath} and {trackArtworkPath}");
        if (File.Exists(trackFilePath))
        {
            File.Delete(trackFilePath);
            Console.WriteLine($"Deleted {trackFilePath}");
        }
        else
            Console.WriteLine($"File {trackFilePath} does not exist");

        if (trackArtworkPath == _artwork.FallbackFilePath)
        {
            Console.WriteLine($"Skipping fallback artwork: {trackArtworkPath}");
            return;
        }

        if (File.Exists(trackArtworkPath))
        {
            File.Delete(trackArtworkPath);
            Console.WriteLine($"Deleted {trackArtworkPath}");
        }
        else
            Console.WriteLine($"File {trackArtworkPath} does not exist");
    }

    public async Task DeleteTracksFromManifest(
        HashSet<long> PotentiallyDeletedTracks,
        Manifest manifest
    )
    {
        Console.WriteLine(
            "Was passed a collection of PotentiallyDeletedTracks to delete from manifest"
        );
        foreach (var track in PotentiallyDeletedTracks)
        {
            await DeleteTrackFromManifest(track, manifest);
        }
        Console.WriteLine("Finished deleting tracks from manifest\n");
    }

    public async Task DeleteTrackFromManifest(long trackId, Manifest manifest)
    {
        Console.WriteLine($"Removing {trackId} from manifest");
        manifest.Tracks.Remove(trackId.ToString());
        Console.WriteLine($"Removed {trackId} from manifest");
        if (manifest.PotentiallyDeletedTracks.ContainsKey(trackId))
        {
            manifest.PotentiallyDeletedTracks.Remove(trackId);
            Console.WriteLine($"Removed {trackId} from PotentiallyDeletedTracks");
        }
    }
}
