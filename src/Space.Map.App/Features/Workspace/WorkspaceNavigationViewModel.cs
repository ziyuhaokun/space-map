using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Space.Map.App.Messages;
using Space.Map.App.Services;
using Space.Map.Core.Abstractions;
using Space.Map.Core.Navigation;
using Space.Map.Core.Scanning;
using Space.Map.Infrastructure.Logging;
using System.Collections.ObjectModel;

namespace Space.Map.App.ViewModels;

/// <summary>
/// Treemap/list navigation stack, selection, breadcrumbs, and level projections.
/// Uses <see cref="ITreeNavigationSession"/> for hierarchy navigation.
/// </summary>
public partial class WorkspaceNavigationViewModel : ObservableObject
{
    private const int ChartTopItemCount = 10;

    private readonly IShellActions _shellActions;
    private readonly ITreeNavigationSession _session;

    public WorkspaceNavigationViewModel(IShellActions shellActions)
        : this(shellActions, new TreeNavigationSession())
    {
    }

    public WorkspaceNavigationViewModel(IShellActions shellActions, ITreeNavigationSession session)
    {
        _shellActions = shellActions;
        _session = session;
        _session.Navigated += OnSessionNavigated;
    }

    private void OnSessionNavigated(object? sender, EventArgs e)
    {
        CurrentTreemapNode = _session.CurrentNode;
        RebuildProjections();
        OnPropertyChanged(nameof(CanNavigateUp));
        OnPropertyChanged(nameof(HasScanResult));
        NavigateUpCommand.NotifyCanExecuteChanged();
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

    /// <summary>
    /// Row selected in the top-files list. Kept apart from <see cref="SelectedNode"/>
    /// because those files are not children of the current level, so they have no
    /// <see cref="ScanNodeSummary"/> in <see cref="LevelItems"/> to share.
    /// </summary>
    [ObservableProperty]
    private ScanNodeSummary? _selectedLargeFile;

    public bool CanNavigateUp => _session.CanNavigateUp;

    public bool HasScanResult => _session.RootNode is not null;

    /// <summary>
    /// What the detail panel describes: the level selection, or the top-files row
    /// when one is picked (those files are not part of the current level).
    /// </summary>
    public ScanNodeSummary? DetailNode => SelectedLargeFile ?? SelectedNode;

    /// <summary>
    /// Name of the detail node, or the localized placeholder when nothing is
    /// selected. A binding cannot carry a DynamicResource into TargetNullValue, so
    /// the fallback text is resolved here and refreshed on a language switch.
    /// </summary>
    public string SelectedNodeName => DetailNode?.Name
        ?? LocalizationService.Instance.GetString("Detail_NoSelection");

    partial void OnSelectedNodeChanged(ScanNodeSummary? value)
    {
        OpenInExplorerCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedNodeName));
        OnPropertyChanged(nameof(DetailNode));

        // Picking a level node supersedes a top-files row, so the panel cannot keep
        // showing a file that is no longer what the user just clicked.
        if (value is not null && SelectedLargeFile is not null)
        {
            SelectedLargeFile = null;
        }

