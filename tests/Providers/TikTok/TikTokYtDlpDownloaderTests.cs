using Microsoft.Extensions.Logging.Abstractions;
using UpDownLoaderBot.Core;
using UpDownLoaderBot.Providers.TikTok;

namespace UpDownLoaderBot.Tests.Providers.TikTok;

public class TikTokYtDlpDownloaderTests
{
    private static readonly TikTokLinks Links = new();

    private static readonly TikTokYtDlpDownloader Downloader =
        new(Links, NullLogger<TikTokYtDlpDownloader>.Instance);

    // The fallback takes every TikTok link, short ones included: those carry no post id for the
    // mirror to rewrite, so yt-dlp is the only one that can follow them.
    [Theory]
    [InlineData("https://www.tiktok.com/@scout2015/video/123")]
    [InlineData("https://www.tiktok.com/@scout2015/photo/123")]
    [InlineData("https://www.tiktok.com/share/video/123")]
    [InlineData("https://www.tiktokv.com/share/video/123")]
    [InlineData("https://lite.tiktok.com/t/ZSqetDcY9")]
    [InlineData("https://www.tiktok.com/embed/123")]
    [InlineData("https://m.tiktok.com/v/123.html")]
    [InlineData("https://vm.tiktok.com/ZNdA1qXBb/")]
    [InlineData("https://vt.tiktok.com/ZSabc123/")]
    [InlineData("https://www.tiktok.com/t/ZTd2y5Rar/")]
    public void Takes_every_tiktok_link(string url)
    {
        Assert.True(Downloader.CanHandle(new MediaLink(url, Links.Platform, "123")));
    }

    [Fact]
    public void Takes_no_link_of_another_platform()
    {
        Assert.False(Downloader.CanHandle(new MediaLink("https://example.com/watch/ABC", "example", "ABC")));
    }
}
