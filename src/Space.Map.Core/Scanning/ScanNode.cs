namespace Space.Map.Core.Scanning;

public sealed record ScanNode(
    string Name,
    string FullPath,
    ScanNodeType NodeType,
    long SizeBytes,
    int FileCount,
    int DirectoryCount,
    IReadOnlyList<ScanNode> Children,
    string? Extension = null,
    DateTimeOffset? ModifiedAt = null,
    ScanNodeStatus Status = ScanNodeStatus.Completed,
    string? ErrorReason = null,
    bool IsReparsePoint = false,
    int AggregatedItemCount = 0);
