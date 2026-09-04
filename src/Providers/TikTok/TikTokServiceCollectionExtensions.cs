using UpDownLoaderBot.Core;

namespace UpDownLoaderBot.Providers.TikTok;

/// <summary>Everything TikTok contributes to the container, so composition stays one line.</summary>
public static class TikTokServiceCollectionExtensions
{
    public static IServiceCollection AddTikTok(
        this IServiceCollection services,
        IConfiguration section,
        ILogger startupLogger)
    {
        // One instance, two roles: a platform to the intake, the TikTok link shapes to the
        // downloaders. Registering the interface separately would hand out a second one.
        services.AddSingleton<TikTokLinks>();
        services.AddSingleton<IPlatformLinks>(provider => provider.GetRequiredService<TikTokLinks>());

        var downloaders = section.GetSection("Downloaders").Get<TikTokDownloadersOptions>()
                          ?? new TikTokDownloadersOptions();

        if (!downloaders.TnkTok && !downloaders.YtDlp)
        {
            throw new InvalidOperationException(
                "No TikTok download strategy is enabled. Enable at least one of "
                + "UpDownLoaderBot:TikTok:Downloaders:YtDlp or UpDownLoaderBot:TikTok:Downloaders:TnkTok.");
        }

        startupLogger.LogInformation(
            "TikTok downloaders: TnkTok={TnkTok}, YtDlp={YtDlp}.", downloaders.TnkTok, downloaders.YtDlp);

        // Registration order is the order MediaFetcher tries them, among those that take the link:
        // the mirror (plain HTTP, one request, no process to spawn) first, yt-dlp as the fallback.
        if (downloaders.TnkTok)
        {
            services.AddHttpClient(nameof(TikTokMirrorDownloader));
            services.AddSingleton<IMediaDownloader, TikTokMirrorDownloader>();
        }

        if (downloaders.YtDlp)
        {
            services.AddSingleton<IMediaDownloader, TikTokYtDlpDownloader>();
        }

        return services;
    }
}
