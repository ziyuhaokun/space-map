using System.Collections;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Space.Map.App.Services;
using Space.Map.App.ViewModels;
using Space.Map.Core.Formatting;

namespace Space.Map.App.Controls;

public sealed class PieChartCanvas : FrameworkElement
{
    private static ILocalizationService Loc => LocalizationService.Instance;

    private const double InnerRadiusRatio = 0.62;
    private const double SliceGapDegrees = 1.0;
    private const double HoverExplodeDistance = 6;
    private const double ChartPadding = 32;
    private const double InactiveSliceOpacity = 0.42;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(PieChartCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TotalSizeBytesProperty = DependencyProperty.Register(
        nameof(TotalSizeBytes),
        typeof(long),
        typeof(PieChartCanvas),
        new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(ScanNodeSummary),
        typeof(PieChartCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private static readonly Typeface LabelTypeface = new("Segoe UI Semibold");
    private static readonly Typeface SubLabelTypeface = new("Segoe UI");
    private static readonly SolidColorBrush CenterBackgroundBrush = CreateFrozenBrush(Color.FromArgb(0xE6, 0x14, 0x1C, 0x28));
    private static readonly SolidColorBrush CenterBorderBrush = CreateFrozenBrush(Color.FromArgb(0x44, 0x4F, 0x66, 0x80));
    private static readonly SolidColorBrush TrackBrush = CreateFrozenBrush(Color.FromArgb(0x28, 0x4F, 0x66, 0x80));
    private static readonly SolidColorBrush CenterTitleBrush = CreateFrozenBrush(Color.FromRgb(0xE8, 0xEE, 0xF8));
    private static readonly SolidColorBrush CenterValueBrush = CreateFrozenBrush(Color.FromRgb(0x66, 0xA6, 0xFF));
    private static readonly SolidColorBrush CenterSubtitleBrush = CreateFrozenBrush(Color.FromRgb(0x9A, 0xA8, 0xBC));
    private static readonly SolidColorBrush EmptyStateBrush = CreateFrozenBrush(Color.FromArgb(0xCC, 0xA8, 0xB4, 0xC7));
    private static readonly Pen CenterHubPen = CreateFrozenPen(Color.FromArgb(0x44, 0x4F, 0x66, 0x80), 0.5);
    private static readonly Pen SliceSeparatorPen = CreateFrozenPen(Color.FromArgb(0x99, 0x0D, 0x18, 0x27), 0.5);
    private static readonly Pen SelectedSlicePen = CreateFrozenPen(Color.FromArgb(0xFF, 0x66, 0xA6, 0xFF), 1.5);

    private readonly List<PieSlice> _slices = [];
    private ScanNodeSummary? _hoveredItem;
    private string? _lastToolTip;
    private Point _chartCenter;
    private double _outerRadius;
    private double _innerRadius;

    public PieChartCanvas()
    {
        Cursor = Cursors.Hand;
        ClipToBounds = true;

        // Labels are drawn from C#, not DynamicResource, so a language switch
        // needs an explicit repaint.
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded can fire again after a reparent; -= first so the handler is not
        // subscribed twice (which would repaint twice per language switch).
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        InvalidateVisual();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => InvalidateVisual();

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public long TotalSizeBytes
    {
        get => (long)GetValue(TotalSizeBytesProperty);
        set => SetValue(TotalSizeBytesProperty, value);
    }

    public ScanNodeSummary? SelectedItem
    {
        get => (ScanNodeSummary?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        _slices.Clear();

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width < 80 || bounds.Height < 80)
        {
            return;
        }

        _chartCenter = new Point(bounds.Width / 2, bounds.Height / 2);
        _outerRadius = Math.Min(bounds.Width, bounds.Height) / 2 - ChartPadding;
        _innerRadius = _outerRadius * InnerRadiusRatio;

        if (_outerRadius <= 0)
        {
            return;
        }

        var entries = GetSliceEntries();
        if (entries.Count == 0)
        {
            DrawTrackRing(drawingContext);
            DrawEmptyState(drawingContext, bounds);
            return;
        }

        DrawTrackRing(drawingContext);

        // Only dim inactive slices when selection maps to a chart slice
        // (list view may select LevelItems outside ChartTopItems).
        var hasSelectedSlice = false;
        if (SelectedItem is { } selected)
        {
            foreach (var entry in entries)
            {
                if (entry.Item is not null
                    && string.Equals(entry.Item.FullPath, selected.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    hasSelectedSlice = true;
                    break;
                }
            }
        }

        var hasFocus = hasSelectedSlice || _hoveredItem is not null;
        var startAngle = -90d;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var sweep = Math.Max(0, entry.Fraction * 360d - SliceGapDegrees);
            if (sweep <= 0.2)
            {
                startAngle += entry.Fraction * 360d;
                continue;
            }

            var sliceStart = startAngle + SliceGapDegrees / 2;
            var isSelected = IsSelected(entry.Item);
            var isHovered = IsHovered(entry.Item);
            var isActive = isSelected || isHovered;
            var sliceCenter = isHovered
                ? GetExplodedCenter(_chartCenter, sliceStart, sweep, HoverExplodeDistance)
                : _chartCenter;

            var geometry = CreateDonutSlice(sliceCenter, _outerRadius, _innerRadius, sliceStart, sweep);
            var fill = entry.ColorIndex >= 0
                ? ChartPalette.GetBrush(entry.ColorIndex)
                : ChartPalette.OtherBrush;

            var opacity = hasFocus && !isActive ? InactiveSliceOpacity : 1.0;
            drawingContext.PushOpacity(opacity);
            drawingContext.DrawGeometry(fill, isSelected ? SelectedSlicePen : SliceSeparatorPen, geometry);
            drawingContext.Pop();

            _slices.Add(new PieSlice(entry.Item, sliceStart, sweep, entry.ColorIndex));
            startAngle += entry.Fraction * 360d;
        }

        DrawCenterHub(drawingContext);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var slice = HitTestSlice(e.GetPosition(this));
        var newHover = slice?.Item;

        if (!ReferenceEquals(_hoveredItem, newHover))
        {
            _hoveredItem = newHover;
            InvalidateVisual();
        }

        // MouseMove fires continuously; only touch ToolTip when the text actually
        // changes, otherwise every move rebuilds the tooltip object and its visual.
        var toolTip = newHover is { } item
            ? $"{item.Name}\n{item.Size} · {item.PercentOfRoot}\n{item.FullPath}"
            : slice?.ColorIndex == -1
                ? $"{Loc.GetString("Chart_OtherSlice")}\n{Loc.GetString("Chart_OtherSliceHint")}"
                : null;

        if (!string.Equals(toolTip, _lastToolTip, StringComparison.Ordinal))
        {
            _lastToolTip = toolTip;
            ToolTip = toolTip;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoveredItem is not null)
        {
            _hoveredItem = null;
            InvalidateVisual();
        }

        ToolTip = null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var slice = HitTestSlice(e.GetPosition(this));
        if (slice?.Item is null)
        {
            return;
        }

        SelectedItem = slice.Item;
        InvalidateVisual();
        e.Handled = true;
    }

    private void DrawTrackRing(DrawingContext drawingContext)
    {
        var trackGeometry = CreateDonutSlice(_chartCenter, _outerRadius, _innerRadius, -90, 360);
        drawingContext.DrawGeometry(TrackBrush, null, trackGeometry);
    }

    private void DrawCenterHub(DrawingContext drawingContext)
    {
        var hubRadius = _innerRadius - 2;
        drawingContext.DrawEllipse(CenterBackgroundBrush, CenterHubPen, _chartCenter, hubRadius, hubRadius);

        if (SelectedItem is { } selected)
        {
            DrawCenterText(
                drawingContext,
                TruncateLabel(selected.Name, 10),
                selected.Size,
                selected.PercentOfRoot);
            return;
        }

        DrawCenterText(
            drawingContext,
            Loc.GetString("Chart_TotalUsage"),
            ByteSizeFormatter.Format(TotalSizeBytes),
            null);
    }

    private void DrawCenterText(DrawingContext drawingContext, string title, string value, string? subtitle)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var maxWidth = Math.Max(48, (_innerRadius - 18) * 2);
        var left = _chartCenter.X - maxWidth / 2;

        var titleText = CreateCenterLine(title, 13, CenterTitleBrush, maxWidth, dpi);
        var valueText = CreateCenterLine(value, 18, CenterValueBrush, maxWidth, dpi);
        FormattedText? subtitleText = subtitle is null
            ? null
            : CreateCenterLine(subtitle, 12, CenterSubtitleBrush, maxWidth, dpi);

        var lineGap = 3d;
        var totalHeight = titleText.Height + lineGap + valueText.Height;
        if (subtitleText is not null)
        {
            totalHeight += lineGap + subtitleText.Height;
        }

        var top = _chartCenter.Y - totalHeight / 2;
        drawingContext.DrawText(titleText, new Point(left, top));

        top += titleText.Height + lineGap;
        drawingContext.DrawText(valueText, new Point(left, top));

        if (subtitleText is null)
        {
            return;
        }

        top += valueText.Height + lineGap;
        drawingContext.DrawText(subtitleText, new Point(left, top));
    }

    private static FormattedText CreateCenterLine(
        string text,
        double fontSize,
        Brush brush,
        double maxWidth,
        double pixelsPerDip)
    {
        return new FormattedText(
            text,
            Thread.CurrentThread.CurrentCulture,
            FlowDirection.LeftToRight,
            fontSize >= 16 ? LabelTypeface : SubLabelTypeface,
            fontSize,
            brush,
            pixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis
        };
    }

    private void DrawEmptyState(DrawingContext drawingContext, Rect bounds)
    {
        var hubRadius = _innerRadius - 2;
        drawingContext.DrawEllipse(CenterBackgroundBrush, CenterHubPen, _chartCenter, hubRadius, hubRadius);

        var maxWidth = Math.Max(48, (_innerRadius - 18) * 2);
        var text = new FormattedText(
            Loc.GetString("Chart_EmptyState"),
            Thread.CurrentThread.CurrentCulture,
            FlowDirection.LeftToRight,
            SubLabelTypeface,
            13,
            EmptyStateBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            TextAlignment = TextAlignment.Center
        };

        drawingContext.DrawText(
            text,
            new Point(_chartCenter.X - maxWidth / 2, _chartCenter.Y - text.Height / 2));
    }

    private List<SliceEntry> GetSliceEntries()
    {
        var items = ItemsSource?
            .Cast<object>()
            .OfType<ScanNodeSummary>()
            .Where(item => item.SizeBytes > 0)
            .OrderByDescending(item => item.SizeBytes)
            .ToList() ?? [];

        if (items.Count == 0 || TotalSizeBytes <= 0)
        {
            return [];
        }

        var entries = new List<SliceEntry>();
        long accountedBytes = 0;

        foreach (var item in items)
        {
            accountedBytes += item.SizeBytes;
            entries.Add(new SliceEntry(item, item.SizeBytes / (double)TotalSizeBytes, item.ColorIndex));
        }

        var remainingBytes = TotalSizeBytes - accountedBytes;
        if (remainingBytes > 0)
        {
            entries.Add(new SliceEntry(null, remainingBytes / (double)TotalSizeBytes, -1));
        }

        return entries;
    }

    private PieSlice? HitTestSlice(Point point)
    {
        if (_slices.Count == 0)
        {
            return null;
        }

        var dx = point.X - _chartCenter.X;
        var dy = point.Y - _chartCenter.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance > _outerRadius + HoverExplodeDistance || distance < _innerRadius - 2)
        {
            return null;
        }

        var clickAngle = NormalizeAngle(Math.Atan2(dy, dx) * 180 / Math.PI);
        foreach (var slice in _slices)
        {
            var endAngle = slice.StartAngle + slice.SweepAngle;
            if (IsAngleInSlice(clickAngle, slice.StartAngle, endAngle))
            {
                return slice;
            }
        }

        return null;
    }

    private static Point GetExplodedCenter(Point baseCenter, double startAngle, double sweep, double distance)
    {
        var midAngle = startAngle + sweep / 2;
        return PointOnCircle(baseCenter, distance, midAngle);
    }

    private bool IsSelected(ScanNodeSummary? item)
    {
        if (item is null || SelectedItem is null)
        {
            return false;
        }

        return string.Equals(item.FullPath, SelectedItem.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsHovered(ScanNodeSummary? item)
    {
        if (item is null || _hoveredItem is null)
        {
            return false;
        }

        return string.Equals(item.FullPath, _hoveredItem.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAngleInSlice(double angle, double startAngle, double endAngle)
    {
        var normalizedStart = NormalizeAngle(startAngle);
        var normalizedEnd = NormalizeAngle(endAngle);

        if (normalizedStart <= normalizedEnd)
        {
            return angle >= normalizedStart && angle < normalizedEnd;
        }

        return angle >= normalizedStart || angle < normalizedEnd;
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= 360;
        if (angle < 0)
        {
            angle += 360;
        }

        return angle;
    }

    private static Geometry CreateDonutSlice(
        Point center,
        double outerRadius,
        double innerRadius,
        double startAngleDeg,
        double sweepAngleDeg)
    {
        if (sweepAngleDeg >= 359.95)
        {
            var outer = new EllipseGeometry(center, outerRadius, outerRadius);
            var inner = new EllipseGeometry(center, innerRadius, innerRadius);
            return new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
        }

        var outerStart = PointOnCircle(center, outerRadius, startAngleDeg);
        var outerEnd = PointOnCircle(center, outerRadius, startAngleDeg + sweepAngleDeg);
        var innerEnd = PointOnCircle(center, innerRadius, startAngleDeg + sweepAngleDeg);
        var innerStart = PointOnCircle(center, innerRadius, startAngleDeg);

        var figure = new PathFigure { StartPoint = outerStart, IsClosed = true };
        figure.Segments.Add(new ArcSegment(
            outerEnd,
            new Size(outerRadius, outerRadius),
            0,
            sweepAngleDeg > 180,
            SweepDirection.Clockwise,
            true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(
            innerStart,
            new Size(innerRadius, innerRadius),
            0,
            sweepAngleDeg > 180,
            SweepDirection.Counterclockwise,
            true));

        return new PathGeometry([figure]);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDeg)
    {
        var radians = angleDeg * Math.PI / 180;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }

    private static string TruncateLabel(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        return value[..Math.Max(1, maxChars - 1)] + "…";
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

    private sealed record SliceEntry(ScanNodeSummary? Item, double Fraction, int ColorIndex);

    private sealed record PieSlice(
        ScanNodeSummary? Item,
        double StartAngle,
        double SweepAngle,
        int ColorIndex);
}
