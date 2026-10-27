using FluentAssertions;
using Space.Map.Core.Navigation;
using Space.Map.Core.Scanning;
using Xunit;

namespace Space.Map.Core.Tests;

public class TreeNavigationSessionTests
{
    [Fact]
    public void DrillDown_And_NavigateUp_BehavesCorrectly()
    {
        var session = new TreeNavigationSession();

        var child = new ScanNode("child", "C:\\root\\child", ScanNodeType.Directory, 100, 1, 0, []);
        var root = new ScanNode("root", "C:\\root", ScanNodeType.Directory, 200, 2, 1, [child]);

        session.SetRoot(root);
        session.CurrentNode.Should().Be(root);
        session.CanNavigateUp.Should().BeFalse();

        // Drill down
        var drilled = session.DrillDown(child);
        drilled.Should().BeTrue();
        session.CurrentNode.Should().Be(child);
        session.CanNavigateUp.Should().BeTrue();
        session.Breadcrumbs.Should().HaveCount(2);

        // Navigate up
        var navigatedUp = session.NavigateUp();
        navigatedUp.Should().BeTrue();
        session.CurrentNode.Should().Be(root);
        session.CanNavigateUp.Should().BeFalse();
    }

    /// <summary>
    /// While a scan runs the scanner keeps pushing newer root snapshots. Following
    /// them is what makes the map grow instead of freezing on the first report.
    /// </summary>
    [Fact]
    public void UpdatePartialRoot_AtRoot_ReplacesSnapshotAndRaisesNavigated()
    {
        var session = new TreeNavigationSession();
        var first = new ScanNode("root", "C:\\root", ScanNodeType.Directory, 100, 1, 0, []);
        var second = new ScanNode("root", "C:\\root", ScanNodeType.Directory, 900, 5, 2, []);

        session.SetRoot(first);
        var raised = 0;
        session.Navigated += (_, _) => raised++;

        var applied = session.UpdatePartialRoot(second);

        applied.Should().BeTrue();
        session.CurrentNode.Should().BeSameAs(second);
        session.RootNode.Should().BeSameAs(second);
        raised.Should().Be(1);
    }

    /// <summary>
    /// A live refresh must not yank the user out of the level they drilled into.
    /// </summary>
    [Fact]
    public void UpdatePartialRoot_WhenDrilledDown_KeepsCurrentNode()
    {
        var session = new TreeNavigationSession();
        var child = new ScanNode("child", "C:\\root\\child", ScanNodeType.Directory, 100, 1, 0, []);
        var root = new ScanNode("root", "C:\\root", ScanNodeType.Directory, 200, 2, 1, [child]);
        var newerRoot = new ScanNode("root", "C:\\root", ScanNodeType.Directory, 800, 9, 3, []);

        session.SetRoot(root);
        session.DrillDown(child);

        var applied = session.UpdatePartialRoot(newerRoot);

        applied.Should().BeFalse();
        session.CurrentNode.Should().BeSameAs(child, "the user is looking at a deeper level");
        session.Breadcrumbs.Should().HaveCount(2);
    }
}
