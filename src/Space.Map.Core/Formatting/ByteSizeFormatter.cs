namespace Space.Map.Core.Formatting;

public static class ByteSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            // Math.Abs(long.MinValue) overflows; format magnitude without Abs.
            if (bytes == long.MinValue)
            {
                return $"-{FormatMagnitude(1UL << 63)}";
            }

            return $"-{FormatMagnitude((ulong)(-bytes))}";
        }

        return FormatMagnitude((ulong)bytes);
    }

    private static string FormatMagnitude(ulong bytes)
    {
        var size = (double)bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < Units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes:N0} {Units[unitIndex]}"
            : $"{size:N1} {Units[unitIndex]}";
    }
}
