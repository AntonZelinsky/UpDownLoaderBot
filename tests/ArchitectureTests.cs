namespace UpDownLoaderBot.Tests;

/// <summary>
///     Guards the layer boundaries by reading the sources, because nothing else can: it is one
///     assembly, so the compiler has no say in who may reference Telegram.Bot.
/// </summary>
public class ArchitectureTests
{
    /// <summary>A guard that reads nothing passes every rule below for the wrong reason.</summary>
    [Fact]
    public void Sees_the_sources_it_guards()
    {
        Assert.NotEmpty(SourceFiles().Where(file => !IsBotLayer(file)).ToArray());
        Assert.NotEmpty(CoreFiles().ToArray());
        Assert.Contains("TelegramBotWorker.cs", SourceFiles().Select(file => file.Name));
    }

    /// <summary>Program.cs is exempt: it composes every layer and serves /health.</summary>
    [Fact]
    public void Only_the_bot_layer_knows_telegram()
    {
        var offenders = SourceFiles()
            .Where(file => !IsBotLayer(file) && File.ReadAllText(file.FullName).Contains("using Telegram"))
            .Select(Describe)
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>Core and nothing below it: no downloader, no ffprobe, no platform.</summary>
    [Fact]
    public void The_bot_layer_reaches_for_the_core_layer_only()
    {
        string[] offLimits =
            ["using UpDownLoaderBot.Providers", "using UpDownLoaderBot.Media", "ProcessRunner"];

        var offenders = SourceFiles()
            .Where(file => Layer(file) == "Bot")
            .SelectMany(file => File.ReadAllLines(file.FullName).Select(line => (file, line: line.Trim())))
            .Where(source => offLimits.Any(name => source.line.StartsWith(name, StringComparison.Ordinal)
                                                   || source.line.Contains(name, StringComparison.Ordinal)))
            .Select(source => $"{Describe(source.file)}: {source.line}")
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>So that adding one stays a matter of Providers/ and Program.cs alone.</summary>
    [Fact]
    public void The_core_layer_names_no_platform()
    {
        string[] platforms = ["Instagram", "TikTok"];

        var offenders = CoreFiles()
            .Where(file => platforms.Any(platform =>
                File.ReadAllText(file.FullName).Contains(platform, StringComparison.OrdinalIgnoreCase)))
            .Select(Describe)
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>
    ///     Media, holding the preparer, is the one it may reach for. ProcessRunner and DownloadFolder
    ///     live in the root namespace, which a child sees without a using, so they are named outright
    ///     rather than looked for among the imports.
    /// </summary>
    [Fact]
    public void The_core_layer_reaches_for_one_piece_of_infrastructure_only()
    {
        string[] offLimits = ["ProcessRunner", "DownloadFolder"];

        var offenders = CoreFiles()
            .SelectMany(file => File.ReadAllLines(file.FullName).Select(line => (file, line: line.Trim())))
            .Where(source =>
                (source.line.StartsWith("using UpDownLoaderBot", StringComparison.Ordinal)
                 && source.line.TrimEnd(';') != "using UpDownLoaderBot.Media")
                || offLimits.Any(name => source.line.Contains(name, StringComparison.Ordinal)))
            .Select(source => $"{Describe(source.file)}: {source.line}")
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>Found once: Folder() is called per file, and each call used to walk the tree again.</summary>
    private static readonly DirectoryInfo Root = FindSourceRoot();

    private static IEnumerable<FileInfo> CoreFiles()
    {
        return SourceFiles().Where(file => Layer(file) == "Core");
    }

    private static IEnumerable<FileInfo> SourceFiles()
    {
        // obj/ holds generated sources (AssemblyInfo, global usings) that no rule here is about.
        return Root
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    // Runs out of bin/<configuration>/<framework>, so the repository is found by walking up.
    private static DirectoryInfo FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = new DirectoryInfo(Path.Combine(directory.FullName, "src"));
            if (candidate.Exists && File.Exists(Path.Combine(candidate.FullName, "Program.cs")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find src/ above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    ///     The folder directly under src/, or the file's own name for one sitting at the top level —
    ///     Program.cs is a layer of one.
    /// </summary>
    private static string Layer(FileInfo file)
    {
        var relative = Path.GetRelativePath(Root.FullName, file.FullName);
        var separator = relative.IndexOf(Path.DirectorySeparatorChar);

        return separator < 0 ? relative : relative[..separator];
    }

    private static bool IsBotLayer(FileInfo file)
    {
        return Layer(file) is "Bot" or "Program.cs";
    }

    private static string Describe(FileInfo file)
    {
        return Path.GetRelativePath(Root.Parent!.FullName, file.FullName);
    }
}