        if (value is not null && !ReferenceEquals(SelectedTreemapNode, value.Node))
        {
            _selectedTreemapNode = value.Node;
            OnPropertyChanged(nameof(SelectedTreemapNode));
        }
    }

    partial void OnSelectedLargeFileChanged(ScanNodeSummary? value)
    {
        OpenInExplorerCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DetailNode));
        OnPropertyChanged(nameof(SelectedNodeName));
    }

    partial void OnSelectedTreemapNodeChanged(ScanNode? value)
    {
        if (value is null)
        {
            if (SelectedNode is not null)
            {
                _selectedNode = null;
                OnPropertyChanged(nameof(SelectedNode));
            }

            return;
        }

        // Only adopt a summary that already belongs to this level. Fabricating one
        // would hand a grid a SelectedItem absent from its ItemsSource, so the grid
        // would clear it again immediately and the selection would look like it
        // vanished. Nodes outside the level (the top-files list) are handled by
        // SelectedLargeFile instead.
        var summary = LevelItems.FirstOrDefault(item => ReferenceEquals(item.Node, value));
        if (summary is null || ReferenceEquals(SelectedNode, summary))
        {
            return;
        }

        _selectedNode = summary;
        OnPropertyChanged(nameof(SelectedNode));
    }

    [RelayCommand(CanExecute = nameof(CanNavigateUpCommand))]
    private void NavigateUp()
    {
        _session.NavigateUp();
    }

    private bool CanNavigateUpCommand() => _session.CanNavigateUp;

    [RelayCommand]
    private void NavigateToBreadcrumb(int index)
    {
        var breadcrumbs = _session.Breadcrumbs;
        if (index >= 0 && index < breadcrumbs.Count)
        {
            _session.NavigateTo(breadcrumbs[index]);
        }
    }

    [RelayCommand]
    private void ActivateTreemapNode(ScanNode? node)
    {
        if (node is null || !CanDrillInto(node))
        {
            return;
        }

        if (_session.DrillDown(node))
        {
            AppLog.Debug($"Treemap drill-down: {node.FullPath}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseShellActions))]
    private void OpenInExplorer()
    {
        var path = DetailNode?.FullPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_shellActions.TryOpenInExplorer(path))
        {
            WeakReferenceMessenger.Default.Send(new StatusChangedMessage(LocalizationService.Instance.GetString("Action_OpenedInExplorer")));
            return;
        }

        WeakReferenceMessenger.Default.Send(new StatusChangedMessage(LocalizationService.Instance.GetString("Action_OpenFailed")));
        AppLog.Warning($"Open in explorer failed: {path}");
    }

    [RelayCommand(CanExecute = nameof(CanUseShellActions))]
    private void CopyPath()
    {
        var path = DetailNode?.FullPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_shellActions.TryCopyPath(path))
        {
            WeakReferenceMessenger.Default.Send(new StatusChangedMessage(LocalizationService.Instance.GetString("Action_PathCopied")));
            return;
        }

        WeakReferenceMessenger.Default.Send(new StatusChangedMessage(LocalizationService.Instance.GetString("Action_CopyFailed")));
        AppLog.Warning($"Copy path failed: {path}");
    }

    private bool CanUseShellActions()
    {
        var node = DetailNode?.Node;
        return node is not null
            && node.NodeType is not ScanNodeType.VirtualGroup
            && !string.IsNullOrWhiteSpace(node.FullPath);
    }

    public void Reset()
    {
        LevelItems.Clear();
        ChartTopItems.Clear();
        Breadcrumbs.Clear();
        SelectedNode = null;
        SelectedLargeFile = null;
        SelectedTreemapNode = null;
        CurrentTreemapNode = null;
        _session.Reset();
        OnPropertyChanged(nameof(CanNavigateUp));
        OnPropertyChanged(nameof(HasScanResult));
        NavigateUpCommand.NotifyCanExecuteChanged();
    }

    public void ApplyPartialRoot(ScanNode partialRoot)
    {
        if (_session.RootNode is null)
        {
            _session.SetRoot(partialRoot);
        }
    }

    public void ApplyResultRoot(ScanNode root)
    {
        _session.SetRoot(root);
    }

    /// <summary>
    /// Rebuilds the projections after a language switch: node type labels and the
    /// virtual-group name are captured when a summary is constructed.
    /// </summary>
    public void RefreshLocalization()
    {
        var currentNode = _session.CurrentNode;
        if (currentNode is not null)
        {
            var previousSelection = SelectedNode?.Node;
            RebuildProjections();

            if (previousSelection is not null)
            {
                var restored = LevelItems.FirstOrDefault(item => ReferenceEquals(item.Node, previousSelection));
                if (restored is not null)
                {
                    SelectedNode = restored;
                    SelectedTreemapNode = restored.Node;
                }
            }
        }

        // The placeholder is only rendered while nothing is selected, in which
        // case the rebuild above bails out early.
        OnPropertyChanged(nameof(SelectedNodeName));
    }

    private void RebuildProjections()
    {
        LevelItems.Clear();
        ChartTopItems.Clear();
        Breadcrumbs.Clear();

        var current = _session.CurrentNode;
        if (current is null)
        {
            SelectedNode = null;
            SelectedTreemapNode = null;
            return;
        }

        var children = _session.CurrentLevelChildren;
        var parentSize = current.SizeBytes;

        var rankedItems = children
            .Where(static node => node.SizeBytes > 0)
            .OrderByDescending(static node => node.SizeBytes)
            .ToList();

        for (var index = 0; index < rankedItems.Count; index++)
        {
            var summary = new ScanNodeSummary(rankedItems[index], parentSize, index);
            LevelItems.Add(summary);
            if (index < ChartTopItemCount)
            {
                ChartTopItems.Add(summary);
            }
        }

        var rawBreadcrumbs = _session.Breadcrumbs;
        for (var i = 0; i < rawBreadcrumbs.Count; i++)
        {
            var item = rawBreadcrumbs[i];
            Breadcrumbs.Add(new BreadcrumbItem(item.Name, i));
        }

        SelectedNode = LevelItems.FirstOrDefault();
        SelectedTreemapNode = SelectedNode?.Node;
    }

    private static bool CanDrillInto(ScanNode node)
    {
        return node.NodeType is ScanNodeType.Drive or ScanNodeType.Directory
            && node.Children.Count > 0;
    }
}
