using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace soundCloudArchiver.Services;

public class ArtworkService
{
    private readonly HttpClient _http;
    private readonly string _artworkPath;
    private static readonly Uri FallbackArtwork = new(
        "https://i1.sndcdn.com/avatars-4oLYo9NB9XBQQ11s-2Kdzzg-t500x500.jpg"
    );
    private Bitmap? _fallbackBitmap;

    public ArtworkService(HttpClient http, string artworkPath)
    {
        _http = http;
        _artworkPath = artworkPath;
    }

    public async Task<string> FetchAndSaveArtwork(string title, Uri? url)
    {
        var safeName = string.Concat(title.Split(Path.GetInvalidFileNameChars()));
        var artworkPath = Path.Combine(_artworkPath, safeName + ".jpg");

        if (File.Exists(artworkPath))
            return artworkPath;

        try
        {
            var bytes = await _http.GetByteArrayAsync(url ?? FallbackArtwork);
            await File.WriteAllBytesAsync(artworkPath, bytes);
        }
        catch (HttpRequestException)
        {
            var bytes = await _http.GetByteArrayAsync(FallbackArtwork);
            await File.WriteAllBytesAsync(artworkPath, bytes);
        }

        return artworkPath;
    }

    public async Task<Bitmap?> FetchBitmap(Uri? url)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url ?? FallbackArtwork);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch (HttpRequestException)
        {
            _fallbackBitmap ??= await FetchBitmap(FallbackArtwork);
            return _fallbackBitmap;
        }
    }

    public Bitmap? LoadBitmap(string path)
    {
        return File.Exists(path) ? new Bitmap(path) : null;
    }
}
