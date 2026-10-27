using Microsoft.Win32;
using Space.Map.Core.Abstractions;

namespace Space.Map.Infrastructure.Platform;

public sealed class WindowsFolderPicker : IFolderPicker
{
    public string? PickFolder(string? initialDirectory, string? title = null)
    {
        var fallbackDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Prefer provided path without sync Exists (network paths can stall the UI thread).
        var startDirectory = string.IsNullOrWhiteSpace(initialDirectory)
            ? fallbackDirectory
            : initialDirectory;

        var dialog = new OpenFolderDialog
        {
            // Infrastructure has no access to the App's resource dictionaries, so the
            // caller-supplied title keeps this layer free of user-facing strings.
            Title = title ?? "Select folder",
            InitialDirectory = startDirectory,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
