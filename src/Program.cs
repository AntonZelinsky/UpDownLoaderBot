using Telegram.Bot;
using UpDownLoaderBot;
using UpDownLoaderBot.Providers.Instagram;
using KkInstagramDownloader = UpDownLoaderBot.Providers.Instagram.KkInstagramDownloader;

var builder = WebApplication.CreateBuilder(args);

// All application settings live under the "UpDownLoaderBot" section.
var appConfig = builder.Configuration.GetSection("UpDownLoaderBot");

// Token comes from appsettings.json, or preferably the UpDownLoaderBot__Telegram__Token
// environment variable, which the default configuration providers overlay automatically.
var token = appConfig["Telegram:Token"];

if (string.IsNullOrWhiteSpace(token))
{
    throw new InvalidOperationException(
        "Telegram bot token is missing. Set the UpDownLoaderBot__Telegram__Token environment variable or UpDownLoaderBot:Telegram:Token in appsettings.json.");
}

builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(token));
builder.Services.Configure<InstagramYtDlpOptions>(appConfig.GetSection("YtDlp"));

// Register the Instagram download strategies enabled by feature flags. Order here is the order
// the worker tries them: kkinstagram (lightweight HTTP) first, yt-dlp as the robust fallback.
var features = appConfig.GetSection("InstagramDownloaders").Get<InstagramDownloadersOptions>()
               ?? new InstagramDownloadersOptions();

if (features.KkInstagram)
{
    builder.Services.AddHttpClient(nameof(KkInstagramDownloader));
    builder.Services.AddSingleton<IInstagramVideoDownloader, KkInstagramDownloader>();
}

if (features.YtDlp)
{
    builder.Services.AddSingleton<IInstagramVideoDownloader, InstagramYtDlpDownloader>();
}

if (!features.KkInstagram && !features.YtDlp)
{
    throw new InvalidOperationException(
        "No Instagram download strategy is enabled. Enable at least one of " +
        "UpDownLoaderBot:InstagramDownloaders:YtDlp or UpDownLoaderBot:InstagramDownloaders:KkInstagram.");
}

builder.Services.AddHostedService<TelegramBotWorker>();

var app = builder.Build();

app.MapGet("/health", async (ITelegramBotClient bot, CancellationToken ct) =>
{
    try
    {
        var me = await bot.GetMe(ct);
        return Results.Ok(new { status = "ok", bot = me.Username });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "error", error = ex.Message }, statusCode: 503);
    }
});

app.Run();