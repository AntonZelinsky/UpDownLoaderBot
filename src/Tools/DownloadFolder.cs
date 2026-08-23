using System.Globalization;

namespace UpDownLoaderBot.Tools;

/// <summary>
///     A directory of its own for everything one request downloads — the video and whatever yt-dlp
///     leaves half-written. Disposing it removes the lot, which is the only place anything is
///     deleted: no downloader, preparer or handler has to track its own files.
/// </summary>
public sealed class DownloadFolder : IDisposable
{
    private const string Root = "downloads";

    /// <summary>
    ///     Names the folder after the moment the request arrived — <c>2026-08-21_14-05-33.482</c> — so
    ///     a folder left behind says when it happened without anything to look it up in. Sorts
    ///     chronologically as plain text, and the clock is the machine's own.
    /// </summary>
    private const string NameFormat = "yyyy-MM-dd_HH-mm-ss.fff";

    private readonly ILogger _logger;

    public DownloadFolder(ILogger logger)
    {
        _logger = logger;
        FullPath = Path.Combine(Root, DateTime.Now.ToString(NameFormat, CultureInfo.InvariantCulture));

        Directory.CreateDirectory(FullPath);

        _logger.LogInformation("Created the download folder {Folder}", FullPath);
    }

    public string FullPath { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullPath, recursive: true);

            _logger.LogInformation("Deleted the download folder {Folder}", FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete the download folder {Folder}", FullPath);
        }
    }

    /// <summary>
    ///     Removes what earlier runs left in the root: a killed process cannot clean up after itself,
    ///     and a folder whose deletion failed would otherwise stay for good.
    /// </summary>
    public static void DeleteLeftovers(ILogger logger)
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(Root))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
                logger.LogInformation("Deleted the leftover download folder {Folder}", folder);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete the leftover download folder {Folder}", folder);
            }
        }
    }
}
