using System.Text.RegularExpressions;

namespace UpDownLoaderBot.Providers.Instagram;

/// <summary>
///     Downloads an Instagram video by rewriting the link to the kkinstagram proxy host
///     and fetching the video file directly over HTTP (no external tooling required).
/// </summary>
public sealed partial class KkInstagramDownloader : IInstagramVideoDownloader
{
    // The proxy that mirrors Instagram media at the same path under a different host.
    private const string ProxyHost = "https://www.kkinstagram.com";

    // User-Agent sent to the proxy host. The proxy varies its response by client: a crawler/bot
    // UA gets a 302 straight to the direct video file, while a browser UA is sent to an HTML
    // landing page. So we deliberately identify as a bot to receive the media redirect.
    private const string UserAgent = "TelegramBot (like TwitterBot)";

    // Directory where downloaded files are written.
    private const string OutputDirectory = "downloads";

    // Maximum time a single download may run before it is cancelled.
    private const int TimeoutSeconds = 120;

    // Most a download may write to disk: the Bot API refuses anything over 50 MB anyway, so a
    // response past this point is either not a reel or not worth finishing.
    private const long MaxBytes = 60L * 1024 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KkInstagramDownloader> _logger;

    public KkInstagramDownloader(
        IHttpClientFactory httpClientFactory,
        ILogger<KkInstagramDownloader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(OutputDirectory);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        using var request = BuildHttpMessage(url);
        var http = _httpClientFactory.CreateClient(nameof(KkInstagramDownloader));

        // Log the rewritten proxy URL that is actually requested; the original link is
        // already logged by the caller.
        var requestUrl = request.RequestUri;

        _logger.LogInformation("Fetching {Url} via kkinstagram", requestUrl);

        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var contentType = RequireVideoContentType(response, requestUrl);
        EnsureAnnouncedSizeFits(response, requestUrl);

        var filePath = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}{ExtensionFor(contentType)}");

        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            await using var file = File.Create(filePath);

            await CopyCappedAsync(source, file, requestUrl, timeoutCts.Token);
        }
        catch
        {
            // A partial file is of no use to anyone, and leaving it would fill the disk over time.
            TryDelete(filePath);
            throw;
        }

        _logger.LogInformation(
            "Downloaded {Url} via kkinstagram -> {FilePath} ({SizeMb:F1} MB)",
            requestUrl, filePath, new FileInfo(filePath).Length / 1048576.0);

        return filePath;
    }

    // Copies the response with a hard ceiling on what reaches the disk. Content-Length is absent on
    // a chunked response and can simply be wrong, so the bytes actually written are what counts.
    private static async Task CopyCappedAsync(
        Stream source,
        Stream destination,
        Uri? requestUrl,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long written = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return;
            }

            written += read;
            if (written > MaxBytes)
            {
                throw new InvalidOperationException(
                    $"kkinstagram sent more than {MaxBytes / 1048576} MB for {requestUrl}; download aborted.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private void TryDelete(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete the partial download {FilePath}.", filePath);
        }
    }

    // The proxy answers a bot user agent with the media file, but a wrong link or an expired one
    // gets an HTML landing page instead.
    private static string RequireVideoContentType(HttpResponseMessage response, Uri? requestUrl)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;

        return contentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true
            ? contentType
            : throw new InvalidOperationException(
                $"kkinstagram returned non-video content ('{contentType ?? "unknown"}') for {requestUrl}.");
    }

    // Checked before the body is read: an announced size over the limit saves downloading something
    // that would be rejected anyway. Absent or untruthful lengths are caught by CopyCappedAsync.
    private static void EnsureAnnouncedSizeFits(HttpResponseMessage response, Uri? requestUrl)
    {
        var announcedBytes = response.Content.Headers.ContentLength;

        if (announcedBytes > MaxBytes)
        {
            throw new InvalidOperationException(
                $"kkinstagram announced {announcedBytes / 1048576} MB for {requestUrl}, over the "
                + $"{MaxBytes / 1048576} MB limit.");
        }
    }

    // Matches the Instagram host (with or without scheme/www) so we can swap it for the proxy.
    [GeneratedRegex(@"^https?://(?:www\.)?instagram\.com", RegexOptions.IgnoreCase)]
    private static partial Regex InstagramHostRegex();

    private HttpRequestMessage BuildHttpMessage(string contentUrl)
    {
        var url = InstagramHostRegex().Replace(contentUrl, ProxyHost);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        return request;
    }

    private static string ExtensionFor(string mediaType)
    {
        return mediaType.ToLowerInvariant() switch
        {
            "video/webm" => ".webm",
            "video/quicktime" => ".mov",
            _ => ".mp4"
        };
    }
}