using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using UpDownLoaderBot.Providers.Instagram;

namespace UpDownLoaderBot.Tests;

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

    private const string ReelUrl = "https://www.instagram.com/reel/DN-wdswgp9n/";

    [Fact]
    public async Task Rewrites_instagram_host_to_the_proxy_and_sends_a_bot_user_agent()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            captured = request;
            return VideoResponse("video/mp4", [1, 2, 3]);
        });

        var filePath = await CreateDownloader(handler).DownloadVideo(ReelUrl, _folder, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("https://www.kkinstagram.com/reel/DN-wdswgp9n/", captured!.RequestUri!.ToString());
        Assert.Contains("TelegramBot", captured.Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData("video/mp4", ".mp4")]
    [InlineData("video/webm", ".webm")]
    [InlineData("video/quicktime", ".mov")]
    [InlineData("video/x-unknown", ".mp4")]
    public async Task Picks_the_file_extension_from_the_content_type(string mediaType, string expectedExtension)
    {
        var handler = new StubHttpMessageHandler(_ => VideoResponse(mediaType, [1]));

        var filePath = await CreateDownloader(handler).DownloadVideo(ReelUrl, _folder, CancellationToken.None);

        Assert.Equal(expectedExtension, Path.GetExtension(filePath));
    }

    [Fact]
    public async Task Throws_when_the_proxy_returns_non_video_content()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not a video</html>", Encoding.UTF8, "text/html")
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateDownloader(handler).DownloadVideo(ReelUrl, _folder, CancellationToken.None));
    }

    [Fact]
    public async Task Refuses_a_download_whose_announced_size_is_over_the_limit()
    {
        // Content-Length is checked before the body is read, so nothing reaches the disk.
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = VideoResponse("video/mp4", [1, 2, 3]);
            response.Content.Headers.ContentLength = 100L * 1024 * 1024;
            return response;
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateDownloader(handler).DownloadVideo(ReelUrl, _folder, CancellationToken.None));

        Assert.Contains("over the 60 MB limit", error.Message);
        // Refused before the body was read, so nothing was written at all.
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Stops_a_download_that_outgrows_the_limit_without_announcing_it()
    {
        // No Content-Length, as on a chunked response, and more data than allowed.
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new EndlessStream())
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            return response;
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateDownloader(handler).DownloadVideo(ReelUrl, _folder, CancellationToken.None));

        Assert.Contains("sent more than 60 MB", error.Message);
    }

    private static KkInstagramDownloader CreateDownloader(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        return new KkInstagramDownloader(factory, NullLogger<KkInstagramDownloader>.Instance);
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

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
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
    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, disposeHandler: false);
        }
    }
}