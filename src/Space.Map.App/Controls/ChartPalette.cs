using System.Windows.Media;

namespace Space.Map.App.Controls;

public static class ChartPalette
{
    private static readonly Color[] Colors =
    [
        Color.FromRgb(0x2F, 0x80, 0xED),
        Color.FromRgb(0x27, 0xAE, 0x60),
        Color.FromRgb(0xEB, 0x57, 0x57),
        Color.FromRgb(0xF2, 0xC9, 0x4C),
        Color.FromRgb(0x9B, 0x51, 0xE0),
        Color.FromRgb(0x00, 0x8B, 0x8B),
        Color.FromRgb(0xD9, 0x6C, 0x06),
        Color.FromRgb(0x56, 0xCC, 0xF2)
    ];

    private static readonly SolidColorBrush[] Brushes = CreateBrushes(Colors);

    public static SolidColorBrush OtherBrush { get; } = CreateBrush(Color.FromRgb(0x6B, 0x7A, 0x90));

    public static int Count => Colors.Length;

    public static Color GetColor(int index) => Colors[Math.Abs(index) % Colors.Length];

    public static SolidColorBrush GetBrush(int index) => Brushes[Math.Abs(index) % Brushes.Length];

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
