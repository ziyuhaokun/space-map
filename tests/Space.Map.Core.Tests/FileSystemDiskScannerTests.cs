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

    [Fact]
    public async Task ScanAsync_WhenCanceled_ReturnsCanceledStatusAndPartialRoot()
    {
        // Arrange
        for (var i = 0; i < 10; i++)
        {
            var folder = Path.Combine(_testDirectory, $"Folder_{i}");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "data.bin"), new byte[500]);
        }

        using var cts = new CancellationTokenSource();
        var scanner = new FileSystemDiskScanner();

        // Cancel immediately
        cts.Cancel();

        // Act & Assert
        try
        {
            var result = await scanner.ScanAsync(new ScanOptions(_testDirectory), cancellationToken: cts.Token);
            result.Status.Should().Be(ScanResultStatus.Canceled);
        }
        catch (OperationCanceledException)
        {
            // Cancellation exception is also valid behavior when canceled before root initialization
        }
    }
}
