namespace Space.Map.Core.Abstractions;

public enum DriveCategory
{
    Unknown,
    NoRootDirectory,
    Removable,
    Fixed,
    Network,
    CdRom,
    Ram
}

public sealed record DriveVolume(
    string RootPath,
    string Name,
    string VolumeLabel,
    string Format,
    DriveCategory Category,
    bool IsReady,
    long TotalSizeBytes,
    long FreeSizeBytes)
{
    public long UsedSizeBytes => TotalSizeBytes > 0 ? Math.Max(0, TotalSizeBytes - FreeSizeBytes) : 0;

    public double UsedPercent => TotalSizeBytes > 0 ? UsedSizeBytes * 100d / TotalSizeBytes : 0;
}
