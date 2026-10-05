using System.Text.RegularExpressions;
using UpDownLoaderBot.Core;

namespace UpDownLoaderBot.Providers.Facebook;

/// <summary>
///     Everything the bot knows about the shape of a Facebook video link, in one place: the share
///     links the app hands out, the reel, watch and page-video paths they lead to, and fb.watch.
/// </summary>
public sealed partial class FacebookLinks : IPlatformLinks
{
    public string Platform => "facebook";

    public MediaLink? Find(string text)
    {
        var match = SupportedRegex().Match(text);

        return match.Success ? new MediaLink(match.Value, Platform, match.Groups["id"].Value) : null;
    }

    // The id group repeats across the alternatives on purpose — .NET keeps the branch that matched.
    // A share link and fb.watch carry no post id, so their own code stands in: the id only names a
    // file inside the request's own folder. /share/p/ is left out: it is a post, mostly photos or
    // text, and yt-dlp answers one without a video with "Cannot parse data", which would read as 😴.
    // For watch the query is the id itself, and Facebook may put another parameter (?extid=, ?ref=)
    // before it — those stay in, yt-dlp does not mind them. A query after the id is left out.
    [GeneratedRegex(
        @"https?://(?:(?:www\.|m\.|web\.|mbasic\.)?facebook\.com/"
        + @"(?:share/[rv]/(?<id>[A-Za-z0-9_-]+)|reel/(?<id>\d+)|watch/?\?(?:[^\s#]*?&)?v=(?<id>\d+)"
        + @"|[\w.-]+/videos/(?:[\w.-]+/)?(?<id>\d+))"
        + @"|fb\.watch/(?<id>[A-Za-z0-9_-]+))/?",
        RegexOptions.IgnoreCase)]
    private static partial Regex SupportedRegex();
}
