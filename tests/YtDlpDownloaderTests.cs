using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UpDownLoaderBot.Providers.Instagram;
using Xunit.Abstractions;

namespace UpDownLoaderBot.Tests;

public class YtDlpDownloaderTests(ITestOutputHelper output)
{
    private const string ReelUrl = "https://www.instagram.com/reel/DN-wdswgp9n/";

    // Integration test: actually invokes yt-dlp against a live Instagram reel.
    // Requires yt-dlp on PATH. Instagram needs authentication via cookies:
    //   - IG_COOKIES=/path/to/cookies.txt, or
    //   - a cookies/InstagramCookies.txt file in the repository root (auto-detected).
    [Fact]
    public async Task Downloads_instagram_reel_to_a_nonempty_file()
    {
        var cookiesFile = Environment.GetEnvironmentVariable("IG_COOKIES") ?? FindRepoCookies();
        Assert.False(
            string.IsNullOrWhiteSpace(cookiesFile),
            "No cookies found. Set IG_COOKIES or place cookies/InstagramCookies.txt in the repository root.");

        var options = new InstagramYtDlpOptions
        {
            InstagramCookiesFile = cookiesFile
        };

        var downloader = new InstagramYtDlpDownloader(Options.Create(options), NullLogger<InstagramYtDlpDownloader>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        string filePath;
        try
        {
            filePath = await downloader.DownloadAsync(ReelUrl, cts.Token);
        }
        catch (Exception ex)
        {
            output.WriteLine(ex.ToString());
            throw;
        }

        try
        {
            Assert.True(File.Exists(filePath), $"Expected a downloaded file at {filePath}");
            Assert.True(new FileInfo(filePath).Length > 0, "Downloaded file is empty");
            output.WriteLine($"Downloaded {new FileInfo(filePath).Length} bytes to {filePath}");
        }
        finally
        {
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                /* best effort */
            }
        }
    }

    // Walks up from the test binary to find cookies/InstagramCookies.txt in the repo root.
    private static string? FindRepoCookies()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "cookies", "InstagramCookies.txt");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}