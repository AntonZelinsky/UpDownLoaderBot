using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using UpDownLoaderBot.Media;
using UpDownLoaderBot.Providers.Instagram;

namespace UpDownLoaderBot.Bot;

/// <summary>
///     Long-polling worker. <see cref="HandleUpdate" /> only decides what a message is — a command or
///     a link — and hands it on; the work itself lives in a method per kind.
/// </summary>
public sealed partial class TelegramBotWorker(
    ITelegramBotClient bot,
    IEnumerable<IInstagramVideoDownloader> downloaders,
    TelegramVideoPreparer preparer,
    ILogger<TelegramBotWorker> logger) : BackgroundService
{
    private const string StartCommand = "/start";

    private readonly IReadOnlyList<IInstagramVideoDownloader> _downloaders = downloaders.ToArray();

    [GeneratedRegex(
        @"https?://(?:www\.)?instagram\.com/(?:[^\s/]+/)?(?:reel|reels|p|tv)/[A-Za-z0-9_-]+/?",
        RegexOptions.IgnoreCase)]
    private static partial Regex SupportedUrlRegex();

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bot.StartReceiving(
            updateHandler: HandleUpdate,
            errorHandler: HandleError,
            // The bot only reads text out of a new message or a channel post; the rest is traffic we
            // would drop anyway.
            receiverOptions: new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.ChannelPost] },
            cancellationToken: stoppingToken);

        logger.LogInformation("Telegram bot started and receiving updates.");
        return Task.CompletedTask;
    }

    // StartReceiving hands over the very client that was injected, so the parameter goes unused.
    private async Task HandleUpdate(ITelegramBotClient _, Update update, CancellationToken ct)
    {
        var message = update.Message ?? update.ChannelPost;
        if (message?.Text?.Trim() is not { Length: > 0 } text)
        {
            return;
        }

        LogIncomingMessage(message, text);

        // A command we do not know is neither answered nor swallowed: the message goes on to the link
        // search, since it may belong to another bot in the group and still carry a link.
        if (CommandName(text) == StartCommand)
        {
            await SendStartCommandInstructionsToChat(message, ct);
            return;
        }

        if (SupportedUrlRegex().Match(text) is { Success: true } match)
        {
            await HandleVideoRequest(match.Value, message, ct);
        }
    }

    private void LogIncomingMessage(Message message, string text)
    {
        logger.LogInformation(
            "Incoming message from user {UserId} ({UserName}) in chat {ChatId} ({ChatName}): {Text}",
            message.From?.Id, DescribeUser(message.From), message.Chat.Id, DescribeChat(message.Chat), text);
    }

    // Telegram adds an @BotName suffix in groups and a payload to deep links, hence the first word only.
    private static string? CommandName(string text)
    {
        if (!text.StartsWith('/'))
        {
            return null;
        }

        var firstWord = text.Split([' ', '\t', '\n'], 2)[0];

        return firstWord.Split('@')[0].ToLowerInvariant();
    }

    private async Task SendStartCommandInstructionsToChat(Message message, CancellationToken ct)
    {
        try
        {
            await bot.SendMessage(
                chatId: message.Chat.Id,
                text: BotTexts.StartInstructions.For(message.From?.LanguageCode),
                // The bare instagram.com paths in the text are enough for Telegram to try a preview.
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: ct);

            logger.LogInformation(
                "Sent the instructions to chat {ChatId} for client language '{LanguageCode}'",
                message.Chat.Id, message.From?.LanguageCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send the instructions to chat {ChatId}", message.Chat.Id);
        }
    }

    private async Task HandleVideoRequest(string url, Message message, CancellationToken ct)
    {
        logger.LogInformation("Found URL: {Url}", url);

        // Everything this request downloads lands in here and goes away with it.
        using var folder = new DownloadFolder(logger);

        try
        {
            await bot.SendChatAction(message.Chat.Id, ChatAction.UploadVideo, cancellationToken: ct);

            var video = await DownloadAndPrepare(url, folder.FullPath, ct);
            await SendVideoToChat(message, url, video, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The bot is shutting down, not failing to handle the link: no reaction to leave behind.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process {Url}", url);
            await TryReactWithFailure(message, ct);
        }
    }

    // Preparation runs inside the loop so a download that turns out not to be a video — the cover
    // image kkinstagram serves for a carousel, say — moves on to the next downloader instead of
    // being sent.
    private async Task<PreparedVideo> DownloadAndPrepare(string url, string folder, CancellationToken ct)
    {
        Exception? lastError = null;
        foreach (var downloader in _downloaders)
        {
            ct.ThrowIfCancellationRequested();
            var downloaderName = downloader.GetType().Name;
            try
            {
                logger.LogInformation("Trying downloader '{Name}' for {Url}", downloaderName, url);
                var filePath = await downloader.DownloadVideo(url, folder, ct);

                return await preparer.Prepare(filePath, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogError(ex, "Downloader '{Name}' failed for {Url}", downloaderName, url);
            }
        }

        throw new InvalidOperationException($"All enabled downloaders failed for {url}.", lastError);
    }

    private async Task SendVideoToChat(Message message, string url, PreparedVideo video, CancellationToken ct)
    {
        await using var file = File.OpenRead(video.FilePath);

        // Dimensions and duration matter: the mobile clients lay the player out from them, and
        // left out they stay zero in the message and the frame gets squashed.
        await bot.SendVideo(
            chatId: message.Chat.Id,
            video: InputFile.FromStream(file, Path.GetFileName(video.FilePath)),
            caption: url,
            duration: video.Duration,
            width: video.Width,
            height: video.Height,
            supportsStreaming: true,
            replyParameters: new ReplyParameters { MessageId = message.MessageId },
            cancellationToken: ct);

        logger.LogInformation(
            "Sent a {Width}x{Height} {Duration}s video for {Url} to chat {ChatId}",
            video.Width, video.Height, video.Duration, url, message.Chat.Id);
    }

    private async Task TryReactWithFailure(Message message, CancellationToken ct)
    {
        try
        {
            await bot.SetMessageReaction(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                reaction: [new ReactionTypeEmoji { Emoji = "👎" }],
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to react to message {MessageId} in chat {ChatId}",
                message.MessageId, message.Chat.Id);
        }
    }

    private Task HandleError(ITelegramBotClient _, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        logger.LogError(exception, "Polling error from {Source}", source);
        return Task.CompletedTask;
    }

    private static string DescribeUser(User? user)
    {
        if (user is null)
        {
            return "unknown";
        }

        var name = string.Join(' ',
            new[] { user.FirstName, user.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return user.Username is { } handle
            ? string.IsNullOrWhiteSpace(name) ? $"@{handle}" : $"{name} @{handle}"
            : string.IsNullOrWhiteSpace(name)
                ? "?"
                : name;
    }

    private static string DescribeChat(Chat chat)
    {
        var name = chat.Title
                   ?? string.Join(' ',
                       new[] { chat.FirstName, chat.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return chat.Username is { } handle ? $"@{handle}" : chat.Type.ToString();
    }
}
