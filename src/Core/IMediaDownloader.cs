namespace UpDownLoaderBot.Core;

/// <summary>A way of getting the media behind a link onto disk.</summary>
public interface IMediaDownloader
{
    /// <summary>
    ///     Claims only links it can deliver <b>whole</b>: answering with a part of a post stops the
    ///     fallback there, and the downloader that would have returned all of it never runs. This is
    ///     the routing — nothing above dispatches by platform.
    /// </summary>
    bool CanHandle(MediaLink link);

    /// <summary>
    ///     Writes into <paramref name="folder" /> and returns what it wrote. Never returns nothing: a
    ///     download that produced no file throws, and that throw is what gives the next downloader its
    ///     turn. Keep it that way once several files come back — an empty success would need handling
    ///     everywhere a throw already is.
    /// </summary>
    Task<DownloadedPost> Download(MediaLink link, string folder, CancellationToken cancellationToken);
}
