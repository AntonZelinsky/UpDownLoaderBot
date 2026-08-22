namespace UpDownLoaderBot.Core;

/// <summary>
///     A downloaded video described the way Telegram needs it. Width and height are the ones to
///     display, with pixel aspect ratio and rotation applied.
/// </summary>
public sealed record PreparedVideo(
    string FilePath,
    int Width,
    int Height,
    int Duration);
