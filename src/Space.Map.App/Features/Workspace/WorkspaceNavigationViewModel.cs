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
    private readonly IClipboardService _clipboard;
    private readonly ILocalizationService _localization;
    private readonly ITreeNavigationSession _session;

    public WorkspaceNavigationViewModel(
        IShellActions shellActions,
        IClipboardService clipboard,
        ILocalizationService localization)
        : this(shellActions, clipboard, localization, new TreeNavigationSession())
    {
    }

    public WorkspaceNavigationViewModel(
        IShellActions shellActions,
        IClipboardService clipboard,
        ILocalizationService localization,
        ITreeNavigationSession session)
    {
        _shellActions = shellActions;
        _clipboard = clipboard;
        _localization = localization;
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

    public bool HasDetailSelection => DetailNode is not null;

    /// <summary>
    /// Symbol shown in the detail panel's icon tile; it should describe what is
    /// actually selected rather than always showing a folder.
    /// </summary>
    public string DetailIconSymbol => DetailNode?.Node.NodeType switch
    {
        ScanNodeType.Drive => "HardDrive24",
        ScanNodeType.File => "Document24",
        ScanNodeType.VirtualGroup => "FolderList24",
        _ => "Folder24"
    };

    /// <summary>
    /// Name of the detail node, or the localized placeholder when nothing is
    /// selected. A binding cannot carry a DynamicResource into TargetNullValue, so
    /// the fallback text is resolved here and refreshed on a language switch.
    /// </summary>
    public string SelectedNodeName => DetailNode?.Name
        ?? _localization.GetString("Detail_NoSelection");

    partial void OnSelectedNodeChanged(ScanNodeSummary? value)
    {
        OpenInExplorerCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        NotifyDetailChanged();

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
        NotifyDetailChanged();
    }

    /// <summary>Everything derived from <see cref="DetailNode"/>.</summary>
    private void NotifyDetailChanged()
    {
        OnPropertyChanged(nameof(DetailNode));
        OnPropertyChanged(nameof(SelectedNodeName));
        OnPropertyChanged(nameof(HasDetailSelection));
        OnPropertyChanged(nameof(DetailIconSymbol));
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
            WeakReferenceMessenger.Default.Send(new StatusChangedMessage(_localization.GetString("Action_OpenedInExplorer")));
            return;
        }

        WeakReferenceMessenger.Default.Send(new StatusChangedMessage(_localization.GetString("Action_OpenFailed")));
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

        if (_clipboard.TryCopyText(path))
        {
            WeakReferenceMessenger.Default.Send(new StatusChangedMessage(_localization.GetString("Action_PathCopied")));
            return;
        }

        WeakReferenceMessenger.Default.Send(new StatusChangedMessage(_localization.GetString("Action_CopyFailed")));
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
        LevelItems.DetachAndClear();
        ChartTopItems.DetachAndClear();
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
            return;
        }

        // Follow the live snapshot so the map grows while the scan runs instead of
        // freezing on whatever the first report happened to contain.
        _session.UpdatePartialRoot(partialRoot);
    }

    public void ApplyResultRoot(ScanNode root)
    {
        _session.SetRoot(root);
    }

    /// <summary>
    /// Re-renders the text this view model owns after a language switch. The row
    /// objects refresh themselves (they hold resource keys, not baked strings), so
    /// only the placeholder needs pushing -- and rebuilding the collections just to
    /// change wording would drop the user's selection.
    /// </summary>
    public void RefreshLocalization() => NotifyDetailChanged();

    private void RebuildProjections()
    {
        // Keep whatever the user picked across a rebuild: during a scan this runs
        // every progress tick, and re-selecting the first row each time would fight
        // the user's clicks.
        var previousSelection = SelectedNode?.Node.FullPath;

        LevelItems.DetachAndClear();
        ChartTopItems.DetachAndClear();
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
            var summary = new ScanNodeSummary(rankedItems[index], parentSize, _localization, index);
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
            Breadcrumbs.Add(new BreadcrumbItem(item.Name, i, isLast: i == rawBreadcrumbs.Count - 1));
        }

        // Restore the previous pick when it is still in this level, otherwise fall
        // back to the largest entry.
        var restored = previousSelection is null
            ? null
            : LevelItems.FirstOrDefault(item =>
                string.Equals(item.FullPath, previousSelection, StringComparison.OrdinalIgnoreCase));

        SelectedNode = restored ?? LevelItems.FirstOrDefault();
        SelectedTreemapNode = SelectedNode?.Node;
    }

    private static bool CanDrillInto(ScanNode node)
    {
        return node.NodeType is ScanNodeType.Drive or ScanNodeType.Directory
            && node.Children.Count > 0;
    }
}
