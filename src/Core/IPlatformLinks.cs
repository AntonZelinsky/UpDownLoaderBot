namespace UpDownLoaderBot.Core;

/// <summary>
///     One platform's link shapes. It hands back a filled <see cref="MediaLink" />, so digging the id
///     out of a URL happens once per platform rather than in every caller.
/// </summary>
public interface IPlatformLinks
{
    /// <summary>Travels inside every link it finds; that is how a downloader recognizes its own.</summary>
    string Platform { get; }

    /// <summary>The first link of this platform in the text, or nothing if it carries none.</summary>
    MediaLink? Find(string text);
}
