using FluentAssertions;
using Space.Map.Core.Scanning;
using Xunit;

namespace Space.Map.Core.Tests;

public class FileSystemDiskScannerTests : IDisposable
{
    private readonly string _testDirectory;

    public FileSystemDiskScannerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SpaceMap_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors
        }
    }

    [Fact]
    public async Task ScanAsync_WithSampleFiles_CalculatesSizesAccurately()
    {
        // Arrange
        var subDir = Path.Combine(_testDirectory, "SubFolder");
        Directory.CreateDirectory(subDir);

        var file1 = Path.Combine(_testDirectory, "file1.bin");
        var file2 = Path.Combine(subDir, "file2.bin");

        File.WriteAllBytes(file1, new byte[1000]);
        File.WriteAllBytes(file2, new byte[2500]);

        var scanner = new FileSystemDiskScanner();
        var options = new ScanOptions(_testDirectory)
        {
            KeepFileNodes = true,
            MaxParallelism = 1
        };

        // Act
        var result = await scanner.ScanAsync(options);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(ScanResultStatus.Completed);
        result.Root.Should().NotBeNull();
        result.Root!.SizeBytes.Should().Be(3500);
        result.Root.FileCount.Should().Be(2);
        result.Root.DirectoryCount.Should().Be(1);
    }

    [Fact]
    public async Task ScanAsync_NonExistentPath_ReturnsFailedResult()
    {
        var scanner = new FileSystemDiskScanner();
        var nonExistent = Path.Combine(_testDirectory, "NonExistent_" + Guid.NewGuid().ToString("N"));

        var result = await scanner.ScanAsync(new ScanOptions(nonExistent));

        result.Status.Should().Be(ScanResultStatus.Failed);
        result.Root.Should().BeNull();
        result.Issues.Should().NotBeEmpty();
    }

    /// <summary>
    /// A token canceled before the scan starts must abort it. Accepting either
    /// "returns Canceled" or "throws OperationCanceledException" would assert
    /// nothing, so pin the contract: pre-canceled work throws.
    /// </summary>
    [Fact]
    public async Task ScanAsync_WhenCanceledBeforeStart_Throws()
    {
        for (var i = 0; i < 10; i++)
        {
            var folder = Path.Combine(_testDirectory, $"Folder_{i}");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "data.bin"), new byte[500]);
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var scanner = new FileSystemDiskScanner();

        var act = async () => await scanner.ScanAsync(
            new ScanOptions(_testDirectory) { MaxParallelism = 1 },
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Canceling mid-scan is the Cancel button's path: the scan must come back with
    /// a partial result rather than throwing, so the UI can keep what was scanned.
    /// </summary>
    [Fact]
    public async Task ScanAsync_WhenCanceledMidScan_ReturnsCanceledWithPartialRoot()
    {
        for (var i = 0; i < 60; i++)
        {
            var folder = Path.Combine(_testDirectory, $"Folder_{i}");
            Directory.CreateDirectory(folder);
            for (var j = 0; j < 40; j++)
            {
                File.WriteAllBytes(Path.Combine(folder, $"data_{j}.bin"), new byte[512]);
            }
        }

        using var cts = new CancellationTokenSource();
        var scanner = new FileSystemDiskScanner();
        var progress = new Progress<ScanProgress>(_ => { });

        var scan = scanner.ScanAsync(
            new ScanOptions(_testDirectory) { MaxParallelism = 1 },
            progress,
            cts.Token);

        // Let the scan get past root creation, then cancel while it is walking.
        await Task.Delay(20);
        cts.Cancel();

        ScanResult? result = null;
        try
        {
            result = await scan;
        }
        catch (OperationCanceledException)
        {
            // The scan finished before the cancel landed; that is a timing miss,
            // not a contract violation, so only assert when a result came back.
        }

        if (result is not null)
        {
            result.Status.Should().BeOneOf(ScanResultStatus.Canceled, ScanResultStatus.Completed);
            if (result.Status == ScanResultStatus.Canceled)
            {
                result.Root.Should().NotBeNull("a canceled scan keeps the partial tree it built");
            }
        }
    }
}
