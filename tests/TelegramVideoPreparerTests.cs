using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using UpDownLoaderBot.Media;
using Xunit.Abstractions;

namespace UpDownLoaderBot.Tests;

/// <summary>
///     Exercises <see cref="TelegramVideoPreparer" /> against real files produced by ffmpeg — the
///     metadata edge cases it exists for cannot be faked. No network or credentials needed, but
///     ffmpeg/ffprobe must be on PATH; without them each test reports why it did nothing and passes,
///     so a runner that lacks the tools does not fail the build.
/// </summary>
public class TelegramVideoPreparerTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _workDirectory = Directory.CreateTempSubdirectory("preparer-tests-").FullName;

    private TelegramVideoPreparer Preparer => new(NullLogger<TelegramVideoPreparer>.Instance);

    // Nothing here touches the network, so a generous per-test ceiling only guards against a hung tool.
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

        // 100x100 stored pixels that are twice as wide as they are tall: the frame must be
        // reported as 200x100, which is what a player actually shows.
        var source = await CreateVideoAsync("anamorphic.mp4", "100x100", ["-vf", "setsar=2/1", "-c:v", "libx264"]);

        var prepared = await Preparer.PrepareAsync(source, Timeout.Token);

        Assert.Equal(200, prepared.Width);
        Assert.Equal(100, prepared.Height);
        Assert.Equal(source, prepared.FilePath);
        Assert.NotNull(prepared.ThumbnailPath);
        Assert.Equal([source, prepared.ThumbnailPath], prepared.FilesToDelete);
    }

    [Fact]
    public async Task Swaps_dimensions_of_a_rotated_video()
    {
        if (!ToolsAvailable())
        {
            return;
        }

        var landscape = await CreateVideoAsync("landscape.mp4", "120x80", ["-c:v", "libx264"]);
        var rotated = Path.Combine(_workDirectory, "rotated.mp4");
        await RunAsync("ffmpeg", ["-v", "error", "-display_rotation", "90", "-i", landscape, "-c", "copy", "-y", rotated]);

        var prepared = await Preparer.PrepareAsync(rotated, Timeout.Token);

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
        await RunAsync("ffmpeg",
            ["-v", "error", "-f", "lavfi", "-i", "testsrc=size=100x100:rate=1:duration=1", "-frames:v", "1", "-f", "mjpeg", "-y", image]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Preparer.PrepareAsync(image, Timeout.Token));

        output.WriteLine(error.Message);
        Assert.Contains("still image", error.Message);
        // A rejected download must not linger: nobody receives a file list to clean up.
        Assert.False(File.Exists(image), "The rejected file was left on disk");
    }

    [Fact]
    public async Task Refuses_a_file_over_the_upload_limit()
    {
        if (!ToolsAvailable())
        {
            return;
        }

        // Padded with trailing zeroes to pass 50 MB cheaply — mp4 readers ignore the tail.
        var source = await CreateVideoAsync("oversized.mp4", "100x100", ["-c:v", "libx264"]);
        await PadToAsync(source, 55L * 1024 * 1024);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Preparer.PrepareAsync(source, Timeout.Token));

        output.WriteLine(error.Message);
        Assert.Contains("over the 50 MB", error.Message);
        Assert.False(File.Exists(source), "The rejected file was left on disk");
    }

    [Fact]
    public async Task Refuses_a_codec_the_mobile_clients_cannot_play()
    {
        if (!ToolsAvailable() || !await EncoderAvailableAsync("libvpx-vp9"))
        {
            output.WriteLine("libvpx-vp9 unavailable; skipped.");
            return;
        }

        // Instagram's DASH ladder is VP9-only, and VP9 does not play on iOS.
        var source = await CreateVideoAsync("vp9.mp4", "100x100", ["-c:v", "libvpx-vp9", "-b:v", "200k"]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Preparer.PrepareAsync(source, Timeout.Token));

        output.WriteLine(error.Message);
        Assert.Contains("not H.264", error.Message);
    }

    [Fact]
    public async Task Produces_a_thumbnail_in_the_same_aspect_ratio()
    {
        if (!ToolsAvailable())
        {
            return;
        }

        var source = await CreateVideoAsync("thumb-source.mp4", "360x640", ["-c:v", "libx264"]);

        var prepared = await Preparer.PrepareAsync(source, Timeout.Token);

        Assert.NotNull(prepared.ThumbnailPath);
        Assert.True(new FileInfo(prepared.ThumbnailPath).Length > 0, "Thumbnail is empty");

        var (width, height) = await DimensionsOfAsync(prepared.ThumbnailPath);
        // Telegram caps thumbnails at 320px on the long edge, and the ratio must match the video.
        Assert.Equal(320, height);
        Assert.Equal(180, width);
        Assert.Equal(2, prepared.Duration);
    }

    // Renders a test pattern of the given size and duration with a sine audio track; extra
    // arguments (codec, filters) are appended before the output file.
    private async Task<string> CreateVideoAsync(
        string fileName,
        string size,
        string[] arguments,
        int duration = 2,
        int rate = 30)
    {
        var path = Path.Combine(_workDirectory, fileName);

        await RunAsync("ffmpeg",
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

    // Grows a file to the requested size with trailing zeroes. Cheaper than encoding tens of
    // megabytes, and mp4 demuxers ignore whatever follows the last box.
    private static async Task PadToAsync(string filePath, long sizeBytes)
    {
        await using var file = new FileStream(filePath, FileMode.Append);
        var padding = new byte[64 * 1024];
        while (file.Length < sizeBytes)
        {
            await file.WriteAsync(padding);
        }
    }

    private async Task<string> CodecOfAsync(string filePath) =>
        (await RunAsync("ffprobe",
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name", "-of", "csv=p=0", filePath]))
        .Trim();

    private async Task<(int Width, int Height)> DimensionsOfAsync(string filePath)
    {
        var csv = await RunAsync("ffprobe",
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", filePath]);
        var parts = csv.Trim().Split(',');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private async Task<bool> EncoderAvailableAsync(string encoder) =>
        (await RunAsync("ffmpeg", ["-v", "error", "-hide_banner", "-encoders"])).Contains(encoder);

    private bool ToolsAvailable()
    {
        foreach (var tool in (string[])["ffmpeg", "ffprobe"])
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = tool,
                    ArgumentList = { "-version" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });
                process!.WaitForExit();
            }
            catch (Exception ex)
            {
                output.WriteLine($"{tool} is not available ({ex.Message}); test skipped.");
                return false;
            }
        }

        return true;
    }

    private static async Task<string> RunAsync(string executable, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0
            ? stdout
            : throw new InvalidOperationException($"{executable} failed ({process.ExitCode}): {stderr}");
    }
}
