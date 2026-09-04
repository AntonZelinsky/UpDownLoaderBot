using System.Text.RegularExpressions;
using UpDownLoaderBot.Core;
using UpDownLoaderBot.Tools.Http;

namespace UpDownLoaderBot.Providers.Instagram;

/// <summary>
///     Downloads an Instagram video by rewriting the link to the kkinstagram proxy host
///     and fetching the video file directly over HTTP (no external tooling required).
/// </summary>
public sealed partial class KkInstagramDownloader : MirrorDownloaderBase
{
    private const string ProxyHost = "https://www.kkinstagram.com";

    private readonly InstagramLinks _links;

    public KkInstagramDownloader(
        IHttpClientFactory httpClientFactory,
        InstagramLinks links,
        ILogger<KkInstagramDownloader> logger)
        : base(httpClientFactory, logger)
    {
        _links = links;
    }

    protected override string MirrorName => "kkinstagram";

    // The proxy answers with a single file, so it takes only links that hold exactly one video: of a
    // /p/ carousel it would deliver a part, and the fallback would stop there instead of trying yt-dlp.
    public override bool CanHandle(MediaLink link)
    {
        return link.Platform == _links.Platform && _links.IsSingleVideo(link);
    }

    protected override Uri MirrorUrlFor(MediaLink link)
    {
        var mirrorUrl = InstagramHostRegex().Replace(link.Url, ProxyHost);

        return new Uri(mirrorUrl);
    }

    [GeneratedRegex(@"^https?://(?:www\.)?instagram\.com", RegexOptions.IgnoreCase)]
    private static partial Regex InstagramHostRegex();
}
