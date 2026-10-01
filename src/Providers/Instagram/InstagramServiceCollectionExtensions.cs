using UpDownLoaderBot.Core;

namespace UpDownLoaderBot.Providers.Instagram;

/// <summary>Everything Instagram contributes to the container, so composition stays one line.</summary>
public static class InstagramServiceCollectionExtensions
{
    public static IServiceCollection AddInstagram(
        this IServiceCollection services,
        IConfiguration section,
        ILogger startupLogger)
    {
        services.Configure<InstagramYtDlpOptions>(section.GetSection("YtDlp"));

        // One instance, two roles: a platform to the intake, the Instagram link shapes to the
        // downloaders. Registering the interface separately would hand out a second one.
        services.AddSingleton<InstagramLinks>();
        services.AddSingleton<IPlatformLinks>(provider => provider.GetRequiredService<InstagramLinks>());

        var downloaders = section.GetSection("Downloaders").Get<InstagramDownloadersOptions>()
                          ?? new InstagramDownloadersOptions();

        if (!downloaders.KkInstagram && !downloaders.YtDlp)
        {
            throw new InvalidOperationException(
                "No Instagram download strategy is enabled. Enable at least one of "
                + "UpDownLoaderBot:Instagram:Downloaders:YtDlp or "
                + "UpDownLoaderBot:Instagram:Downloaders:KkInstagram.");
        }

        startupLogger.LogInformation(
            "Instagram downloaders: KkInstagram={KkInstagram}, YtDlp={YtDlp}.",
            downloaders.KkInstagram, downloaders.YtDlp);

        // Registration order is the order MediaFetcher tries them, among those that take the link:
        // kkinstagram (plain HTTP, one-video links only) first, yt-dlp as the fallback for everything else.
        if (downloaders.KkInstagram)
        {
            services.AddHttpClient(nameof(KkInstagramDownloader));
            services.AddSingleton<IMediaDownloader, KkInstagramDownloader>();
        }

        if (downloaders.YtDlp)
        {
            services.AddSingleton<IMediaDownloader, InstagramYtDlpDownloader>();
        }

        return services;
    }
}
