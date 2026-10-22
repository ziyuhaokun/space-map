namespace Space.Map.App.Services;

public interface IShellActions
{
    bool TryOpenInExplorer(string path);

    bool TryCopyPath(string path);
}
