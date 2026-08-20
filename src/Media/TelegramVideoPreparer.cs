using System.Globalization;
using System.Text.Json;

namespace UpDownLoaderBot.Media;

/// <summary>
///     A downloaded video described the way Telegram needs it.
/// </summary>
/// <param name="FilePath">The file to upload.</param>
/// <param name="Width">Display width: pixel aspect ratio and rotation applied.</param>
/// <param name="Height">Display height: pixel aspect ratio and rotation applied.</param>
/// <param name="Duration">Duration in whole seconds, rounded up.</param>
/// <param name="ThumbnailPath">Cover image, or <c>null</c> if one could not be produced.</param>
/// <param name="FilesToDelete">Everything the caller must delete after the upload.</param>
public sealed record PreparedVideo(
    string FilePath,
    int Width,
    int Height,
    int Duration,
    string? ThumbnailPath,
    IReadOnlyList<string> FilesToDelete);

/// <summary>
///     Measures a downloaded file so Telegram renders it correctly, and rejects what it cannot send.
///     The Bot API never inspects an upload, so a sendVideo without width/height leaves them at zero:
///     desktop clients read the local file and get it right, the mobile ones lay the player out from
///     the message and squash the frame.
/// </summary>
public sealed class TelegramVideoPreparer(ILogger<TelegramVideoPreparer> logger)
{
    /// <summary>The Bot API's upload ceiling for a bot's own files: 50 MB.</summary>
    private const long UploadLimitBytes = 50L * 1024 * 1024;

    /// <summary>Longest edge of the cover image: 320 px, Telegram's maximum (9:16 gives 180x320).</summary>
    private const int ThumbnailLongEdge = 320;

    /// <summary>Ceiling for one ffmpeg/ffprobe run: 5 minutes.</summary>
    private const int ToolTimeoutSeconds = 300;

    /// <summary>
    ///     Throws when the file cannot be sent as it is, which lets the caller fall through to the
    ///     next downloader — and, if none succeeds, tell the sender so.
    /// </summary>
    public async Task<PreparedVideo> PrepareAsync(string filePath, CancellationToken cancellationToken)
    {
        var filesToDelete = new List<string> { filePath };

        try
        {
            var probe = await ProbeAsync(filePath, cancellationToken);
            var sizeBytes = new FileInfo(filePath).Length;

            // Both downloaders hand over progressive H.264 (see YtDlpDownloaderBase's format
            // selector). Anything else is refused rather than re-encoded: VP9 does not play on iOS,
            // and re-encoding to fix that would be minutes of CPU for a case that should not happen.
            if (!probe.IsH264)
            {
                throw new InvalidOperationException($"'{filePath}' is '{probe.CodecName}', not H.264.");
            }

            if (sizeBytes > UploadLimitBytes)
            {
                throw new InvalidOperationException(
                    $"'{filePath}' is {sizeBytes / 1048576} MB, over the {UploadLimitBytes / 1048576} MB "
                    + "a bot may upload.");
            }

            var thumbnailPath = await TryCreateThumbnailAsync(filePath, probe, cancellationToken);
            if (thumbnailPath is not null)
            {
                filesToDelete.Add(thumbnailPath);
            }

            logger.LogInformation(
                "Prepared {FilePath}: {Width}x{Height}, {Duration:F0}s, {SizeMb:F1} MB.",
                filePath, probe.DisplayWidth, probe.DisplayHeight, probe.DurationSeconds, sizeBytes / 1048576.0);

            return new PreparedVideo(
                filePath,
                probe.DisplayWidth,
                probe.DisplayHeight,
                (int)Math.Ceiling(probe.DurationSeconds),
                thumbnailPath,
                filesToDelete);
        }
        catch
        {
            // Nobody receives the file list when this throws, so clean up whatever exists.
            foreach (var file in filesToDelete)
            {
                TryDelete(file);
            }

            throw;
        }
    }

