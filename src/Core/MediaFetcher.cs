using UpDownLoaderBot.Tools.Ffprobe;

namespace UpDownLoaderBot.Core;

/// <summary>
///     Downloading and preparing, one stage on purpose: the downloaders that take the link are tried
///     in registration order, and the first whose files survive preparation wins. Knows nothing about
///     Telegram — what to do with the result, and with the throw, is the caller's business.
/// </summary>
public sealed class MediaFetcher
{
    private readonly IReadOnlyList<IMediaDownloader> _downloaders;
    private readonly ILogger<MediaFetcher> _logger;
    private readonly TelegramVideoPreparer _preparer;

    public MediaFetcher(
        IEnumerable<IMediaDownloader> downloaders,
        TelegramVideoPreparer preparer,
        ILogger<MediaFetcher> logger)
    {
        _downloaders = downloaders.ToArray();
        _preparer = preparer;
        _logger = logger;
    }

    /// <summary>
    ///     Throws when nothing sendable came out of any downloader — that throw is the caller's 👎.
    ///     Preparation runs inside the loop: a file the preparer refuses moves on to the next
    ///     downloader instead of being sent or failing the request.
    /// </summary>
    public async Task<PreparedPost> Fetch(MediaLink link, string folder, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        var tried = 0;

        foreach (var downloader in _downloaders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = downloader.GetType().Name;
            if (!downloader.CanHandle(link))
            {
                _logger.LogDebug("Downloader '{Name}' does not take {Url}", name, link.Url);
                continue;
            }

            tried++;
            try
            {
                _logger.LogInformation("Trying downloader '{Name}' for {Url}", name, link.Url);

                var downloaded = await downloader.Download(link, folder, cancellationToken);
                var (media, refusal) = await PrepareWhatIsSendable(downloaded, cancellationToken);

                if (media.Count > 0)
                {
                    return new PreparedPost(media);
                }

                // If this was the last downloader, the refusal is the whole reason for the 👎.
                lastError = refusal ?? lastError;

                // Not an error but a hand-over: the next downloader may serve the same link better.
                _logger.LogWarning(
                    "Downloader '{Name}' produced {Count} file(s) for {Url}, none of them sendable",
                    name, downloaded.Files.Count, link.Url);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;

                // Warning, not error: the request failing is logged by the caller, once.
                _logger.LogWarning(ex, "Downloader '{Name}' failed for {Url}", name, link.Url);
            }
        }

        // When nobody took the link there is no failure in the log to explain the 👎, so say so here.
        throw new InvalidOperationException(
            tried == 0
                ? $"No enabled downloader takes {link.Url} ({link.Platform})."
                : $"All {tried} downloader(s) that take {link.Url} failed.",
            lastError);
    }

    /// <summary>
    ///     What Telegram can be handed, plus the last refusal — returned rather than kept in a field,
    ///     because this class is a singleton serving requests concurrently.
    /// </summary>
    private async Task<(IReadOnlyList<PreparedVideo> Media, Exception? LastRefusal)> PrepareWhatIsSendable(
        DownloadedPost post,
        CancellationToken cancellationToken)
    {
        var media = new List<PreparedVideo>(post.Files.Count);
        Exception? lastRefusal = null;

        foreach (var file in post.Files)
        {
            try
            {
                var prepared = await _preparer.Prepare(file, cancellationToken);

                media.Add(prepared);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastRefusal = ex;
                _logger.LogWarning(ex, "Dropped {File}: it cannot be sent as it is", file);
            }
        }

        return (media, lastRefusal);
    }
}
