using UpDownLoaderBot.Core;
using UpDownLoaderBot.Tools.YtDlp;

namespace UpDownLoaderBot.Providers.TikTok;

/// <summary>
///     The fallback for everything the mirror will not take. TikTok needs no authentication, so this
///     adds nothing to the generic runner but a format sort of its own.
/// </summary>
public sealed class TikTokYtDlpDownloader : YtDlpDownloaderBase
{
    private readonly TikTokLinks _links;

    public TikTokYtDlpDownloader(TikTokLinks links, ILogger<TikTokYtDlpDownloader> logger)
        : base(logger)
    {
        _links = links;
    }

    // TikTok offers the same resolution at several bitrates, where the default's +size would settle on
    // the worst of them. The 45 MB filter in the format selector is what keeps the ceiling.
    protected override string FormatSort => "vcodec:h264,res:1080,tbr";

    // The last downloader takes every link that may hold a video, shapes it may fail on included, so
    // a failure gets its reaction. A photo post holds none by definition and is left to silence.
    public override bool CanHandle(MediaLink link)
    {
        if (link.Platform != _links.Platform)
        {
            return false;
        }

        return _links.ShapeOf(link) is not TikTokLinkShape.Photo;
    }
}
