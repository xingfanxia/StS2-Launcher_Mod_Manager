using System;

namespace STS2Mobile.Launcher;

// Process-local recovery override. It never writes the user's mod configuration
// and naturally disappears on the next real process launch.
internal static class ModRecoverySession
{
    private static readonly object Lock = new();
    private static ModRecoveryPlan _current = ModRecoveryPlan.Normal;

    public static ModRecoveryPlan Current
    {
        get
        {
            lock (Lock)
                return _current;
        }
    }

    public static void Configure(ModRecoveryPlan plan)
    {
        lock (Lock)
            _current = plan ?? ModRecoveryPlan.Normal;
        PatchHelper.Log(
            $"[Recovery] session action={_current.Action} "
                + $"selectedMods={_current.SelectedModCount}/{_current.TotalModCount}"
        );
    }

    public static bool ShouldExposeDirectory(string path) =>
        Current.ShouldExposeDirectory(AppPaths.ExternalModsDir, path);

    public static bool ShouldExposeFile(string path) =>
        Current.ShouldExposeFile(AppPaths.ExternalModsDir, path);
}
