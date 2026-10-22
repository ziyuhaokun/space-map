namespace Space.Map.Core.Scanning;

public interface IDiskScanner
{
    Task<ScanResult> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
