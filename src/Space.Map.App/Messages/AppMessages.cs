using Space.Map.Core.Scanning;

namespace Space.Map.App.Messages;

/// <summary>
/// Fired when a scan operation begins.
/// </summary>
public sealed record ScanStartedMessage(string Path);

/// <summary>
/// Fired when a scan operation finishes (completed, partial, canceled, failed).
/// </summary>
public sealed record ScanCompletedMessage(ScanResult Result);

/// <summary>
/// Fired to request navigation to a node or toggle the active workspace view.
/// </summary>
public sealed record ShowDriveHomeRequestedMessage;

/// <summary>
/// Fired when status text is updated by any sub-system.
/// </summary>
public sealed record StatusChangedMessage(string Status);
