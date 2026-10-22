using Space.Map.Core.Scanning;

namespace Space.Map.App.ViewModels;

public sealed class ScanIssueSummary(ScanIssue issue)
{
    public string Kind { get; } = issue.Kind.ToString();

    public string KindLabel { get; } = FormatKind(issue.Kind);

    public string Path { get; } = issue.Path;

    public string Message { get; } = issue.Message;

    private static string FormatKind(ScanIssueKind kind)
    {
        return kind switch
        {
            ScanIssueKind.UnauthorizedAccess => "权限不足",
            ScanIssueKind.PathTooLong => "路径过长",
            ScanIssueKind.PathNotFound => "路径不存在",
            ScanIssueKind.Io => "IO 错误",
            ScanIssueKind.ReparsePointSkipped => "跳过重解析点",
            ScanIssueKind.HiddenSkipped => "跳过隐藏项",
            ScanIssueKind.SystemSkipped => "跳过系统项",
            _ => "未知问题"
        };
    }
}
