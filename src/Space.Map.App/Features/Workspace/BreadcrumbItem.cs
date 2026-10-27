namespace Space.Map.App.ViewModels;

public sealed class BreadcrumbItem(string name, int index, bool isLast = false)
{
    public string Name { get; } = name;

    public int Index { get; } = index;

    /// <summary>The trailing crumb renders no separator after it.</summary>
    public bool IsLast { get; } = isLast;
}
