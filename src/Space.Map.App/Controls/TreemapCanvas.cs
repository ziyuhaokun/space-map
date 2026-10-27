using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Space.Map.App.Services;
using Space.Map.Core.Formatting;
using Space.Map.Core.Layout;
using Space.Map.Core.Scanning;

namespace Space.Map.App.Controls;

public sealed class TreemapCanvas : FrameworkElement
{
    private static ILocalizationService Loc => LocalizationService.Instance;

    private const int MaxTiles = 64;
    private const double LayoutSizeEpsilon = 0.5;

    private static readonly ITreemapLayoutEngine<ScanNode> LayoutEngine = new SquarifiedTreemapLayoutEngine<ScanNode>();

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

        // Empty-state text is drawn from C#, so a language switch needs a repaint.
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        InvalidateVisual();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => InvalidateVisual();

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
            : $"{GetDisplayName(tile.Node)}\n{ByteSizeFormatter.Format(tile.Node.SizeBytes)}\n{tile.Node.FullPath}";
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

        var children = parent.Children;
        if (children.Count == 0 || children.All(static node => node.SizeBytes <= 0))
        {
            AddTile(parent, bounds, paletteOffset: 0);
            return;
        }

        var layoutBounds = new LayoutBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var tiles = LayoutEngine.Layout(children, static node => node.SizeBytes, layoutBounds, MaxTiles);

        if (tiles.Count == 0)
        {
            AddTile(parent, bounds, paletteOffset: 0);
            return;
        }

        foreach (var tile in tiles)
        {
            var r = new Rect(tile.Bounds.X, tile.Bounds.Y, tile.Bounds.Width, tile.Bounds.Height);
            AddTile(tile.Item, Deflate(r, 1.2), tile.PaletteIndex);
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

        var displayName = GetDisplayName(tile.Node);
        var label = bounds.Height >= 52
            ? $"{displayName}\n{ByteSizeFormatter.Format(tile.Node.SizeBytes)}"
            : displayName;

        DrawText(drawingContext, label, Deflate(bounds, 14), bounds.Width - 28, 14);
    }

    private void DrawEmptyState(DrawingContext drawingContext, Rect bounds)
    {
        DrawText(
            drawingContext,
            Loc.GetString("Treemap_EmptyState"),
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

    /// <summary>
    /// Virtual groups carry a language-neutral placeholder from the scanner; render
    /// the localized aggregate label instead.
    /// </summary>
    private static string GetDisplayName(ScanNode node)
    {
        return node.NodeType == ScanNodeType.VirtualGroup
            ? Loc.Format("NodeType_OtherFormat", node.AggregatedItemCount)
            : node.Name;
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
