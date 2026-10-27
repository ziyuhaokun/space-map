using FluentAssertions;
using Space.Map.Infrastructure.Drives;
using Xunit;

namespace Space.Map.Core.Tests;

/// <summary>
/// These exercise the real machine, so they assert invariants that hold on any
/// Windows box rather than facts about this one. The old version required at least
/// one drive to be present and the system root to exist, which is not guaranteed on
/// a CI runner with no mounted volume.
/// </summary>
public class SystemDriveServiceTests
{
    [Fact]
    public void GetDrives_NeverReturnsNullAndEntriesAreSelfConsistent()
    {
        var service = new SystemDriveService();

        var drives = service.GetDrives();

        drives.Should().NotBeNull();
        foreach (var drive in drives)
        {
            drive.RootPath.Should().NotBeNullOrWhiteSpace();
            drive.UsedPercent.Should().BeInRange(0, 100);
            drive.UsedSizeBytes.Should().BeGreaterThanOrEqualTo(0);

            if (drive.IsReady)
            {
                // A ready volume always reports a size; an unready one reports zero
                // rather than throwing, which is what the caller relies on.
                drive.TotalSizeBytes.Should().BeGreaterThan(0);
                drive.FreeSizeBytes.Should().BeInRange(0, drive.TotalSizeBytes);
            }
            else
            {
                drive.TotalSizeBytes.Should().Be(0);
                drive.UsedPercent.Should().Be(0);
            }
        }
    }

    [Fact]
    public void ResolveSystemDriveRoot_AlwaysReturnsUsablePath()
    {
        var service = new SystemDriveService();

        var root = service.ResolveSystemDriveRoot();

        // The method has a hardcoded fallback, so it must never return empty even
        // when no drive is enumerable.
        root.Should().NotBeNullOrWhiteSpace();
        Path.IsPathRooted(root).Should().BeTrue("callers treat the result as a scan root");
    }
}
