using Space.Map.Core.Abstractions;
using Space.Map.Core.Formatting;

namespace Space.Map.App.ViewModels;

public sealed class DriveEntry
{
    public required string RootPath { get; init; }

    public required string Name { get; init; }

    public required string VolumeLabel { get; init; }

    public required string DisplayName { get; init; }

    public required string DriveTypeLabel { get; init; }

    public required string FormatLabel { get; init; }

    public string DisplayTypeAndFormat => $"{DriveTypeLabel} · {FormatLabel}";

    public required string TotalSizeText { get; init; }

    public required string FreeSizeText { get; init; }

    public required string UsedSizeText { get; init; }

    public double UsedPercent { get; init; }

    public bool IsReady { get; init; }

    public bool CanScan => IsReady;

    public string StatusLabel => IsReady ? "就绪" : "未就绪";

    public static DriveEntry FromVolume(DriveVolume volume)
    {
        ArgumentNullException.ThrowIfNull(volume);

        var typeLabel = FormatDriveCategory(volume.Category);
        var displayName = string.IsNullOrWhiteSpace(volume.VolumeLabel)
            ? $"{volume.Name} ({typeLabel})"
            : $"{volume.VolumeLabel} ({volume.Name})";

        return new DriveEntry
        {
            RootPath = volume.RootPath,
            Name = volume.Name,
            VolumeLabel = volume.VolumeLabel,
            DisplayName = displayName,
            DriveTypeLabel = typeLabel,
            FormatLabel = volume.Format,
            TotalSizeText = volume.IsReady ? ByteSizeFormatter.Format(volume.TotalSizeBytes) : "—",
            FreeSizeText = volume.IsReady ? ByteSizeFormatter.Format(volume.FreeSizeBytes) : "—",
            UsedSizeText = volume.IsReady ? ByteSizeFormatter.Format(volume.UsedSizeBytes) : "—",
            UsedPercent = volume.UsedPercent,
            IsReady = volume.IsReady
        };
    }

    private static string FormatDriveCategory(DriveCategory category)
    {
        return category switch
        {
            DriveCategory.Fixed => "本地磁盘",
            DriveCategory.Removable => "可移动",
            DriveCategory.Network => "网络",
            DriveCategory.CdRom => "光盘",
            DriveCategory.Ram => "内存盘",
            _ => "未知"
        };
    }
}
