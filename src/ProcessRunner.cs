using System.Diagnostics;

namespace UpDownLoaderBot;

/// <summary>Output of a finished process.</summary>
public sealed record ProcessResult(string StandardOutput, string StandardError);

/// <summary>
///     Runs a command-line tool (yt-dlp, ffmpeg, ffprobe) with a timeout, capturing both streams.
/// </summary>
public static class ProcessRunner
{
    /// <exception cref="TimeoutException">The tool outlived <paramref name="timeoutSeconds" />.</exception>
    /// <exception cref="InvalidOperationException">The tool could not start, or exited non-zero.</exception>
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Launch directly instead of via the OS shell; required for stream redirection.
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
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new TimeoutException($"'{executable}' timed out after {timeoutSeconds}s.");
        }

        var result = new ProcessResult((await stdoutTask).Trim(), (await stderrTask).Trim());

        return process.ExitCode == 0
            ? result
            : throw new InvalidOperationException(
                $"'{executable}' exited with code {process.ExitCode}. {result.StandardError}");
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
