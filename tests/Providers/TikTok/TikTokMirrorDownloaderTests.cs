using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using UpDownLoaderBot.Core;
using UpDownLoaderBot.Providers.TikTok;
using UpDownLoaderBot.Tests.Support;

namespace UpDownLoaderBot.Tests.Providers.TikTok;

public class TikTokMirrorDownloaderTests : IDisposable
{
    private static readonly TikTokLinks Links = new();

    private const string VideoUrl = "https://www.tiktok.com/@scout2015/video/6718335390845095173";

    private static readonly MediaLink Video = new(VideoUrl, Links.Platform, "6718335390845095173");

    private readonly string _folder = Directory.CreateTempSubdirectory("tnktok-tests-").FullName;

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

    // Only the host is rewritten, so the mirror claims exactly the shapes whose path it routes. It
    // must not claim the rest: CanHandle is the whole of the routing, and a downloader that claims
    // what it will 404 on spends a request to learn what the shape already said.
    [Theory]
    [InlineData("https://www.tiktok.com/@scout2015/video/123", true)]
    [InlineData("https://m.tiktok.com/@scout2015/video/123", true)]
    [InlineData("https://www.tiktok.com/share/video/123", true)]
    [InlineData("https://www.tiktokv.com/share/video/123", true)]
    [InlineData("https://vm.tiktok.com/ZNdA1qXBb/", true)]
    [InlineData("https://vt.tiktok.com/ZSqetDcY9/", true)]
    [InlineData("https://www.tiktok.com/t/ZTd2y5Rar/", true)]
    [InlineData("https://lite.tiktok.com/t/ZSqetDcY9", true)]
    [InlineData("https://www.tiktok.com/@scout2015/photo/123", false)]
    [InlineData("https://www.tiktok.com/share/photo/123", false)]
    [InlineData("https://www.tiktok.com/embed/123", false)]
    [InlineData("https://m.tiktok.com/v/123.html", false)]
    public void Takes_only_the_shapes_whose_path_the_mirror_routes(string url, bool expected)
    {
        var downloader = CreateDownloader(new StubHttp.Handler(_ => StubHttp.VideoResponse("video/mp4", [1])));
        var link = new MediaLink(url, Links.Platform, "123");

        Assert.Equal(expected, downloader.CanHandle(link));
    }

    [Fact]
    public void Takes_no_link_of_another_platform()
    {
        var downloader = CreateDownloader(new StubHttp.Handler(_ => StubHttp.VideoResponse("video/mp4", [1])));

        Assert.False(downloader.CanHandle(new MediaLink("https://example.com/watch/ABC", "example", "ABC")));
    }

    [Fact]
    public async Task Rewrites_tiktok_host_to_the_mirror_and_sends_a_bot_user_agent()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttp.Handler(request =>
        {
            captured = request;
            return StubHttp.VideoResponse("video/mp4", [1, 2, 3]);
        });

        await CreateDownloader(handler).Download(Video, _folder, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(
            "https://d.tnktok.com/@scout2015/video/6718335390845095173", captured!.RequestUri!.ToString());

        // A browser UA gets a redirect back to tiktok.com instead of the file.
        Assert.Contains("TelegramBot", captured.Headers.UserAgent.ToString());
    }

    // A short link needs no rewriting of its path either: the mirror answers /<code> the way it
    // answers /t/<code>, so it resolves the link itself and the id stays the code we already have.
    [Theory]
    [InlineData("https://vt.tiktok.com/ZSqetDcY9/", "https://d.tnktok.com/ZSqetDcY9/")]
    [InlineData("https://vm.tiktok.com/ZNdA1qXBb/", "https://d.tnktok.com/ZNdA1qXBb/")]
    [InlineData("https://www.tiktok.com/t/ZTd2y5Rar/", "https://d.tnktok.com/t/ZTd2y5Rar/")]
    [InlineData("https://lite.tiktok.com/t/ZSqetDcY9", "https://d.tnktok.com/t/ZSqetDcY9")]
    [InlineData("https://www.tiktokv.com/share/video/123", "https://d.tnktok.com/share/video/123")]
    public async Task Sends_a_short_link_to_the_mirror_untouched_but_for_the_host(
        string url, string expectedMirrorUrl)
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttp.Handler(request =>
        {
            captured = request;
            return StubHttp.VideoResponse("video/mp4", [1, 2, 3]);
        });

        var link = new MediaLink(url, Links.Platform, "ZSqetDcY9");
        await CreateDownloader(handler).Download(link, _folder, CancellationToken.None);

        Assert.Equal(expectedMirrorUrl, captured!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Names_the_file_after_the_post()
    {
        var handler = new StubHttp.Handler(_ => StubHttp.VideoResponse("video/mp4", [1, 2, 3]));

        var downloaded = await CreateDownloader(handler).Download(Video, _folder, CancellationToken.None);
        var filePath = downloaded.Files.Single();

        Assert.Equal("6718335390845095173.mp4", Path.GetFileName(filePath));
        Assert.Equal(_folder, Path.GetDirectoryName(filePath));
    }

    [Fact]
    public async Task Throws_when_the_mirror_returns_non_video_content()
    {
        // What the mirror answers when it does not recognize the client as a bot.
        var handler = new StubHttp.Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>embed page</html>", Encoding.UTF8, "text/html")
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateDownloader(handler).Download(Video, _folder, CancellationToken.None));

        Assert.Contains("tnktok returned non-video content", error.Message);
    }

    [Fact]
    public async Task Refuses_a_download_whose_announced_size_is_over_the_limit()
    {
        var handler = new StubHttp.Handler(_ =>
        {
            var response = StubHttp.VideoResponse("video/mp4", [1, 2, 3]);
            response.Content.Headers.ContentLength = 100L * 1024 * 1024;
            return response;
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateDownloader(handler).Download(Video, _folder, CancellationToken.None));

        Assert.Contains("over the 60 MB limit", error.Message);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Stops_a_download_that_outgrows_the_limit_without_announcing_it()
    {
        var handler = new StubHttp.Handler(_ => StubHttp.EndlessVideoResponse());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateDownloader(handler).Download(Video, _folder, CancellationToken.None));

        Assert.Contains("sent more than 60 MB", error.Message);
    }

    private static TikTokMirrorDownloader CreateDownloader(HttpMessageHandler handler)
    {
        return new TikTokMirrorDownloader(
            new StubHttp.Factory(handler), Links, NullLogger<TikTokMirrorDownloader>.Instance);
    }
}
