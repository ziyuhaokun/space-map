namespace Space.Map.Core.Scanning;

public sealed record ScanResult(
    Guid SessionId,
    string RootPath,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    ScanResultStatus Status,
    ScanNode? Root,
    IReadOnlyList<ScanIssue> Issues,
    ScanOptions Options,
    long TotalIssueCount,
    IReadOnlyList<ScanNode> TopFiles);
