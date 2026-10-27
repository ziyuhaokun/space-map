using System.ComponentModel;
using Space.Map.App.Services;
using Space.Map.Core.Scanning;

namespace Space.Map.App.ViewModels;

/// <summary>
/// One row in the issues panel. The label and message are resolved on read so a
/// language switch only has to raise a notification, not rebuild the list.
/// </summary>
public sealed class ScanIssueSummary : INotifyPropertyChanged
{
    private readonly ILocalizationService _localizationService;
    private readonly LocalizationSubscription _localization;

    public ScanIssueSummary(ScanIssue issue)
        : this(issue, LocalizationService.Instance)
    {
    }

    public ScanIssueSummary(ScanIssue issue, ILocalizationService localization)
    {
        Issue = issue;
        _localizationService = localization;
        _localization = new LocalizationSubscription(localization, OnLanguageChanged);
        _localization.Attach();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScanIssue Issue { get; }

    public string KindLabel => _localizationService.GetString($"IssueKind_{Issue.Kind}");

    public string Path => Issue.Path;

    public string Message
    {
        get
        {
            // The scanner reports a machine-readable kind plus the raw OS message.
            // Prefer a localized sentence so the panel reads in both languages, and
            // fall back to the raw message when no resource matches.
            var key = $"ScanIssue_Message_{Issue.Kind}";
            var localized = _localizationService.GetString(key);
            if (!string.Equals(localized, key, StringComparison.Ordinal))
            {
                return localized;
            }

            return string.IsNullOrWhiteSpace(Issue.Message) ? string.Empty : Issue.Message;
        }
    }

    public void Detach() => _localization.Detach();

    private void OnLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KindLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
    }
}
