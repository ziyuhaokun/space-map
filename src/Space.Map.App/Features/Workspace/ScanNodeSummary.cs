using Space.Map.App.Services;
using Space.Map.Core.Formatting;
using Space.Map.Core.Scanning;

namespace Space.Map.App.ViewModels;

public sealed class ScanNodeSummary(ScanNode node, long rootSizeBytes, int colorIndex = 0)
{
    public ScanNode Node { get; } = node;

    public int ColorIndex { get; } = colorIndex;

    public int Rank { get; } = colorIndex + 1;

    public string Name { get; } = ResolveName(node);

    public string FullPath { get; } = node.FullPath;

    public string Type { get; } = LocalizationService.Instance.GetString($"NodeType_{node.NodeType}");

    public long SizeBytes { get; } = node.SizeBytes;

    public string Size { get; } = ByteSizeFormatter.Format(node.SizeBytes);

    public string PercentOfRoot { get; } = rootSizeBytes > 0
        ? $"{node.SizeBytes * 100d / rootSizeBytes:N1}%"
        : "0.0%";

    public double PercentValue { get; } = rootSizeBytes > 0
        ? node.SizeBytes * 100d / rootSizeBytes
        : 0;

    public int FileCount { get; } = node.FileCount;

    public int DirectoryCount { get; } = node.DirectoryCount;

    /// <summary>
    /// Virtual groups carry a language-neutral "#others" placeholder name from the
    /// scanner; render the localized aggregate label instead.
    /// </summary>
    private static string ResolveName(ScanNode node)
    {
        if (node.NodeType == ScanNodeType.VirtualGroup)
        {
            return LocalizationService.Instance.Format("NodeType_OtherFormat", node.AggregatedItemCount);
        }

        return node.Name;
    }
}
