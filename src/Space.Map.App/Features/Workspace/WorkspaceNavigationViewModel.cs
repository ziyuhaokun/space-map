using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Space.Map.App.Messages;
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

    public bool CanNavigateUp => _session.CanNavigateUp;

    public bool HasScanResult => _session.RootNode is not null;

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
        var path = SelectedNode?.FullPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_shellActions.TryOpenInExplorer(path))
        {
            WeakReferenceMessenger.Default.Send(new StatusChangedMessage("已在资源管理器中打开"));
            return;
        }

        WeakReferenceMessenger.Default.Send(new StatusChangedMessage("无法打开路径"));
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
            WeakReferenceMessenger.Default.Send(new StatusChangedMessage("路径已复制到剪贴板"));
            return;
        }

        WeakReferenceMessenger.Default.Send(new StatusChangedMessage("复制路径失败"));
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
        SelectedNode = null;
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
