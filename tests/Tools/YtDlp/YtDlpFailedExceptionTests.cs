using UpDownLoaderBot.Tools.YtDlp;

namespace UpDownLoaderBot.Tests.Tools.YtDlp;

/// <summary>
///     Telling "try again" from "no point trying" — the classification reads yt-dlp's prose, so what
///     each of its messages means is pinned here rather than discovered in production.
/// </summary>
public class YtDlpFailedExceptionTests
{
    /// <summary>The line a post carrying only a photo produces, verbatim.</summary>
    private const string NoVideoFormats =
        "ERROR: [Instagram] DW5-8r3gH0-: No video formats found!; please report this issue on  "
        + "https://github.com/yt-dlp/yt-dlp/issues?q= , filling out the appropriate issue template. "
        + "Confirm you are on the latest version using  yt-dlp -U";

    [Theory]
    [InlineData(NoVideoFormats)]
    [InlineData("ERROR: [Instagram] ABC: Requested content is not available, rate-limit reached or login required")]
    [InlineData("ERROR: [Instagram] ABC: This post is not available")]
    [InlineData("ERROR: Unsupported URL: https://example.com/whatever")]
    [InlineData("ERROR: [Instagram] ABC: Video unavailable")]
    [InlineData("ERROR: [Instagram] ABC: The page does not exist")]
    [InlineData("ERROR: [Instagram] ABC: This account is private")]
    [InlineData("ERROR: [Instagram] ABC: requested format is not available")]
    // A TikTok photo post: its URL shape has no extractor, so it falls through to the generic one.
    [InlineData("ERROR: Unsupported URL: https://www.tiktok.com/@someone/photo/123")]
    public void Knows_a_failure_about_the_post_is_the_same_on_every_attempt(string standardError)
    {
        Assert.True(IsFinal(standardError));
    }

    [Theory]
    [InlineData("ERROR: unable to download webpage: The read operation timed out")]
    [InlineData("ERROR: [Instagram] ABC: HTTP Error 503: Service Unavailable")]
    [InlineData("ERROR: unable to download video data: Connection reset by peer")]
    [InlineData("ERROR: [Instagram] ABC: Temporary failure in name resolution")]
    // What TikTok's extractor prints whenever the API hands back no post — for a deleted one as much
    // as for a genuine block. The wording cannot tell those apart, so it keeps its second go: reading
    // it as final would swallow a rate-limit that a retry would have got through.
    [InlineData("ERROR: [TikTok] 123: Your IP address is blocked from accessing this post")]
    public void Gives_a_failure_on_the_way_to_the_post_another_go(string standardError)
    {
        Assert.False(IsFinal(standardError));
    }

    [Theory]
    [InlineData("")]
    [InlineData("WARNING: Falling back on generic information extractor")]
    [InlineData("ERROR: something nobody has written a case for yet")]
    public void Retries_when_it_cannot_tell(string standardError)
    {
        Assert.False(IsFinal(standardError));
    }

    [Fact]
    public void Reads_the_error_line_rather_than_a_warning_above_it()
    {
        var stderr = string.Join('\n',
            "WARNING: [Instagram] ABC: no video formats found in the first entry, trying the next",
            "ERROR: unable to download webpage: Connection reset by peer");

        Assert.False(IsFinal(stderr));
    }

    // With --ignore-errors a carousel reports one failure per item, so a batch can be mixed.
    [Fact]
    public void Retries_when_one_of_several_failures_is_not_about_the_post()
    {
        var stderr = string.Join('\n',
            "ERROR: [Instagram] ABC: No video formats found!",
            "ERROR: [Instagram] DEF: No video formats found!",
            "ERROR: unable to download video data: Connection reset by peer");

        Assert.False(IsFinal(stderr));
    }

    [Fact]
    public void Gives_up_when_every_failure_is_about_the_post()
    {
        var stderr = string.Join('\n',
            "ERROR: [Instagram] ABC: No video formats found!",
            "ERROR: [Instagram] DEF: No video formats found!");

        Assert.True(IsFinal(stderr));
    }

    [Fact]
    public void Knows_a_post_of_photos_holds_no_video()
    {
        var carousel = string.Join('\n',
            "ERROR: [Instagram] ABC: No video formats found!",
            "ERROR: [Instagram] DEF: No video formats found!");

        Assert.True(HoldsNoVideo(NoVideoFormats));
        Assert.True(HoldsNoVideo(carousel));
    }

    // A carousel with a photo and a video that failed on the way had something to download.
    [Theory]
    [InlineData("")]
    [InlineData("ERROR: [Instagram] ABC: Requested content is not available, rate-limit reached or login required")]
    [InlineData("ERROR: Unsupported URL: https://www.tiktok.com/@someone/photo/123")]
    [InlineData("ERROR: [Instagram] ABC: No video formats found!\nERROR: unable to download video data: Connection reset by peer")]
    [InlineData("WARNING: [Instagram] ABC: No video formats found in the first entry\nERROR: [Instagram] ABC: This post is not available")]
    public void Does_not_call_a_failure_a_post_without_video(string standardError)
    {
        Assert.False(HoldsNoVideo(standardError));
    }

    private static bool HoldsNoVideo(string standardError)
    {
        return new YtDlpFailedException(1, standardOutput: "", standardError).HoldsNoVideo;
    }

    /// <summary>The message is what ends up under "Failed to process".</summary>
    [Fact]
    public void Carries_the_reason_into_its_message()
    {
        var error = new YtDlpFailedException(1, standardOutput: "", standardError: NoVideoFormats);

        Assert.True(error.IsFinal);
        Assert.Contains("No video formats found", error.Message);
        Assert.Contains("exited with code 1", error.Message);
    }

    /// <summary>What the retry loop reads, so the tests ask the same thing it does.</summary>
    private static bool IsFinal(string standardError)
    {
        return new YtDlpFailedException(1, standardOutput: "", standardError).IsFinal;
    }

    [Fact]
    public void Says_so_when_yt_dlp_reported_nothing_at_all()
    {
        var error = new YtDlpFailedException(101, standardOutput: "", standardError: "");

        Assert.False(error.IsFinal);
        Assert.Contains("reported no error", error.Message);
    }
}
