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

        _logger.LogInformation("Fetching {Url} via kkinstagram", url);

        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is null || !contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"kkinstagram returned non-video content ('{contentType ?? "unknown"}') for {url}.");
        }

        var filePath = Path.Combine(OutputDirectory, $"{Guid.NewGuid():N}{ExtensionFor(contentType)}");

        await using (var source = await response.Content.ReadAsStreamAsync(timeoutCts.Token))
        await using (var file = File.Create(filePath))
        {
            await source.CopyToAsync(file, timeoutCts.Token);
        }

        _logger.LogInformation("Downloaded {Url} via kkinstagram -> {FilePath}", url, filePath);
        return filePath;
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