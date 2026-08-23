using Microsoft.Extensions.Logging.Abstractions;
using UpDownLoaderBot.Tests.Support;
using UpDownLoaderBot.Tools.Ffprobe;
using Xunit.Abstractions;

namespace UpDownLoaderBot.Tests.Tools.Ffprobe;

/// <summary>
///     Runs against real files produced by ffmpeg — the metadata edge cases cannot be faked. Needs
///     no network; without ffmpeg on PATH each test says so and passes, so a bare runner stays green.
/// </summary>
public class TelegramVideoPreparerTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public TelegramVideoPreparerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private readonly string _workDirectory = Directory.CreateTempSubdirectory("preparer-tests-").FullName;

    private TelegramVideoPreparer Preparer => new(NullLogger<TelegramVideoPreparer>.Instance);

    private static CancellationTokenSource Timeout => new(TimeSpan.FromMinutes(2));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }

    [Fact]
    public async Task Reports_display_size_of_a_video_with_non_square_pixels()
    {
        if (!ToolsAvailable())
        {
            return;
        }

        // Stored 100x100 with pixels twice as wide as tall, so a player shows 200x100.
        var source = await CreateVideo("anamorphic.mp4", "100x100", ["-vf", "setsar=2/1", "-c:v", "libx264"]);

        var prepared = await Preparer.Prepare(source, Timeout.Token);

        Assert.Equal(200, prepared.Width);
        Assert.Equal(100, prepared.Height);
        Assert.Equal(source, prepared.FilePath);
    }

    [Fact]
    public async Task Swaps_dimensions_of_a_rotated_video()
    {
        if (!ToolsAvailable())
        {
            return;
        }

        var landscape = await CreateVideo("landscape.mp4", "120x80", ["-c:v", "libx264"]);
        var rotated = Path.Combine(_workDirectory, "rotated.mp4");
        await Run("ffmpeg", ["-v", "error", "-display_rotation", "90", "-i", landscape, "-c", "copy", "-y", rotated]);

        var prepared = await Preparer.Prepare(rotated, Timeout.Token);

        // Stored as 120x80, displayed as 80x120 after the quarter turn.
        Assert.Equal(80, prepared.Width);
        Assert.Equal(120, prepared.Height);
    }

    [Fact]
    public async Task Rejects_a_still_image_served_as_a_video()
    {
        if (!ToolsAvailable())
        {
            return;
        }

        // What kkinstagram occasionally returns: a JPEG behind a video/* content type, saved as .mp4.
        var image = Path.Combine(_workDirectory, "still.mp4");
        await Run("ffmpeg",
            ["-v", "error", "-f", "lavfi", "-i", "testsrc=size=100x100:rate=1:duration=1", "-frames:v", "1", "-f", "mjpeg", "-y", image]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Preparer.Prepare(image, Timeout.Token));

        _output.WriteLine(error.Message);
        Assert.Contains("still image", error.Message);
    }

    [Fact]
    public async Task Refuses_a_file_over_the_upload_limit()
    {
        if (!ToolsAvailable())
        {
            return;
        }

        // Padded with trailing zeroes to pass 50 MB cheaply — mp4 readers ignore the tail.
        var source = await CreateVideo("oversized.mp4", "100x100", ["-c:v", "libx264"]);
        await PadTo(source, 55L * 1024 * 1024);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Preparer.Prepare(source, Timeout.Token));

        _output.WriteLine(error.Message);
        Assert.Contains("over the 50 MB", error.Message);
    }

    [Fact]
    public async Task Refuses_a_codec_the_mobile_clients_cannot_play()
    {
        if (!ToolsAvailable() || !await EncoderAvailable("libvpx-vp9"))
        {
            _output.WriteLine("libvpx-vp9 unavailable; skipped.");
            return;
        }

        // Instagram's DASH ladder is VP9-only, and VP9 does not play on iOS.
        var source = await CreateVideo("vp9.mp4", "100x100", ["-c:v", "libvpx-vp9", "-b:v", "200k"]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Preparer.Prepare(source, Timeout.Token));

        _output.WriteLine(error.Message);
        Assert.Contains("not H.264", error.Message);
    }

    [Fact]
    public async Task Reports_the_duration_and_size_telegram_lays_the_player_out_from()
    {
        if (!ToolsAvailable())
        {
            return;
        }

        var source = await CreateVideo("two-seconds.mp4", "360x640", ["-c:v", "libx264"]);

        var prepared = await Preparer.Prepare(source, Timeout.Token);

        Assert.Equal(2, prepared.Duration);
        Assert.Equal(360, prepared.Width);
        Assert.Equal(640, prepared.Height);
    }

    // Extra arguments (codec, filters) are appended before the output file.
    private async Task<string> CreateVideo(
        string fileName,
        string size,
        string[] arguments,
        int duration = 2,
        int rate = 30)
    {
        var path = Path.Combine(_workDirectory, fileName);

        await Run("ffmpeg",
        [
            "-v", "error",
            "-f", "lavfi", "-i", $"testsrc=size={size}:rate={rate}:duration={duration}",
            "-f", "lavfi", "-i", $"sine=frequency=440:duration={duration}",
            "-map", "0:v", "-map", "1:a",
            .. arguments,
            "-c:a", "aac",
            "-pix_fmt", "yuv420p",
            "-y", path
        ]);

        return path;
    }

    // Cheaper than encoding tens of megabytes, and mp4 demuxers ignore what follows the last box.
    private static async Task PadTo(string filePath, long sizeBytes)
    {
        await using var file = new FileStream(filePath, FileMode.Append);
        var padding = new byte[64 * 1024];
        while (file.Length < sizeBytes)
        {
            await file.WriteAsync(padding);
        }
    }

    private static Task<bool> EncoderAvailable(string encoder) => Ffmpeg.EncoderAvailable(encoder);

    private bool ToolsAvailable()
    {
        if (Ffmpeg.Unavailable() is not { } reason)
        {
            return true;
        }

        _output.WriteLine(reason);

        return false;
    }

    private static Task<string> Run(string executable, IEnumerable<string> arguments) =>
        Ffmpeg.Run(executable, arguments);
}
