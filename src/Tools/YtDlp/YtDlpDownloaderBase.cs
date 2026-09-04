using UpDownLoaderBot.Core;

namespace UpDownLoaderBot.Tools.YtDlp;

/// <summary>
///     Site-agnostic yt-dlp runner: arguments, retries and picking the produced file out of what it
///     printed. Anything service-specific (cookies, say) comes from subclasses via
///     <see cref="AddServiceArguments" />.
/// </summary>
public abstract class YtDlpDownloaderBase : IMediaDownloader
{
    private const string Executable = "yt-dlp";

    /// <summary>Marks the copy yt-dlp writes to, as in <c>InstagramCookies.session.txt</c>.</summary>
    private const string SessionSuffix = ".session";

    /// <summary>Five minutes: one invocation covers the download itself and an ffmpeg merge.</summary>
    private const int TimeoutSeconds = 300;

    /// <summary>
    ///     A second go at a download that failed on the way to the post; one about the post itself
    ///     stops the loop instead (<see cref="YtDlpFailedException.IsFinal" />).
    /// </summary>
    private const int Attempts = 2;

    private const int RetryDelaySeconds = 5;

    /// <summary>
    ///     How far into a post the search for a video goes. Photos count towards the range and are
    ///     skipped, so a video sitting past the tenth item of a carousel is not looked for.
    /// </summary>
    private const int ItemsToScan = 10;

    /// <summary>
    ///     Prefers a progressive file that already carries audio — Instagram's DASH ladder is
    ///     VP9-only and reaches ~70 MB, past what a bot may upload. 45 MB is that 50 MB limit with
    ///     headroom; <c>&lt;?</c> keeps formats of unknown size eligible, as progressive ones are.
    /// </summary>
    private const string FormatSelector = "b[filesize<?45M]/bv*[filesize<?45M]+ba/b/bv*+ba";

    private readonly ILogger _logger;

