using Space.Map.Core.Scanning;

namespace Space.Map.Core.Navigation;

public interface ITreeNavigationSession
{
    ScanNode? CurrentNode { get; }

    ScanNode? RootNode { get; }

    bool CanNavigateUp { get; }

    IReadOnlyList<ScanNode> Breadcrumbs { get; }

    IReadOnlyList<ScanNode> CurrentLevelChildren { get; }

    void SetRoot(ScanNode? root);

    /// <summary>
    /// Replaces the root with a newer snapshot while a scan is still running.
    /// Does nothing once the user has drilled below the root, so a live refresh
    /// cannot yank them out of the level they are looking at.
    /// </summary>
    bool UpdatePartialRoot(ScanNode partialRoot);

    bool DrillDown(ScanNode node);

    bool NavigateUp();

    bool NavigateTo(ScanNode node);

    void Reset();

    event EventHandler? Navigated;
}