    /// <summary>
    ///     Reads the metadata Telegram needs. Throws when the file is not a playable video —
    ///     kkinstagram sometimes answers with a still image under a <c>video/*</c> content type.
    /// </summary>
    private async Task<VideoProbe> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            "ffprobe",
            ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", filePath],
            ToolTimeoutSeconds,
            cancellationToken);

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        var streams = Child(root, "streams")?.EnumerateArray().ToArray() ?? [];

        // Cover art counts as a video stream; taking it would describe the upload with its size.
        var video = streams.FirstOrDefault(stream =>
            Text(stream, "codec_type") == "video" && Int(Child(stream, "disposition"), "attached_pic") != 1);

        if (video.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"'{filePath}' contains no video stream.");
        }

        // A single-frame image is a video stream too; the format name (jpeg_pipe, png_pipe, …) tells.
        var formatName = Text(Child(root, "format"), "format_name") ?? "";
        if (formatName.Contains("_pipe", StringComparison.Ordinal)
            || formatName.StartsWith("image", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{filePath}' is a still image ('{formatName}'), not a video.");
        }

        var width = Int(video, "width");
        var height = Int(video, "height");

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"'{filePath}' has no usable dimensions ({width}x{height}).");
        }

        return new VideoProbe(
            Text(video, "codec_name") ?? "",
            width,
            height,
            ParseSampleAspectRatio(video),
            ParseRotation(video),
            ParseSeconds(Child(root, "format"), "duration"));
    }

    // The cover image keeps the mobile layout right before playback starts. Best effort: a missing
    // thumbnail is not worth failing an otherwise good upload.
    private async Task<string?> TryCreateThumbnailAsync(
        string filePath,
        VideoProbe probe,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(filePath);
        var outputPath = Path.Combine(
            Path.GetDirectoryName(fullPath)!,
            Path.GetFileNameWithoutExtension(fullPath) + ".thumb.jpg");

        var (width, height) = ScaleToThumbnail(probe.DisplayWidth, probe.DisplayHeight);
        // One second in, or the midpoint of a very short clip, to avoid a black opening frame.
        var seek = probe.DurationSeconds > 2 ? 1 : probe.DurationSeconds / 2;

        try
        {
            await ProcessRunner.RunAsync(
                "ffmpeg",
                [
                    "-v", "error",
                    "-y",
                    "-ss", seek.ToString("0.###", CultureInfo.InvariantCulture),
                    "-i", filePath,
                    "-frames:v", "1",
                    "-vf", $"scale={width}:{height},setsar=1",
                    "-q:v", "4",
                    outputPath
                ],
                ToolTimeoutSeconds,
                cancellationToken);

            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0 ? outputPath : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to create a thumbnail for {FilePath}; sending without one.", filePath);
            return null;
        }
    }

    // Fits the frame inside a square of ThumbnailLongEdge, keeping the ratio and rounding to even
    // numbers. Small frames are left alone; upscaling only adds bytes.
    private static (int Width, int Height) ScaleToThumbnail(int width, int height)
    {
        var scale = Math.Min(1.0, ThumbnailLongEdge / (double)Math.Max(width, height));

        return (Even(width * scale), Even(height * scale));

        static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2, MidpointRounding.AwayFromZero) * 2);
    }

    // ffprobe's json is sparse — every field may be absent, so read it through these four.
    private static JsonElement? Child(JsonElement? parent, string name) =>
        parent?.TryGetProperty(name, out var value) == true ? value : null;

    private static string? Text(JsonElement? element, string name) =>
        Child(element, name)?.GetString();

    private static int Int(JsonElement? element, string name) =>
        Child(element, name) is { } value && value.TryGetInt32(out var number) ? number : 0;

    private static double ParseSeconds(JsonElement? element, string name) =>
        double.TryParse(Text(element, name), CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? seconds
            : 0;

    private static (int Numerator, int Denominator) ParseSampleAspectRatio(JsonElement video)
    {
        // ffprobe writes "N/A" (and occasionally "0:1") when the container says nothing.
        if (Text(video, "sample_aspect_ratio")?.Split(':') is not [var first, var second]
            || !int.TryParse(first, CultureInfo.InvariantCulture, out var numerator)
            || !int.TryParse(second, CultureInfo.InvariantCulture, out var denominator)
            || numerator <= 0
            || denominator <= 0)
        {
            return (1, 1);
        }

        return (numerator, denominator);
    }

    private static int ParseRotation(JsonElement video)
    {
        foreach (var sideData in Child(video, "side_data_list")?.EnumerateArray() ?? default)
        {
            if (Child(sideData, "rotation")?.TryGetDouble(out var rotation) == true)
            {
                // ffprobe reports a quarter turn as either 90 or -90.
                return ((int)Math.Round(rotation) % 360 + 360) % 360;
            }
        }

        return 0;
    }

    private void TryDelete(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete {FilePath}.", filePath);
        }
    }

    /// <summary>Video metadata as ffprobe reports it, plus the display size derived from it.</summary>
    private sealed record VideoProbe(
        string CodecName,
        int CodedWidth,
        int CodedHeight,
        (int Numerator, int Denominator) SampleAspectRatio,
        int Rotation,
        double DurationSeconds)
    {
        public bool IsH264 => CodecName is "h264";

        public int DisplayWidth => IsSideways ? ScaledHeight : ScaledWidth;

        public int DisplayHeight => IsSideways ? ScaledWidth : ScaledHeight;

        private bool IsSideways => Rotation is 90 or 270;

        // Non-square pixels are widened rather than shortened, so no detail is lost.
        private int ScaledWidth => SampleAspectRatio.Numerator > SampleAspectRatio.Denominator
            ? (int)Math.Round(CodedWidth * (double)SampleAspectRatio.Numerator / SampleAspectRatio.Denominator)
            : CodedWidth;

        private int ScaledHeight => SampleAspectRatio.Numerator < SampleAspectRatio.Denominator
            ? (int)Math.Round(CodedHeight * (double)SampleAspectRatio.Denominator / SampleAspectRatio.Numerator)
            : CodedHeight;
    }
}
