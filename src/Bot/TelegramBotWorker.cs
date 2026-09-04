using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using UpDownLoaderBot.Core;
using UpDownLoaderBot.Tools;

namespace UpDownLoaderBot.Bot;

/// <summary>
///     Long-polling worker. <see cref="HandleUpdate" /> only decides what a message is — a command or
///     a link — and hands it on; the work itself lives in a method per kind.
/// </summary>
public sealed class TelegramBotWorker : BackgroundService
{
    private const string StartCommand = "/start";

    private readonly ITelegramBotClient _bot;
    private readonly MediaFetcher _fetcher;
    private readonly MediaLinkParser _links;
    private readonly ILogger<TelegramBotWorker> _logger;

    public TelegramBotWorker(
        ITelegramBotClient bot,
        MediaLinkParser links,
        MediaFetcher fetcher,
        ILogger<TelegramBotWorker> logger)
    {
        _bot = bot;
        _links = links;
        _fetcher = fetcher;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _bot.StartReceiving(
            updateHandler: HandleUpdate,
            errorHandler: HandleError,
            // The bot only reads text out of a new message or a channel post; the rest is traffic we
            // would drop anyway.
            receiverOptions: new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.ChannelPost] },
            cancellationToken: stoppingToken);

        _logger.LogInformation("Telegram bot started and receiving updates.");
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

        if (_links.FirstIn(text) is { } link)
        {
            await HandleMediaRequest(link, message, ct);
        }
    }

    private void LogIncomingMessage(Message message, string text)
    {
        _logger.LogInformation(
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
            await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: BotTexts.StartInstructions.For(message.From?.LanguageCode),
                // The bare instagram.com and tiktok.com paths in the text are enough for Telegram to try a preview.
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: ct);

            _logger.LogInformation(
                "Sent the instructions to chat {ChatId} for client language '{LanguageCode}'",
                message.Chat.Id, message.From?.LanguageCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send the instructions to chat {ChatId}", message.Chat.Id);
        }
    }

    private async Task HandleMediaRequest(MediaLink link, Message message, CancellationToken ct)
    {
        var url = link.Url;

        _logger.LogInformation("Found a {Platform} link: {Url}", link.Platform, url);

        // Everything this request downloads lands in here and goes away with it.
        using var folder = new DownloadFolder(_logger);

        try
        {
            var post = await _fetcher.Fetch(link, folder.FullPath, ct);

            // Telegram drops the action after a few seconds, so said before the download it would be
            // gone by the time the upload — the part that keeps the user waiting — begins.
            await TryShowChatAction(message, ChatAction.UploadVideo, ct);

            // One video per request for now; the album branch arrives with multi-media.
            await SendVideoToChat(message, url, post.Media[0], ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The bot is shutting down, not failing to handle the link: no reaction to leave behind.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process {Url}", url);
            await TryReportFailure(message, ct);
        }
    }

    private async Task SendVideoToChat(Message message, string url, PreparedVideo video, CancellationToken ct)
    {
        await using var file = File.OpenRead(video.FilePath);

        // Dimensions and duration matter: the mobile clients lay the player out from them, and
        // left out they stay zero in the message and the frame gets squashed.
        await _bot.SendVideo(
            chatId: message.Chat.Id,
            video: InputFile.FromStream(file, Path.GetFileName(video.FilePath)),
            caption: url,
            duration: video.Duration,
            width: video.Width,
            height: video.Height,
            supportsStreaming: true,
            replyParameters: new ReplyParameters { MessageId = message.MessageId },
            cancellationToken: ct);

        _logger.LogInformation(
            "Sent a {Width}x{Height} {Duration}s video for {Url} to chat {ChatId}",
            video.Width, video.Height, video.Duration, url, message.Chat.Id);
    }

    // Decoration: losing it must not cost the video, nor the 👎 that follows.
    private async Task TryShowChatAction(Message message, ChatAction action, CancellationToken ct)
    {
        try
        {
            await _bot.SendChatAction(message.Chat.Id, action, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Failed to show the '{Action}' action in chat {ChatId}", action, message.Chat.Id);
        }
    }

    // Both halves of it: stop promising a video, then 👎 the link. Together, so neither is forgotten.
    private async Task TryReportFailure(Message message, CancellationToken ct)
    {
        await TryShowChatAction(message, ChatAction.Typing, ct);

        try
        {
            await _bot.SetMessageReaction(
                chatId: message.Chat.Id,
                messageId: message.MessageId,
                reaction: [new ReactionTypeEmoji { Emoji = "👎" }],
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to react to message {MessageId} in chat {ChatId}",
                message.MessageId, message.Chat.Id);
        }
    }

    private Task HandleError(ITelegramBotClient _, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        _logger.LogError(exception, "Polling error from {Source}", source);
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
