using System.ComponentModel;
using Space.Map.App.Services;
using Space.Map.Core.Abstractions;
using Space.Map.Core.Formatting;

namespace Space.Map.App.ViewModels;

/// <summary>
/// One drive card. Localized members are resolved on read and re-notified on a
/// language switch, so the drive list does not have to be re-enumerated (which also
/// meant a disk IO round trip) just to change the wording.
/// </summary>
public sealed class DriveEntry : INotifyPropertyChanged
{
    private readonly ILocalizationService _localizationService = LocalizationService.Instance;
    private readonly LocalizationSubscription _localization;

    private DriveEntry()
    {
        _localization = new LocalizationSubscription(_localizationService, OnLanguageChanged);
        _localization.Attach();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string RootPath { get; init; }

    public required string Name { get; init; }

    public required string VolumeLabel { get; init; }

    public required DriveCategory Category { get; init; }

    /// <summary>Resolved on read so it follows a language switch.</summary>
    public string DriveTypeLabel => LocalizationService.Instance.GetString($"DriveCategory_{Category}");

    public required string FormatLabel { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(VolumeLabel)
        ? LocalizationService.Instance.Format("DriveDisplay_NoLabel", Name, DriveTypeLabel)
        : LocalizationService.Instance.Format("DriveDisplay_WithLabel", VolumeLabel, Name);

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

        return new DriveEntry
        {
            RootPath = volume.RootPath,
            Name = volume.Name,
            VolumeLabel = volume.VolumeLabel,
            Category = volume.Category,
            FormatLabel = volume.Format,
            TotalSizeText = volume.IsReady ? ByteSizeFormatter.Format(volume.TotalSizeBytes) : "—",
            FreeSizeText = volume.IsReady ? ByteSizeFormatter.Format(volume.FreeSizeBytes) : "—",
            UsedSizeText = volume.IsReady ? ByteSizeFormatter.Format(volume.UsedSizeBytes) : "—",
            UsedPercent = volume.UsedPercent,
            IsReady = volume.IsReady
        };
    }

    public void Detach() => _localization.Detach();

    private void OnLanguageChanged()
    {
        // DriveTypeLabel is captured from the volume; re-resolve it so the card's
        // type line follows the language too.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DriveTypeLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTypeAndFormat)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalSizeDisplay)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
    }
}
