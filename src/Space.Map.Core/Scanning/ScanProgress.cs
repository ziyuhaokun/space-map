namespace Space.Map.Core.Scanning;

public sealed record ScanProgress(
    long ScannedFiles,
    long ScannedDirectories,
    long TotalBytes,
    string? CurrentPath,
    long IssueCount,
    long SkippedCount,
    TimeSpan Elapsed,
    ScanNode? PartialRoot = null);
