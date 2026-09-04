using UpDownLoaderBot.Core;

namespace UpDownLoaderBot.Tools.Http;

/// <summary>
///     Downloads a post's video from a mirror that answers a rewritten link with the file itself, over
///     plain HTTP and without external tooling. Everything a mirror needs beyond its own address is
///     here; a subclass says which links are its own and where they live on the mirror.
/// </summary>
public abstract class MirrorDownloaderBase : IMediaDownloader
{
    // Mirrors vary their response by client: a bot UA is redirected to the video file itself, a
    // browser UA to an HTML landing page.
    private const string UserAgent = "TelegramBot (like TwitterBot)";

    private const int TimeoutSeconds = 120;

    // The Bot API refuses anything over 50 MB, so a response past this point is not worth finishing.
    private const long MaxBytes = 60L * 1024 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    protected MirrorDownloaderBase(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Names the mirror in log lines and refusals.</summary>
    protected abstract string MirrorName { get; }

    public abstract bool CanHandle(MediaLink link);

    public async Task<DownloadedPost> Download(MediaLink link, string folder, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        using var request = BuildHttpMessage(link);

        // Named after the concrete downloader, which is how each one is registered.
        var http = _httpClientFactory.CreateClient(GetType().Name);

        var requestUrl = request.RequestUri;

        _logger.LogInformation("Fetching {Url} via {Mirror}", requestUrl, MirrorName);

        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var contentType = RequireVideoContentType(response, requestUrl);
        EnsureAnnouncedSizeFits(response, requestUrl);

        var extension = ExtensionFor(contentType);
        var filePath = Path.Combine(folder, $"{link.Id}{extension}");

        await using (var source = await response.Content.ReadAsStreamAsync(timeoutCts.Token))
        await using (var file = File.Create(filePath))
        {
            await CopyCapped(source, file, requestUrl, timeoutCts.Token);
        }

        _logger.LogInformation(
            "Downloaded {Url} via {Mirror} -> {FilePath} ({SizeMb:F1} MB)",
            requestUrl, MirrorName, filePath, new FileInfo(filePath).Length / 1048576.0);

        return new DownloadedPost([filePath]);
    }

    /// <summary>The mirror's own address for this link; a plain host rewrite in both cases so far.</summary>
    protected abstract Uri MirrorUrlFor(MediaLink link);

    // Content-Length is absent on a chunked response and can lie, so the bytes written are what count.
    private async Task CopyCapped(
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
                    $"{MirrorName} sent more than {MaxBytes / 1048576} MB for {requestUrl}; download aborted.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    // A wrong or expired link gets an HTML landing page instead of the file.
    private string RequireVideoContentType(HttpResponseMessage response, Uri? requestUrl)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;

        return contentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true
            ? contentType
            : throw new InvalidOperationException(
                $"{MirrorName} returned non-video content ('{contentType ?? "unknown"}') for {requestUrl}.");
    }

    // Checked before the body is read; absent or untruthful lengths are left to CopyCapped.
    private void EnsureAnnouncedSizeFits(HttpResponseMessage response, Uri? requestUrl)
    {
        var announcedBytes = response.Content.Headers.ContentLength;

        if (announcedBytes > MaxBytes)
        {
            throw new InvalidOperationException(
                $"{MirrorName} announced {announcedBytes / 1048576} MB for {requestUrl}, over the "
                + $"{MaxBytes / 1048576} MB limit.");
        }
    }

    private HttpRequestMessage BuildHttpMessage(MediaLink link)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, MirrorUrlFor(link));
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
