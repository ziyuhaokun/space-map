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

    bool DrillDown(ScanNode node);

    bool NavigateUp();

    bool NavigateTo(ScanNode node);

    void Reset();

    event EventHandler? Navigated;
}
