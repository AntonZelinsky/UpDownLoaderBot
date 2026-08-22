using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using UpDownLoaderBot.Core;
using UpDownLoaderBot.Media;
using Xunit.Abstractions;

namespace UpDownLoaderBot.Tests;

/// <summary>
///     The fallback between downloaders. Runs against the real preparer, so no seam is needed in
///     production code: a file that is not a video is refused whether ffprobe rejects it or is missing.
/// </summary>
public class MediaFetcherTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public MediaFetcherTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly MediaLink Reel = new("https://www.instagram.com/reel/ABC123/", "instagram", "ABC123");

    private readonly string _folder = Directory.CreateTempSubdirectory("fetcher-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }

    [Fact]
    public async Task Moves_on_when_a_downloader_throws()
    {
        var first = StubDownloader.Failing();
        var second = StubDownloader.Failing();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Fetch(first, second));

        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    /// <summary>
    ///     The invariant the design turns on. The second downloader need not succeed — that it was
    ///     asked at all is the point.
    /// </summary>
    [Fact]
    public async Task Moves_on_when_a_downloaded_file_is_not_sendable()
    {
        var first = StubDownloader.Writing(_folder, "cover.mp4");
        var second = StubDownloader.Writing(_folder, "also-not-a-video.mp4");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Fetch(first, second));

        _output.WriteLine(error.Message);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    // When it was the last downloader, the refusal is the only explanation the 👎 has.
    [Fact]
    public async Task Reports_a_refused_file_as_the_cause()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Fetch(StubDownloader.Writing(_folder, "not-a-video.mp4")));

        _output.WriteLine(error.ToString());
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public async Task Reports_the_last_failure_as_the_cause()
    {
        var boom = new HttpRequestException("the mirror is down");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Fetch(StubDownloader.Failing(), StubDownloader.Failing(boom)));

        Assert.Contains(Reel.Url, error.Message);
        Assert.Same(boom, error.InnerException);
    }

    [Fact]
    public async Task Fails_when_no_downloader_is_registered()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Fetch());
    }

    // Skipped outright, not asked and forgiven: an attempt could stop the fallback at a partial post.
    [Fact]
    public async Task Never_asks_a_downloader_that_does_not_take_the_link()
    {
        var declining = StubDownloader.Declining();
        var taking = StubDownloader.Failing();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Fetch(declining, taking));

        Assert.Equal(0, declining.Calls);
        Assert.Equal(1, taking.Calls);
    }

    // Without this the user gets a 👎 and the log holds no failure to explain it.
    [Fact]
    public async Task Says_so_when_no_downloader_takes_the_link_at_all()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Fetch(StubDownloader.Declining(), StubDownloader.Declining()));

        _output.WriteLine(error.Message);
        Assert.Contains("No enabled downloader takes", error.Message);
        Assert.Null(error.InnerException);
    }

    // Shutting down is not a failed request: the worker tells them apart by the exception type.
    [Fact]
    public async Task Passes_cancellation_through_rather_than_calling_it_a_failure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var downloader = StubDownloader.Failing();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Fetcher(downloader).Fetch(Reel, _folder, cts.Token));

        Assert.Equal(0, downloader.Calls);
    }

    [Fact]
    public async Task Hands_the_downloader_the_folder_it_was_given()
    {
        var downloader = StubDownloader.Failing();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Fetch(downloader));

        Assert.Equal(_folder, downloader.LastFolder);
    }

    /// <summary>
    ///     The only test here needing a file the preparer accepts, so the only one that wants ffmpeg.
    /// </summary>
    [Fact]
    public async Task Stops_at_the_first_downloader_whose_file_can_be_sent()
    {
        if (await CreateVideo("ABC123.mp4") is not { } video)
        {
            _output.WriteLine("ffmpeg is not available; test skipped.");
            return;
        }

        var first = StubDownloader.Returning(video);
        var second = StubDownloader.Failing();

        var post = await Fetch(first, second);

        Assert.Equal(video, Assert.Single(post.Media).FilePath);
        Assert.Equal(1, first.Calls);
        Assert.Equal(0, second.Calls);
    }

    private Task<PreparedPost> Fetch(params StubDownloader[] downloaders)
    {
        return Fetcher(downloaders).Fetch(Reel, _folder, CancellationToken.None);
    }

    private static MediaFetcher Fetcher(params StubDownloader[] downloaders)
    {
        return new MediaFetcher(
            downloaders,
            new TelegramVideoPreparer(NullLogger<TelegramVideoPreparer>.Instance),
            NullLogger<MediaFetcher>.Instance);
    }

    private async Task<string?> CreateVideo(string fileName)
    {
        var path = Path.Combine(_folder, fileName);
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                ArgumentList =
                {
                    "-v", "error",
                    "-f", "lavfi", "-i", "testsrc=size=120x80:rate=30:duration=1",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p",
                    "-y", path
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            await process.WaitForExitAsync();

            return process.ExitCode == 0 && File.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            _output.WriteLine(ex.Message);
            return null;
        }
    }

    private sealed class StubDownloader : IMediaDownloader
    {
        private readonly Func<string, DownloadedPost> _download;
        private readonly bool _takesTheLink;

        private StubDownloader(Func<string, DownloadedPost> download, bool takesTheLink = true)
        {
            _download = download;
            _takesTheLink = takesTheLink;
        }

        public int Calls { get; private set; }

        public string? LastFolder { get; private set; }

        /// <summary>Takes nothing, the way kkinstagram passes on a link that may be a carousel.</summary>
        public static StubDownloader Declining()
        {
            return new StubDownloader(
                _ => throw new InvalidOperationException("must not be asked to download"),
                takesTheLink: false);
        }

        public bool CanHandle(MediaLink link)
        {
            return _takesTheLink;
        }

        /// <summary>Throws, the way a downloader reports it could not serve the link.</summary>
        public static StubDownloader Failing(Exception? error = null)
        {
            return new StubDownloader(_ => throw error ?? new InvalidOperationException("no video here"));
        }

        /// <summary>Writes a file the preparer is bound to refuse — three bytes of text, not a video.</summary>
        public static StubDownloader Writing(string folder, string fileName)
        {
            return new StubDownloader(_ =>
            {
                var path = Path.Combine(folder, fileName);
                File.WriteAllText(path, "not a video");

                return new DownloadedPost([path]);
            });
        }

        public static StubDownloader Returning(string filePath)
        {
            return new StubDownloader(_ => new DownloadedPost([filePath]));
        }

        public Task<DownloadedPost> Download(MediaLink link, string folder, CancellationToken cancellationToken)
        {
            Calls++;
            LastFolder = folder;

            return Task.FromResult(_download(folder));
        }
    }
}
