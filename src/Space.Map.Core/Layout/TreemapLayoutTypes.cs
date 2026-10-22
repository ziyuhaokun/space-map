namespace Space.Map.Core.Layout;

public readonly record struct LayoutBounds(double X, double Y, double Width, double Height);

public sealed record TreemapLayoutTile<T>(
    T Item,
    LayoutBounds Bounds,
    int PaletteIndex);

public interface ITreemapLayoutEngine<T>
{
    IReadOnlyList<TreemapLayoutTile<T>> Layout(
        IReadOnlyList<T> items,
        Func<T, long> sizeSelector,
        LayoutBounds bounds,
        int maxTiles = 64);
}
