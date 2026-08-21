using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;

namespace UpDownLoaderBot.Tests;

public class DownloadFolderTests
{
    [Fact]
    public void Deletes_everything_it_holds_when_disposed()
    {
        string path;

        using (var folder = new DownloadFolder(NullLogger.Instance))
        {
            path = folder.FullPath;

            // A finished video and whatever yt-dlp left half-written: both go together.
            File.WriteAllText(Path.Combine(path, "video.mp4"), "video");
            File.WriteAllText(Path.Combine(path, "video.mp4.part"), "partial");

            Assert.Equal(2, Directory.GetFiles(path).Length);
        }

        Assert.False(Directory.Exists(path), "The folder outlived the request");
    }

    [Fact]
    public void Deletes_what_an_earlier_run_left_behind()
    {
        // Stands in for a process killed mid-request, or a deletion that failed.
        var orphan = new DownloadFolder(NullLogger.Instance);
        File.WriteAllText(Path.Combine(orphan.FullPath, "video.mp4"), "video");

        DownloadFolder.DeleteLeftovers(NullLogger.Instance);

        Assert.False(Directory.Exists(orphan.FullPath));
    }

    // The startup call comes before any request has created the root, and neither the image nor the
    // repository ships one — so on a fresh container this is the first thing that touches it.
    [Fact]
    public void Does_nothing_when_no_request_has_created_the_root_yet()
    {
        var emptyWorkingDirectory = Directory.CreateTempSubdirectory("downloadfolder-tests-").FullName;
        var callerDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(emptyWorkingDirectory);

            DownloadFolder.DeleteLeftovers(NullLogger.Instance);
        }
        finally
        {
            Directory.SetCurrentDirectory(callerDirectory);
            Directory.Delete(emptyWorkingDirectory, recursive: true);
        }
    }

    // The point of the name: a folder left behind after a failure says when it happened.
    [Fact]
    public void Names_the_folder_after_the_moment_the_request_arrived()
    {
        var before = DateTime.Now;

        using var folder = new DownloadFolder(NullLogger.Instance);

        Assert.True(
            DateTime.TryParseExact(
                Path.GetFileName(folder.FullPath),
                "yyyy-MM-dd_HH-mm-ss.fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var named),
            $"'{Path.GetFileName(folder.FullPath)}' does not read as a timestamp");

        Assert.InRange(named, before.AddSeconds(-1), DateTime.Now.AddSeconds(1));
    }
}
