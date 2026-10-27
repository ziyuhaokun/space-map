namespace Space.Map.Core.Scanning;

/// <summary>
/// One entry in the scanned tree.
/// </summary>
/// <remarks>
/// Deliberately lean: a full-volume scan builds hundreds of thousands of these, so
/// anything the scanner or the UI never reads is not worth its per-node cost.
/// <para>
/// Dropped for that reason: <c>Extension</c> and <c>ModifiedAt</c> (the app scans
/// with KeepFileNodes off, so they were always null),
/// <c>IsReparsePoint</c> (never read), and <c>ErrorReason</c> (never read from a
/// node; the same text already reaches the user through <see cref="ScanIssue"/>).
/// </para>
/// <para>
/// <see cref="Status"/> stays: the scanner reads it off completed children to
/// propagate <see cref="ScanNodeStatus.Partial"/> up the tree.
/// </para>
/// </remarks>
public sealed record ScanNode(
    string Name,
    string FullPath,
    ScanNodeType NodeType,
    long SizeBytes,
    int FileCount,
    int DirectoryCount,
    IReadOnlyList<ScanNode> Children,
    ScanNodeStatus Status = ScanNodeStatus.Completed,
    int AggregatedItemCount = 0);
