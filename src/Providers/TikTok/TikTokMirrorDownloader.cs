using System.Text.RegularExpressions;
using UpDownLoaderBot.Core;
using UpDownLoaderBot.Tools.Http;

namespace UpDownLoaderBot.Providers.TikTok;

/// <summary>
///     Downloads a TikTok video by rewriting the link to the tnktok mirror and fetching the file
///     directly over HTTP (no external tooling, no cookies).
/// </summary>
public sealed partial class TikTokMirrorDownloader : MirrorDownloaderBase
{
    // The d. prefix is the one that answers with the file; the bare host serves an HTML embed page.
    private const string MirrorHost = "https://d.tnktok.com";

    private readonly TikTokLinks _links;

    public TikTokMirrorDownloader(
        IHttpClientFactory httpClientFactory,
        TikTokLinks links,
        ILogger<TikTokMirrorDownloader> logger)
        : base(httpClientFactory, logger)
    {
        _links = links;
    }

    protected override string MirrorName => "tnktok";

    // Only the host is rewritten, so the shapes this can serve are the ones whose path the mirror
    // routes: a post id, or a short code it follows itself. A photo post holds no video, and the two
    // legacy paths answer 404 — claiming either would spend a request to learn what the shape says.
    public override bool CanHandle(MediaLink link)
    {
        if (link.Platform != _links.Platform)
        {
            return false;
        }

        return _links.ShapeOf(link) is TikTokLinkShape.Video or TikTokLinkShape.Short;
    }

    protected override Uri MirrorUrlFor(MediaLink link)
    {
        var mirrorUrl = TikTokHostRegex().Replace(link.Url, MirrorHost);

        return new Uri(mirrorUrl);
    }

    // Every host the intake accepts, short ones included: the mirror answers /<code> the same way it
    // answers /t/<code>, so the path never has to be rewritten into the /@user/ form.
    [GeneratedRegex(
        @"^https?://(?:www\.|m\.|vm\.|vt\.|lite\.)?tiktokv?\.com", RegexOptions.IgnoreCase)]
    private static partial Regex TikTokHostRegex();
}
