namespace UpDownLoaderBot.Core;

/// <summary>Files one download produced, all of them inside the request's folder.</summary>
public sealed record DownloadedPost(IReadOnlyList<string> Files);
