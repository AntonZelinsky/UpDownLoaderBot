using UpDownLoaderBot.Providers.Facebook;

namespace UpDownLoaderBot.Tests.Providers.Facebook;

public class FacebookLinksTests
{
    private static readonly FacebookLinks Links = new();

    [Theory]
    // What the share sheet hands out; it carries no post id, so its own code stands in as the file name.
    [InlineData("https://www.facebook.com/share/r/1Co1ByriW7/", "1Co1ByriW7")]
    [InlineData("https://m.facebook.com/share/v/1Co1ByriW7/", "1Co1ByriW7")]
    [InlineData("https://facebook.com/share/r/1Co1ByriW7", "1Co1ByriW7")]
    [InlineData("https://www.facebook.com/reel/1548309110288155/", "1548309110288155")]
    [InlineData("http://web.facebook.com/reel/1548309110288155", "1548309110288155")]
    [InlineData("https://www.facebook.com/watch/?v=1548309110288155", "1548309110288155")]
    [InlineData("https://www.facebook.com/watch?v=1548309110288155", "1548309110288155")]
    [InlineData("https://mbasic.facebook.com/watch/?v=1548309110288155", "1548309110288155")]
    // Facebook's own share sheet may put another parameter before v.
    [InlineData("https://www.facebook.com/watch/?extid=CL-UNK-UNK-UNK-AN_GK0T-GK1C&v=1548309110288155", "1548309110288155")]
    [InlineData("https://www.facebook.com/watch?ref=sharing&v=1548309110288155", "1548309110288155")]
    [InlineData("https://www.facebook.com/SomePage/videos/1548309110288155/", "1548309110288155")]
    [InlineData("https://www.facebook.com/some.page/videos/a-video-title/1548309110288155/", "1548309110288155")]
    [InlineData("https://fb.watch/abcXYZ_12/", "abcXYZ_12")]
    public void Reads_the_id_out_of_every_shape_it_takes(string url, string expectedId)
    {
        var link = Links.Find(url);

        Assert.NotNull(link);
        Assert.Equal("facebook", link.Platform);
        Assert.Equal(expectedId, link.Id);
    }

    // Sharing pastes a tracking tail along, and yt-dlp is better off without it.
    [Theory]
    [InlineData(
        "https://www.facebook.com/share/r/1Co1ByriW7/?mibextid=wwXIfr", "https://www.facebook.com/share/r/1Co1ByriW7/")]
    [InlineData(
        "https://www.facebook.com/reel/1548309110288155/?rdid=FqLwHaCmhSxhQgB8&share_url=x",
        "https://www.facebook.com/reel/1548309110288155/")]
    public void Leaves_the_tracking_tail_out_of_the_link(string url, string expected)
    {
        var link = Links.Find(url);

        Assert.NotNull(link);
        Assert.Equal(expected, link.Url);
    }

    // For watch the query is the id itself, so that part of it has to stay.
    [Fact]
    public void Keeps_the_video_id_of_a_watch_link()
    {
        var link = Links.Find("https://www.facebook.com/watch/?v=1548309110288155&ref=sharing");

        Assert.NotNull(link);
        Assert.Equal("https://www.facebook.com/watch/?v=1548309110288155", link.Url);
    }

    [Fact]
    public void Finds_the_link_inside_a_longer_message()
    {
        var link = Links.Find("глянь https://www.facebook.com/share/r/1Co1ByriW7/ огонь");

        Assert.NotNull(link);
        Assert.Equal("https://www.facebook.com/share/r/1Co1ByriW7/", link.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no links here at all")]
    [InlineData("https://www.facebook.com/")]
    [InlineData("https://www.facebook.com/SomePage")]
    [InlineData("https://www.facebook.com/watch/")]
    [InlineData("https://www.facebook.com/watch/?ref=sharing")]
    [InlineData("https://www.facebook.com/watch/?tv=1548309110288155")]
    [InlineData("https://www.facebook.com/profile.php?id=100000000000000")]
    // A post, mostly photos or text: yt-dlp would answer one without a video with a failure.
    [InlineData("https://www.facebook.com/share/p/1AbCdEfGhI/")]
    [InlineData("https://www.notfacebook.com/reel/1548309110288155/")]
    [InlineData("https://www.instagram.com/reel/ABC123/")]
    public void Ignores_text_carrying_nothing_it_answers(string text)
    {
        Assert.Null(Links.Find(text));
    }
}
