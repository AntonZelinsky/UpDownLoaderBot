namespace UpDownLoaderBot.Providers.Instagram;

/// <summary>
///     A strategy for downloading an Instagram video to a local file.
///     Implementations are enabled independently via feature flags; the worker tries
///     each enabled one in turn until a download succeeds.
/// </summary>
public interface IInstagramVideoDownloader
{
    /// <summary>Downloads the video and returns the full path to the saved file.</summary>
    Task<string> DownloadAsync(string url, CancellationToken cancellationToken);
}