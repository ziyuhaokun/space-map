using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Space.Map.App.Controls;
using Space.Map.App.ViewModels;
using Space.Map.Core.Scanning;
using Xunit;

namespace Space.Map.App.Tests;

/// <summary>
/// Renders the real control through the WPF compositor.
/// </summary>
/// <remarks>
/// OnRender builds a DrawingContext by hand, so defects like an unbalanced
/// Push/Pop are invisible to the compiler and to any test that only inspects
/// state. A Pop with no matching Push throws InvalidOperationException at render
/// time and nothing else catches it. These tests exist because exactly that
/// shipped in 91b3dc0: the statistics view threw as soon as a slice reached 25%.
///
/// Rendering must happen on an STA thread, which is why the tests marshal onto a
/// dedicated one rather than using the runner's thread pool.
/// </remarks>
[SupportedOSPlatform("windows")]
public class PieChartCanvasRenderTests
{
    private const long Total = 151_300_000_000L;

    private static ScanNodeSummary Item(string name, long size, int colorIndex) =>
        new(
            new ScanNode(name, @"C:\" + name, ScanNodeType.Directory, size, 1, 1, []),
            Total,
            colorIndex);

    /// <summary>
    /// A slice of 25% or more takes the isMajor path, which pushes an opacity and
    /// then drew a second, unmatched Pop. Rendering must complete.
    /// </summary>
    [Fact]
    public void Render_WithMajorSlice_DoesNotThrow()
    {
        var items = new List<ScanNodeSummary>
        {
            Item("Windows", 47_000_000_000L, 0),          // 31.1% -> isMajor
            Item("Program Files", 43_600_000_000L, 1),    // 28.8% -> isMajor
            Item("ProgramData", 17_400_000_000L, 2),
            Item("Program Files (x86)", 16_700_000_000L, 3),
        };

        var act = () => Render(items, Total);

        act.Should().NotThrow();
    }

    /// <summary>Every slice under the label threshold exercises the other branch.</summary>
    [Fact]
    public void Render_WithOnlySmallSlices_DoesNotThrow()
    {
        var items = Enumerable.Range(0, 8)
            .Select(i => Item($"Dir{i}", Total / 8 / 2, i))
            .ToList();

        var act = () => Render(items, Total);

        act.Should().NotThrow();
    }

    /// <summary>
    /// A single slice owning everything takes the full-circle path in
    /// CreatePieSlice, which returns an EllipseGeometry instead of a wedge.
    /// </summary>
    [Fact]
    public void Render_WithOneSliceCoveringEverything_DoesNotThrow()
    {
        var items = new List<ScanNodeSummary> { Item("Everything", Total, 0) };

        var act = () => Render(items, Total);

        act.Should().NotThrow();
    }

    /// <summary>
    /// Items summing to less than the total leave a remainder slice with a null
    /// item and ColorIndex -1; it must render (and must not try to label itself).
    /// </summary>
    [Fact]
    public void Render_WithRemainderSlice_DoesNotThrow()
    {
        var items = new List<ScanNodeSummary>
        {
            Item("Windows", 30_000_000_000L, 0),
            Item("Program Files", 20_000_000_000L, 1),
        };

        var act = () => Render(items, Total);

        act.Should().NotThrow();
    }

    [Fact]
    public void Render_WithNoItems_ShowsEmptyStateWithoutThrowing()
    {
        var act = () => Render([], 0);

        act.Should().NotThrow();
    }

    /// <summary>Zero total with items present must not divide by zero.</summary>
    [Fact]
    public void Render_WithZeroTotal_DoesNotThrow()
    {
        var items = new List<ScanNodeSummary> { Item("Windows", 1000, 0) };

        var act = () => Render(items, 0);

        act.Should().NotThrow();
    }

    /// <summary>
    /// Colour indices outside the palette are legal: the converter wraps them, and
    /// the old Math.Abs implementation threw on int.MinValue.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-9)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Render_WithOutOfRangeColorIndex_DoesNotThrow(int colorIndex)
    {
        var items = new List<ScanNodeSummary>
        {
            Item("Windows", 47_000_000_000L, colorIndex),
            Item("Program Files", 43_600_000_000L, 1),
        };

        var act = () => Render(items, Total);

        act.Should().NotThrow();
    }

    /// <summary>
    /// Lays the control out at a fixed size and forces a render pass. Returns once
    /// the drawing is complete, so any exception from OnRender surfaces here.
    /// </summary>
    private static void Render(IReadOnlyList<ScanNodeSummary> items, long totalBytes)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var canvas = new PieChartCanvas
                {
                    Width = 400,
                    Height = 400,
                    ItemsSource = items,
                    TotalSizeBytes = totalBytes,
                };

                // Measure/Arrange give the element an ActualWidth for OnRender.
                canvas.Measure(new Size(400, 400));
                canvas.Arrange(new Rect(0, 0, 400, 400));
                canvas.UpdateLayout();

                var target = new RenderTargetBitmap(400, 400, 96, 96, PixelFormats.Pbgra32);
                target.Render(canvas);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }
}
