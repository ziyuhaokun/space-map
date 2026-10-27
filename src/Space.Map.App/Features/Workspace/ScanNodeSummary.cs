using System.ComponentModel;
using Space.Map.App.Services;
using Space.Map.Core.Formatting;
using Space.Map.Core.Scanning;

namespace Space.Map.App.ViewModels;

/// <summary>
/// Row shown for one scanned node. The two localized members (<see cref="Name"/> for
/// a virtual group, <see cref="Type"/>) are re-rendered when the language changes
/// rather than baked in at construction, so a language switch does not have to
/// rebuild every collection and restore the selection afterwards.
/// </summary>
public sealed class ScanNodeSummary : INotifyPropertyChanged
{
    private readonly long _rootSizeBytes;
    private readonly ILocalizationService _localizationService;
    private readonly LocalizationSubscription _localization;

    public ScanNodeSummary(ScanNode node, long rootSizeBytes, int colorIndex = 0)
        : this(node, rootSizeBytes, LocalizationService.Instance, colorIndex)
    {
    }

    public ScanNodeSummary(
        ScanNode node,
        long rootSizeBytes,
        ILocalizationService localization,
        int colorIndex = 0)
    {
        Node = node;
        _rootSizeBytes = rootSizeBytes;
        _localizationService = localization;
        ColorIndex = colorIndex;
        Rank = colorIndex + 1;
        _localization = new LocalizationSubscription(localization, OnLanguageChanged);
        _localization.Attach();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScanNode Node { get; }

    public int ColorIndex { get; }

    public int Rank { get; }

    public string Name => Node.NodeType == ScanNodeType.VirtualGroup
        // Virtual groups carry a language-neutral "#others" placeholder from the
        // scanner; render the localized aggregate label instead.
        ? _localizationService.Format("NodeType_OtherFormat", Node.AggregatedItemCount)
        : Node.Name;

    public string FullPath => Node.FullPath;

    public string Type => _localizationService.GetString($"NodeType_{Node.NodeType}");

    public long SizeBytes => Node.SizeBytes;

    public string Size => ByteSizeFormatter.Format(Node.SizeBytes);

    public string PercentOfRoot => _rootSizeBytes > 0
        ? $"{Node.SizeBytes * 100d / _rootSizeBytes:N1}%"
        : "0.0%";

    public double PercentValue => _rootSizeBytes > 0
        ? Node.SizeBytes * 100d / _rootSizeBytes
        : 0;

    public int FileCount => Node.FileCount;

    public int DirectoryCount => Node.DirectoryCount;

    /// <summary>Releases the language subscription early; the weak link would get there anyway.</summary>
    public void Detach() => _localization.Detach();

    private void OnLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
    }
}
