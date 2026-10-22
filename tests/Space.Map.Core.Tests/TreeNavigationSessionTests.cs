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
}
