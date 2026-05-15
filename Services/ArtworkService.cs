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
    private static readonly Uri FallbackUrl = new(
        "https://i1.sndcdn.com/avatars-4oLYo9NB9XBQQ11s-2Kdzzg-t500x500.jpg"
    );
    private Bitmap? _fallbackBitmap;

    public string FallbackFilePath => Path.Combine(_artworkPath, "_fallback.jpg");

    public ArtworkService(string artworkPath)
    {
        _http = new HttpClient();
        _artworkPath = artworkPath;
    }

    public async Task<string> FetchAndSaveArtwork(string title, Uri? url)
    {
        var safeName = string.Concat(title.Split(Path.GetInvalidFileNameChars()));
        var artworkPath = Path.Combine(_artworkPath, safeName + ".jpg");

        if (File.Exists(artworkPath))
            return artworkPath;

        if (url == null)
        {
            await EnsureFallbackFile();
            return FallbackFilePath;
        }

        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(artworkPath, bytes);
        }
        catch (HttpRequestException)
        {
            await EnsureFallbackFile();
            return FallbackFilePath;
        }

        return artworkPath;
    }

    public async Task<Bitmap?> FetchBitmap(Uri? url)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url ?? FallbackUrl);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch (HttpRequestException)
        {
            _fallbackBitmap ??= await LoadOrFetchFallbackBitmap();
            return _fallbackBitmap;
        }
    }

    private async Task EnsureFallbackFile()
    {
        if (!File.Exists(FallbackFilePath))
        {
            var bytes = await _http.GetByteArrayAsync(FallbackUrl);
            await File.WriteAllBytesAsync(FallbackFilePath, bytes);
        }
    }

    private async Task<Bitmap?> LoadOrFetchFallbackBitmap()
    {
        if (File.Exists(FallbackFilePath))
            return new Bitmap(FallbackFilePath);

        try
        {
            await EnsureFallbackFile();
            return new Bitmap(FallbackFilePath);
        }
        catch
        {
            return null;
        }
    }

    public Bitmap? LoadBitmap(string path)
    {
        return File.Exists(path) ? new Bitmap(path) : null;
    }
}
