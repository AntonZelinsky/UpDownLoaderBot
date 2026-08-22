namespace UpDownLoaderBot.Providers;

/// <summary>
///     yt-dlp finished without producing a file. <see cref="IsFinal" /> says whether another attempt
///     could change that: a failure about the post reads the same every time, a dropped connection not.
/// </summary>
public sealed class YtDlpFailedException : Exception
{
    /// <summary>
    ///     What yt-dlp says about the post rather than about getting to it. Rate limiting and an expired
    ///     login count too: neither clears within the retry delay, and the user is waiting for the 👎.
    /// </summary>
    private static readonly string[] FinalReasons =
    [
        "No video formats found",
        // Covers "Requested content…", "This post…" and "requested format is not available" alike.
        "is not available",
        "Video unavailable",
        "Unsupported URL",
        "does not exist",
        "login required",
        "requires authentication",
        "is private",
        "Restricted Video",
        "age-restricted",
        "has been removed"
    ];

    public YtDlpFailedException(int exitCode, string standardOutput, string standardError)
        : base(Describe(exitCode, standardOutput, standardError))
    {
        IsFinal = IsFinalReason(standardError);
    }

    /// <summary>Whether another attempt is pointless, so the caller can stop rather than wait.</summary>
    public bool IsFinal { get; }

    /// <summary>
    ///     Whether <b>every</b> reported failure is about the post: with <c>--ignore-errors</c> a
    ///     carousel prints one per item, and a single unrecognized line earns another attempt. Reading
    ///     prose is brittle, so the default stays the old behaviour — retry.
    /// </summary>
    private static bool IsFinalReason(string standardError)
    {
        var reported = ReportedErrors(standardError);

        return reported.Count > 0
               && reported.TrueForAll(line =>
                   FinalReasons.Any(reason => line.Contains(reason, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Only the ERROR lines: stderr also carries warnings and "please report this issue".</summary>
    private static List<string> ReportedErrors(string standardError)
    {
        return standardError
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains("ERROR:", StringComparison.Ordinal))
            .ToList();
    }

    // This is what ends up under "Failed to process", which without the reason says only 'stdout: '''.
    private static string Describe(int exitCode, string standardOutput, string standardError)
    {
        var reported = ReportedErrors(standardError).FirstOrDefault() ?? "it reported no error";

        return $"yt-dlp exited with code {exitCode} and produced no output file: {reported} "
               + $"(stdout: '{standardOutput.Trim()}')";
    }
}
