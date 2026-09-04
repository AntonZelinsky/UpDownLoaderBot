namespace UpDownLoaderBot.Core;

/// <summary>
///     Asks each platform in turn; the first to recognize something wins, which makes registration
///     order the arbiter when a message carries links of two platforms. Deciding by position in the
///     text instead would need each match's offset, which <see cref="IPlatformLinks.Find" /> does not
///     report — a deliberate simplification rather than the intended answer.
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
