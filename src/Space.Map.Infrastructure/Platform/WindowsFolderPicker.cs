using Microsoft.Win32;
using Space.Map.Core.Abstractions;

namespace Space.Map.Infrastructure.Platform;

public sealed class WindowsFolderPicker : IFolderPicker
{
    public string? PickFolder(string? initialDirectory)
    {
        var fallbackDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Prefer provided path without sync Exists (network paths can stall the UI thread).
        var startDirectory = string.IsNullOrWhiteSpace(initialDirectory)
            ? fallbackDirectory
            : initialDirectory;

        var dialog = new OpenFolderDialog
        {
            Title = "选择要扫描的目录",
            InitialDirectory = startDirectory,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
