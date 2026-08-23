namespace UpDownLoaderBot.Providers.Instagram;

/// <summary>Instagram yt-dlp settings.</summary>
public sealed class InstagramYtDlpOptions
{
    /// <summary>Path to a Netscape-format cookies.txt used to authenticate Instagram downloads.</summary>
    public string? InstagramCookiesFile { get; set; }
}
