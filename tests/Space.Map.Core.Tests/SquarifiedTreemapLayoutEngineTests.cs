using FluentAssertions;
using Space.Map.Core.Layout;
using Xunit;

namespace Space.Map.Core.Tests;

public class SquarifiedTreemapLayoutEngineTests
{
    private record TestItem(string Name, long Size);

    [Fact]
    public void Layout_WithMultipleItems_FillsBoundsWithoutNegativeDimensions()
    {
        var engine = new SquarifiedTreemapLayoutEngine<TestItem>();
        var items = new List<TestItem>
        {
            new("Item1", 600),
            new("Item2", 300),
            new("Item3", 100),
            new("Item4", 50)
        };

        var bounds = new LayoutBounds(0, 0, 800, 600);
        var tiles = engine.Layout(items, x => x.Size, bounds, maxTiles: 10);

        tiles.Should().NotBeEmpty();
        tiles.Count.Should().Be(4);

        foreach (var tile in tiles)
        {
            tile.Bounds.Width.Should().BeGreaterThan(0);
            tile.Bounds.Height.Should().BeGreaterThan(0);
            tile.Bounds.X.Should().BeInRange(0, 800);
            tile.Bounds.Y.Should().BeInRange(0, 600);
            (tile.Bounds.X + tile.Bounds.Width).Should().BeLessThanOrEqualTo(800.01);
            (tile.Bounds.Y + tile.Bounds.Height).Should().BeLessThanOrEqualTo(600.01);
        }
    }

    [Fact]
    public void Layout_EmptyItems_ReturnsEmptyTiles()
    {
        var engine = new SquarifiedTreemapLayoutEngine<TestItem>();
        var tiles = engine.Layout([], x => x.Size, new LayoutBounds(0, 0, 500, 500));
        tiles.Should().BeEmpty();
    }
}
