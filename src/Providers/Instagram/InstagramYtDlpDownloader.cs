using Microsoft.Extensions.Options;
using UpDownLoaderBot.Core;

namespace UpDownLoaderBot.Providers.Instagram;

/// <summary>Instagram yt-dlp settings.</summary>
public sealed class InstagramYtDlpOptions
{
    /// <summary>Path to a Netscape-format cookies.txt used to authenticate Instagram downloads.</summary>
    public string? InstagramCookiesFile { get; set; }
}

/// <summary>
///     Adds nothing to the generic runner but the Instagram cookies, so this authentication cannot
///     leak into downloads for other services.
/// </summary>
public sealed class InstagramYtDlpDownloader : YtDlpDownloaderBase
{
    private readonly string? _cookiesFile;
    private readonly InstagramLinks _links;

    public InstagramYtDlpDownloader(
        IOptions<InstagramYtDlpOptions> options,
        InstagramLinks links,
        ILogger<InstagramYtDlpDownloader> logger)
        : base(logger)
    {
        _links = links;

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

    // Takes every Instagram link: a carousel is walked to its first video, which the mirror cannot do.
    public override bool CanHandle(MediaLink link)
    {
        return link.Platform == _links.Platform;
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
