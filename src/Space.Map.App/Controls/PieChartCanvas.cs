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

    /// <summary>Below this share a slice is too thin to carry a readable label.</summary>
    private const double MinLabelPercent = 11.0;

    /// <summary>Where labels sit, as a fraction of the outer radius.</summary>
    private const double LabelRadiusRatio = 0.60;

    private const double HoverExplodeDistance = 6;
    private const double ChartPadding = 24;
    private const double ShadowOffsetY = 2;

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
    private static readonly Typeface BoldLabelTypeface = new(
        new FontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);
    private static readonly Typeface SubLabelTypeface = new("Segoe UI");

    private static readonly SolidColorBrush EmptyStateBrush = CreateFrozenBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
    private static readonly SolidColorBrush SliceLabelBrush = Brushes.White;
    private static readonly SolidColorBrush DarkSliceLabelBrush = CreateFrozenBrush(Color.FromRgb(0x1F, 0x29, 0x37));
    private static readonly SolidColorBrush ShadowBrush = CreateFrozenBrush(Color.FromArgb(0x0F, 0x0F, 0x17, 0x2A));

    /// <summary>
    /// White gap between slices. Drawn as a stroke rather than by shortening each
    /// sweep: a stroke keeps the gap width constant no matter how small the slice is.
    /// </summary>
    private static readonly Pen SliceSeparatorPen = CreateFrozenPen(Colors.White, 2.5);

    private static readonly Pen SelectedSlicePen = CreateFrozenPen(Color.FromRgb(0x1F, 0x29, 0x37), 2.5);

    private readonly List<PieSlice> _slices = [];
    private ScanNodeSummary? _hoveredItem;
    private string? _lastToolTip;
    private Point _chartCenter;
    private double _outerRadius;

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

        // Leave room below for the drop shadow.
        _chartCenter = new Point(bounds.Width / 2, bounds.Height / 2 - ShadowOffsetY / 2);
        _outerRadius = Math.Min(bounds.Width, bounds.Height) / 2 - ChartPadding;

        if (_outerRadius <= 0)
        {
            return;
        }

        var entries = GetSliceEntries();
        if (entries.Count == 0)
        {
            DrawEmptyState(drawingContext, bounds);
            return;
        }

        // A soft disc under the pie so it reads as a lifted object rather than a
        // flat stamp. Drawn before the slices, offset down by ShadowOffsetY.
        drawingContext.DrawEllipse(
            ShadowBrush,
            null,
            new Point(_chartCenter.X, _chartCenter.Y + ShadowOffsetY),
            _outerRadius,
            _outerRadius);

        var startAngle = -90d;
        var labels = new List<PendingLabel>();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var sweep = entry.Fraction * 360d;
            if (sweep <= 0.2)
            {
                startAngle += sweep;
                continue;
            }

            var isSelected = IsSelected(entry.Item);
            var isHovered = IsHovered(entry.Item);
            var sliceCenter = isHovered
                ? GetExplodedCenter(_chartCenter, startAngle, sweep, HoverExplodeDistance)
                : _chartCenter;

            var geometry = CreatePieSlice(sliceCenter, _outerRadius, startAngle, sweep);
            var fill = entry.ColorIndex >= 0
                ? ChartPalette.GetBrush(entry.ColorIndex)
                : ChartPalette.OtherBrush;

            // No dimming of the other slices: on a solid pie it washes the whole
            // disc out and makes the palette look muddy. The selected slice is
            // marked by its outline instead.
            drawingContext.DrawGeometry(fill, isSelected ? SelectedSlicePen : SliceSeparatorPen, geometry);

            if (entry.Item is { } item && entry.Fraction * 100 >= MinLabelPercent)
            {
                labels.Add(new PendingLabel(
                    sliceCenter,
                    startAngle + sweep / 2,
                    entry.Fraction,
                    item));
            }

            _slices.Add(new PieSlice(entry.Item, startAngle, sweep, entry.ColorIndex));
            startAngle += sweep;
        }

        // Labels last so the separators never draw over them.
        foreach (var label in labels)
        {
            DrawSliceLabel(drawingContext, label);
        }
    }

    /// <summary>
    /// Percentage (and size, when it fits) centred on the slice's bisector. The
    /// radius is a fraction of the outer radius so the text stays inside the wedge
    /// as the chart resizes.
    /// </summary>
    private void DrawSliceLabel(DrawingContext drawingContext, PendingLabel label)
    {
        var anchor = PointOnCircle(label.Center, _outerRadius * LabelRadiusRatio, label.MidAngle);
        var percent = label.Fraction * 100;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Pick a text colour that survives on both light and dark fills.
        var brush = IsLightFill(label.Item) ? DarkSliceLabelBrush : SliceLabelBrush;
        var isMajor = percent >= 25;

        var percentText = new FormattedText(
            $"{percent:N1}%",
            Thread.CurrentThread.CurrentCulture,
            FlowDirection.LeftToRight,
            isMajor ? BoldLabelTypeface : LabelTypeface,
            isMajor ? 19 : 12,
            brush,
            dpi)
        {
            TextAlignment = TextAlignment.Center
        };


        var top = anchor.Y - percentText.Height / 2;
        drawingContext.DrawText(percentText, new Point(anchor.X - percentText.Width / 2, top));

        if (isMajor)
        {
            var sizeText = new FormattedText(
                label.Item.Size,
                Thread.CurrentThread.CurrentCulture,
                FlowDirection.LeftToRight,
                SubLabelTypeface,
                11,
                brush,
                dpi)
            {
                TextAlignment = TextAlignment.Center
            };

            // Dim the secondary line slightly so the percentage stays dominant.
            drawingContext.PushOpacity(0.8);
            drawingContext.DrawText(
                sizeText,
                new Point(anchor.X - sizeText.Width / 2, top + percentText.Height + 1));
            drawingContext.Pop();
        }

        drawingContext.Pop();
    }

    /// <summary>
    /// Yellow and cyan fills are bright enough that white text disappears on them;
    /// those slices get dark labels instead.
    /// </summary>
    private static bool IsLightFill(ScanNodeSummary item)
    {
        if (item.ColorIndex < 0)
        {
            return false;
        }

        var color = ChartPalette.GetColor(item.ColorIndex);
        // Rec. 601 luma, 0..1.
        var luma = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255d;
        return luma > 0.62;
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


    private void DrawEmptyState(DrawingContext drawingContext, Rect bounds)
    {
        var maxWidth = Math.Min(bounds.Width - 48, 320);
        var text = new FormattedText(
            Loc.GetString("Chart_EmptyState"),
            Thread.CurrentThread.CurrentCulture,
            FlowDirection.LeftToRight,
            SubLabelTypeface,
            13,
            EmptyStateBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(48, maxWidth),
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

        // Solid pie: anything inside the disc is a hit.
        if (distance > _outerRadius + HoverExplodeDistance)
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

    /// <summary>
    /// A solid wedge from the centre to the arc. The white separation between
    /// slices comes from the stroke, not from shrinking the sweep, so the gap stays
    /// the same width even for a 1% slice.
    /// </summary>
    private static Geometry CreatePieSlice(
        Point center,
        double radius,
        double startAngleDeg,
        double sweepAngleDeg)
    {
        if (sweepAngleDeg >= 359.95)
        {
            return new EllipseGeometry(center, radius, radius);
        }

        var arcStart = PointOnCircle(center, radius, startAngleDeg);
        var arcEnd = PointOnCircle(center, radius, startAngleDeg + sweepAngleDeg);

        var figure = new PathFigure { StartPoint = center, IsClosed = true };
        figure.Segments.Add(new LineSegment(arcStart, false));
        figure.Segments.Add(new ArcSegment(
            arcEnd,
            new Size(radius, radius),
            0,
            sweepAngleDeg > 180,
            SweepDirection.Clockwise,
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

    /// <summary>A slice label held back until every wedge is drawn.</summary>
    private sealed record PendingLabel(
        Point Center,
        double MidAngle,
        double Fraction,
        ScanNodeSummary Item);

    private sealed record PieSlice(
        ScanNodeSummary? Item,
        double StartAngle,
        double SweepAngle,
        int ColorIndex);
}
