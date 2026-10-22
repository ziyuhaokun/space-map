using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Space.Map.App.Logging;
using Space.Map.App.Services;
using Space.Map.Core.Scanning;
using System.Collections.ObjectModel;

namespace Space.Map.App.ViewModels;

/// <summary>
/// Treemap/list navigation stack, selection, breadcrumbs, and level projections.
/// Does not own scan lifecycle — receives roots from <see cref="ScanSessionViewModel"/>.
/// </summary>
public partial class WorkspaceNavigationViewModel : ObservableObject
{
    private const int ChartTopItemCount = 10;

    private readonly IShellActions _shellActions;
    private readonly Action<string> _setStatus;
    private readonly List<ScanNode> _navigationStack = [];
    private ScanNode? _scanResultRoot;

    public WorkspaceNavigationViewModel(IShellActions shellActions, Action<string> setStatus)
    {
        _shellActions = shellActions;
        _setStatus = setStatus;
    }

    public ObservableCollection<ScanNodeSummary> LevelItems { get; } = [];

    public ObservableCollection<ScanNodeSummary> ChartTopItems { get; } = [];

    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenInExplorerCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    private ScanNodeSummary? _selectedNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNavigateUp))]
    [NotifyPropertyChangedFor(nameof(HasScanResult))]
    private ScanNode? _currentTreemapNode;

    [ObservableProperty]
    private ScanNode? _selectedTreemapNode;

    public bool CanNavigateUp => _navigationStack.Count > 1;

    public bool HasScanResult => _scanResultRoot is not null;

    partial void OnSelectedNodeChanged(ScanNodeSummary? value)
    {
        OpenInExplorerCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();

        if (value is not null && !ReferenceEquals(SelectedTreemapNode, value.Node))
        {
            _selectedTreemapNode = value.Node;
            OnPropertyChanged(nameof(SelectedTreemapNode));
        }
    }

    partial void OnSelectedTreemapNodeChanged(ScanNode? value)
    {
        if (value is null)
        {
            return;
        }

        var parentSize = CurrentTreemapNode?.SizeBytes ?? value.SizeBytes;
        var summary = LevelItems.FirstOrDefault(item => ReferenceEquals(item.Node, value))
            ?? new ScanNodeSummary(value, parentSize, 0);

        if (!ReferenceEquals(SelectedNode, summary))
        {
            _selectedNode = summary;
            OnPropertyChanged(nameof(SelectedNode));
        }
    }

    [RelayCommand(CanExecute = nameof(CanNavigateUpCommand))]
    private void NavigateUp()
    {
        if (_navigationStack.Count <= 1)
        {
            return;
        }

        _navigationStack.RemoveAt(_navigationStack.Count - 1);
        NavigateToCurrentStackTop();
        NavigateUpCommand.NotifyCanExecuteChanged();
    }

    private bool CanNavigateUpCommand() => _navigationStack.Count > 1;

    [RelayCommand]
    private void NavigateToBreadcrumb(int index)
    {
        if (index < 0 || index >= _navigationStack.Count)
        {
            return;
        }

        _navigationStack.RemoveRange(index + 1, _navigationStack.Count - index - 1);
        NavigateToCurrentStackTop();
        NavigateUpCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ActivateTreemapNode(ScanNode? node)
    {
        if (node is null || !CanDrillInto(node))
        {
            return;
        }

        if (_navigationStack.Count > 0 && ReferenceEquals(node, _navigationStack[^1]))
        {
            return;
        }

        _navigationStack.Add(node);
        NavigateToCurrentStackTop();
        NavigateUpCommand.NotifyCanExecuteChanged();
        AppLog.Debug($"Treemap drill-down: {node.FullPath}");
    }

    [RelayCommand(CanExecute = nameof(CanUseShellActions))]
    private void OpenInExplorer()
    {
        var path = SelectedNode?.FullPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_shellActions.TryOpenInExplorer(path))
        {
            _setStatus("已在资源管理器中打开");
            return;
        }

        _setStatus("无法打开路径");
        AppLog.Warning($"Open in explorer failed: {path}");
    }

    [RelayCommand(CanExecute = nameof(CanUseShellActions))]
    private void CopyPath()
    {
        var path = SelectedNode?.FullPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_shellActions.TryCopyPath(path))
        {
            _setStatus("路径已复制到剪贴板");
            return;
        }

        _setStatus("复制路径失败");
        AppLog.Warning($"Copy path failed: {path}");
    }

    private bool CanUseShellActions()
    {
        var node = SelectedNode?.Node;
        return node is not null
            && node.NodeType is not ScanNodeType.VirtualGroup
            && !string.IsNullOrWhiteSpace(node.FullPath);
    }

    public void Reset()
    {
        LevelItems.Clear();
        ChartTopItems.Clear();
        Breadcrumbs.Clear();
        _navigationStack.Clear();
        _scanResultRoot = null;
        SelectedNode = null;
        SelectedTreemapNode = null;
        CurrentTreemapNode = null;
        OnPropertyChanged(nameof(CanNavigateUp));
        OnPropertyChanged(nameof(HasScanResult));
        NavigateUpCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Progressive first-level root from the scanner (may replace stack[0] while user is drilled in).
    /// </summary>
    public void ApplyPartialRoot(ScanNode partialRoot)
    {
        _scanResultRoot = partialRoot;

        if (_navigationStack.Count == 0)
        {
            _navigationStack.Add(partialRoot);
            NavigateToCurrentStackTop();
            OnPropertyChanged(nameof(HasScanResult));
            return;
        }

        _navigationStack[0] = partialRoot;

        if (_navigationStack.Count == 1)
        {
            NavigateToCurrentStackTop();
            OnPropertyChanged(nameof(HasScanResult));
            return;
        }

        if (!TryRebindNavigationStack(partialRoot))
        {
            _navigationStack.RemoveRange(1, _navigationStack.Count - 1);
            NavigateToCurrentStackTop();
        }

        OnPropertyChanged(nameof(HasScanResult));
        OnPropertyChanged(nameof(CanNavigateUp));
        NavigateUpCommand.NotifyCanExecuteChanged();
    }

    public void ApplyResultRoot(ScanNode root)
    {
        _scanResultRoot = root;
        _navigationStack.Clear();
        _navigationStack.Add(root);
        NavigateToCurrentStackTop();
        OnPropertyChanged(nameof(HasScanResult));
    }

    private bool TryRebindNavigationStack(ScanNode partialRoot)
    {
        if (_navigationStack.Count <= 1)
        {
            return true;
        }

        var rebound = new List<ScanNode> { partialRoot };
        var cursor = partialRoot;

        for (var index = 1; index < _navigationStack.Count; index++)
        {
            var expectedPath = _navigationStack[index].FullPath;
            var next = cursor.Children.FirstOrDefault(
                child => string.Equals(child.FullPath, expectedPath, StringComparison.OrdinalIgnoreCase));

            if (next is null)
            {
                return false;
            }

            rebound.Add(next);
            cursor = next;
        }

        _navigationStack.Clear();
        _navigationStack.AddRange(rebound);
        CurrentTreemapNode = _navigationStack[^1];
        UpdateBreadcrumbs();
        return true;
    }

    private void NavigateToCurrentStackTop()
    {
        if (_navigationStack.Count == 0)
        {
            CurrentTreemapNode = null;
            Breadcrumbs.Clear();
            LevelItems.Clear();
            ChartTopItems.Clear();
            SelectedNode = null;
            SelectedTreemapNode = null;
            OnPropertyChanged(nameof(CanNavigateUp));
            return;
        }

        CurrentTreemapNode = _navigationStack[^1];
        UpdateBreadcrumbs();
        RefreshLevelItemsForCurrentLevel();
        OnPropertyChanged(nameof(CanNavigateUp));
    }

    private void UpdateBreadcrumbs()
    {
        Breadcrumbs.Clear();
        for (var index = 0; index < _navigationStack.Count; index++)
        {
            Breadcrumbs.Add(new BreadcrumbItem(_navigationStack[index].Name, index));
        }
    }

    private void RefreshLevelItemsForCurrentLevel()
    {
        LevelItems.Clear();
        ChartTopItems.Clear();

        if (CurrentTreemapNode is null)
        {
            SelectedNode = null;
            SelectedTreemapNode = null;
            return;
        }

        var levelNodes = CurrentTreemapNode.Children.Count > 0
            ? CurrentTreemapNode.Children
            : (IReadOnlyList<ScanNode>)[CurrentTreemapNode];

        var parentSize = CurrentTreemapNode.SizeBytes;
        var rankedItems = levelNodes
            .Where(static node => node.SizeBytes > 0)
            .OrderByDescending(static node => node.SizeBytes)
            .ToList();

        for (var index = 0; index < rankedItems.Count; index++)
        {
            LevelItems.Add(new ScanNodeSummary(rankedItems[index], parentSize, index));
        }

        var chartCount = Math.Min(ChartTopItemCount, LevelItems.Count);
        for (var index = 0; index < chartCount; index++)
        {
            ChartTopItems.Add(LevelItems[index]);
        }

        SelectedNode = LevelItems.FirstOrDefault();
        SelectedTreemapNode = SelectedNode?.Node;
        NavigateUpCommand.NotifyCanExecuteChanged();
    }

    private static bool CanDrillInto(ScanNode node)
    {
        return node.NodeType is ScanNodeType.Drive or ScanNodeType.Directory
            && node.Children.Count > 0;
    }
}
