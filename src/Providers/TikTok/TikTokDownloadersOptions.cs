namespace UpDownLoaderBot.Providers.TikTok;

/// <summary>Which TikTok downloaders are on. At least one must be.</summary>
public sealed class TikTokDownloadersOptions
{
    public bool YtDlp { get; set; } = true;

    public bool TnkTok { get; set; } = true;
}
