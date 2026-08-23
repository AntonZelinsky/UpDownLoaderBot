using System.Globalization;
using System.Text.Json.Serialization;

namespace UpDownLoaderBot.Tools.Ffprobe;

/// <summary>
///     What <c>ffprobe -print_format json -show_streams -show_format</c> prints, as much of it as this
///     bot reads. Every part is optional: a still image has no duration, an audio stream no
///     dimensions, an unrotated video no side data — so absence is modelled here instead of being
///     guarded against at every read. Fields ffprobe prints and nobody reads are ignored.
/// </summary>
public sealed record FfprobeOutput
{
    [JsonPropertyName("streams")]
    public IReadOnlyList<FfprobeStream>? Streams { get; init; }

    [JsonPropertyName("format")]
    public FfprobeFormat? Format { get; init; }
}

/// <summary>The container the streams sit in.</summary>
public sealed record FfprobeFormat
{
    /// <summary>A comma-separated list, as in <c>mov,mp4,m4a</c> — or <c>jpeg_pipe</c> for an image.</summary>
    [JsonPropertyName("format_name")]
    public string? FormatName { get; init; }

    /// <summary>Written as a string, and as <c>N/A</c> when the container never said.</summary>
    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    /// <summary>Zero when ffprobe reported nothing usable — no duration is not an error.</summary>
    public double DurationSeconds =>
        double.TryParse(Duration, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? seconds
            : 0;
}

/// <summary>One stream of the file: a video track, an audio track or a piece of cover art.</summary>
public sealed record FfprobeStream
{
    [JsonPropertyName("codec_type")]
    public string? CodecType { get; init; }

    [JsonPropertyName("codec_name")]
    public string? CodecName { get; init; }

    /// <summary>Stored size, before non-square pixels and rotation are applied.</summary>
    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    /// <summary><c>N/A</c> (and occasionally <c>0:1</c>) when the container says nothing.</summary>
    [JsonPropertyName("sample_aspect_ratio")]
    public string? SampleAspectRatio { get; init; }

    [JsonPropertyName("disposition")]
    public FfprobeDisposition? Disposition { get; init; }

    [JsonPropertyName("side_data_list")]
    public IReadOnlyList<FfprobeSideData>? SideDataList { get; init; }

    public bool IsVideo => CodecType == "video";

    /// <summary>Cover art is a video stream too, and its size would describe the file wrongly.</summary>
    public bool IsAttachedPicture => Disposition?.AttachedPicture == 1;

    /// <summary>What a player shows, with non-square pixels and rotation applied.</summary>
    public int DisplayWidth => IsSideways ? ScaledHeight : ScaledWidth;

    public int DisplayHeight => IsSideways ? ScaledWidth : ScaledHeight;

    /// <summary>A quarter turn either way, normalized — ffprobe reports it as 90 or -90.</summary>
    private int Rotation
    {
        get
        {
            foreach (var sideData in SideDataList ?? [])
            {
                if (sideData.Rotation is { } rotation)
                {
                    return ((int)Math.Round(rotation) % 360 + 360) % 360;
                }
            }

            return 0;
        }
    }

    private bool IsSideways => Rotation is 90 or 270;

    // Non-square pixels are widened rather than shortened, so no detail is lost.
    private int ScaledWidth => PixelAspectRatio is var (numerator, denominator) && numerator > denominator
        ? (int)Math.Round(Width * (double)numerator / denominator)
        : Width;

    private int ScaledHeight => PixelAspectRatio is var (numerator, denominator) && numerator < denominator
        ? (int)Math.Round(Height * (double)denominator / numerator)
        : Height;

    /// <summary>Square pixels (1:1) whenever ffprobe gave nothing to work with.</summary>
    private (int Numerator, int Denominator) PixelAspectRatio
    {
        get
        {
            if (SampleAspectRatio?.Split(':') is not [var first, var second]
                || !int.TryParse(first, CultureInfo.InvariantCulture, out var numerator)
                || !int.TryParse(second, CultureInfo.InvariantCulture, out var denominator)
                || numerator <= 0
                || denominator <= 0)
            {
                return (1, 1);
            }

            return (numerator, denominator);
        }
    }
}

public sealed record FfprobeDisposition
{
    [JsonPropertyName("attached_pic")]
    public int AttachedPicture { get; init; }
}

public sealed record FfprobeSideData
{
    /// <summary>Absent on an unrotated video, and zero is a rotation of its own.</summary>
    [JsonPropertyName("rotation")]
    public double? Rotation { get; init; }
}
