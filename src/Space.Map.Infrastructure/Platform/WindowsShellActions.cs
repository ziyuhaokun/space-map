using System.Diagnostics;
using System.Windows;
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

    public bool TryCopyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            Clipboard.SetText(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
