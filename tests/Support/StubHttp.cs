using System.Net;
using System.Net.Http.Headers;

namespace UpDownLoaderBot.Tests.Support;

/// <summary>
///     The HTTP stand-ins shared by the two mirror downloader tests, which are the same test written
///     against two hosts.
/// </summary>
public static class StubHttp
{
    public static HttpResponseMessage VideoResponse(string mediaType, byte[] body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);

        return response;
    }

    /// <summary>A response with no Content-Length that keeps on coming, as a chunked one does.</summary>
    public static HttpResponseMessage EndlessVideoResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new EndlessStream())
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");

        return response;
    }

    public sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public Handler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responder(request);

            return Task.FromResult(response);
        }
    }

    /// <summary>Hands out an HttpClient wired to the stub handler, mimicking IHttpClientFactory.</summary>
    public sealed class Factory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public Factory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => count;

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
