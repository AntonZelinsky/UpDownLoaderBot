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
    ///     Throws when nothing sendable came out of any downloader, and
    ///     <see cref="NothingToSendException" /> when there was nothing to send in the first place.
    ///     Preparation runs inside the loop, so a refused file moves on to the next downloader.
    /// </summary>
    public async Task<PreparedPost> Fetch(MediaLink link, string folder, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        NothingToSendException? nothingToSend = null;
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
            catch (NothingToSendException ex)
            {
                // A hand-over still: the next downloader may yet find the video.
                nothingToSend = ex;
                _logger.LogInformation(
                    "Downloader '{Name}' found nothing to send for {Url}: {Reason}", name, link.Url, ex.Message);
            }
            catch (Exception ex)
            {
                lastError = ex;

                // Warning, not error: the request failing is logged by the caller, once.
                _logger.LogWarning(ex, "Downloader '{Name}' failed for {Url}", name, link.Url);
            }
        }

        // A downloader that reached the post and found no video has said something about the post
        // itself, which outweighs another one failing on the way to it.
        if (nothingToSend is not null)
        {
            throw new NothingToSendException($"There is no video in {link.Url}.", nothingToSend);
        }

        if (tried == 0)
        {
            throw new NothingToSendException($"No enabled downloader takes {link.Url} ({link.Platform}).");
        }

        throw new InvalidOperationException($"All {tried} downloader(s) that take {link.Url} failed.", lastError);
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
