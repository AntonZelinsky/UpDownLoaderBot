namespace UpDownLoaderBot.Providers.TikTok;

/// <summary>
///     Feature flags selecting which download strategies are used <b>for TikTok</b>. At least one must
///     be enabled. These flags are TikTok-scoped: other services get their own and are unaffected.
/// </summary>
public sealed class TikTokDownloadersOptions
{
    public bool YtDlp { get; set; } = true;

    public bool TnkTok { get; set; } = true;
}
