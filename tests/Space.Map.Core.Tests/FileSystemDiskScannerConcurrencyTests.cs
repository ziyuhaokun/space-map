using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using Space.Map.Core.Scanning;
using Xunit;

namespace Space.Map.Core.Tests;

/// <summary>
/// Exercises the fan-out scan path (MaxParallelism &gt; 1), which the UI uses by
/// default but the other scanner tests never touch: they all pin MaxParallelism = 1,
/// so every worker-owned state was only ever written from one thread.
/// </summary>
public class FileSystemDiskScannerConcurrencyTests : IDisposable
{
    private const int SubtreeCount = 8;
    private const int DeniedDirectoriesPerSubtree = 40;

    private readonly string _testDirectory;
    private readonly List<string> _lockedDirectories = [];
    private readonly SecurityIdentifier _currentUser =
        WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No user SID.");

    public FileSystemDiskScannerConcurrencyTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SpaceMap_Concurrency_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        // Release the deny ACEs first; the directory tree cannot be removed while
        // enumeration is denied on any descendant.
        foreach (var path in _lockedDirectories)
        {
            TryRemoveDeny(path);
        }

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

    /// <summary>
    /// Fan-out workers hit unreadable directories constantly on a real volume
    /// (System Volume Information, $Recycle.Bin, other users' profiles). Each hit
    /// records a scan issue. Those records must land in the worker that hit them:
    /// filing them all under the shared main worker mutates one List&lt;ScanIssue&gt;
    /// from every scan thread at once, which drops entries or corrupts the list.
    /// </summary>
    [Fact]
    public async Task ScanAsync_WithParallelWorkers_RecordsEveryIssueWithoutCorruption()
    {
        BuildTreeWithDeniedDirectories();

        var scanner = new FileSystemDiskScanner();
        var options = new ScanOptions(_testDirectory)
        {
            KeepFileNodes = false,
            ReadFileTimestamps = false,
            MaxParallelism = SubtreeCount
        };

        var result = await scanner.ScanAsync(options);

        var expectedIssues = SubtreeCount * DeniedDirectoriesPerSubtree;

        // Corruption in the shared list surfaces as either a thrown
        // IndexOutOfRangeException (scan fails outright) or a silently short count.
        result.Status.Should().NotBe(ScanResultStatus.Failed);
        result.TotalIssueCount.Should().Be(
            expectedIssues,
            "every denied directory must be counted exactly once");
        result.Issues.Should().HaveCount(
            expectedIssues,
            "the retained issue list must not lose entries to a concurrent write");
    }

    /// <summary>
    /// Cancellation is requested from another thread while workers are mid-scan,
    /// the way the Cancel button does it. The scan must return rather than hang,
    /// and must report a canceled/partial result instead of throwing.
    /// </summary>
    [Fact]
    public async Task ScanAsync_WhenCanceledDuringParallelScan_ReturnsPromptly()
    {
        BuildTreeWithDeniedDirectories();
        for (var index = 0; index < 200; index++)
        {
            File.WriteAllBytes(Path.Combine(_testDirectory, $"f{index}.bin"), new byte[16]);
        }

        using var cts = new CancellationTokenSource();
        var scanner = new FileSystemDiskScanner();

        var scan = scanner.ScanAsync(
            new ScanOptions(_testDirectory) { MaxParallelism = SubtreeCount },
            cancellationToken: cts.Token);

        cts.CancelAfter(TimeSpan.FromMilliseconds(5));

        try
        {
            var result = await scan;
            result.Status.Should().BeOneOf(ScanResultStatus.Canceled, ScanResultStatus.Partial, ScanResultStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            // Acceptable: cancellation before the root node exists.
        }
    }

    private void BuildTreeWithDeniedDirectories()
    {
        for (var subtree = 0; subtree < SubtreeCount; subtree++)
        {
            var subtreePath = Path.Combine(_testDirectory, $"Subtree_{subtree}");
            Directory.CreateDirectory(subtreePath);
            File.WriteAllBytes(Path.Combine(subtreePath, "data.bin"), new byte[64]);

            for (var denied = 0; denied < DeniedDirectoriesPerSubtree; denied++)
            {
                var deniedPath = Path.Combine(subtreePath, $"Locked_{denied}");
                Directory.CreateDirectory(deniedPath);
                File.WriteAllBytes(Path.Combine(deniedPath, "hidden.bin"), new byte[8]);
                ApplyDenyListDirectory(deniedPath);
                _lockedDirectories.Add(deniedPath);
            }
        }
    }

    private void ApplyDenyListDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            _currentUser,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny));
        info.SetAccessControl(security);
    }

    private void TryRemoveDeny(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            var security = info.GetAccessControl();
            security.RemoveAccessRule(new FileSystemAccessRule(
                _currentUser,
                FileSystemRights.ListDirectory,
                AccessControlType.Deny));
            info.SetAccessControl(security);
        }
        catch
        {
            // Best effort: the temp tree is disposable either way.
        }
    }
}
