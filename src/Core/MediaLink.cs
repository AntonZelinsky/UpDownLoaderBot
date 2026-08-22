namespace UpDownLoaderBot.Core;

/// <summary>
///     A link the bot answers. The id names the file a download produces, so one link comes back
///     under the same name whichever downloader served it.
/// </summary>
public sealed record MediaLink(string Url, string Platform, string Id)
{
    /// <summary>
    ///     Empty would name a file nothing but its extension, so a platform that loses the id fails
    ///     here rather than three stages later.
    /// </summary>
    public string Id { get; } = string.IsNullOrWhiteSpace(Id)
        ? throw new ArgumentException($"A {Platform} link must carry the post's id: {Url}", nameof(Id))
        : Id;
}
