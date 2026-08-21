namespace UpDownLoaderBot.Providers.Instagram;

/// <summary>A strategy for downloading an Instagram video to a local file.</summary>
public interface IInstagramVideoDownloader
{
    /// <summary>
    ///     Returns the saved file: the video of a reel, or the first video of a carousel post.
    ///     Never returns nothing — a download that produced no video throws instead.
    /// </summary>
    Task<string> DownloadVideo(string url, string folder, CancellationToken cancellationToken);
}