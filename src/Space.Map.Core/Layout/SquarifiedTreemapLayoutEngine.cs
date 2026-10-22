namespace Space.Map.Core.Layout;

/// <summary>
/// Squarified treemap layout algorithm (Bruls, Huizing, van Wijk).
/// Produces rectangle tiles with aspect ratios as close to 1:1 as possible.
/// </summary>
public sealed class SquarifiedTreemapLayoutEngine<T> : ITreemapLayoutEngine<T>
{
    public IReadOnlyList<TreemapLayoutTile<T>> Layout(
        IReadOnlyList<T> items,
        Func<T, long> sizeSelector,
        LayoutBounds bounds,
        int maxTiles = 64)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(sizeSelector);

        var tiles = new List<TreemapLayoutTile<T>>();

        if (bounds.Width < 2 || bounds.Height < 2 || items.Count == 0)
        {
            return tiles;
        }

        var validItems = items
            .Where(item => sizeSelector(item) > 0)
            .OrderByDescending(sizeSelector)
            .Take(maxTiles)
            .ToList();

        if (validItems.Count == 0)
        {
            return tiles;
        }

        var rect = bounds;
        var paletteIndex = 0;
        var start = 0;
        var count = validItems.Count;

        while (start < count)
        {
            if (rect.Width < 1 || rect.Height < 1)
            {
                break;
            }

            long remainingBytes = 0;
            for (var index = start; index < count; index++)
            {
                remainingBytes += sizeSelector(validItems[index]);
            }

            if (remainingBytes <= 0)
            {
                break;
            }

            var area = rect.Width * rect.Height;
            var side = Math.Min(rect.Width, rect.Height);
            var row = new List<T>();
            long rowBytes = 0;
            var cursor = start;

            while (cursor < count)
            {
                var next = validItems[cursor];
                var nextSize = sizeSelector(next);

                if (row.Count == 0)
                {
                    row.Add(next);
                    rowBytes = nextSize;
                    cursor++;
                    continue;
                }

                var worstBefore = WorstAspectRatio(row, rowBytes, side, area, remainingBytes, sizeSelector);
                var worstAfter = WorstAspectRatio(row, rowBytes, next, nextSize, side, area, remainingBytes, sizeSelector);
                if (worstAfter <= worstBefore)
                {
                    row.Add(next);
                    rowBytes += nextSize;
                    cursor++;
                }
                else
                {
                    break;
                }
            }

            rect = LayoutRow(row, rowBytes, remainingBytes, rect, ref paletteIndex, sizeSelector, tiles);
            start = cursor;
        }

        return tiles;
    }

    private static LayoutBounds LayoutRow(
        List<T> row,
        long rowBytes,
        long remainingBytes,
        LayoutBounds rect,
        ref int paletteIndex,
        Func<T, long> sizeSelector,
        List<TreemapLayoutTile<T>> tiles)
    {
        if (rowBytes <= 0 || remainingBytes <= 0 || row.Count == 0)
        {
            return rect;
        }

        var wider = rect.Width >= rect.Height;
        if (wider)
        {
            var stripWidth = rect.Width * (rowBytes / (double)remainingBytes);
            var y = rect.Y;
            foreach (var item in row)
            {
                var height = rect.Height * (sizeSelector(item) / (double)rowBytes);
                tiles.Add(new TreemapLayoutTile<T>(
                    item,
                    new LayoutBounds(rect.X, y, stripWidth, height),
                    paletteIndex++));
                y += height;
            }

            return new LayoutBounds(rect.X + stripWidth, rect.Y, rect.Width - stripWidth, rect.Height);
        }
        else
        {
            var stripHeight = rect.Height * (rowBytes / (double)remainingBytes);
            var x = rect.X;
            foreach (var item in row)
            {
                var width = rect.Width * (sizeSelector(item) / (double)rowBytes);
                tiles.Add(new TreemapLayoutTile<T>(
                    item,
                    new LayoutBounds(x, rect.Y, width, stripHeight),
                    paletteIndex++));
                x += width;
            }

            return new LayoutBounds(rect.X, rect.Y + stripHeight, rect.Width, rect.Height - stripHeight);
        }
    }

    private static double WorstAspectRatio(
        List<T> row,
        long rowBytes,
        double side,
        double area,
        long remainingBytes,
        Func<T, long> sizeSelector)
    {
        if (rowBytes <= 0 || side <= 0 || remainingBytes <= 0)
        {
            return double.MaxValue;
        }

        var rowArea = area * (rowBytes / (double)remainingBytes);
        var rowSide = rowArea / side;
        if (rowSide <= 0)
        {
            return double.MaxValue;
        }

        var worst = 1.0;
        foreach (var item in row)
        {
            var itemArea = rowArea * (sizeSelector(item) / (double)rowBytes);
            var itemSide = itemArea / rowSide;
            var aspect = Math.Max(itemSide / rowSide, rowSide / itemSide);
            if (aspect > worst)
            {
                worst = aspect;
            }
        }

        return worst;
    }

    private static double WorstAspectRatio(
        List<T> row,
        long rowBytes,
        T next,
        long nextBytes,
        double side,
        double area,
        long remainingBytes,
        Func<T, long> sizeSelector)
    {
        var totalRowBytes = rowBytes + nextBytes;
        if (totalRowBytes <= 0 || side <= 0 || remainingBytes <= 0)
        {
            return double.MaxValue;
        }

        var rowArea = area * (totalRowBytes / (double)remainingBytes);
        var rowSide = rowArea / side;
        if (rowSide <= 0)
        {
            return double.MaxValue;
        }

        var worst = 1.0;
        foreach (var item in row)
        {
            var itemArea = rowArea * (sizeSelector(item) / (double)totalRowBytes);
            var itemSide = itemArea / rowSide;
            var aspect = Math.Max(itemSide / rowSide, rowSide / itemSide);
            if (aspect > worst)
            {
                worst = aspect;
            }
        }

        var nextArea = rowArea * (nextBytes / (double)totalRowBytes);
        var nextSide = nextArea / rowSide;
        var nextAspect = Math.Max(nextSide / rowSide, rowSide / nextSide);
        return Math.Max(worst, nextAspect);
    }
}
