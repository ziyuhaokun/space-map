namespace Space.Map.Core.Scanning;

public sealed record ScanOptions(string RootPath)
{
    public bool IncludeHidden { get; init; } = true;

    public bool IncludeSystem { get; init; } = true;

    public bool FollowReparsePoints { get; init; }

    public long? MinimumFileSizeBytes { get; init; }

    public bool KeepFileNodes { get; init; } = true;

    /// <summary>
    /// Maximum number of largest files to retain during scan (0 = disabled).
    /// Uses a bounded min-heap; memory cost is O(MaxTopFiles).
    /// </summary>
    public int MaxTopFiles { get; init; } = 100;

    /// <summary>
    /// Maximum children retained under each directory (0 = unlimited).
    /// When exceeded, the largest children are kept and the remainder is
    /// aggregated into a single <see cref="ScanNodeType.VirtualGroup"/> node
    /// so smaller subtrees can be released for GC.
    /// </summary>
    public int MaxChildrenPerDirectory { get; init; } = 64;

    /// <summary>
    /// Upper bound on <see cref="MaxParallelism"/>, shared with the scanner so both
    /// agree on where the clamp stops.
    /// </summary>
    public const int MaxParallelismLimit = 64;

    /// <summary>
    /// Maximum number of concurrent directory-scan workers (1 = single-threaded).
    /// Larger values speed up SSD/NVMe and network shares; HDDs gain little.
    /// Values outside 1..<see cref="MaxParallelismLimit"/> are clamped by the scanner.
    /// </summary>
    public int MaxParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount, 1, 8);
}
