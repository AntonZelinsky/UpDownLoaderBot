using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using UpDownLoaderBot.Providers.Instagram;

namespace UpDownLoaderBot;

/// <summary>
///     Long-polling worker: finds a supported URL in the message, downloads it via the enabled
///     download strategies (trying each in turn), replies with the video, then deletes the file.
/// </summary>
public sealed partial class TelegramBotWorker(
    ITelegramBotClient bot,
    IEnumerable<IInstagramVideoDownloader> downloaders,
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

        string? filePath = null;
        try
        {
            await client.SendChatAction(message.Chat.Id, ChatAction.UploadVideo, cancellationToken: ct);

            filePath = await DownloadAsync(url, ct);

            await using var stream = File.OpenRead(filePath);
            await client.SendVideo(
                chatId: message.Chat.Id,
                video: InputFile.FromStream(stream, Path.GetFileName(filePath)),
                caption: url,
                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                cancellationToken: ct);

            logger.LogInformation("Sent video for {Url} to chat {ChatId}", url, message.Chat.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process {Url}", url);
        }
        finally
        {
            TryDelete(filePath);
        }
    }

    // Tries each enabled downloader in order, returning the first successful result.
    private async Task<string> DownloadAsync(string url, CancellationToken ct)
    {
        Exception? lastError = null;
        foreach (var downloader in _downloaders)
        {
            ct.ThrowIfCancellationRequested();
            var name = downloader.GetType().Name;
            try
            {
                logger.LogInformation("Trying downloader '{Name}' for {Url}", name, url);
                return await downloader.DownloadAsync(url, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogWarning(ex, "Downloader '{Name}' failed for {Url}", name, url);
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