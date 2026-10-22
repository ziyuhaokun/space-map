namespace Space.Map.Core.Scanning;

public sealed record ScanIssue(
    string Path,
    ScanIssueKind Kind,
    string Message);
