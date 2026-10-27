using Space.Map.Core.Scanning;

namespace Space.Map.Core.Navigation;

public sealed class TreeNavigationSession : ITreeNavigationSession
{
    private readonly List<ScanNode> _stack = [];
    private ScanNode? _root;

    public ScanNode? CurrentNode => _stack.Count > 0 ? _stack[^1] : null;

    public ScanNode? RootNode => _root;

    public bool CanNavigateUp => _stack.Count > 1;

    public IReadOnlyList<ScanNode> Breadcrumbs => _stack.AsReadOnly();

    public IReadOnlyList<ScanNode> CurrentLevelChildren
    {
        get
        {
            var current = CurrentNode;
            if (current is null)
            {
                return [];
            }

            return current.Children.Count > 0
                ? current.Children
                : [current];
        }
    }

    public event EventHandler? Navigated;

    public void SetRoot(ScanNode? root)
    {
        _root = root;
        _stack.Clear();

        if (root is not null)
        {
            _stack.Add(root);
        }

        Navigated?.Invoke(this, EventArgs.Empty);
    }

    public bool UpdatePartialRoot(ScanNode partialRoot)
    {
        ArgumentNullException.ThrowIfNull(partialRoot);

        _root = partialRoot;

        // Only follow the live snapshot while the user is still looking at the
        // root. Deeper levels are rebuilt when the scan finishes.
        if (_stack.Count > 1)
        {
            return false;
        }

        _stack.Clear();
        _stack.Add(partialRoot);
        Navigated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool DrillDown(ScanNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.NodeType == ScanNodeType.VirtualGroup)
        {
            return false;
        }

        _stack.Add(node);
        Navigated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool NavigateUp()
    {
        if (!CanNavigateUp)
        {
            return false;
        }

        _stack.RemoveAt(_stack.Count - 1);
        Navigated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool NavigateTo(ScanNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var index = _stack.FindIndex(n => ReferenceEquals(n, node));
        if (index < 0)
        {
            return false;
        }

        _stack.RemoveRange(index + 1, _stack.Count - (index + 1));
        Navigated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Reset()
    {
        _root = null;
        _stack.Clear();
        Navigated?.Invoke(this, EventArgs.Empty);
    }
}
