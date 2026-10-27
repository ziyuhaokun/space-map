using System.Diagnostics;
using Space.Map.Core.Abstractions;

namespace Space.Map.Infrastructure.Platform;

public sealed class WindowsShellActions : IShellActions
{
    public bool TryOpenInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            // Trust caller path; avoid sync Exists on UI thread (slow/network paths).
            // Explorer still starts when the path is missing; caller surfaces failure via StatusText only if start throws.
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });

            return true;
        }
        catch
        {
            return false;
        }
    }
}
