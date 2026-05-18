using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace soundCloudArchiver.Services;

public class KlickaudDownloader : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private bool _disposed;

    private const string BaseUrl = "https://www.klickaud.org";
    private const string DlUrl = "https://dl.klickaud.org";

    public KlickaudDownloader()
    {
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
        );
        _httpClient.DefaultRequestHeaders.Referrer = new Uri($"{BaseUrl}/en14");
        _httpClient.Timeout = TimeSpan.FromSeconds(90);
    }

    public async Task<bool> TryDownloadAsync(string soundCloudUrl, string outputPath,
        Action<int>? onProgress = null)
    {
        try
        {
            // 1. Visit main page to establish session
            Console.WriteLine("    Klickaud: fetching main page...");
            var mainResponse = await _httpClient.GetAsync($"{BaseUrl}/en14");
            mainResponse.EnsureSuccessStatusCode();

            // 2. Fetch CSRF token
            Console.WriteLine("    Klickaud: fetching CSRF token...");
            var csrfResponse = await _httpClient.GetAsync($"{BaseUrl}/csrf-token-endpoint.php");
            csrfResponse.EnsureSuccessStatusCode();
            var csrfJson = await csrfResponse.Content.ReadAsStringAsync();
            var csrfToken = JsonDocument.Parse(csrfJson).RootElement.GetProperty("csrf_token").GetString();

            if (string.IsNullOrEmpty(csrfToken))
            {
                Console.WriteLine("    Klickaud: failed to get CSRF token");
                return false;
            }

            // 3. Submit the download form
            Console.WriteLine("    Klickaud: submitting URL...");
            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("value", soundCloudUrl),
                new KeyValuePair<string, string>("csrf_token", csrfToken),
            });

            var downloadResponse = await _httpClient.PostAsync($"{BaseUrl}/download.php", formContent);
            downloadResponse.EnsureSuccessStatusCode();

            // 4. Connect to SSE endpoint to get the actual download URL
            Console.WriteLine("    Klickaud: waiting for SSE download URL...");
            var encodedUrl = Uri.EscapeDataString(soundCloudUrl);
            var sseResponse = await _httpClient.GetAsync(
                $"{BaseUrl}/worker_sse.php?url={encodedUrl}",
                HttpCompletionOption.ResponseHeadersRead
            );
            sseResponse.EnsureSuccessStatusCode();

            var sseResult = await ReadSseAsync(sseResponse, TimeSpan.FromSeconds(60), onProgress);
            if (sseResult == null)
            {
                Console.WriteLine("    Klickaud: SSE timed out or failed");
                return false;
            }

            var downloadUrl = sseResult.Url;
            var fileName = sseResult.FileName;

            Console.WriteLine($"    Klickaud: got download URL, file size ~{sseResult.FileSize ?? 0} bytes");

            // 5. Download the file from dl.klickaud.org
            // The cookie from klickaud.org session may not auto-forward to dl.klickaud.org
            // so ensure PHPSESSID is sent to the download domain
            await EnsureCookieForwardedAsync();

            Console.WriteLine("    Klickaud: downloading file...");
            var fileResponse = await _httpClient.GetAsync(downloadUrl);
            if (!fileResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"    Klickaud: download returned {fileResponse.StatusCode}");
                return false;
            }

            var contentType = fileResponse.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("text/html") || contentType.Contains("text/plain"))
            {
                var body = await fileResponse.Content.ReadAsStringAsync();
                if (body.Contains("Cloudflare") || body.Contains("Attention Required"))
                {
                    Console.WriteLine("    Klickaud: download blocked by Cloudflare");
                    return false;
                }
            }

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await using var fileStream = File.Create(outputPath);
            await fileResponse.Content.CopyToAsync(fileStream);
            await fileStream.FlushAsync();

            var fileInfo = new FileInfo(outputPath);
            if (fileInfo.Exists && fileInfo.Length > 0)
            {
                Console.WriteLine($"    Klickaud: saved {fileInfo.Length} bytes to {outputPath}");
                return true;
            }

            return false;
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("    Klickaud: request timed out");
            return false;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"    Klickaud: HTTP error - {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Klickaud: unexpected error - {ex.Message}");
            return false;
        }
    }

    private async Task EnsureCookieForwardedAsync()
    {
        var baseUri = new Uri($"{BaseUrl}/");
        var dlUri = new Uri($"{DlUrl}/");
        var cookies = _cookieContainer.GetCookies(baseUri);

        var phpsessid = cookies["PHPSESSID"]?.Value;
        if (phpsessid != null)
        {
            // Check if the cookie already exists on dl.klickaud.org
            var dlCookies = _cookieContainer.GetCookies(dlUri);
            if (dlCookies["PHPSESSID"] == null)
            {
                _cookieContainer.Add(dlUri, new Cookie("PHPSESSID", phpsessid));
            }
        }
    }

    private record SseResult(string Url, string FileName, long? FileSize);

    private static async Task<SseResult?> ReadSseAsync(HttpResponseMessage response, TimeSpan timeout,
        Action<int>? onProgress = null)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        string? currentEvent = null;
        string? currentData = null;
        var cts = new CancellationTokenSource(timeout);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line == null)
                    break;

                if (line.StartsWith("event: "))
                {
                    currentEvent = line[7..];
                }
                else if (line.StartsWith("data: "))
                {
                    currentData = line[6..];
                }
                else if (string.IsNullOrEmpty(line) && currentEvent != null && currentData != null)
                {
                    if (currentEvent == "progress")
                    {
                        try
                        {
                            var pdoc = JsonDocument.Parse(currentData);
                            if (pdoc.RootElement.TryGetProperty("percent", out var pp))
                                onProgress?.Invoke(pp.GetInt32());
                        }
                        catch { }
                    }
                    else if (currentEvent == "ready")
                    {
                        var doc = JsonDocument.Parse(currentData);
                        var url = doc.RootElement.GetProperty("download_url").GetString();
                        var fileName = doc.RootElement.TryGetProperty("file_name", out var fn)
                            ? fn.GetString()
                            : null;

                        long? fileSize = null;
                        if (doc.RootElement.TryGetProperty("filesize", out var fs))
                            fileSize = fs.GetInt64();

                        if (!string.IsNullOrEmpty(url))
                            return new SseResult(url, fileName ?? "audio.mp3", fileSize);
                    }

                    currentEvent = null;
                    currentData = null;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout
        }

        return null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient?.Dispose();
            _disposed = true;
        }
    }
}
