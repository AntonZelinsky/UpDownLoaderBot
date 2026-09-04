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

    // The last downloader of its platform takes every link of it, shapes it will fail on included:
    // something has to answer, or a link the bot plainly recognized would get no reaction at all. A
    // 👎 is the answer then, which is why this one does not consult the shape.
    public override bool CanHandle(MediaLink link)
    {
        return link.Platform == _links.Platform;
    }
}
