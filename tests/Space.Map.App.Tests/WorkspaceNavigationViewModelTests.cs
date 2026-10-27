using FluentAssertions;
using Space.Map.App.Services;
using Space.Map.App.ViewModels;
using Space.Map.Core.Abstractions;
using Space.Map.Core.Navigation;
using Space.Map.Core.Scanning;
using Xunit;

namespace Space.Map.App.Tests;

/// <summary>
/// Covers the selection plumbing that the UI relies on. These were untestable before
/// the view models took their dependencies as parameters.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class WorkspaceNavigationViewModelTests
{
    private static ScanNode File(string name, long size) =>
        new(name, $@"C:\root\{name}", ScanNodeType.File, size, 1, 0, []);

    private static ScanNode Directory(string name, long size, params ScanNode[] children) =>
        new(name, $@"C:\root\{name}", ScanNodeType.Directory, size, children.Length, children.Length, children);

    private static WorkspaceNavigationViewModel CreateViewModel(ScanNode root)
    {
        var session = new TreeNavigationSession();

        var viewModel = new WorkspaceNavigationViewModel(
            new StubShellActions(),
            new StubClipboard(),
            new StubLocalization(),
            session);

        // Set the root after construction: the view model only rebuilds its
        // projections from the session's Navigated event.
        session.SetRoot(root);
        return viewModel;
    }

    /// <summary>
    /// The statistics view's largest-files grid binds to SelectedLargeFile. Selecting
    /// a top-files row used to feed the node through SelectedNode, which has no
    /// summary for it in LevelItems; the grid then cleared the selection and the
    /// detail panel snapped back to "no selection".
    /// </summary>
    [Fact]
    public void SelectingTopFile_KeepsSelectionAndDrivesDetailPanel()
    {
        var bigFile = File("big.bin", 900);
        var viewModel = CreateViewModel(Directory("root", 1000, bigFile));

        var largeFileSummary = new ScanNodeSummary(bigFile, 1000);
        viewModel.SelectedLargeFile = largeFileSummary;

        viewModel.SelectedLargeFile.Should().BeSameAs(largeFileSummary, "the grid must not be handed a different object");
        viewModel.DetailNode.Should().BeSameAs(largeFileSummary);
        viewModel.SelectedNodeName.Should().Be("big.bin");
        viewModel.HasDetailSelection.Should().BeTrue();
        viewModel.DetailIconSymbol.Should().Be("Document24", "a file must not render the folder glyph");
    }

    /// <summary>Picking a level node supersedes a top-files row.</summary>
    [Fact]
    public void SelectingLevelNode_ClearsTopFileSelection()
    {
        var bigFile = File("big.bin", 900);
        var first = Directory("first", 100);
        var second = Directory("second", 200);
        var viewModel = CreateViewModel(Directory("root", 1000, first, second));

        viewModel.SelectedLargeFile = new ScanNodeSummary(bigFile, 1000);

        // Move to a different level row: assigning the already-selected one is a
        // no-op by design (ObservableProperty only raises on change).
        var other = viewModel.LevelItems.Single(item => item.Node.Name == "first");
        viewModel.SelectedNode = other;

        viewModel.SelectedLargeFile.Should().BeNull();
        viewModel.DetailNode.Should().BeSameAs(other);
        viewModel.DetailIconSymbol.Should().Be("Folder24");
    }

    [Fact]
    public void ClearingSelection_FallsBackToPlaceholder()
    {
        var viewModel = CreateViewModel(Directory("root", 1000, File("a.bin", 10)));

        viewModel.SelectedNode = null;

        viewModel.HasDetailSelection.Should().BeFalse();
        viewModel.SelectedNodeName.Should().Be("Detail_NoSelection", "the stub returns the key itself");
    }

    /// <summary>
    /// A summary resolves its labels on read, so a language switch only has to raise
    /// a notification instead of rebuilding the collection (which lost the selection).
    /// </summary>
    [Fact]
    public void Summary_NotifiesWhenLanguageChanges()
    {
        var localization = new StubLocalization();
        var virtualGroup = new ScanNode("#others", @"C:\root\#others", ScanNodeType.VirtualGroup, 10, 1, 0, [], AggregatedItemCount: 3);
        var summary = new ScanNodeSummary(virtualGroup, 100, localization);
        var changed = new List<string?>();
        summary.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        localization.RaiseLanguageChanged();

        changed.Should().Contain(nameof(ScanNodeSummary.Name));
        changed.Should().Contain(nameof(ScanNodeSummary.Type));
    }

    private sealed class StubShellActions : IShellActions
    {
        public bool TryOpenInExplorer(string path) => true;
    }

    private sealed class StubClipboard : IClipboardService
    {
        public bool TryCopyText(string text) => true;
    }

    /// <summary>Echoes the key back so assertions stay independent of the real resources.</summary>
    private sealed class StubLocalization : ILocalizationService
    {
        public string CurrentLanguage => "en-US";

        public event EventHandler? LanguageChanged;

        public void RaiseLanguageChanged() => LanguageChanged?.Invoke(this, EventArgs.Empty);

        public void SetLanguage(string cultureCode) => RaiseLanguageChanged();

        public string ToggleLanguage() => CurrentLanguage;

        public string GetString(string key) => key;

        public string Format(string key, params object[] args) => key;
    }
}
