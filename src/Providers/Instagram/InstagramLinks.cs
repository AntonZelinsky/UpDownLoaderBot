using System.Text.RegularExpressions;
using UpDownLoaderBot.Core;

namespace UpDownLoaderBot.Providers.Instagram;

/// <summary>
///     Everything the bot knows about the shape of an Instagram link, in one place. A second platform
///     gets a class like this one and nothing else.
/// </summary>
public sealed partial class InstagramLinks : IPlatformLinks
{
    /// <summary>A reel and an IGTV post hold one video by definition; a <c>/p/</c> post may not.</summary>
    private static readonly string[] SingleVideoKinds = ["reel", "reels", "tv"];

    public string Platform => "instagram";

    public MediaLink? Find(string text)
    {
        return SupportedRegex().Match(text) is { Success: true } match
            ? new MediaLink(match.Value, Platform, match.Groups["id"].Value)
            : null;
    }

    /// <summary>
    ///     Whether the link holds exactly one video, which is what a downloader answering with a
    ///     single file may take. A <c>/p/</c> post may be a carousel, so it is not one of these.
    /// </summary>
    public bool IsSingleVideo(MediaLink link)
    {
        return SupportedRegex().Match(link.Url) is { Success: true } match
               && SingleVideoKinds.Contains(match.Groups["kind"].Value, StringComparer.OrdinalIgnoreCase);
    }

    // The trailing query is left out on purpose: Telegram pastes an ?igsh= tracking tail along, and
    // the downloaders are better off without it.
    [GeneratedRegex(
        @"https?://(?:www\.)?instagram\.com/(?:[^\s/]+/)?(?<kind>reel|reels|p|tv)/(?<id>[A-Za-z0-9_-]+)/?",
        RegexOptions.IgnoreCase)]
    private static partial Regex SupportedRegex();
}
