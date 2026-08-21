using Telegram.Bot;
using UpDownLoaderBot;
using UpDownLoaderBot.Media;
using UpDownLoaderBot.Providers.Instagram;

var builder = WebApplication.CreateBuilder(args);

// Logger for startup decisions, which are made before the host (and its logging) is built.
using var startupLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var startupLogger = startupLoggerFactory.CreateLogger("UpDownLoaderBot.Startup");

var appConfig = builder.Configuration.GetSection("UpDownLoaderBot");

var token = appConfig["Telegram:Token"];

if (string.IsNullOrWhiteSpace(token))
{
    throw new InvalidOperationException(
        "Telegram bot token is missing. Set the UpDownLoaderBot__Telegram__Token environment variable or UpDownLoaderBot:Telegram:Token in appsettings.json.");
}

builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(token));
builder.Services.AddSingleton<TelegramVideoPreparer>();
builder.Services.Configure<InstagramYtDlpOptions>(appConfig.GetSection("YtDlp"));

var features = appConfig.GetSection("InstagramDownloaders").Get<InstagramDownloadersOptions>()
               ?? new InstagramDownloadersOptions();

if (!features.KkInstagram && !features.YtDlp)
{
    throw new InvalidOperationException(
        "No Instagram download strategy is enabled. Enable at least one of " +
        "UpDownLoaderBot:InstagramDownloaders:YtDlp or UpDownLoaderBot:InstagramDownloaders:KkInstagram.");
}

startupLogger.LogInformation(
    "Instagram downloaders: KkInstagram={KkInstagram}, YtDlp={YtDlp}.", features.KkInstagram, features.YtDlp);

// Registration order is the order the worker tries them: kkinstagram (plain HTTP) first, yt-dlp
// as the fallback.
if (features.KkInstagram)
{
    builder.Services.AddHttpClient(nameof(KkInstagramDownloader));
    builder.Services.AddSingleton<IInstagramVideoDownloader, KkInstagramDownloader>();
}

if (features.YtDlp)
{
    builder.Services.AddSingleton<IInstagramVideoDownloader, InstagramYtDlpDownloader>();
}

DownloadFolder.DeleteLeftovers(startupLogger);

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