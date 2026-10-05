using UpDownLoaderBot.Core;
using UpDownLoaderBot.Tools.YtDlp;

namespace UpDownLoaderBot.Providers.Facebook;

/// <summary>
///     The only Facebook downloader: no mirror serves the file, and a public video needs no account.
///     yt-dlp follows a share link to the reel itself, and the reel's progressive <c>hd</c> rendition
///     is H.264 where its DASH ladder is AV1 only, so the default format sort already gets it.
/// </summary>
public sealed class FacebookYtDlpDownloader : YtDlpDownloaderBase
{
    private readonly FacebookLinks _links;

    public FacebookYtDlpDownloader(FacebookLinks links, ILogger<FacebookYtDlpDownloader> logger)
        : base(logger)
    {
        _links = links;
    }

    // The last downloader takes every link of its platform, so a failure gets its reaction.
    public override bool CanHandle(MediaLink link)
    {
        return link.Platform == _links.Platform;
    }
}
