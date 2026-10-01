using Microsoft.Extensions.Options;
using UpDownLoaderBot.Core;
using UpDownLoaderBot.Tools.YtDlp;

namespace UpDownLoaderBot.Providers.Instagram;

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

        var configured = options.Value.CookiesFile;
        var deployedFile = ResolveCookiesFile(configured);
        _cookiesFile = deployedFile is null ? null : PrepareCookiesFile(deployedFile);

        if (string.IsNullOrWhiteSpace(configured))
        {
            // Instagram serves video to signed-in users only, and a misspelled key reads exactly like
            // a missing one — so this is said out loud rather than found out in production.
            logger.LogWarning(
                "No Instagram cookies configured (UpDownLoaderBot:Instagram:YtDlp:CookiesFile); "
                + "yt-dlp will run without authentication and Instagram downloads will fail.");
        }
        else if (deployedFile is null)
        {
            logger.LogWarning(
                "Configured Instagram cookies file '{Configured}' was not found; Instagram downloads may fail.",
                configured);
        }
        else
        {
            logger.LogInformation(
                "Instagram cookies: deployed {DeployedFile}, yt-dlp uses {CookiesFile}",
                deployedFile,
                _cookiesFile);
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
