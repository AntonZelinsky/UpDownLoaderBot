using UpDownLoaderBot.Core;
using UpDownLoaderBot.Providers.TikTok;

namespace UpDownLoaderBot.Tests.Providers.TikTok;

public class TikTokLinksTests
{
    private static readonly TikTokLinks Links = new();

    [Theory]
    [InlineData("https://www.tiktok.com/@scout2015/video/6718335390845095173", "6718335390845095173")]
    [InlineData("https://tiktok.com/@scout2015/video/6718335390845095173", "6718335390845095173")]
    [InlineData("https://m.tiktok.com/@scout2015/video/6718335390845095173", "6718335390845095173")]
    [InlineData("http://www.tiktok.com/@scout2015/video/6718335390845095173", "6718335390845095173")]
    [InlineData("https://www.tiktok.com/@user.name_1/video/123", "123")]
    [InlineData("https://www.tiktok.com/@scout2015/photo/6718335390845095173", "6718335390845095173")]
    // The shape the share sheet hands out; both yt-dlp and the mirror take it as it is.
    [InlineData("https://www.tiktok.com/share/video/6718335390845095173", "6718335390845095173")]
    // A share link from the app still uses tiktokv.com, and TikTok Lite has its own host.
    [InlineData("https://www.tiktokv.com/share/video/6718335390845095173", "6718335390845095173")]
    [InlineData("https://lite.tiktok.com/t/ZSqetDcY9", "ZSqetDcY9")]
    // Legacy, and only redirects now, but an old forwarded message still carries one.
    [InlineData("https://www.tiktok.com/embed/6718335390845095173", "6718335390845095173")]
    [InlineData("https://m.tiktok.com/v/6718335390845095173.html", "6718335390845095173")]
    // A short link carries no post id, so its own code stands in as the file name.
    [InlineData("https://vm.tiktok.com/ZNdA1qXBb/", "ZNdA1qXBb")]
    [InlineData("https://vt.tiktok.com/ZSabc123/", "ZSabc123")]
    [InlineData("https://www.tiktok.com/t/ZTd2y5Rar/", "ZTd2y5Rar")]
    public void Reads_the_id_out_of_every_shape_it_takes(string url, string expectedId)
    {
        var link = Links.Find(url);

        Assert.NotNull(link);
        Assert.Equal("tiktok", link.Platform);
        Assert.Equal(expectedId, link.Id);
    }

    // Sharing pastes a tracking tail along, and the downloaders are better off without it.
    [Fact]
    public void Leaves_the_tracking_tail_out_of_the_link()
    {
        var link = Links.Find(
            "look https://www.tiktok.com/@scout2015/video/6718335390845095173?is_from_webapp=1&_t=ZS-9 ok");

        Assert.NotNull(link);
        Assert.Equal("https://www.tiktok.com/@scout2015/video/6718335390845095173", link.Url);
    }

    // Dropping the suffix would leave a URL yt-dlp no longer recognizes, so it has to stay in.
    [Fact]
    public void Keeps_the_html_suffix_of_a_legacy_mobile_link()
    {
        var link = Links.Find("https://m.tiktok.com/v/6718335390845095173.html");

        Assert.NotNull(link);
        Assert.Equal("https://m.tiktok.com/v/6718335390845095173.html", link.Url);
    }

    [Fact]
    public void Finds_the_link_inside_a_longer_message()
    {
        var link = Links.Find("вот держи https://vm.tiktok.com/ZNdA1qXBb/ смешное");

        Assert.NotNull(link);
        Assert.Equal("https://vm.tiktok.com/ZNdA1qXBb/", link.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no links here at all")]
    [InlineData("https://www.tiktok.com/@scout2015")]
    [InlineData("https://www.tiktok.com/")]
    [InlineData("https://www.tiktok.com/tag/foryoupage")]
    [InlineData("https://www.tiktok.com/music/original-sound-6689804660171082501")]
    // TikTok itself answers a locale-prefixed path with a 404, so it is not a shape at all.
    [InlineData("https://www.tiktok.com/en/@someone/video/123")]
    [InlineData("https://www.notiktok.com/@someone/video/123")]
    [InlineData("https://www.instagram.com/reel/ABC123/")]
    public void Ignores_text_carrying_nothing_it_answers(string text)
    {
        Assert.Null(Links.Find(text));
    }

    [Theory]
    [InlineData("https://www.tiktok.com/@scout2015/video/123", TikTokLinkShape.Video)]
    [InlineData("https://m.tiktok.com/@scout2015/video/123", TikTokLinkShape.Video)]
    [InlineData("https://www.tiktok.com/share/video/123", TikTokLinkShape.Video)]
    [InlineData("https://www.tiktokv.com/share/video/123", TikTokLinkShape.Video)]
    [InlineData("https://www.tiktok.com/@scout2015/photo/123", TikTokLinkShape.Photo)]
    [InlineData("https://www.tiktok.com/share/photo/123", TikTokLinkShape.Photo)]
    [InlineData("https://vm.tiktok.com/ZNdA1qXBb/", TikTokLinkShape.Short)]
    [InlineData("https://vt.tiktok.com/ZSqetDcY9/", TikTokLinkShape.Short)]
    [InlineData("https://www.tiktok.com/t/ZTd2y5Rar/", TikTokLinkShape.Short)]
    [InlineData("https://lite.tiktok.com/t/ZSqetDcY9", TikTokLinkShape.Short)]
    [InlineData("https://www.tiktok.com/embed/123", TikTokLinkShape.Legacy)]
    [InlineData("https://m.tiktok.com/v/123.html", TikTokLinkShape.Legacy)]
    // A link built by hand rather than parsed: nothing may assume it is one of the known shapes.
    [InlineData("https://example.com/watch/ABC", TikTokLinkShape.Unknown)]
    public void Names_the_shape_of_the_link(string url, TikTokLinkShape expected)
    {
        var link = new MediaLink(url, Links.Platform, "123");

        Assert.Equal(expected, Links.ShapeOf(link));
    }
}
