namespace UpDownLoaderBot.Core;

/// <summary>
///     Asks each platform in turn; the first to recognize something wins. Arbitrating between two
///     links of different platforms will need each match's position — a question for when a second
///     platform exists, not a guess now.
/// </summary>
public sealed class MediaLinkParser
{
    private readonly IReadOnlyList<IPlatformLinks> _platforms;

    public MediaLinkParser(IEnumerable<IPlatformLinks> platforms)
    {
        _platforms = platforms.ToArray();
    }

    /// <summary>Nothing when the text carries no link any registered platform recognizes.</summary>
    public MediaLink? FirstIn(string text)
    {
        return _platforms.Select(platform => platform.Find(text)).FirstOrDefault(link => link is not null);
    }
}
