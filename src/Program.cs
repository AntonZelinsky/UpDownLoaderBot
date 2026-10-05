using Telegram.Bot;
using UpDownLoaderBot.Bot;
using UpDownLoaderBot.Core;
using UpDownLoaderBot.Providers.Facebook;
using UpDownLoaderBot.Providers.Instagram;
using UpDownLoaderBot.Providers.TikTok;
using UpDownLoaderBot.Tools;
using UpDownLoaderBot.Tools.Ffprobe;

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
// Platform registration order decides the intake: a message carrying links of several platforms is
// answered with the first platform here that recognizes one, not with the link that comes first in
// the text. Arbitrating by position needs each match's offset (backlog.md §8).
builder.Services.AddInstagram(appConfig.GetSection("Instagram"), startupLogger);
builder.Services.AddTikTok(appConfig.GetSection("TikTok"), startupLogger);
builder.Services.AddFacebook(appConfig.GetSection("Facebook"), startupLogger);

builder.Services.AddSingleton<MediaLinkParser>();
builder.Services.AddSingleton<MediaFetcher>();

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
