using System.Diagnostics;

namespace UpDownLoaderBot.Tests.Support;

/// <summary>
///     The bits of ffmpeg-driving shared by the tests that need real video files: the metadata edge
///     cases cannot be faked, so more than one test builds its fixture with it.
/// </summary>
public static class Ffmpeg
{
    /// <summary>
    ///     Why the tools cannot be used, or <c>null</c> when they can. Returned rather than logged:
    ///     each caller writes it to its own <c>ITestOutputHelper</c> before passing the test, so a
    ///     runner without ffmpeg stays green.
    /// </summary>
    public static string? Unavailable()
    {
        foreach (var tool in (string[])["ffmpeg", "ffprobe"])
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = tool,
                    ArgumentList = { "-version" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });
                process!.WaitForExit();
            }
            catch (Exception ex)
            {
                return $"{tool} is not available ({ex.Message}); test skipped.";
            }
        }

        return null;
    }

    /// <summary>Which encoders a build ships with is not fixed, so a test wanting one asks first.</summary>
    public static async Task<bool> EncoderAvailable(string encoder)
    {
        return (await Run("ffmpeg", ["-v", "error", "-hide_banner", "-encoders"])).Contains(encoder);
    }

    public static async Task<string> Run(string executable, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0
            ? stdout
            : throw new InvalidOperationException($"{executable} failed ({process.ExitCode}): {stderr}");
    }
}
