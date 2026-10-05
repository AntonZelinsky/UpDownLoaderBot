using Microsoft.Extensions.Logging.Abstractions;
using UpDownLoaderBot.Core;
using UpDownLoaderBot.Providers.Facebook;

namespace UpDownLoaderBot.Tests.Providers.Facebook;

public class FacebookYtDlpDownloaderTests
{
    private static readonly FacebookLinks Links = new();

    private static readonly FacebookYtDlpDownloader Downloader =
        new(Links, NullLogger<FacebookYtDlpDownloader>.Instance);

    // The only Facebook downloader, so it is also the last one and takes every link of the platform.
    [Theory]
    [InlineData("https://www.facebook.com/share/r/1Co1ByriW7/")]
    [InlineData("https://www.facebook.com/reel/1548309110288155/")]
    [InlineData("https://www.facebook.com/watch/?v=1548309110288155")]
    [InlineData("https://fb.watch/abcXYZ_12/")]
    public void Takes_every_facebook_link(string url)
    {
        Assert.True(Downloader.CanHandle(new MediaLink(url, Links.Platform, "123")));
    }

    [Fact]
    public void Takes_no_link_of_another_platform()
    {
        Assert.False(Downloader.CanHandle(new MediaLink("https://example.com/watch/ABC", "example", "ABC")));
    }
}
