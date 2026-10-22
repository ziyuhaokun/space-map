namespace Space.Map.Core.Abstractions;

public interface IShellActions
{
    bool TryOpenInExplorer(string path);

    bool TryCopyPath(string path);
}
