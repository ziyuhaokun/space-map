using Space.Map.App.Services;
using Space.Map.Core.Scanning;

namespace Space.Map.App.ViewModels;

public sealed class ScanIssueSummary(ScanIssue issue)
{
    public string Kind { get; } = issue.Kind.ToString();

    public string KindLabel { get; } = LocalizationService.Instance.GetString($"IssueKind_{issue.Kind}");

    public string Path { get; } = issue.Path;

    public string Message { get; } = ResolveMessage(issue);

    /// <summary>
    /// The scanner reports a machine-readable <see cref="ScanIssueKind"/> plus the raw
    /// OS message. Prefer a localized sentence so the panel is readable in both
    /// languages; fall back to the raw message when there is no matching resource.
    /// </summary>
    private static string ResolveMessage(ScanIssue issue)
    {
        var key = $"ScanIssue_Message_{issue.Kind}";
        var localized = LocalizationService.Instance.GetString(key);
        if (!string.Equals(localized, key, StringComparison.Ordinal))
        {
            return localized;
        }

        return string.IsNullOrWhiteSpace(issue.Message) ? string.Empty : issue.Message;
    }
}
