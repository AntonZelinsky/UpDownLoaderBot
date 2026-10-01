namespace UpDownLoaderBot.Core;

/// <summary>What of one link can be handed on for sending.</summary>
public sealed record PreparedPost
{
    public PreparedPost(IReadOnlyList<PreparedVideo> media)
    {
        if (media.Count <= 0)
        {
            throw new ArgumentException("A prepared post holds at least one item.", nameof(media));
        }

        Media = media;
    }

    /// <summary>Never empty, so the caller may take the first item without checking.</summary>
    public IReadOnlyList<PreparedVideo> Media { get; }
}
