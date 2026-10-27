using Space.Map.App.Services;
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

    /// <summary>Localized "Total 1.0 TB" caption; StringFormat cannot take a DynamicResource.</summary>
    public string TotalSizeDisplay => LocalizationService.Instance.Format("DriveHome_TotalFormat", TotalSizeText);

    public required string FreeSizeText { get; init; }

    public required string UsedSizeText { get; init; }

    public double UsedPercent { get; init; }

    public bool IsReady { get; init; }

    public bool CanScan => IsReady;

    public string StatusLabel => LocalizationService.Instance.GetString(
        IsReady ? "DriveHome_Ready" : "DriveHome_NotReady");

    public static DriveEntry FromVolume(DriveVolume volume)
    {
        ArgumentNullException.ThrowIfNull(volume);

        var loc = LocalizationService.Instance;
        var typeLabel = loc.GetString($"DriveCategory_{volume.Category}");
        var displayName = string.IsNullOrWhiteSpace(volume.VolumeLabel)
            ? loc.Format("DriveDisplay_NoLabel", volume.Name, typeLabel)
            : loc.Format("DriveDisplay_WithLabel", volume.VolumeLabel, volume.Name);

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
}
