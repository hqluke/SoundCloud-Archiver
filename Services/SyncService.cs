using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SoundCloudExplode;
using SoundCloudExplode.Tracks;

namespace soundCloudArchiver.Services;

using soundCloudArchiver.Models;

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
        Action<Track, Bitmap?>? onProgress
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

            if (!manifest.Tracks.ContainsKey(trackId))
            {
                currentTrack = manifest.Tracks[trackId] = new TrackManifestEntry
                {
                    Id = track.Id,
                    Title = track.Title,
                    InPlaylists = new HashSet<long> { playlist.Id },
                    ArtworkPath = artworkPath,
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

            ManifestStore.Save(manifest, manifestPath);

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
            onProgress?.Invoke(track, bitmap);
        }

        var bottomLine = new string('-', 21 + playlist.Title.Length + 10);
        Console.WriteLine(
            $"\n----------{playlist.Title} completed-----------\n"
                + $"Total Songs Processed: {seenTrackIds.Count}\n"
                + $"Already on disk: {alreadyDownloaded}\n"
                + $"Downloaded: {downloaded}\n"
                + $"Failed to download: {failed}\n"
                + $"{string.Join(", ", failedDownloads)}\n"
                + bottomLine
        );

        FindPotentiallyDeletedTracks(manifest, playlist, seenTrackIds);
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
        HashSet<long> seenTrackIds
    )
    {
        var msg = $"\nFinding potentially deleted tracks in {playlist.Title}";
        Console.WriteLine(msg);
        Console.WriteLine(new string('-', msg.Length));
        int potentialDeleted = 0;
        int restored = 0;
        int noChanges = 0;
        foreach (var track in playlist.TrackIds)
        {
            if (!seenTrackIds.Contains(track))
            {
                Console.WriteLine($"Track {track} is no longer in {playlist.Title}");
                Console.WriteLine($"Added to potentially deleted tracks");
                if (!manifest.PotentiallyDeletedTracks.ContainsKey(track))
                    manifest.PotentiallyDeletedTracks[track] = new HashSet<long>();
                manifest.PotentiallyDeletedTracks[track].Add(playlist.Id);
                potentialDeleted++;
            }
            else if (
                manifest.PotentiallyDeletedTracks.ContainsKey(track)
                && manifest.PotentiallyDeletedTracks[track].Contains(playlist.Id)
            )
            {
                Console.WriteLine(
                    $"Track {track} is now in {playlist.Title}.\nRemoved from potentially deleted tracks"
                );
                manifest.PotentiallyDeletedTracks[track].Remove(playlist.Id);
                restored++;
            }
            else
                noChanges++;
        }
        Console.WriteLine(
            $"Potentially deleted tracks in {playlist.Title}: {potentialDeleted}/{playlist.TrackIds.Count} \n"
                + $"Restored tracks: {restored}/{playlist.TrackIds.Count} \n"
                + $"No changes: {noChanges}/{playlist.TrackIds.Count}"
        );
    }
}
