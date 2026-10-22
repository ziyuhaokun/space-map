using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Space.Map.Core.Formatting;
using Space.Map.Core.Scanning;

namespace Space.Map.App.Controls;

public sealed class TreemapCanvas : FrameworkElement
{
    private const int MaxTiles = 64;
    private const double LayoutSizeEpsilon = 0.5;

    public static readonly DependencyProperty RootProperty = DependencyProperty.Register(
        nameof(Root),
        typeof(ScanNode),
        typeof(TreemapCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutInvalidatingPropertyChanged));

    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(
        nameof(SelectedNode),
        typeof(ScanNode),
        typeof(TreemapCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty NodeActivatedCommandProperty = DependencyProperty.Register(
        nameof(NodeActivatedCommand),
        typeof(ICommand),
        typeof(TreemapCanvas),
        new PropertyMetadata(null));

    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly Brush BackgroundBrush = CreateFrozenBrush(Color.FromArgb(0x40, 0x0D, 0x18, 0x27));
    private static readonly Pen BackgroundBorderPen = CreateFrozenPen(Color.FromArgb(0x55, 0x4F, 0x66, 0x80), 1);
    private static readonly Pen SelectedBorderPen = CreateFrozenPen(Color.FromArgb(0xF0, 0xF8, 0xFB, 0xFF), 2);
    private static readonly Pen NormalBorderPen = CreateFrozenPen(Color.FromArgb(0x88, 0xF8, 0xFB, 0xFF), 0.8);
    private static readonly Brush LabelBrush = Brushes.White;

    private readonly List<TreemapTile> _tiles = [];
    private ScanNode? _layoutRoot;
    private Size _layoutSize;
    private bool _layoutIsEmptyState;

    public TreemapCanvas()
    {
        Cursor = Cursors.Hand;
    }

    public ScanNode? Root
    {
        get => (ScanNode?)GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    public ScanNode? SelectedNode
    {
        get => (ScanNode?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    public ICommand? NodeActivatedCommand
    {
        get => (ICommand?)GetValue(NodeActivatedCommandProperty);
        set => SetValue(NodeActivatedCommandProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width < 8 || bounds.Height < 8)
        {
            return;
        }

        drawingContext.DrawRoundedRectangle(BackgroundBrush, BackgroundBorderPen, bounds, 7, 7);

        EnsureLayout(bounds);

        if (_layoutIsEmptyState)
        {
            DrawEmptyState(drawingContext, bounds);
            return;
        }

        foreach (var tile in _tiles)
        {
            DrawTile(drawingContext, tile);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var tile = HitTestTile(e.GetPosition(this));
        ToolTip = tile is null
            ? null
            : $"{tile.Node.Name}\n{ByteSizeFormatter.Format(tile.Node.SizeBytes)}\n{tile.Node.FullPath}";
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var tile = HitTestTile(e.GetPosition(this));
        if (tile is null)
        {
            return;
        }

        SelectedNode = tile.Node;

        if (tile.Node.NodeType is ScanNodeType.Drive or ScanNodeType.Directory
            && tile.Node.Children.Count > 0
            && NodeActivatedCommand?.CanExecute(tile.Node) == true)
        {
            NodeActivatedCommand.Execute(tile.Node);
        }

        e.Handled = true;
    }

    private static void OnLayoutInvalidatingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TreemapCanvas)d).InvalidateLayoutCache();
    }

    private void InvalidateLayoutCache()
    {
        _layoutRoot = null;
        _layoutSize = default;
        _layoutIsEmptyState = false;
        _tiles.Clear();
    }

    private void EnsureLayout(Rect bounds)
    {
        var content = Deflate(bounds, 10);
        var size = new Size(content.Width, content.Height);

        if (ReferenceEquals(_layoutRoot, Root)
            && Math.Abs(_layoutSize.Width - size.Width) < LayoutSizeEpsilon
            && Math.Abs(_layoutSize.Height - size.Height) < LayoutSizeEpsilon)
        {
            return;
        }

        _layoutRoot = Root;
        _layoutSize = size;
        _tiles.Clear();
        _layoutIsEmptyState = false;

        if (Root is null || Root.SizeBytes <= 0)
        {
            _layoutIsEmptyState = true;
            return;
        }

        ComputeCurrentLevel(Root, content);
    }

    private void ComputeCurrentLevel(ScanNode parent, Rect bounds)
    {
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        var children = parent.Children
            .Where(static node => node.SizeBytes > 0)
            .OrderByDescending(static node => node.SizeBytes)
            .Take(MaxTiles)
            .ToList();

        if (children.Count == 0)
        {
            AddTile(parent, bounds, paletteOffset: 0);
            return;
        }

        long total = 0;
        foreach (var node in children)
        {
            total += node.SizeBytes;
        }

        if (total <= 0)
        {
            AddTile(parent, bounds, paletteOffset: 0);
            return;
        }

        LayoutSquarified(children, bounds);
    }

    /// <summary>
    /// Squarified treemap (Bruls, Huizing, van Wijk): pack rows to improve tile aspect ratios.
    /// Uses an index cursor — no RemoveAt(0) O(n²) shifts.
    /// </summary>
    private void LayoutSquarified(List<ScanNode> items, Rect bounds)
    {
        var rect = bounds;
        var paletteIndex = 0;
        var start = 0;
        var count = items.Count;

        while (start < count)
        {
            if (rect.Width < 1 || rect.Height < 1)
            {
                break;
            }

            long remainingBytes = 0;
            for (var index = start; index < count; index++)
            {
                remainingBytes += items[index].SizeBytes;
            }

            if (remainingBytes <= 0)
            {
                break;
            }

            var area = rect.Width * rect.Height;
            var side = Math.Min(rect.Width, rect.Height);
            var row = new List<ScanNode>();
            long rowBytes = 0;
            var cursor = start;

            while (cursor < count)
            {
                var next = items[cursor];
                if (row.Count == 0)
                {
                    row.Add(next);
                    rowBytes = next.SizeBytes;
                    cursor++;
                    continue;
                }

                var worstBefore = WorstAspectRatio(row, rowBytes, side, area, remainingBytes);
                var worstAfter = WorstAspectRatio(row, rowBytes, next, side, area, remainingBytes);
                if (worstAfter <= worstBefore)
                {
                    row.Add(next);
                    rowBytes += next.SizeBytes;
                    cursor++;
                }
                else
                {
                    break;
                }
            }

            rect = LayoutRow(row, rowBytes, remainingBytes, rect, ref paletteIndex);
            start = cursor;
        }
    }

    private Rect LayoutRow(
        List<ScanNode> row,
        long rowBytes,
        long remainingBytes,
        Rect rect,
        ref int paletteIndex)
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
            foreach (var node in row)
            {
                var height = rect.Height * (node.SizeBytes / (double)rowBytes);
                var tileBounds = Deflate(new Rect(rect.X, y, stripWidth, height), 1.2);
                AddTile(node, tileBounds, paletteIndex++);
                y += height;
            }

            return new Rect(rect.X + stripWidth, rect.Y, Math.Max(0, rect.Width - stripWidth), rect.Height);
        }

        var stripHeight = rect.Height * (rowBytes / (double)remainingBytes);
        var x = rect.X;
        foreach (var node in row)
        {
            var width = rect.Width * (node.SizeBytes / (double)rowBytes);
            var tileBounds = Deflate(new Rect(x, rect.Y, width, stripHeight), 1.2);
            AddTile(node, tileBounds, paletteIndex++);
            x += width;
        }

        return new Rect(rect.X, rect.Y + stripHeight, rect.Width, Math.Max(0, rect.Height - stripHeight));
    }

    /// <summary>
    /// Worst aspect ratio of rectangles in a candidate row placed along side length <paramref name="side"/>.
    /// Uses normalized areas so remaining children fill <paramref name="area"/>.
    /// </summary>
    private static double WorstAspectRatio(
        List<ScanNode> row,
        long rowBytes,
        double side,
        double area,
        long remainingBytes)
    {
        if (rowBytes <= 0 || side <= 0 || area <= 0 || remainingBytes <= 0 || row.Count == 0)
        {
            return double.MaxValue;
        }

        var rowArea = area * (rowBytes / (double)remainingBytes);
        var worst = 1.0;

        foreach (var node in row)
        {
            var itemArea = area * (node.SizeBytes / (double)remainingBytes);
            if (itemArea <= 0)
            {
                continue;
            }

            var ratio = Math.Max(
                (side * side * itemArea) / (rowArea * rowArea),
                (rowArea * rowArea) / (side * side * itemArea));
            if (ratio > worst)
            {
                worst = ratio;
            }
        }

        return worst;
    }

    private static double WorstAspectRatio(
        List<ScanNode> row,
        long rowBytes,
        ScanNode next,
        double side,
        double area,
        long remainingBytes)
    {
        row.Add(next);
        try
        {
            return WorstAspectRatio(row, rowBytes + next.SizeBytes, side, area, remainingBytes);
        }
        finally
        {
            row.RemoveAt(row.Count - 1);
        }
    }

    private void AddTile(ScanNode node, Rect bounds, int paletteOffset)
    {
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        _tiles.Add(new TreemapTile(node, bounds, paletteOffset));
    }

    private void DrawTile(DrawingContext drawingContext, TreemapTile tile)
    {
        var bounds = tile.Bounds;
        var brush = ChartPalette.GetBrush(tile.PaletteIndex);
        var pen = IsSelected(tile.Node) ? SelectedBorderPen : NormalBorderPen;

        drawingContext.DrawRoundedRectangle(brush, pen, bounds, 5, 5);

        if (bounds.Width < 48 || bounds.Height < 28)
        {
            return;
        }

        var label = bounds.Height >= 52
            ? $"{tile.Node.Name}\n{ByteSizeFormatter.Format(tile.Node.SizeBytes)}"
            : tile.Node.Name;

        DrawText(drawingContext, label, Deflate(bounds, 14), bounds.Width - 28, 14);
    }

    private void DrawEmptyState(DrawingContext drawingContext, Rect bounds)
    {
        DrawText(
            drawingContext,
            "选择目录并开始扫描后，这里会按占用比例显示文件夹画布。\n点击目录块可下钻查看子目录。",
            Deflate(bounds, 24),
            Math.Max(0, bounds.Width - 48),
            14);
    }

    private void DrawText(DrawingContext drawingContext, string text, Rect bounds, double maxWidth, double fontSize)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var formattedText = new FormattedText(
            text,
            Thread.CurrentThread.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            fontSize,
            LabelBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxTextHeight = Math.Max(1, bounds.Height),
            Trimming = TextTrimming.CharacterEllipsis,
            LineHeight = fontSize + 3
        };

        drawingContext.DrawText(formattedText, bounds.TopLeft);
    }

    private TreemapTile? HitTestTile(Point point)
    {
        for (var index = _tiles.Count - 1; index >= 0; index--)
        {
            if (_tiles[index].Bounds.Contains(point))
            {
                return _tiles[index];
            }
        }

        return null;
    }

    private bool IsSelected(ScanNode node)
    {
        return SelectedNode is not null
            && string.Equals(SelectedNode.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static Rect Deflate(Rect rect, double inset)
    {
        if (rect.Width <= inset * 2 || rect.Height <= inset * 2)
        {
            return rect;
        }

        return new Rect(rect.X + inset, rect.Y + inset, rect.Width - inset * 2, rect.Height - inset * 2);
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private sealed record TreemapTile(ScanNode Node, Rect Bounds, int PaletteIndex);
}
