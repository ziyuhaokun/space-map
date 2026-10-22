using System.Diagnostics;
using System.IO;

namespace Space.Map.Core.Scanning;

public sealed class FileSystemDiskScanner : IDiskScanner
{
    public Task<ScanResult> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            throw new ArgumentException("Root path must not be empty.", nameof(options));
        }

        return Task.Run(
            () => new ScannerRun(options, progress, cancellationToken).Execute(),
            cancellationToken);
    }

    private sealed class ScannerRun(
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        private const int MaxRetainedIssues = 500;

        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private long _lastProgressTicks;
        private long _lastPartialRootTicks;
        private string? _currentPath;
        private volatile bool _canceled;
        private volatile bool _hasEmittedPartialRoot;
        private volatile bool _scanCompleted;
        private readonly HashSet<DirectoryIdentity>? _visitedDirectories =
            options.FollowReparsePoints ? [] : null;
        private readonly object _identityLock = new();
        private readonly WorkerState _mainWorker = new(options);
        private readonly List<WorkerState> _workers = [];
        private List<SubtreeState> _subtrees = [];
        private int _nextSubtree;
        private int _completedSubtrees;
        private Exception? _fatalException;

        public ScanResult Execute()
        {
            var sessionId = Guid.NewGuid();
            var startedAt = DateTimeOffset.UtcNow;
            ScanNode? root = null;
            var status = ScanResultStatus.Completed;

            try
            {
                root = ScanRoot(options.RootPath);

                if (_canceled && root is not null)
                {
                    status = ScanResultStatus.Canceled;
                }
                else if (TotalIssueCount > 0 && root is not null)
                {
                    status = ScanResultStatus.Partial;
                }

                if (root is null && TotalIssueCount > 0)
                {
                    status = ScanResultStatus.Failed;
                }
            }
            catch (OperationCanceledException)
            {
                if (root is null)
                {
                    throw;
                }

                status = ScanResultStatus.Canceled;
            }
            catch (Exception exception)
            {
                AddIssue(_mainWorker, options.RootPath, ToIssueKind(exception), exception.Message);
                status = ScanResultStatus.Failed;
            }
            finally
            {
                ReportProgress(force: true);
            }

            return new ScanResult(
                sessionId,
                options.RootPath,
                startedAt,
                DateTimeOffset.UtcNow,
                status,
                root,
                MergeIssues(),
                options,
                TotalIssueCount,
                BuildTopFiles());
        }

        private ScanNode? ScanRoot(string rootPath)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(rootPath))
            {
                return ScanFile(new FileInfo(rootPath), _mainWorker);
            }

            if (Directory.Exists(rootPath))
            {
                return ScanDirectory(new DirectoryInfo(rootPath));
            }

            AddIssue(_mainWorker, rootPath, ScanIssueKind.PathNotFound, "The scan root does not exist.");
            return null;
        }

        /// <summary>
        /// 顶层扇出：MaxParallelism &lt;= 1 时按原始单线程迭代 DFS 扫描整棵树（零同步开销）；
        /// 否则枚举根目录的立即子项，把每个子目录交给一个 worker 用迭代 DFS 独立扫描，
        /// 同步开销仅 O(顶层子目录数)。根级渐进快照由周期 Timer 从各子树的实时占用构建。
        /// </summary>
        private ScanNode? ScanDirectory(DirectoryInfo directory)
        {
            var degree = Math.Clamp(options.MaxParallelism, 1, 64);
            if (degree <= 1)
            {
                return ScanTree(directory, isRoot: true, progressSink: null, _mainWorker);
            }

            return ScanDirectoryFanOut(directory);
        }

        private ScanNode? ScanDirectoryFanOut(DirectoryInfo directory)
        {
            var state = new RootScanState(directory);
            EnumerateRootChildren(directory, state, _mainWorker);

            if (state.Subtrees.Count == 0)
            {
                return AssembleRoot(state);
            }

            _subtrees = state.Subtrees;
            _nextSubtree = 0;
            _completedSubtrees = 0;

            // 周期刷新器：扫描期间定时推送根级渐进快照（含各子树占位实时累计）。
            using var refresher = new Timer(
                _ => RefreshPartialRoot(state),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(250));

            var workers = StartFanOutWorkers();
            foreach (var worker in workers)
            {
                worker.Join();
            }

            refresher.Change(Timeout.Infinite, Timeout.Infinite);
            if (_fatalException is not null)
            {
                throw _fatalException;
            }

            return AssembleRoot(state);
        }

        /// <summary>
        /// 在扫描线程上枚举根目录立即子项：文件累计到根节点自身统计，子目录进入子树任务列表。
        /// </summary>
        private void EnumerateRootChildren(DirectoryInfo directory, RootScanState state, WorkerState worker)
        {
            try
            {
                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry is DirectoryInfo childDirectory)
                    {
                        state.Subtrees.Add(new SubtreeState { Directory = childDirectory });
                    }
                    else if (entry is FileInfo file)
                    {
                        if (!options.KeepFileNodes)
                        {
                            var fileStats = ScanFileStats(file, worker);
                            if (fileStats is null)
                            {
                                continue;
                            }

                            state.OwnBytes += fileStats.Value.SizeBytes;
                            state.OwnFiles += fileStats.Value.FileCount;
                        }
                        else
                        {
                            var fileNode = ScanFile(file, worker);
                            if (fileNode is null)
                            {
                                continue;
                            }

                            state.OwnBytes += fileNode.SizeBytes;
                            state.OwnFiles += fileNode.FileCount;
                            state.FileNodes.Add(fileNode);
                        }
                    }
                }
            }
            catch (Exception exception) when (IsRecoverableFileSystemException(exception))
            {
                AddIssue(worker, directory.FullName, ToIssueKind(exception), exception.Message);
                state.Status = ScanNodeStatus.Partial;
            }
        }

        private Thread[] StartFanOutWorkers()
        {
            var degree = Math.Clamp(options.MaxParallelism, 1, 64);
            var threads = new Thread[degree];
            for (var index = 0; index < degree; index++)
            {
                var worker = new WorkerState(options);
                _workers.Add(worker);
                var thread = new Thread(() => FanOutWorker(worker))
                {
                    IsBackground = true,
                    Name = $"Space.Map Scan Worker {index}"
                };
                thread.Start();
                threads[index] = thread;
            }

            return threads;
        }

        private void FanOutWorker(WorkerState worker)
        {
            while (true)
            {
                var index = Interlocked.Increment(ref _nextSubtree) - 1;
                if (index >= _subtrees.Count)
                {
                    return;
                }

                var subtree = _subtrees[index];
                try
                {
                    subtree.Node = ScanTree(subtree.Directory, isRoot: false, subtree, worker);
                }
                catch (OperationCanceledException)
                {
                    _canceled = true;
                    subtree.Node = CreateDirectoryNode(
                        subtree.Directory,
                        [],
                        0,
                        0,
                        0,
                        ScanNodeStatus.Partial,
                        "Scan canceled.",
                        false);
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref _fatalException, exception, null);
                    subtree.Node = CreateDirectoryNode(
                        subtree.Directory,
                        [],
                        0,
                        0,
                        0,
                        ScanNodeStatus.Partial,
                        exception.Message,
                        false);
                }

                if (Interlocked.Increment(ref _completedSubtrees) == _subtrees.Count)
                {
                    _scanCompleted = true;
                }
            }
        }

        private void RefreshPartialRoot(RootScanState state)
        {
            if (_scanCompleted)
            {
                return;
            }

            ReportFirstLevelProgress(state);
        }

        private ScanNode AssembleRoot(RootScanState state)
        {
            var children = new List<ScanNode>(state.Subtrees.Count + state.FileNodes.Count);
            long childBytes = 0;
            var childFiles = 0;
            var childDirs = 0;
            var status = state.Status;

            foreach (var subtree in state.Subtrees)
            {
                var node = subtree.Node
                    ?? CreateDirectoryNode(subtree.Directory, [], 0, 0, 0, ScanNodeStatus.Partial, "Subtree did not complete.", false);
                children.Add(node);
                childBytes += node.SizeBytes;
                childFiles += node.FileCount;
                childDirs += node.DirectoryCount + 1;
                if (node.Status is ScanNodeStatus.Partial or ScanNodeStatus.Error or ScanNodeStatus.Skipped)
                {
                    status = ScanNodeStatus.Partial;
                }
            }

            children.AddRange(state.FileNodes);
            var retained = CapChildren(state.Directory.FullName, children);
            return CreateDirectoryNode(
                state.Directory,
                retained,
                state.OwnBytes + childBytes,
                state.OwnFiles + childFiles,
                childDirs,
                status,
                status == ScanNodeStatus.Partial ? "One or more child paths were skipped or could not be scanned." : null,
                false);
        }

        /// <summary>
        /// Iterative post-order directory walk (explicit stack) so deep trees cannot blow the call stack.
        /// Semantics match the previous recursive DFS: cancel-with-partial, issue isolation, CapChildren,
        /// and first-level progressive progress. Used both for the single-threaded path (root, isRoot=true,
        /// progressSink=null) and for each fan-out subtree (isRoot=false, progressSink=subtree state).
        /// </summary>
        private ScanNode? ScanTree(DirectoryInfo directory, bool isRoot, SubtreeState? progressSink, WorkerState worker)
        {
            var stack = new Stack<DirectoryFrame>();
            stack.Push(new DirectoryFrame(directory, isRoot) { ProgressSink = progressSink });

            while (stack.Count > 0)
            {
                var frame = stack.Peek();

                try
                {
                    if (!frame.IsEntered)
                    {
                        if (!TryEnterDirectory(frame, out var terminalNode, worker))
                        {
                            stack.Pop();
                            if (stack.Count == 0)
                            {
                                return terminalNode;
                            }

                            if (terminalNode is not null)
                            {
                                AttachChild(stack.Peek(), terminalNode);
                            }

                            continue;
                        }
                    }

                    if (_canceled)
                    {
                        if (frame.Status == ScanNodeStatus.Completed)
                        {
                            frame.Status = ScanNodeStatus.Partial;
                        }

                        if (!FinishAndUnwind(stack, frame, out var canceledRoot))
                        {
                            return canceledRoot;
                        }

                        continue;
                    }

                    if (TryMoveNextEntry(frame, out var item, worker))
                    {
                        if (item is DirectoryInfo childDirectory)
                        {
                            stack.Push(new DirectoryFrame(childDirectory, isRoot: false));
                            continue;
                        }

                        if (item is FileInfo file)
                        {
                            ProcessFileEntry(frame, file, worker);
                        }

                        continue;
                    }

                    if (!FinishAndUnwind(stack, frame, out var completedRoot))
                    {
                        return completedRoot;
                    }
                }
                catch (OperationCanceledException)
                {
                    _canceled = true;

                    if (!frame.IsEntered)
                    {
                        stack.Pop();
                        if (stack.Count == 0)
                        {
                            throw;
                        }

                        var parent = stack.Peek();
                        MarkFrameCanceled(parent);
                        if (!FinishAndUnwind(stack, parent, out var canceledRoot))
                        {
                            return canceledRoot;
                        }

                        continue;
                    }

                    MarkFrameCanceled(frame);
                    if (!FinishAndUnwind(stack, frame, out var canceledEnteredRoot))
                    {
                        return canceledEnteredRoot;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Completes <paramref name="frame"/>, pops it, and either returns it as the subtree root
        /// or attaches it to the new stack top. Returns <c>false</c> when the completed node is the root.
        /// </summary>
        private bool FinishAndUnwind(Stack<DirectoryFrame> stack, DirectoryFrame frame, out ScanNode? rootNode)
        {
            var completed = CompleteDirectoryFrame(frame);
            stack.Pop();

            if (stack.Count == 0)
            {
                rootNode = completed;
                return false;
            }

            AttachChild(stack.Peek(), completed);
            rootNode = null;
            return true;
        }

        private bool TryEnterDirectory(DirectoryFrame frame, out ScanNode? terminalNode, WorkerState worker)
        {
            terminalNode = null;
            cancellationToken.ThrowIfCancellationRequested();

            var directory = frame.Directory;
            _currentPath = directory.FullName;
            worker.ScannedDirectories++;
            ReportProgress();

            if (!TryGetAttributes(directory.FullName, out var attributes))
            {
                terminalNode = CreateDirectoryNode(
                    directory, [], 0, 0, 0, ScanNodeStatus.Error, "Unable to read directory attributes.", false);
                frame.IsEntered = true;
                return false;
            }

            var isReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);
            frame.IsReparsePoint = isReparsePoint;

            if (isReparsePoint && !frame.IsRoot && !options.FollowReparsePoints)
            {
                AddIssue(worker, directory.FullName, ScanIssueKind.ReparsePointSkipped, "Reparse point skipped.");
                worker.SkippedCount++;
                terminalNode = CreateDirectoryNode(
                    directory, [], 0, 0, 0, ScanNodeStatus.Skipped, "Reparse point skipped.", true);
                frame.IsEntered = true;
                return false;
            }

            if (!frame.IsRoot && ShouldSkipByAttributes(directory.FullName, attributes, out var skippedKind, out var skippedMessage))
            {
                AddIssue(worker, directory.FullName, skippedKind, skippedMessage);
                worker.SkippedCount++;
                terminalNode = CreateDirectoryNode(
                    directory, [], 0, 0, 0, ScanNodeStatus.Skipped, skippedMessage, isReparsePoint);
                frame.IsEntered = true;
                return false;
            }

            if (!TryRegisterDirectoryIdentity(frame, out terminalNode, worker))
            {
                frame.IsEntered = true;
                return false;
            }

            frame.IsEntered = true;
            frame.Enumerator = EnumerateFileSystemInfos(directory).GetEnumerator();
            return true;
        }

        private bool TryRegisterDirectoryIdentity(
            DirectoryFrame frame,
            out ScanNode? terminalNode,
            WorkerState worker)
        {
            terminalNode = null;
            if (_visitedDirectories is null)
            {
                return true;
            }

            var directory = frame.Directory;
            if (!WindowsDirectoryIdentity.TryGet(directory.FullName, out var identity, out var errorMessage))
            {
                AddIssue(worker, directory.FullName, ScanIssueKind.Io, errorMessage);
                worker.SkippedCount++;
                terminalNode = CreateDirectoryNode(
                    directory, [], 0, 0, 0, ScanNodeStatus.Error, errorMessage, frame.IsReparsePoint);
                return false;
            }

            lock (_identityLock)
            {
                if (_visitedDirectories.Add(identity))
                {
                    return true;
                }
            }

            const string duplicateMessage =
                "Directory target already visited; reparse-point cycle or duplicate target skipped.";
            AddIssue(worker, directory.FullName, ScanIssueKind.ReparsePointSkipped, duplicateMessage);
            worker.SkippedCount++;
            terminalNode = CreateDirectoryNode(
                directory, [], 0, 0, 0, ScanNodeStatus.Skipped, duplicateMessage, frame.IsReparsePoint);
            return false;
        }

        private bool TryMoveNextEntry(DirectoryFrame frame, out FileSystemInfo? item, WorkerState worker)
        {
            item = null;
            if (frame.Enumerator is null)
            {
                return false;
            }

            try
            {
                if (!frame.Enumerator.MoveNext())
                {
                    return false;
                }

                item = frame.Enumerator.Current;
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableFileSystemException(exception))
            {
                // Enumeration can throw mid-stream after the enumerator is opened.
                // Keep already-scanned children and mark this directory partial.
                AddIssue(worker, frame.Directory.FullName, ToIssueKind(exception), exception.Message);
                frame.Status = ScanNodeStatus.Partial;
                frame.ErrorReason = exception.Message;
                return false;
            }
        }

        private void ProcessFileEntry(DirectoryFrame frame, FileInfo file, WorkerState worker)
        {
            if (!options.KeepFileNodes)
            {
                var fileStats = ScanFileStats(file, worker);
                if (fileStats is null)
                {
                    return;
                }

                frame.SizeBytes += fileStats.Value.SizeBytes;
                frame.FileCount += fileStats.Value.FileCount;
                ReportFirstLevelProgressIfRoot(frame);
                return;
            }

            var fileNode = ScanFile(file, worker);
            if (fileNode is null)
            {
                return;
            }

            frame.SizeBytes += fileNode.SizeBytes;
            frame.FileCount += fileNode.FileCount;
            frame.Children.Add(fileNode);
            ReportFirstLevelProgressIfRoot(frame);
        }

        private void AttachChild(DirectoryFrame parent, ScanNode child)
        {
            parent.SizeBytes += child.SizeBytes;
            parent.FileCount += child.FileCount;
            parent.DirectoryCount += child.DirectoryCount + 1;
            parent.Children.Add(child);

            if (child.Status is ScanNodeStatus.Partial or ScanNodeStatus.Error or ScanNodeStatus.Skipped)
            {
                parent.Status = ScanNodeStatus.Partial;
            }

            ReportFirstLevelProgressIfRoot(parent);
        }

        private void ReportFirstLevelProgressIfRoot(DirectoryFrame frame)
        {
            if (frame.ProgressSink is not null)
            {
                // 顶层扇出：把子树根帧的实时累计写入子树状态，供根级渐进快照读取。
                Volatile.Write(ref frame.ProgressSink.LiveBytes, frame.SizeBytes);
                frame.ProgressSink.LiveFiles = frame.FileCount;
                frame.ProgressSink.LiveDirs = frame.DirectoryCount;
                return;
            }

            if (frame.IsRoot)
            {
                ReportFirstLevelProgress(frame);
            }
        }

        private static void MarkFrameCanceled(DirectoryFrame frame)
        {
            frame.Status = ScanNodeStatus.Partial;
            frame.ErrorReason ??= "Scan canceled.";
        }

        private ScanNode CompleteDirectoryFrame(DirectoryFrame frame)
        {
            frame.Enumerator?.Dispose();
            frame.Enumerator = null;

            if (frame.Status == ScanNodeStatus.Partial && frame.ErrorReason is null)
            {
                frame.ErrorReason = "One or more child paths were skipped or could not be scanned.";
            }

            var retainedChildren = CapChildren(frame.Directory.FullName, frame.Children);
            return CreateDirectoryNode(
                frame.Directory,
                retainedChildren,
                frame.SizeBytes,
                frame.FileCount,
                frame.DirectoryCount,
                frame.Status,
                frame.ErrorReason,
                frame.IsReparsePoint);
        }

        private sealed class DirectoryFrame(DirectoryInfo directory, bool isRoot)
        {
            public DirectoryInfo Directory { get; } = directory;

            public bool IsRoot { get; } = isRoot;

            public bool IsEntered { get; set; }

            public bool IsReparsePoint { get; set; }

            public List<ScanNode> Children { get; } = [];

            public long SizeBytes { get; set; }

            public int FileCount { get; set; }

            public int DirectoryCount { get; set; }

            public ScanNodeStatus Status { get; set; } = ScanNodeStatus.Completed;

            public string? ErrorReason { get; set; }

            public IEnumerator<FileSystemInfo>? Enumerator { get; set; }

            /// <summary>顶层扇出时指向子树状态，用于把实时占用写入渐进快照。</summary>
            public SubtreeState? ProgressSink { get; set; }
        }

        /// <summary>
        /// Full file node for tree inclusion (root-as-file or KeepFileNodes=true).
        /// </summary>
        private ScanNode? ScanFile(FileInfo file, WorkerState worker)
        {
            if (TryAcceptFile(file, worker) is not { } accepted)
            {
                return null;
            }

            return CreateFileNode(file, accepted, includeModifiedAt: options.ReadFileTimestamps);
        }

        /// <summary>
        /// Lightweight path: roll size/count into the parent without retaining a tree node.
        /// </summary>
        private FileStats? ScanFileStats(FileInfo file, WorkerState worker)
        {
            if (TryAcceptFile(file, worker) is not { } accepted)
            {
                return null;
            }

            return new FileStats(accepted.Length, 1);
        }

        /// <summary>
        /// Shared file gate: cancel, attributes, reparse/hidden/system filters, length, min size,
        /// counters, top-file heap, and throttled progress. Returns null when the file is skipped.
        /// </summary>
        private AcceptedFile? TryAcceptFile(FileInfo file, WorkerState worker)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _currentPath = file.FullName;

            if (!TryGetAttributes(file, out var attributes))
            {
                return null;
            }

            var isReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);
            if (isReparsePoint && !options.FollowReparsePoints)
            {
                AddIssue(worker, file.FullName, ScanIssueKind.ReparsePointSkipped, "Reparse point skipped.");
                worker.SkippedCount++;
                return null;
            }

            if (ShouldSkipByAttributes(file.FullName, attributes, out var skippedKind, out var skippedMessage))
            {
                AddIssue(worker, file.FullName, skippedKind, skippedMessage);
                worker.SkippedCount++;
                return null;
            }

            long length;

            try
            {
                length = file.Length;
            }
            catch (Exception exception) when (IsRecoverableFileSystemException(exception))
            {
                AddIssue(worker, file.FullName, ToIssueKind(exception), exception.Message);
                return null;
            }

            if (options.MinimumFileSizeBytes is { } minimumFileSizeBytes && length < minimumFileSizeBytes)
            {
                worker.SkippedCount++;
                return null;
            }

            worker.ScannedFiles++;
            worker.TotalBytes += length;

            // Top-file heap keeps a lightweight node (no timestamps) in both scan modes.
            ConsiderTopFile(worker, CreateFileNode(file, new AcceptedFile(length, isReparsePoint), includeModifiedAt: false));
            ReportProgress();

            return new AcceptedFile(length, isReparsePoint);
        }

        private static ScanNode CreateFileNode(FileInfo file, AcceptedFile accepted, bool includeModifiedAt)
        {
            return new ScanNode(
                file.Name,
                file.FullName,
                ScanNodeType.File,
                accepted.Length,
                FileCount: 1,
                DirectoryCount: 0,
                Children: [],
                Extension: file.Extension,
                ModifiedAt: includeModifiedAt ? SafeGetTime(() => file.LastWriteTimeUtc) : null,
                IsReparsePoint: accepted.IsReparsePoint);
        }

        private IEnumerable<FileSystemInfo> EnumerateFileSystemInfos(DirectoryInfo directory)
        {
            try
            {
                return directory.EnumerateFileSystemInfos();
            }
            catch (Exception exception) when (IsRecoverableFileSystemException(exception))
            {
                AddIssue(_mainWorker, directory.FullName, ToIssueKind(exception), exception.Message);
                return [];
            }
        }

        private bool TryGetAttributes(string path, out FileAttributes attributes)
        {
            try
            {
                attributes = File.GetAttributes(path);
                return true;
            }
            catch (Exception exception) when (IsRecoverableFileSystemException(exception))
            {
                attributes = default;
                AddIssue(_mainWorker, path, ToIssueKind(exception), exception.Message);
                return false;
            }
        }

        private bool TryGetAttributes(FileSystemInfo item, out FileAttributes attributes)
        {
            try
            {
                attributes = item.Attributes;
                return true;
            }
            catch (Exception exception) when (IsRecoverableFileSystemException(exception))
            {
                attributes = default;
                AddIssue(_mainWorker, item.FullName, ToIssueKind(exception), exception.Message);
                return false;
            }
        }

        private bool ShouldSkipByAttributes(
            string path,
            FileAttributes attributes,
            out ScanIssueKind issueKind,
            out string message)
        {
            if (!options.IncludeHidden && attributes.HasFlag(FileAttributes.Hidden))
            {
                issueKind = ScanIssueKind.HiddenSkipped;
                message = "Hidden path skipped.";
                return true;
            }

            if (!options.IncludeSystem && attributes.HasFlag(FileAttributes.System))
            {
                issueKind = ScanIssueKind.SystemSkipped;
                message = "System path skipped.";
                return true;
            }

            issueKind = ScanIssueKind.Unknown;
            message = string.Empty;
            return false;
        }

        private ScanNode CreateDirectoryNode(
            DirectoryInfo directory,
            IReadOnlyList<ScanNode> children,
            long sizeBytes,
            int fileCount,
            int directoryCount,
            ScanNodeStatus status,
            string? errorReason,
            bool isReparsePoint)
        {
            return new ScanNode(
                directory.Name.Length == 0 ? directory.FullName : directory.Name,
                directory.FullName,
                IsDriveRoot(directory.FullName) ? ScanNodeType.Drive : ScanNodeType.Directory,
                sizeBytes,
                fileCount,
                directoryCount,
                children,
                Status: status,
                ErrorReason: errorReason,
                IsReparsePoint: isReparsePoint);
        }

        private List<ScanNode> CapChildren(string parentFullPath, List<ScanNode> children)
        {
            var max = options.MaxChildrenPerDirectory;
            if (max <= 0 || children.Count <= max)
            {
                return children;
            }

            // Reserve one slot for the VirtualGroup aggregate.
            // Top-K via min-heap: O(n log k) instead of full O(n log n) sort.
            var keepCount = Math.Max(1, max - 1);
            var heap = new PriorityQueue<ScanNode, long>(keepCount + 1);
            long restSize = 0;
            var restFiles = 0;
            var restDirs = 0;
            var restCount = 0;

            foreach (var node in children)
            {
                if (heap.Count < keepCount)
                {
                    heap.Enqueue(node, node.SizeBytes);
                    continue;
                }

                if (heap.TryPeek(out _, out var smallest) && node.SizeBytes > smallest)
                {
                    var evicted = heap.Dequeue();
                    AccumulateRest(evicted, ref restSize, ref restFiles, ref restDirs, ref restCount);
                    heap.Enqueue(node, node.SizeBytes);
                }
                else
                {
                    AccumulateRest(node, ref restSize, ref restFiles, ref restDirs, ref restCount);
                }
            }

            var kept = new List<ScanNode>(keepCount + 1);
            while (heap.Count > 0)
            {
                kept.Add(heap.Dequeue());
            }

            // Only k items — sort for stable largest-first UI order.
            kept.Sort(static (left, right) =>
            {
                var sizeCompare = right.SizeBytes.CompareTo(left.SizeBytes);
                return sizeCompare != 0
                    ? sizeCompare
                    : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            });

            if (restCount > 0)
            {
                kept.Add(new ScanNode(
                    $"其他 ({restCount})",
                    Path.Combine(parentFullPath, "#others"),
                    ScanNodeType.VirtualGroup,
                    restSize,
                    restFiles,
                    restDirs,
                    Children: [],
                    Status: ScanNodeStatus.Completed,
                    ErrorReason: $"{restCount} smaller items aggregated (MaxChildrenPerDirectory={max})."));
            }

            // Dropped nodes are no longer referenced and can be collected.
            return kept;
        }

        private static void AccumulateRest(
            ScanNode node,
            ref long restSize,
            ref int restFiles,
            ref int restDirs,
            ref int restCount)
        {
            restCount++;
            restSize += node.SizeBytes;
            restFiles += node.FileCount;
            restDirs += node.DirectoryCount;
            if (node.NodeType is ScanNodeType.Directory or ScanNodeType.Drive)
            {
                restDirs++;
            }
        }

        private void ConsiderTopFile(WorkerState worker, ScanNode fileNode)
        {
            var heap = worker.TopFiles;
            if (heap is null || fileNode.SizeBytes <= 0)
            {
                return;
            }

            var capacity = options.MaxTopFiles;
            if (heap.Count < capacity)
            {
                heap.Enqueue(fileNode, fileNode.SizeBytes);
                return;
            }

            if (heap.TryPeek(out _, out var smallest) && fileNode.SizeBytes > smallest)
            {
                heap.Dequeue();
                heap.Enqueue(fileNode, fileNode.SizeBytes);
            }
        }

        private IReadOnlyList<ScanNode> BuildTopFiles()
        {
            var merged = new List<ScanNode>();
            MergeTopFiles(merged, _mainWorker);
            foreach (var worker in _workers)
            {
                MergeTopFiles(merged, worker);
            }

            return merged
                .OrderByDescending(static node => node.SizeBytes)
                .ThenBy(static node => node.FullPath, StringComparer.OrdinalIgnoreCase)
                .Take(options.MaxTopFiles)
                .ToList();
        }

        private static void MergeTopFiles(List<ScanNode> target, WorkerState worker)
        {
            if (worker.TopFiles is not { Count: > 0 })
            {
                return;
            }

            foreach (var item in worker.TopFiles.UnorderedItems)
            {
                target.Add(item.Element);
            }
        }

        private void AddIssue(WorkerState worker, string path, ScanIssueKind kind, string message)
        {
            worker.IssueCount++;
            if (worker.Issues.Count >= MaxRetainedIssues)
            {
                return;
            }

            worker.Issues.Add(new ScanIssue(path, kind, message));
        }

        private List<ScanIssue> MergeIssues()
        {
            var merged = new List<ScanIssue>();
            foreach (var worker in AllWorkers())
            {
                foreach (var issue in worker.Issues)
                {
                    if (merged.Count >= MaxRetainedIssues)
                    {
                        return merged;
                    }

                    merged.Add(issue);
                }
            }

            return merged;
        }

        private long TotalIssueCount
        {
            get
            {
                long total = 0;
                foreach (var worker in AllWorkers())
                {
                    total += worker.IssueCount;
                }

                return total;
            }
        }

        private IEnumerable<WorkerState> AllWorkers()
        {
            yield return _mainWorker;
            foreach (var worker in _workers)
            {
                yield return worker;
            }
        }

        private void ReportFirstLevelProgress(DirectoryFrame frame)
        {
            if (progress is null)
            {
                return;
            }

            var elapsedTicks = _elapsed.ElapsedTicks;
            var minimumTicks = Stopwatch.Frequency / 2;
            var forceFirst = !_hasEmittedPartialRoot;
            if (!forceFirst && !TryClaim(ref _lastPartialRootTicks, elapsedTicks, minimumTicks))
            {
                return;
            }

            _hasEmittedPartialRoot = true;

            // Copy only when actually reporting — children keep mutating while the scan runs.
            var partialRoot = CreateDirectoryNode(
                frame.Directory,
                frame.Children.ToArray(),
                frame.SizeBytes,
                frame.FileCount,
                frame.DirectoryCount,
                ScanNodeStatus.Partial,
                "Scan in progress.",
                frame.IsReparsePoint);

            var totals = GetTotals();
            progress.Report(new ScanProgress(
                totals.Files,
                totals.Directories,
                totals.Bytes,
                _currentPath,
                totals.Issues,
                totals.Skipped,
                _elapsed.Elapsed,
                partialRoot));
        }

        private void ReportFirstLevelProgress(RootScanState state)
        {
            if (progress is null)
            {
                return;
            }

            var elapsedTicks = _elapsed.ElapsedTicks;
            var minimumTicks = Stopwatch.Frequency / 2;
            var forceFirst = !_hasEmittedPartialRoot;
            if (!forceFirst && !TryClaim(ref _lastPartialRootTicks, elapsedTicks, minimumTicks))
            {
                return;
            }

            _hasEmittedPartialRoot = true;

            // 根级渐进快照：已完成子树用完整节点，进行中的子树用占位节点（实时占用）。
            var children = new List<ScanNode>(state.Subtrees.Count + state.FileNodes.Count);
            long sizeBytes = state.OwnBytes;
            var fileCount = state.OwnFiles;
            var directoryCount = 0;
            foreach (var subtree in state.Subtrees)
            {
                if (subtree.Node is not null)
                {
                    children.Add(subtree.Node);
                    sizeBytes += subtree.Node.SizeBytes;
                    fileCount += subtree.Node.FileCount;
                    directoryCount += subtree.Node.DirectoryCount + 1;
                }
                else
                {
                    var placeholder = BuildPlaceholder(subtree);
                    children.Add(placeholder);
                    sizeBytes += placeholder.SizeBytes;
                    fileCount += placeholder.FileCount;
                    directoryCount += placeholder.DirectoryCount;
                }
            }

            children.AddRange(state.FileNodes);

            var partialRoot = CreateDirectoryNode(
                state.Directory,
                children,
                sizeBytes,
                fileCount,
                directoryCount,
                ScanNodeStatus.Partial,
                "Scan in progress.",
                false);

            var totals = GetTotals();
            progress.Report(new ScanProgress(
                totals.Files,
                totals.Directories,
                totals.Bytes,
                _currentPath,
                totals.Issues,
                totals.Skipped,
                _elapsed.Elapsed,
                partialRoot));
        }

        /// <summary>
        /// 用子树状态当前累计构造一个 Partial 占位节点（不含子节点，仅用于渐进显示）。
        /// </summary>
        private static ScanNode BuildPlaceholder(SubtreeState subtree)
        {
            return new ScanNode(
                subtree.Directory.Name.Length == 0 ? subtree.Directory.FullName : subtree.Directory.Name,
                subtree.Directory.FullName,
                IsDriveRoot(subtree.Directory.FullName) ? ScanNodeType.Drive : ScanNodeType.Directory,
                Volatile.Read(ref subtree.LiveBytes),
                subtree.LiveFiles,
                subtree.LiveDirs,
                Children: [],
                Status: ScanNodeStatus.Partial,
                ErrorReason: "Scan in progress.",
                IsReparsePoint: false);
        }

        private void ReportProgress(bool force = false, ScanNode? partialRoot = null)
        {
            if (progress is null)
            {
                return;
            }

            var elapsedTicks = _elapsed.ElapsedTicks;
            var minimumTicks = Stopwatch.Frequency / 2;
            if (!force && !TryClaim(ref _lastProgressTicks, elapsedTicks, minimumTicks))
            {
                return;
            }

            var totals = GetTotals();
            progress.Report(new ScanProgress(
                totals.Files,
                totals.Directories,
                totals.Bytes,
                _currentPath,
                totals.Issues,
                totals.Skipped,
                _elapsed.Elapsed,
                partialRoot));
        }

        /// <summary>
        /// 原子地抢占一次进度上报窗口：仅当距上次上报超过 <paramref name="minimumTicks"/>
        /// 时，将 <paramref name="lastTicks"/> 更新为当前值并返回 true，否则返回 false。
        /// </summary>
        private static bool TryClaim(ref long lastTicks, long nowTicks, long minimumTicks)
        {
            var last = Interlocked.Read(ref lastTicks);
            if (nowTicks - last < minimumTicks)
            {
                return false;
            }

            return Interlocked.CompareExchange(ref lastTicks, nowTicks, last) == last;
        }

        private (long Files, long Directories, long Bytes, long Skipped, long Issues) GetTotals()
        {
            long files = 0;
            long directories = 0;
            long bytes = 0;
            long skipped = 0;
            long issues = 0;
            foreach (var worker in AllWorkers())
            {
                files += worker.ScannedFiles;
                directories += worker.ScannedDirectories;
                bytes += worker.TotalBytes;
                skipped += worker.SkippedCount;
                issues += worker.IssueCount;
            }

            return (files, directories, bytes, skipped, issues);
        }

        private static DateTimeOffset? SafeGetTime(Func<DateTime> readTime)
        {
            try
            {
                return new DateTimeOffset(DateTime.SpecifyKind(readTime(), DateTimeKind.Utc));
            }
            catch
            {
                return null;
            }
        }

        private static bool IsDriveRoot(string path)
        {
            var root = Path.GetPathRoot(path);
            return root is not null && string.Equals(
                Path.TrimEndingDirectorySeparator(root),
                Path.TrimEndingDirectorySeparator(path),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRecoverableFileSystemException(Exception exception)
        {
            return exception is UnauthorizedAccessException
                or PathTooLongException
                or DirectoryNotFoundException
                or FileNotFoundException
                or IOException;
        }

        private static ScanIssueKind ToIssueKind(Exception exception)
        {
            return exception switch
            {
                UnauthorizedAccessException => ScanIssueKind.UnauthorizedAccess,
                PathTooLongException => ScanIssueKind.PathTooLong,
                DirectoryNotFoundException or FileNotFoundException => ScanIssueKind.PathNotFound,
                IOException => ScanIssueKind.Io,
                _ => ScanIssueKind.Unknown
            };
        }

        private readonly record struct FileStats(long SizeBytes, int FileCount);

        private readonly record struct AcceptedFile(long Length, bool IsReparsePoint);

        private sealed class RootScanState(DirectoryInfo directory)
        {
            public DirectoryInfo Directory { get; } = directory;

            /// <summary>根级子目录任务列表（枚举完成后固定，只读）。</summary>
            public List<SubtreeState> Subtrees { get; } = [];

            /// <summary>KeepFileNodes 时根级直接文件节点。</summary>
            public List<ScanNode> FileNodes { get; } = [];

            public long OwnBytes;

            public int OwnFiles;

            public ScanNodeStatus Status { get; set; } = ScanNodeStatus.Completed;
        }

        private sealed class SubtreeState
        {
            public required DirectoryInfo Directory { get; init; }

            /// <summary>子树完成节点（扫描期间为 null）。</summary>
            public volatile ScanNode? Node;

            /// <summary>子树实时占用（由子树根帧写入，供渐进快照读取）。</summary>
            public long LiveBytes;

            public volatile int LiveFiles;

            public volatile int LiveDirs;
        }

        /// <summary>
        /// 单 worker 的本地聚合：计数器与 Top-K 堆仅在本 worker 线程内访问，扫描结束后合并，
        /// 避免为每次文件命中加锁或做原子操作造成竞争。
        /// </summary>
        private sealed class WorkerState
        {
            public WorkerState(ScanOptions options)
            {
                TopFiles = options.MaxTopFiles > 0 ? new PriorityQueue<ScanNode, long>() : null;
            }

            public PriorityQueue<ScanNode, long>? TopFiles { get; }

            public long ScannedFiles;

            public long TotalBytes;

            public long ScannedDirectories;

            public long SkippedCount;

            public int IssueCount;

            public List<ScanIssue> Issues { get; } = [];
        }
    }
}
