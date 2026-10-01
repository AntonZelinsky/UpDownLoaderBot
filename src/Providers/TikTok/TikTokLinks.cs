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

    /// <summary>Which shape the link is, so each downloader can say which shapes are its own.</summary>
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

    // The id group repeats across the alternatives on purpose — .NET keeps the branch that matched.
    // A short link carries no post id, so its own code stands in: the id only names a file inside
    // the request's own folder. A bare code is a path on vm./vt. only, not on any host.
    // \.html has to stay in the match: without it the URL is one yt-dlp no longer recognizes.
    // The trailing query is left out, as for Instagram: sharing pastes an ?_t= tail along.
    [GeneratedRegex(
        @"https?://(?:(?:www\.|m\.|lite\.)?tiktokv?\.com/"
        + @"(?:(?:@[\w.-]+|share)/(?<kind>video|photo)/(?<id>\d+)"
        + @"|(?<kind>embed)/(?<id>\d+)|(?<kind>v)/(?<id>\d+)\.html|t/(?<id>[A-Za-z0-9]+))"
        + @"|(?:vm|vt)\.tiktok\.com/(?<id>[A-Za-z0-9]+))/?",
        RegexOptions.IgnoreCase)]
    private static partial Regex SupportedRegex();
}
