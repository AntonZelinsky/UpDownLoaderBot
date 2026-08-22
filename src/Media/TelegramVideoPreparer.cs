using System.Text.Json;
using UpDownLoaderBot.Core;

namespace UpDownLoaderBot.Media;

/// <summary>
///     Measures a downloaded file so Telegram renders it correctly, and rejects what it cannot send.
///     The Bot API never inspects an upload: without width/height the mobile clients lay the player
///     out from a message that says zero, and the frame comes out squashed.
/// </summary>
public sealed class TelegramVideoPreparer
{
    /// <summary>The Bot API's upload ceiling for a bot's own files: 50 MB.</summary>
    private const long UploadLimitBytes = 50L * 1024 * 1024;

    /// <summary>Ceiling for one ffprobe run: 5 minutes.</summary>
    private const int ToolTimeoutSeconds = 300;

    private readonly ILogger<TelegramVideoPreparer> _logger;

    public TelegramVideoPreparer(ILogger<TelegramVideoPreparer> logger)
    {
        _logger = logger;
    }

    /// <summary>Throws when the file cannot be sent as it is, so the caller can try another source.</summary>
    public async Task<PreparedVideo> Prepare(string filePath, CancellationToken cancellationToken)
    {
        var probed = await Probe(filePath, cancellationToken);
        var sizeBytes = new FileInfo(filePath).Length;

        EnsureNotStillImage(filePath, probed.Format);
        var video = RequireVideoStream(filePath, probed.Streams);
        EnsureMeasurable(filePath, video);
        EnsureTelegramCanSendIt(filePath, video, sizeBytes);

        var durationSeconds = probed.Format?.DurationSeconds ?? 0;

        _logger.LogInformation(
            "Prepared {FilePath}: {Width}x{Height}, {Duration:F0}s, {SizeMb:F1} MB.",
            filePath, video.DisplayWidth, video.DisplayHeight, durationSeconds, sizeBytes / 1048576.0);

        return new PreparedVideo(
            filePath,
            video.DisplayWidth,
            video.DisplayHeight,
            (int)Math.Ceiling(durationSeconds));
    }

    private async Task<FfprobeOutput> Probe(string filePath, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.Run(
            "ffprobe",
            ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", filePath],
            ToolTimeoutSeconds,
            cancellationToken);

        return JsonSerializer.Deserialize<FfprobeOutput>(result.StandardOutput)
               ?? throw new InvalidOperationException($"ffprobe printed no description of '{filePath}'.");
    }

    // A single-frame image is a video stream too; the format name (jpeg_pipe, png_pipe, …) tells.
    private static void EnsureNotStillImage(string filePath, FfprobeFormat? format)
    {
        var formatName = format?.FormatName ?? "";

        if (formatName.Contains("_pipe", StringComparison.Ordinal)
            || formatName.StartsWith("image", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{filePath}' is a still image ('{formatName}'), not a video.");
        }
    }

    private static FfprobeStream RequireVideoStream(string filePath, IReadOnlyList<FfprobeStream>? streams)
    {
        return streams?.FirstOrDefault(stream => stream.IsVideo && !stream.IsAttachedPicture)
               ?? throw new InvalidOperationException($"'{filePath}' contains no video stream.");
    }

    private static void EnsureMeasurable(string filePath, FfprobeStream video)
    {
        if (video.DisplayWidth <= 0 || video.DisplayHeight <= 0)
        {
            throw new InvalidOperationException(
                $"'{filePath}' has no usable dimensions ({video.Width}x{video.Height}).");
        }
    }

    // Both downloaders hand over progressive H.264 within the limit, so anything else is a surprise
    // worth refusing rather than spending minutes of CPU re-encoding (VP9 does not play on iOS) or
    // letting the Bot API refuse the upload itself.
    private static void EnsureTelegramCanSendIt(string filePath, FfprobeStream video, long sizeBytes)
    {
        if (video.CodecName is not "h264")
        {
            throw new InvalidOperationException($"'{filePath}' is '{video.CodecName}', not H.264.");
        }

        if (sizeBytes > UploadLimitBytes)
        {
            throw new InvalidOperationException(
                $"'{filePath}' is {sizeBytes / 1048576} MB, over the {UploadLimitBytes / 1048576} MB "
                + "a bot may upload.");
        }
    }
}
