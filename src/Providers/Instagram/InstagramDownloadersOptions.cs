namespace UpDownLoaderBot.Providers.Instagram;

/// <summary>Which Instagram downloaders are on. At least one must be.</summary>
public sealed class InstagramDownloadersOptions
{
    public bool YtDlp { get; set; } = true;

    public bool KkInstagram { get; set; } = true;
}
