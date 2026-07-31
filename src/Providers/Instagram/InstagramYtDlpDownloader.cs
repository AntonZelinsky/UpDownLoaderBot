using Microsoft.Extensions.Options;

namespace UpDownLoaderBot.Providers.Instagram;

/// <summary>Instagram yt-dlp settings.</summary>
public sealed class InstagramYtDlpOptions
{
    /// <summary>Path to a Netscape-format cookies.txt used to authenticate Instagram downloads.</summary>
    public string? InstagramCookiesFile { get; set; }
}

/// <summary>
///     yt-dlp downloader for Instagram. Reuses the generic runner and only adds the Instagram
///     cookies file, so its authentication never leaks into downloads for other services.
/// </summary>
public sealed class InstagramYtDlpDownloader : YtDlpDownloaderBase, IInstagramVideoDownloader
{
    private readonly string? _cookiesFile;

    public InstagramYtDlpDownloader(IOptions<InstagramYtDlpOptions> options, ILogger<InstagramYtDlpDownloader> logger)
        : base(logger)
    {
        var deployedFile = ResolveCookiesFile(options.Value.InstagramCookiesFile);
        _cookiesFile = deployedFile is null ? null : PrepareCookiesFile(deployedFile);

        if (!string.IsNullOrWhiteSpace(options.Value.InstagramCookiesFile))
        {
            if (deployedFile is not null)
            {
                logger.LogInformation(
                    "Instagram cookies: deployed {DeployedFile}, yt-dlp uses {CookiesFile}",
                    deployedFile,
                    _cookiesFile);
            }
            else
            {
                logger.LogWarning(
                    "Configured Instagram cookies file '{Configured}' was not found; Instagram downloads may fail.",
                    options.Value.InstagramCookiesFile);
            }
        }
    }

    // Instagram requires authentication; supply the Instagram cookies file if configured.
    protected override void AddServiceArguments(IList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(_cookiesFile))
        {
            return;
        }

        arguments.Add("--cookies");
        arguments.Add(_cookiesFile);
    }
}