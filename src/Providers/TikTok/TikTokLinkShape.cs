namespace UpDownLoaderBot.Providers.TikTok;

/// <summary>
///     Which shape of TikTok link this is. A fact about the link, not a permission: each downloader
///     reads it and decides which shapes are its own, so a downloader added later picks its subset
///     without <see cref="TikTokLinks" /> having to grow a predicate for it.
/// </summary>
public enum TikTokLinkShape
{
    /// <summary>The URL is not a TikTok link this bot recognizes.</summary>
    Unknown,

    /// <summary>A path carrying the post id: <c>/@user/video/</c>, <c>/share/video/</c>.</summary>
    Video,

    /// <summary>A slideshow: <c>/@user/photo/</c>, <c>/share/photo/</c>. Holds no video at all.</summary>
    Photo,

    /// <summary>
    ///     A short code standing in for the post: <c>/t/&lt;code&gt;</c> and a bare code on
    ///     <c>vm.</c>/<c>vt.</c>. What it leads to is known only once something follows the redirect.
    /// </summary>
    Short,

    /// <summary>
    ///     <c>/embed/&lt;id&gt;</c> and <c>/v/&lt;id&gt;.html</c> — forms TikTok only redirects from
    ///     now. Still worth recognizing, because an old forwarded message carries them.
    /// </summary>
    Legacy
}
