namespace Space.Map.Core.Scanning;

public enum ScanIssueKind
{
    UnauthorizedAccess,
    PathTooLong,
    PathNotFound,
    Io,
    ReparsePointSkipped,
    HiddenSkipped,
    SystemSkipped,
    Unknown
}
