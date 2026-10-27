using System.Windows.Media;

namespace Space.Map.App.Controls;

public static class ChartPalette
{
    /// <summary>
    /// Categorical palette. Hues are spread far enough apart to stay distinguishable
    /// side by side, and every colour keeps enough contrast against white for the
    /// on-slice percentage labels to remain legible.
    /// </summary>
    private static readonly Color[] Colors =
    [
        Color.FromRgb(0x3B, 0x82, 0xF6),
        Color.FromRgb(0x10, 0xB9, 0x81),
        Color.FromRgb(0xEF, 0x44, 0x44),
        Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromRgb(0x8B, 0x5C, 0xF6),
        Color.FromRgb(0x06, 0xB6, 0xD4),
        Color.FromRgb(0xEC, 0x48, 0x99),
        Color.FromRgb(0x84, 0xCC, 0x16)
    ];

    private static readonly SolidColorBrush[] Brushes = CreateBrushes(Colors);

    public static SolidColorBrush OtherBrush { get; } = CreateBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

    public static int Count => Colors.Length;

    public static Color GetColor(int index) => Colors[Wrap(index, Colors.Length)];

    public static SolidColorBrush GetBrush(int index) => Brushes[Wrap(index, Brushes.Length)];

    /// <summary>
    /// Non-negative modulo. Math.Abs would throw OverflowException on int.MinValue,
    /// and negative indices are legal here because callers use -1 for "other".
    /// </summary>
    private static int Wrap(int index, int length)
    {
        var wrapped = index % length;
        return wrapped < 0 ? wrapped + length : wrapped;
    }

    private static SolidColorBrush[] CreateBrushes(IReadOnlyList<Color> colors)
    {
        var brushes = new SolidColorBrush[colors.Count];
        for (var index = 0; index < colors.Count; index++)
        {
            brushes[index] = CreateBrush(colors[index]);
        }

        return brushes;
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
