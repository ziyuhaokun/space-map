using Microsoft.Win32;
using Space.Map.Core.Abstractions;

namespace Space.Map.App.Services;

/// <summary>
/// The folder picker is a UI concern (it is a WPF dialog), so it lives in the App
/// layer. Keeping it out of Infrastructure lets that assembly stay free of any UI
/// framework reference, which in turn keeps the test project on plain net10.0.
/// </summary>
public sealed class WpfFolderPicker : IFolderPicker
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
            // The caller supplies the caption so this layer stays free of UI strings.
            Title = title ?? "Select folder",
            InitialDirectory = startDirectory,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
