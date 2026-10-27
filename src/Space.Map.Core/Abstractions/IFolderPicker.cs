namespace Space.Map.Core.Abstractions;

public interface IFolderPicker
{
    /// <param name="title">
    /// Dialog caption supplied by the UI layer, so this abstraction stays free of
    /// user-facing strings.
    /// </param>
    string? PickFolder(string? initialDirectory, string? title = null);
}
