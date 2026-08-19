namespace STS2Mobile.Steam;

// Resolves the visible update badge from the last subscription plan plus the
// live queue/config state. A completed install writes TimeUpdated before the
// queue transitions to Completed; once both revisions agree, the old plan is a
// stale snapshot and must not keep rendering "Update available".
internal static class WorkshopUpdateStatus
{
    public static bool ShouldShowUpdateAvailable(
        bool plannedAsUpdate,
        long installedTimeUpdated,
        bool downloadCompleted,
        long downloadedTimeUpdated
    )
    {
        if (!plannedAsUpdate)
            return false;
        if (!downloadCompleted || downloadedTimeUpdated <= 0)
            return true;
        return installedTimeUpdated < downloadedTimeUpdated;
    }
}
