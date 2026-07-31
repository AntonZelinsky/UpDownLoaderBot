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
    // Skipped by default (and in CI, where neither yt-dlp nor cookies exist); run manually.
    [Fact(Skip = "Integration test: requires yt-dlp on PATH and Instagram cookies; not available in CI.")]
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

    [Fact]
    public void Seeds_a_writable_cookies_session_next_to_the_deployed_file()
    {
        using var cookies = new TempCookies("deployed-v1");

        CreateDownloader(cookies.DeployedFile);

        Assert.Equal("deployed-v1", File.ReadAllText(cookies.SessionFile));
    }

    [Fact]
    public void Keeps_a_session_refreshed_by_ytdlp_when_the_deployed_cookies_are_unchanged()
    {
        using var cookies = new TempCookies("deployed-v1");
        CreateDownloader(cookies.DeployedFile);

        // Stand in for yt-dlp writing a renewed Instagram session back to its cookies file.
        File.WriteAllText(cookies.SessionFile, "refreshed-by-ytdlp");

        // A restart (same deployed cookies) must not throw the refreshed session away.
        CreateDownloader(cookies.DeployedFile);

        Assert.Equal("refreshed-by-ytdlp", File.ReadAllText(cookies.SessionFile));
    }

    [Fact]
    public void Reseeds_the_session_when_an_interrupted_write_left_it_empty()
    {
        using var cookies = new TempCookies("deployed-v1");
        CreateDownloader(cookies.DeployedFile);

        // yt-dlp saves cookies by truncating the file first; a killed write leaves it empty.
        File.WriteAllText(cookies.SessionFile, string.Empty);

        CreateDownloader(cookies.DeployedFile);

        Assert.Equal("deployed-v1", File.ReadAllText(cookies.SessionFile));
    }

    [Fact]
    public void Reseeds_the_session_after_the_deploy_deleted_it_for_new_cookies()
    {
        using var cookies = new TempCookies("deployed-v1");
        CreateDownloader(cookies.DeployedFile);
        File.WriteAllText(cookies.SessionFile, "refreshed-by-ytdlp");

        // What the deploy does when the INSTAGRAM_COOKIES secret changed.
        File.WriteAllText(cookies.DeployedFile, "deployed-v2");
        File.Delete(cookies.SessionFile);

        CreateDownloader(cookies.DeployedFile);

        Assert.Equal("deployed-v2", File.ReadAllText(cookies.SessionFile));
    }

    private static InstagramYtDlpDownloader CreateDownloader(string cookiesFile)
    {
        var options = Options.Create(new InstagramYtDlpOptions { InstagramCookiesFile = cookiesFile });
        return new InstagramYtDlpDownloader(options, NullLogger<InstagramYtDlpDownloader>.Instance);
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

    // A throwaway directory holding a deployed cookies file and the session file derived from it.
    private sealed class TempCookies : IDisposable
    {
        private readonly string _directory;

        public TempCookies(string deployedContent)
        {
            _directory = Path.Combine(Path.GetTempPath(), $"updownloaderbot-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);

            DeployedFile = Path.Combine(_directory, "InstagramCookies.txt");
            SessionFile = Path.Combine(_directory, "InstagramCookies.session.txt");
            File.WriteAllText(DeployedFile, deployedContent);
        }

        public string DeployedFile { get; }

        public string SessionFile { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch
            {
                /* best effort */
            }
        }
    }
}