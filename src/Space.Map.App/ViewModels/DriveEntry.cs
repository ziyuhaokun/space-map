using System.IO;
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

    public static DriveEntry FromDriveInfo(DriveInfo drive)
    {
        ArgumentNullException.ThrowIfNull(drive);

        var rootPath = drive.Name;
        var name = rootPath.TrimEnd('\\', '/');
        var isReady = false;
        var volumeLabel = string.Empty;
        var format = "—";
        long total = 0;
        long free = 0;

        try
        {
            isReady = drive.IsReady;
            if (isReady)
            {
                volumeLabel = drive.VolumeLabel ?? string.Empty;
                format = string.IsNullOrWhiteSpace(drive.DriveFormat) ? "—" : drive.DriveFormat;
                total = drive.TotalSize;
                free = drive.TotalFreeSpace;
            }
        }
        catch
        {
            isReady = false;
        }

        var used = total > 0 ? Math.Max(0, total - free) : 0;
        var usedPercent = total > 0 ? used * 100d / total : 0;
        var typeLabel = FormatDriveType(drive.DriveType);
        var displayName = string.IsNullOrWhiteSpace(volumeLabel)
            ? $"{name} ({typeLabel})"
            : $"{volumeLabel} ({name})";

        return new DriveEntry
        {
            RootPath = rootPath,
            Name = name,
            VolumeLabel = volumeLabel,
            DisplayName = displayName,
            DriveTypeLabel = typeLabel,
            FormatLabel = format,
            TotalSizeText = isReady ? ByteSizeFormatter.Format(total) : "—",
            FreeSizeText = isReady ? ByteSizeFormatter.Format(free) : "—",
            UsedSizeText = isReady ? ByteSizeFormatter.Format(used) : "—",
            UsedPercent = usedPercent,
            IsReady = isReady
        };
    }

    private static string FormatDriveType(DriveType driveType)
    {
        return driveType switch
        {
            DriveType.Fixed => "本地磁盘",
            DriveType.Removable => "可移动",
            DriveType.Network => "网络",
            DriveType.CDRom => "光盘",
            DriveType.Ram => "内存盘",
            _ => "未知"
        };
    }
}