    protected YtDlpDownloaderBase(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Left to the subclass on purpose: a downloader added for another service has to say which
    ///     links are its own, and the compiler asks.
    /// </summary>
    public abstract bool CanHandle(MediaLink link);

    /// <summary>
    ///     How yt-dlp ranks what the selector left. The codec comes before the resolution because the
    ///     preparer refuses anything but H.264 outright, so a higher rendition in another codec is not
    ///     a better one — it is an unsendable one. Then the tallest up to 1080p, then the smaller of
    ///     equal matches. A service offering one resolution at several bitrates wants a sort of its own.
    /// </summary>
    protected virtual string FormatSort => "vcodec:h264,res:1080,+size";

    /// <summary>Hook for service-specific arguments, such as <c>--cookies</c>.</summary>
    protected virtual void AddServiceArguments(IList<string> arguments)
    {
    }

    public async Task<DownloadedPost> Download(MediaLink link, string folder, CancellationToken cancellationToken)
    {
        var url = link.Url;

        // The post's id, not yt-dlp's own, so both downloaders name the same link the same way.
        var outputTemplate = Path.Combine(folder, $"{link.Id}.%(ext)s");

        Exception? lastError = null;
        var attempt = 0;

        while (attempt < Attempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                var filePath = await RunYtDlp(url, outputTemplate, cancellationToken);

                return new DownloadedPost([filePath]);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (YtDlpFailedException ex) when (ex.IsFinal)
            {
                // A second run would print the same line, and only delay a 👎 already owed.
                _logger.LogWarning("yt-dlp will not serve {Url}, not retrying: {Reason}", url, ex.Message);
                lastError = ex;
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "yt-dlp attempt {Attempt}/{Attempts} failed for {Url}", attempt, Attempts, url);
            }

            if (attempt < Attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), cancellationToken);
            }
        }

        throw new InvalidOperationException($"yt-dlp failed to download {url} after {attempt} attempt(s).", lastError);
    }

    private List<string> BuildArguments(string url, string outputTemplate)
    {
        var arguments = new List<string>
        {
            url,
            "-o", outputTemplate,
            // A carousel is a playlist that --no-playlist does not collapse, so the range is capped
            // instead. A photo among the videos raises "No video formats found", hence --ignore-errors:
            // together with --max-downloads that walks the post to its first actual video and stops.
            "-I", $"1:{ItemsToScan}",
            "--ignore-errors",
            "--max-downloads", "1",
            "-f", FormatSelector,
            "-S", FormatSort,
            // The file is named after the post, so a mirror that ran first and left one — a partial
            // write, or a whole file the preparer then refused — sits under exactly the name yt-dlp is
            // about to use, and it would reuse it and print its path as a success. That hands the
            // refused file straight back and defeats the fallback the loop exists for.
            "--force-overwrites",
            "--merge-output-format", "mp4"
        };

        AddServiceArguments(arguments);

        arguments.Add("--no-simulate");
        arguments.Add("--print");
        arguments.Add("after_move:filepath");

        return arguments;
    }

    private async Task<string> RunYtDlp(
        string url,
        string outputTemplate,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running yt-dlp for {Url}", url);

        // A non-zero exit is the normal case here: --max-downloads reaching its limit is one (101),
        // and with --ignore-errors so is a skipped photo. What it printed decides success instead.
        var arguments = BuildArguments(url, outputTemplate);

        var result = await ProcessRunner.Run(
            Executable, arguments, TimeoutSeconds, cancellationToken, throwOnNonZeroExit: false);

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            _logger.LogInformation("yt-dlp stderr: {Stderr}", result.StandardError);
        }

        // One printed line per produced file, in playlist order (see --print).
        var filePath = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(File.Exists);

        if (filePath is null)
        {
            throw new YtDlpFailedException(result.ExitCode, result.StandardOutput, result.StandardError);
        }

        _logger.LogInformation(
            "Downloaded {Url} -> {FilePath} (yt-dlp exit code {ExitCode})", url, filePath, result.ExitCode);

        return filePath;
    }

    // Falls back to searching upward from the app base directory, to find repo-root cookies/ in a
    // local run.
    protected static string? ResolveCookiesFile(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        if (File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        if (Path.IsPathRooted(configured))
        {
            return null;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, configured);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    ///     yt-dlp rewrites its cookies file on exit, which is how a refreshed session is kept — so it
    ///     works on a copy, <c>&lt;name&gt;.session&lt;ext&gt;</c>, and the deployed file stays intact
    ///     (it may even be read-only). An existing session is reused; deleting it, as the deploy does
    ///     when new cookies arrive, is what makes it start over.
    /// </summary>
    protected string PrepareCookiesFile(string deployedFile)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(deployedFile))!;
        var name = Path.GetFileNameWithoutExtension(deployedFile);
        var extension = Path.GetExtension(deployedFile);

        // Configured with a session file already — a test pointing IG_COOKIES at one, say. That file
        // is the writable copy, so deriving another level would only leave a stray
        // <name>.session.session behind.
        if (name.EndsWith(SessionSuffix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Cookies file {CookiesFile} is a session copy already; using it as it is.", deployedFile);

            return Path.GetFullPath(deployedFile);
        }

        var sessionFile = Path.Combine(directory, $"{name}{SessionSuffix}{extension}");

        try
        {
            // An empty session file, left by an interrupted write, would authenticate with nothing.
            if (File.Exists(sessionFile) && new FileInfo(sessionFile).Length > 0)
            {
                _logger.LogInformation(
                    "Reusing cookies session {SessionFile}, last written {WrittenUtc:u}.",
                    sessionFile,
                    File.GetLastWriteTimeUtc(sessionFile));

                return sessionFile;
            }

            File.Copy(deployedFile, sessionFile, overwrite: true);

            _logger.LogInformation(
                "Seeded the cookies session {SessionFile} from deployed cookies {DeployedFile}.",
                sessionFile,
                deployedFile);

            return sessionFile;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to prepare a writable cookies session next to {DeployedFile}; passing it to yt-dlp "
                + "directly, which fails if the file is read-only.",
                deployedFile);

            return deployedFile;
        }
    }
}
