namespace Space.Map.App.ViewModels;

public sealed class BreadcrumbItem(string name, int index)
{
    public string Name { get; } = name;

    public int Index { get; } = index;
}
