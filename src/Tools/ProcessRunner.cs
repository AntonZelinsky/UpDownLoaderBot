using System.Diagnostics;

namespace UpDownLoaderBot.Tools;

/// <summary>Output of a finished process, both streams trimmed.</summary>
public sealed record ProcessResult(string StandardOutput, string StandardError, int ExitCode);

/// <summary>
///     Runs a command-line tool (yt-dlp, ffmpeg, ffprobe) with a timeout, capturing both streams.
/// </summary>
public static class ProcessRunner
{
    /// <param name="arguments">One argument per element; the runtime quotes them.</param>
    /// <param name="throwOnNonZeroExit">
    ///     Off where the output still counts — yt-dlp exits non-zero when it stops at its
    ///     --max-downloads limit, or after skipping a photo entry of a carousel.
    /// </param>
    public static async Task<ProcessResult> Run(
        string executable,
        IEnumerable<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        bool throwOnNonZeroExit = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Required for stream redirection.
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = psi };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{executable}'.");
        }

        // Read both streams concurrently to avoid buffer deadlocks.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            // A tool nobody awaits must not keep writing files past the cleanup that follows.
            Kill(process);

            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"'{executable}' timed out after {timeoutSeconds}s.");
            }

            throw;
        }

        var standardOutput = await stdoutTask;
        var standardError = await stderrTask;

        var result = new ProcessResult(standardOutput.Trim(), standardError.Trim(), process.ExitCode);

        return result.ExitCode == 0 || !throwOnNonZeroExit
            ? result
            : throw new InvalidOperationException(
                $"'{executable}' exited with code {result.ExitCode}. {result.StandardError}");
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process is already gone, or not ours to kill; the timeout is reported either way.
        }
    }
}
