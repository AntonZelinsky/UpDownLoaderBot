using System.Text.RegularExpressions;

namespace UpDownLoaderBot.Providers.Instagram;

/// <summary>
///     Downloads an Instagram video by rewriting the link to the kkinstagram proxy host
///     and fetching the video file directly over HTTP (no external tooling required).
/// </summary>
public sealed partial class KkInstagramDownloader : IInstagramVideoDownloader
{
    private const string ProxyHost = "https://www.kkinstagram.com";

    // The proxy varies its response by client: a bot UA is redirected to the video file itself,
    // a browser UA to an HTML landing page.
    private const string UserAgent = "TelegramBot (like TwitterBot)";

    private const int TimeoutSeconds = 120;

    // The Bot API refuses anything over 50 MB, so a response past this point is not worth finishing.
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

    // The proxy answers with a single file, so a carousel post yields at most its first video —
    // and, as often as not, a cover image that the preparer then refuses.
    public async Task<string> DownloadVideo(string url, string folder, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        using var request = BuildHttpMessage(url);
        var http = _httpClientFactory.CreateClient(nameof(KkInstagramDownloader));

        var requestUrl = request.RequestUri;

        _logger.LogInformation("Fetching {Url} via kkinstagram", requestUrl);

        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var contentType = RequireVideoContentType(response, requestUrl);
        EnsureAnnouncedSizeFits(response, requestUrl);

        var filePath = Path.Combine(folder, $"kkinstagram{ExtensionFor(contentType)}");

        await using (var source = await response.Content.ReadAsStreamAsync(timeoutCts.Token))
        await using (var file = File.Create(filePath))
        {
            await CopyCapped(source, file, requestUrl, timeoutCts.Token);
        }

        _logger.LogInformation(
            "Downloaded {Url} via kkinstagram -> {FilePath} ({SizeMb:F1} MB)",
            requestUrl, filePath, new FileInfo(filePath).Length / 1048576.0);

        return filePath;
    }

    // Content-Length is absent on a chunked response and can lie, so the bytes written are what count.
    private static async Task CopyCapped(
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

    // A wrong or expired link gets an HTML landing page instead of the file.
    private static string RequireVideoContentType(HttpResponseMessage response, Uri? requestUrl)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;

        return contentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true
            ? contentType
            : throw new InvalidOperationException(
                $"kkinstagram returned non-video content ('{contentType ?? "unknown"}') for {requestUrl}.");
    }

    // Checked before the body is read; absent or untruthful lengths are left to CopyCapped.
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