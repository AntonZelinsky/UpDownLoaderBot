using System.Diagnostics;

namespace UpDownLoaderBot.Providers;

/// <summary>
///     Generic yt-dlp runner: retry loop, process execution, and output-file resolution.
///     It knows nothing about any particular site — service-specific arguments (e.g. cookies)
///     are contributed by subclasses via <see cref="AddServiceArguments" />.
/// </summary>
public abstract class YtDlpDownloaderBase
{
    /// <summary>The yt-dlp executable name, resolved from PATH.</summary>
    private const string Executable = "yt-dlp";

    /// <summary>Directory where downloaded files are written.</summary>
    private const string OutputDirectory = "downloads";

    /// <summary>Maximum time a single yt-dlp invocation may run before it is killed.</summary>
    private const int TimeoutSeconds = 120;

    /// <summary>Number of times a failed download is attempted before giving up.</summary>
    private const int MaxRetries = 2;

    /// <summary>Delay between retry attempts, in seconds.</summary>
    private const int RetryDelaySeconds = 5;

    private readonly ILogger _logger;

    protected YtDlpDownloaderBase(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Hook for subclasses to append service-specific yt-dlp arguments (e.g. <c>--cookies</c>).
    ///     The base class contributes only generic, site-agnostic arguments.
    /// </summary>
    protected virtual void AddServiceArguments(IList<string> arguments)
    {
    }

    public async Task<string> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(OutputDirectory);
        var outputTemplate = Path.Combine(OutputDirectory, "%(id)s.%(ext)s");

        var attempts = Math.Max(1, MaxRetries);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await RunAsync(url, outputTemplate, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "yt-dlp attempt {Attempt}/{Attempts} failed for {Url}", attempt, attempts, url);
            }

            // Wait before the next attempt (skip the delay after the final one).
            if (attempt < attempts && RetryDelaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), cancellationToken);
            }
        }

        throw new InvalidOperationException($"yt-dlp failed to download {url} after {attempts} attempt(s).", lastError);
    }

    // Builds the full yt-dlp command line: generic arguments, then service-specific ones,
    // then the options that make yt-dlp print the produced file path to stdout.
    private List<string> BuildArguments(string url, string outputTemplate)
    {
        var arguments = new List<string>
        {
            // yt-dlp <url> -o "downloads/%(id)s.%(ext)s" --no-playlist
            url,
            "-o", outputTemplate,
            "--no-playlist"
        };

        AddServiceArguments(arguments);

        // Print the final file path (after any merge/post-processing) to stdout.
        arguments.Add("--no-simulate");
        arguments.Add("--print");
        arguments.Add("after_move:filepath");

        return arguments;
    }

    private async Task<string> RunAsync(string url, string outputTemplate, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Executable,
            // Capture stdout to read the produced file path (see --print below).
            RedirectStandardOutput = true,
            // Capture stderr to surface yt-dlp errors in logs and exceptions.
            RedirectStandardError = true,
            // Launch the process directly instead of via the OS shell; required for stream redirection.
            UseShellExecute = false,
            // Don't pop up a console window (relevant on Windows).
            CreateNoWindow = true
        };
        foreach (var argument in BuildArguments(url, outputTemplate))
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = psi };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        _logger.LogInformation("Running yt-dlp for {Url}", url);

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the yt-dlp process.");
        }

        // Read both streams concurrently to avoid buffer deadlocks.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"yt-dlp timed out after {TimeoutSeconds}s for {url}.");
        }

        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();

        if (!string.IsNullOrEmpty(stdout))
        {
            _logger.LogInformation("yt-dlp stdout: {Stdout}", stdout);
        }

        if (!string.IsNullOrEmpty(stderr))
        {
            _logger.LogInformation("yt-dlp stderr: {Stderr}", stderr);
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"yt-dlp exited with code {process.ExitCode}. {stderr}");
        }

        var filePath = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new InvalidOperationException($"yt-dlp did not produce a valid output file. stdout: '{stdout}'");
        }

        _logger.LogInformation("Downloaded {Url} -> {FilePath}", url, filePath);
        return filePath;
    }

    // Resolves a cookies file: use it as-is if present, otherwise search upward from the app
    // base directory for the same relative path (e.g. repo-root cookies/InstagramCookies.txt).
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

        // Absolute paths are handled above; only relative paths are searched for upward.
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
    ///     Prepares the cookies file that is actually handed to yt-dlp.
    ///     yt-dlp rewrites its cookies file on exit (that is how a refreshed session is kept), so the
    ///     deployed file is never used directly — it would fail on a read-only mount, and any renewed
    ///     session would be thrown away. Instead it is copied to a sibling
    ///     <c>&lt;name&gt;.session&lt;ext&gt;</c> file that yt-dlp keeps updating in place, so the refreshed
    ///     session survives restarts. An existing session is kept as-is; deleting it (as the deploy
    ///     does when new cookies arrive) is what makes the session start over from the deployed file.
    /// </summary>
    protected string PrepareCookiesFile(string deployedFile)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(deployedFile))!;
        var name = Path.GetFileNameWithoutExtension(deployedFile);
        var extension = Path.GetExtension(deployedFile);
        var sessionFile = Path.Combine(directory, $"{name}.session{extension}");

        try
        {
            // A session emptied by an interrupted yt-dlp write (it saves with open(..., 'w'))
            // is worse than useless: it would authenticate with no cookies at all.
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

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill yt-dlp process.");
        }
    }
}