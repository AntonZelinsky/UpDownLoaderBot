using UpDownLoaderBot.Core;

namespace UpDownLoaderBot.Providers.Facebook;

/// <summary>Everything Facebook contributes to the container, so composition stays one line.</summary>
public static class FacebookServiceCollectionExtensions
{
    public static IServiceCollection AddFacebook(
        this IServiceCollection services,
        IConfiguration section,
        ILogger startupLogger)
    {
        // One instance, two roles: a platform to the intake, the Facebook link shapes to the
        // downloader. Registering the interface separately would hand out a second one.
        services.AddSingleton<FacebookLinks>();
        services.AddSingleton<IPlatformLinks>(provider => provider.GetRequiredService<FacebookLinks>());

        var downloaders = section.GetSection("Downloaders").Get<FacebookDownloadersOptions>()
                          ?? new FacebookDownloadersOptions();

        if (!downloaders.YtDlp)
        {
            throw new InvalidOperationException(
                "No Facebook download strategy is enabled. Enable UpDownLoaderBot:Facebook:Downloaders:YtDlp.");
        }

        startupLogger.LogInformation("Facebook downloaders: YtDlp={YtDlp}.", downloaders.YtDlp);

        services.AddSingleton<IMediaDownloader, FacebookYtDlpDownloader>();

        return services;
    }
}
