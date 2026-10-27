using System.Collections.ObjectModel;
using Space.Map.App.Services;

namespace Space.Map.App.ViewModels;

internal static class SummaryCollectionExtensions
{
    /// <summary>
    /// Empties a collection of localized rows, releasing each row's language
    /// subscription first. Rows are weak-linked, so they would be collected anyway,
    /// but detaching keeps a stale handler from firing before the GC gets there.
    /// </summary>
    public static void DetachAndClear<T>(this ObservableCollection<T> collection)
        where T : class
    {
        foreach (var item in collection)
        {
            switch (item)
            {
                case ScanNodeSummary summary:
                    summary.Detach();
                    break;
                case ScanIssueSummary issue:
                    issue.Detach();
                    break;
                case DriveEntry drive:
                    drive.Detach();
                    break;
            }
        }

        collection.Clear();
    }
}
