using System.Text.RegularExpressions;
using UpDownLoaderBot.Core;

namespace UpDownLoaderBot.Providers.TikTok;

/// <summary>
///     Everything the bot knows about the shape of a TikTok link, in one place. Every form TikTok
///     still hands out, plus the two legacy ones that only redirect now, because a link that resolves
///     in a browser and gets no answer from the bot reads as a bug.
/// </summary>
public sealed partial class TikTokLinks : IPlatformLinks
{
    public string Platform => "tiktok";

    public MediaLink? Find(string text)
    {
        var match = SupportedRegex().Match(text);

        return match.Success ? new MediaLink(match.Value, Platform, match.Groups["id"].Value) : null;
    }

    /// <summary>
    ///     Which shape the link is, so each downloader can say which shapes are its own. Reporting the
    ///     shape rather than answering "may the mirror have this one?" keeps the policy with the
    ///     downloader that owns it: one predicate per consumer would need another one per shape.
    /// </summary>
    public TikTokLinkShape ShapeOf(MediaLink link)
    {
        var match = SupportedRegex().Match(link.Url);

        if (!match.Success)
        {
            return TikTokLinkShape.Unknown;
        }

        return match.Groups["kind"].Value.ToLowerInvariant() switch
        {
            "video" => TikTokLinkShape.Video,
            "photo" => TikTokLinkShape.Photo,
            "embed" or "v" => TikTokLinkShape.Legacy,
            // /t/<code>, and a bare code on vm./vt., which the pattern captures without a kind.
            _ => TikTokLinkShape.Short
        };
    }

    // Every shape in one pattern, so a message is searched once. The id group repeats across the
    // alternatives on purpose — .NET keeps the branch that matched.
    //
    // Hosts: tiktokv?\.com covers both tiktok.com and tiktokv.com, which share links from the app
    // still use; lite. is TikTok Lite. A bare code is a path only on vm./vt., so that branch stays
    // scoped to those hosts rather than accepting any one-segment path anywhere.
    //
    // Paths: /@user/ and /share/ carry the post id; /t/ carries a short code, as vm./vt. do bare. A
    // short link carries no post id, so its own code stands in — the id is only ever a file name
    // inside the request's own folder, so that is enough, and normalizing the URL (which would break
    // "Url is a literal substring of the message") is not needed. /embed/ and /v/<id>.html are
    // legacy: only yt-dlp resolves them, so they are named as their own kind and the mirror declines
    // them rather than spending a request to be told 404.
    // \.html has to stay in the match: without it the URL is one yt-dlp no longer recognizes.
    //
    // The trailing query is left out the way it is for Instagram: sharing pastes an ?_t= tail along.
    [GeneratedRegex(
        @"https?://(?:(?:www\.|m\.|lite\.)?tiktokv?\.com/"
        + @"(?:(?:@[\w.-]+|share)/(?<kind>video|photo)/(?<id>\d+)"
        + @"|(?<kind>embed)/(?<id>\d+)|(?<kind>v)/(?<id>\d+)\.html|t/(?<id>[A-Za-z0-9]+))"
        + @"|(?:vm|vt)\.tiktok\.com/(?<id>[A-Za-z0-9]+))/?",
        RegexOptions.IgnoreCase)]
    private static partial Regex SupportedRegex();
}
