namespace UpDownLoaderBot.Core;

/// <summary>
///     There is no video behind the link: the post holds only photos, or no downloader takes a link
///     of that shape. Not a failure — nothing was owed, so the caller leaves no reaction.
/// </summary>
public sealed class NothingToSendException : Exception
{
    public NothingToSendException(string message)
        : base(message)
    {
    }

    public NothingToSendException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
