using Space.Map.Core.Formatting;
using Space.Map.Core.Scanning;

namespace Space.Map.App.ViewModels;

public sealed class ScanNodeSummary(ScanNode node, long rootSizeBytes, int colorIndex = 0)
{
    public ScanNode Node { get; } = node;

    public int ColorIndex { get; } = colorIndex;

    public int Rank { get; } = colorIndex + 1;

    public string Name { get; } = node.Name;

    public string FullPath { get; } = node.FullPath;

    public string Type { get; } = FormatNodeType(node.NodeType);

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

    private static string FormatNodeType(ScanNodeType nodeType)
    {
        return nodeType switch
        {
            ScanNodeType.Drive => "磁盘",
            ScanNodeType.Directory => "目录",
            ScanNodeType.File => "文件",
            ScanNodeType.VirtualGroup => "分组",
            _ => nodeType.ToString()
        };
    }
}
