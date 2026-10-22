using FluentAssertions;
using Space.Map.Core.Abstractions;
using Space.Map.Infrastructure.Drives;
using Xunit;

namespace Space.Map.Core.Tests;

public class SystemDriveServiceTests
{
    [Fact]
    public void GetDrives_ReturnsNonEmptyListOfDrives()
    {
        var service = new SystemDriveService();
        var drives = service.GetDrives();

        drives.Should().NotBeNull();
        drives.Should().NotBeEmpty();

        var readyDrive = drives.FirstOrDefault(d => d.IsReady);
        if (readyDrive is not null)
        {
            readyDrive.TotalSizeBytes.Should().BeGreaterThan(0);
            readyDrive.UsedPercent.Should().BeInRange(0, 100);
        }
    }

    [Fact]
    public void ResolveSystemDriveRoot_ReturnsExistingDirectory()
    {
        var service = new SystemDriveService();
        var root = service.ResolveSystemDriveRoot();

        root.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(root).Should().BeTrue();
    }
}
