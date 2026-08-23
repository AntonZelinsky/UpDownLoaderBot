using System.Text.Json;
using UpDownLoaderBot.Tools.Ffprobe;

namespace UpDownLoaderBot.Tests.Tools.Ffprobe;

/// <summary>
///     How ffprobe's output reads once deserialized, absences included — the preparer trusts these
///     answers instead of checking every field itself. Offline: the JSON is what ffprobe prints.
/// </summary>
public class FfprobeOutputTests
{
    private const string ReelOutput =
        """
        {
          "streams": [
            { "codec_type": "audio", "codec_name": "aac" },
            {
              "codec_type": "video",
              "codec_name": "h264",
              "width": 1080,
              "height": 1920,
              "sample_aspect_ratio": "1:1",
              "disposition": { "attached_pic": 0 },
              "nb_frames": "4590"
            }
          ],
          "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "12.500000" }
        }
        """;

    private static FfprobeOutput Parse(string json) =>
        JsonSerializer.Deserialize<FfprobeOutput>(json)!;

    private static FfprobeStream ParseStream(string json) =>
        Parse($$"""{ "streams": [ {{json}} ] }""").Streams!.Single();

    [Fact]
    public void Reads_what_ffprobe_says_about_a_reel()
    {
        var output = Parse(ReelOutput);

        var video = Assert.Single(output.Streams!, stream => stream.IsVideo);
        Assert.Equal("h264", video.CodecName);
        Assert.Equal(1080, video.DisplayWidth);
        Assert.Equal(1920, video.DisplayHeight);
        Assert.False(video.IsAttachedPicture);
        Assert.Equal(12.5, output.Format!.DurationSeconds);
        Assert.StartsWith("mov,mp4", output.Format.FormatName);
    }

    // ffprobe prints far more than this bot reads, and new versions print more still.
    [Fact]
    public void Ignores_the_fields_nobody_reads()
    {
        var video = ParseStream("""{ "codec_type": "video", "bit_rate": "2043466", "level": 40 }""");

        Assert.True(video.IsVideo);
    }

    [Fact]
    public void Reads_a_file_it_was_told_nothing_about()
    {
        var output = Parse("{ }");

        Assert.Null(output.Streams);
        Assert.Null(output.Format);
    }

    // A container that never said how long the file is; no duration is not an error.
    [Theory]
    [InlineData("N/A")]
    [InlineData("-3.000000")]
    [InlineData("")]
    public void Reads_an_unusable_duration_as_zero(string duration)
    {
        var output = Parse($$"""{ "format": { "duration": "{{duration}}" } }""");

        Assert.Equal(0, output.Format!.DurationSeconds);
    }

    [Fact]
    public void Reads_a_missing_duration_as_zero()
    {
        Assert.Equal(0, Parse("""{ "format": { } }""").Format!.DurationSeconds);
    }

    // Pixels twice as wide as tall: stored 100x100, shown 200x100.
    [Fact]
    public void Applies_non_square_pixels_to_the_display_size()
    {
        var video = ParseStream("""{ "width": 100, "height": 100, "sample_aspect_ratio": "2:1" }""");

        Assert.Equal(200, video.DisplayWidth);
        Assert.Equal(100, video.DisplayHeight);
    }

    [Theory]
    [InlineData(""" "sample_aspect_ratio": "1:1", """)]
    [InlineData(""" "sample_aspect_ratio": "N/A", """)]
    [InlineData(""" "sample_aspect_ratio": "0:1", """)]
    [InlineData("")]
    public void Treats_a_pixel_ratio_it_cannot_use_as_square(string sampleAspectRatio)
    {
        var video = ParseStream($$"""{ {{sampleAspectRatio}} "width": 120, "height": 80 }""");

        Assert.Equal(120, video.DisplayWidth);
        Assert.Equal(80, video.DisplayHeight);
    }

    [Theory]
    [InlineData(-90, 80, 120)]
    [InlineData(90, 80, 120)]
    [InlineData(270, 80, 120)]
    [InlineData(180, 120, 80)]
    [InlineData(0, 120, 80)]
    public void Swaps_the_display_size_of_a_video_stored_sideways(
        int rotation, int expectedWidth, int expectedHeight)
    {
        var video = ParseStream(
            $$"""
              {
                "width": 120,
                "height": 80,
                "side_data_list": [ { "side_data_type": "Display Matrix", "rotation": {{rotation}} } ]
              }
              """);

        Assert.Equal(expectedWidth, video.DisplayWidth);
        Assert.Equal(expectedHeight, video.DisplayHeight);
    }

    [Fact]
    public void Reads_side_data_without_a_rotation_as_unrotated()
    {
        var video = ParseStream(
            """{ "width": 120, "height": 80, "side_data_list": [ { "side_data_type": "Stereo 3D" } ] }""");

        Assert.Equal(120, video.DisplayWidth);
    }

    [Fact]
    public void Sees_cover_art_for_what_it_is()
    {
        var cover = ParseStream("""{ "codec_type": "video", "disposition": { "attached_pic": 1 } }""");

        Assert.True(cover.IsVideo);
        Assert.True(cover.IsAttachedPicture);
    }
}
