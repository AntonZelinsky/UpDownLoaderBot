using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using UpDownLoaderBot.Core;
using UpDownLoaderBot.Providers.Instagram;

namespace UpDownLoaderBot.Tests.Providers.Instagram;

public class KkInstagramDownloaderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("kkinstagram-tests-").FullName;

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

    private static readonly InstagramLinks Links = new();

    private const string ReelUrl = "https://www.instagram.com/reel/DN-wdswgp9n/";

    private static readonly MediaLink Reel = new(ReelUrl, Links.Platform, "DN-wdswgp9n");

    // The proxy answers with one file, so it must claim only links that hold exactly one video.
    // A /p/ post may be a carousel: taking it would deliver a part and stop the fallback at that.
    [Theory]
    [InlineData("https://www.instagram.com/reel/ABC123/", true)]
    [InlineData("https://www.instagram.com/reels/ABC123/", true)]
    [InlineData("https://www.instagram.com/tv/ABC123/", true)]
    [InlineData("https://www.instagram.com/someuser/reel/ABC123/", true)]
    [InlineData("https://www.instagram.com/p/ABC123/", false)]
    [InlineData("https://www.instagram.com/someuser/p/ABC123/", false)]
    public void Takes_only_links_holding_exactly_one_video(string url, bool expected)
    {
        var downloader = CreateDownloader(new StubHttpMessageHandler(_ => VideoResponse("video/mp4", [1])));
        var link = new MediaLink(url, Links.Platform, "ABC123");

        Assert.Equal(expected, downloader.CanHandle(link));
    }

    [Fact]
    public void Takes_no_link_of_another_platform()
    {
        var downloader = CreateDownloader(new StubHttpMessageHandler(_ => VideoResponse("video/mp4", [1])));

        Assert.False(downloader.CanHandle(new MediaLink("https://example.com/watch/ABC", "example", "ABC")));
    }

    [Fact]
    public async Task Rewrites_instagram_host_to_the_proxy_and_sends_a_bot_user_agent()
    {
        HttpRequestMessage? captured = null;
        var _handler = new StubHttpMessageHandler(request =>
        {
            captured = request;
            return VideoResponse("video/mp4", [1, 2, 3]);
        });

        var filePath = (await CreateDownloader(_handler).Download(Reel, _folder, CancellationToken.None)).Files.Single();

        Assert.NotNull(captured);
        Assert.Equal("https://www.kkinstagram.com/reel/DN-wdswgp9n/", captured!.RequestUri!.ToString());
        Assert.Contains("TelegramBot", captured.Headers.UserAgent.ToString());
    }

    // One link produces one name, whichever downloader served it — so a leftover from an interrupted
    // run cannot be mistaken for this request's file, and a carousel can later be numbered off it.
    [Fact]
    public async Task Names_the_file_after_the_post()
    {
        var _handler = new StubHttpMessageHandler(_ => VideoResponse("video/mp4", [1, 2, 3]));

        var filePath = (await CreateDownloader(_handler).Download(Reel, _folder, CancellationToken.None)).Files.Single();

        Assert.Equal("DN-wdswgp9n.mp4", Path.GetFileName(filePath));
        Assert.Equal(_folder, Path.GetDirectoryName(filePath));
    }

    [Theory]
    [InlineData("video/mp4", ".mp4")]
    [InlineData("video/webm", ".webm")]
    [InlineData("video/quicktime", ".mov")]
    [InlineData("video/x-unknown", ".mp4")]
    public async Task Picks_the_file_extension_from_the_content_type(string mediaType, string expectedExtension)
    {
        var _handler = new StubHttpMessageHandler(_ => VideoResponse(mediaType, [1]));

        var filePath = (await CreateDownloader(_handler).Download(Reel, _folder, CancellationToken.None)).Files.Single();

        Assert.Equal(expectedExtension, Path.GetExtension(filePath));
    }

    [Fact]
    public async Task Throws_when_the_proxy_returns_non_video_content()
    {
        var _handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not a video</html>", Encoding.UTF8, "text/html")
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateDownloader(_handler).Download(Reel, _folder, CancellationToken.None));
    }

    [Fact]
    public async Task Refuses_a_download_whose_announced_size_is_over_the_limit()
    {
        // Content-Length is checked before the body is read, so nothing reaches the disk.
        var _handler = new StubHttpMessageHandler(_ =>
        {
            var response = VideoResponse("video/mp4", [1, 2, 3]);
            response.Content.Headers.ContentLength = 100L * 1024 * 1024;
            return response;
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateDownloader(_handler).Download(Reel, _folder, CancellationToken.None));

        Assert.Contains("over the 60 MB limit", error.Message);
        // Refused before the body was read, so nothing was written at all.
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Stops_a_download_that_outgrows_the_limit_without_announcing_it()
    {
        // No Content-Length, as on a chunked response, and more data than allowed.
        var _handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new EndlessStream())
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            return response;
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateDownloader(_handler).Download(Reel, _folder, CancellationToken.None));

        Assert.Contains("sent more than 60 MB", error.Message);
    }

    private static KkInstagramDownloader CreateDownloader(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        return new KkInstagramDownloader(factory, Links, NullLogger<KkInstagramDownloader>.Instance);
    }

    private static HttpResponseMessage VideoResponse(string mediaType, byte[] body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    // Never-ending source of bytes, standing in for a response that keeps on coming.
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => count;

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // Hands out an HttpClient wired to the stub handler, mimicking IHttpClientFactory.
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }
}
