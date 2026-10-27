using System.Windows;
using Space.Map.Core.Abstractions;

namespace Space.Map.App.Services;

/// <summary>
/// WPF's <see cref="Clipboard"/> lives here rather than in Infrastructure: it is the
/// only reason that assembly needed a UI framework reference, which forced the test
/// project onto a Windows-only target framework too.
/// </summary>
public sealed class WpfClipboardService : IClipboardService
{
    public bool TryCopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            // Another process can hold the clipboard open; the caller reports failure.
            return false;
        }
    }
}
