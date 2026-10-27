namespace Space.Map.Core.Abstractions;

/// <summary>
/// Opens paths in the OS shell. Kept separate from <see cref="IClipboardService"/>
/// so this side needs nothing from a UI framework.
/// </summary>
public interface IShellActions
{
    bool TryOpenInExplorer(string path);
}

public interface IClipboardService
{
    bool TryCopyText(string text);
}
