using UpDownLoaderBot.Core;
using UpDownLoaderBot.Providers.Instagram;

namespace UpDownLoaderBot.Tests;

/// <summary>The intake, against the real Instagram patterns.</summary>
public class MediaLinkParserTests
{
    private static readonly InstagramLinks Instagram = new();

    private static MediaLinkParser Parser => new([Instagram]);

    [Theory]
    [InlineData("https://www.instagram.com/reel/DABC123_x-y/")]
    [InlineData("https://www.instagram.com/reels/DABC123_x-y/")]
    [InlineData("https://www.instagram.com/p/DABC123_x-y/")]
    [InlineData("https://www.instagram.com/tv/DABC123_x-y/")]
    [InlineData("https://instagram.com/reel/DABC123_x-y/")]
    [InlineData("http://www.instagram.com/reel/DABC123_x-y/")]
    [InlineData("https://www.instagram.com/reel/DABC123_x-y")]
    [InlineData("https://www.instagram.com/someuser/reel/DABC123_x-y/")]
    [InlineData("HTTPS://WWW.INSTAGRAM.COM/REEL/DABC123_x-y/")]
    public void Recognizes_every_supported_link_shape(string url)
    {
        var link = Parser.FirstIn(url);

        Assert.NotNull(link);
        Assert.Equal(Instagram.Platform, link.Platform);
        Assert.Equal("DABC123_x-y", link.Id, ignoreCase: true);
    }

    [Fact]
    public void Finds_a_link_surrounded_by_words()
    {
        var link = Parser.FirstIn("look at this https://www.instagram.com/reel/ABC123/ isn't it great");

        Assert.NotNull(link);
        Assert.Equal("https://www.instagram.com/reel/ABC123/", link.Url);
    }

    // Telegram pastes a tracking tail onto a shared link; the downloaders are better off without it.
    [Fact]
    public void Leaves_the_tracking_tail_out_of_the_link()
    {
        var link = Parser.FirstIn("https://www.instagram.com/reel/ABC123/?igsh=MzRlODBiNWFlZA==");

        Assert.NotNull(link);
        Assert.Equal("https://www.instagram.com/reel/ABC123/", link.Url);
        Assert.Equal("ABC123", link.Id);
    }

    // A command of another bot in the group is not swallowed: it may still carry a link.
    [Fact]
    public void Finds_a_link_after_a_command_meant_for_someone_else()
    {
        var link = Parser.FirstIn("/download@OtherBot https://www.instagram.com/p/ABC123/");

        Assert.NotNull(link);
        Assert.Equal("ABC123", link.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no links here at all")]
    [InlineData("https://www.instagram.com/someuser/")]
    [InlineData("https://www.instagram.com/stories/someuser/123456/")]
    [InlineData("https://www.instagram.com/")]
    [InlineData("https://www.tiktok.com/@someone/video/123456")]
    public void Ignores_text_carrying_nothing_it_answers(string text)
    {
        Assert.Null(Parser.FirstIn(text));
    }

    [Fact]
    public void Finds_nothing_when_no_platform_is_registered()
    {
        var parser = new MediaLinkParser([]);

        Assert.Null(parser.FirstIn("https://www.instagram.com/reel/ABC123/"));
    }

    // Registration order deciding is a deliberate simplification, so it is pinned rather than assumed.
    [Fact]
    public void Takes_the_link_of_the_first_platform_that_recognizes_one()
    {
        var text = "https://example.com/watch/SECOND and https://www.instagram.com/reel/FIRST/";

        var byInstagram = new MediaLinkParser([Instagram, new ExampleLinks()]).FirstIn(text);
        var byExample = new MediaLinkParser([new ExampleLinks(), Instagram]).FirstIn(text);

        Assert.Equal(Instagram.Platform, byInstagram?.Platform);
        Assert.Equal(new ExampleLinks().Platform, byExample?.Platform);
    }

    [Fact]
    public void Skips_a_platform_that_recognizes_nothing()
    {
        var parser = new MediaLinkParser([new ExampleLinks(), Instagram]);

        var link = parser.FirstIn("https://www.instagram.com/reel/ABC123/");

        Assert.NotNull(link);
        Assert.Equal(Instagram.Platform, link.Platform);
    }

    /// <summary>A stand-in second platform, so the parser can be tested with more than one.</summary>
    private sealed class ExampleLinks : IPlatformLinks
    {
        public string Platform => "example";

        public MediaLink? Find(string text)
        {
            const string prefix = "https://example.com/watch/";
            var at = text.IndexOf(prefix, StringComparison.Ordinal);
            if (at < 0)
            {
                return null;
            }

            var id = new string(text[(at + prefix.Length)..].TakeWhile(char.IsLetterOrDigit).ToArray());

            return new MediaLink(prefix + id, Platform, id);
        }
    }
}
