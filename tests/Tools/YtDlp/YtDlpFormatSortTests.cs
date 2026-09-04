using Microsoft.Extensions.Logging.Abstractions;
using UpDownLoaderBot.Core;
using UpDownLoaderBot.Tools.YtDlp;

namespace UpDownLoaderBot.Tests.Tools.YtDlp;

/// <summary>
///     Pins the one thing about <c>-S</c> that is not a preference but a requirement, because nothing
///     else writes the coupling down: <c>TelegramVideoPreparer</c> refuses every codec but H.264, so a
///     sort that ranks resolution first buys a taller file the bot cannot send. TikTok is where that
///     bites — its 720p rendition exists only in H.265.
/// </summary>
public class YtDlpFormatSortTests
{
    private static readonly Probe Downloader = new();

    [Fact]
    public void Ranks_the_codec_before_the_resolution()
    {
        var sort = Downloader.Sort;

        Assert.Contains("vcodec:h264", sort);
        Assert.Contains("res:", sort);
        Assert.True(
            sort.IndexOf("vcodec", StringComparison.Ordinal) < sort.IndexOf("res:", StringComparison.Ordinal),
            $"The codec has to outrank the resolution, but the sort reads '{sort}'.");
    }

    /// <summary>Reaches the protected default the way only a subclass can.</summary>
    private sealed class Probe : YtDlpDownloaderBase
    {
        public Probe() : base(NullLogger<Probe>.Instance)
        {
        }

        public string Sort => FormatSort;

        public override bool CanHandle(MediaLink link) => false;
    }
}
