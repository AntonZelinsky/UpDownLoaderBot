using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using UpDownLoaderBot.Providers.Instagram;

namespace UpDownLoaderBot.Tests;

public class KkInstagramDownloaderTests
{
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

        var filePath = await CreateDownloader(handler).DownloadAsync(ReelUrl, CancellationToken.None);
        DeleteQuietly(filePath);

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

        var filePath = await CreateDownloader(handler).DownloadAsync(ReelUrl, CancellationToken.None);
        DeleteQuietly(filePath);

        Assert.Equal(expectedExtension, Path.GetExtension(filePath));
    }

    [Fact]
    public async Task Throws_when_the_proxy_returns_non_video_content()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not a video</html>", System.Text.Encoding.UTF8, "text/html")
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateDownloader(handler).DownloadAsync(ReelUrl, CancellationToken.None));
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
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        return response;
    }

    private static void DeleteQuietly(string filePath)
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

    // Returns a canned response for every request and records the last request seen.
    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    // Hands out an HttpClient wired to the stub handler, mimicking IHttpClientFactory.
    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
