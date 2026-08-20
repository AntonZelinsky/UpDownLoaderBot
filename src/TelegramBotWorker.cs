using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using UpDownLoaderBot.Media;
using UpDownLoaderBot.Providers.Instagram;

namespace UpDownLoaderBot;

/// <summary>
///     Long-polling worker: finds a supported URL in the message, downloads it via the enabled
///     download strategies (trying each in turn), replies with the video, then deletes the file.
/// </summary>
public sealed partial class TelegramBotWorker(
    ITelegramBotClient bot,
    IEnumerable<IInstagramVideoDownloader> downloaders,
    TelegramVideoPreparer preparer,
    ILogger<TelegramBotWorker> logger) : BackgroundService
{
    private readonly IReadOnlyList<IInstagramVideoDownloader> _downloaders = downloaders.ToArray();

    [GeneratedRegex(
        @"https?://(?:www\.)?instagram\.com/(?:[^\s/]+/)?(?:reel|reels|p|tv)/[A-Za-z0-9_-]+/?",
        RegexOptions.IgnoreCase)]
    private static partial Regex SupportedUrlRegex();

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: new ReceiverOptions { AllowedUpdates = [] },
            cancellationToken: stoppingToken);

        logger.LogInformation("Telegram bot started and receiving updates.");
        return Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken ct)
    {
        var message = update.Message ?? update.ChannelPost;
        if (message?.Text is not { } text)
        {
            return;
        }

        logger.LogInformation(
            "Incoming message from user {UserId} ({UserName}) in chat {ChatId} ({ChatName}): {Text}",
            message.From?.Id, DescribeUser(message.From), message.Chat.Id, DescribeChat(message.Chat), text);

        var match = SupportedUrlRegex().Match(text);
        if (!match.Success)
        {
            return;
        }

        var url = match.Value;
        logger.LogInformation("Found URL: {Url}", url);

        PreparedVideo? video = null;
        try
        {
            await client.SendChatAction(message.Chat.Id, ChatAction.UploadVideo, cancellationToken: ct);

            video = await DownloadAsync(url, ct);

            await using var stream = File.OpenRead(video.FilePath);
            await using var thumbnailStream = video.ThumbnailPath is null
                ? null
                : File.OpenRead(video.ThumbnailPath);

            // The mobile clients lay the player out from these; left out, they stay zero in the
            // message and the frame gets squashed.
            await client.SendVideo(
                chatId: message.Chat.Id,
                video: InputFile.FromStream(stream, Path.GetFileName(video.FilePath)),
                caption: url,
                duration: video.Duration,
                width: video.Width,
                height: video.Height,
                thumbnail: thumbnailStream is null
                    ? null
                    : InputFile.FromStream(thumbnailStream, Path.GetFileName(video.ThumbnailPath!)),
                supportsStreaming: true,
                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                cancellationToken: ct);

            logger.LogInformation(
                "Sent video for {Url} to chat {ChatId} as {Width}x{Height}, {Duration}s",
                url, message.Chat.Id, video.Width, video.Height, video.Duration);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The bot is shutting down, not failing to handle the link: no reaction to leave behind.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process {Url}", url);
            await TryReactWithFailureAsync(client, message, ct);
        }
        finally
        {
            if (video is not null)
            {
                foreach (var file in video.FilesToDelete)
                {
                    TryDelete(file);
                }
            }
        }
    }

    // A thumbs-down says the link was seen and did not work, without adding a message to the chat.
    // Best effort: where reactions are restricted, the failure is already in the log.
    private async Task TryReactWithFailureAsync(ITelegramBotClient client, Message message, CancellationToken ct)
    {
        try
        {
            await client.SetMessageReaction(
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

    // Tries each enabled downloader in order, returning the first result that is a usable video.
    // Preparation runs inside the loop so that a download which turns out not to be a video (see
    // TelegramVideoPreparer) moves on to the next downloader instead of being sent.
    private async Task<PreparedVideo> DownloadAsync(string url, CancellationToken ct)
    {
        Exception? lastError = null;
        foreach (var downloader in _downloaders)
        {
            ct.ThrowIfCancellationRequested();
            var name = downloader.GetType().Name;
            string? filePath = null;
            // Past this point the preparer deletes everything it produced, the download included.
            var preparerOwnsCleanup = false;
            try
            {
                logger.LogInformation("Trying downloader '{Name}' for {Url}", name, url);
                filePath = await downloader.DownloadAsync(url, ct);
                preparerOwnsCleanup = true;
                return await preparer.PrepareAsync(filePath, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogError(ex, "Downloader '{Name}' failed for {Url}", name, url);

                // A download that never reached the preparer is the only leftover left to us.
                if (filePath is not null && !preparerOwnsCleanup)
                {
                    TryDelete(filePath);
                }
            }
        }

        throw new InvalidOperationException($"All enabled downloaders failed for {url}.", lastError);
    }

    // Human-readable sender label for logs: "First Last @handle" (falls back to whatever is available).
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

    // Human-readable chat label for logs: group/channel title, private-chat name, @username, or chat type.
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

    private void TryDelete(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete file {FilePath}", filePath);
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        logger.LogError(exception, "Polling error from {Source}", source);
        return Task.CompletedTask;
    }
}