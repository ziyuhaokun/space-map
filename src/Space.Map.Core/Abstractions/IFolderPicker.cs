namespace Space.Map.Core.Abstractions;

public interface IFolderPicker
{
    string? PickFolder(string? initialDirectory);
}
