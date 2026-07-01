namespace UpDownLoaderBot.Providers.Instagram;

/// <summary>
///     Feature flags selecting which download strategies are used <b>for Instagram</b>. At least one
///     must be enabled. When both are enabled the worker tries them in order (kkinstagram first,
///     yt-dlp as fallback). These flags are Instagram-scoped: other services (e.g. a future YouTube
///     downloader) get their own flags and are unaffected by these.
/// </summary>
public sealed class InstagramDownloadersOptions
{
    /// <summary>Enables the yt-dlp downloader for Instagram (<see cref="InstagramYtDlpDownloader" />).</summary>
    public bool YtDlp { get; set; } = true;

    /// <summary>Enables the HTTP kkinstagram downloader (<see cref="KkInstagramDownloader" />).</summary>
    public bool KkInstagram { get; set; }
}