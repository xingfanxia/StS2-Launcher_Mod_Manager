using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Saves;

namespace STS2MobileIos.Patches;

// Disables desktop-only platform features that are unavailable or unnecessary on mobile:
// Steam initialization, Sentry crash reporting, system info logging, and telemetry opt-in.
public static class PlatformPatches
{
    // prefix on MegaCrit.Sts2.Core.Nodes.NGame.InitializePlatform (returns Task<bool>)
    public static bool InitializePlatformPrefix(ref Task<bool> __result)
    {
        PatchHelper.Log("Skipping Steam initialization (mobile)");
        PatchHelper.Log("Using isolated iOS saves; automatic cloud sync is disabled.");
        __result = Task.FromResult(true);
        return false;
    }

    // prefix on MegaCrit.Sts2.Core.Debug.OsDebugInfo.LogSystemInfo (static, returns Task).
    // Return a completed Task so callers can still await the skipped operation.
    public static bool LogSystemInfoPrefix(ref Task __result)
    {
        __result = Task.CompletedTask;
        return false;
    }

    // prefix on MegaCrit.Sts2.Core.Debug.SentryService.Initialize (static void) —
    // skips Sentry entirely (the Sentry GDExtension is not bundled in the iOS build).
    public static bool SkipPrefix() => false;

    // prefix on MegaCrit.Sts2.Core.Saves.PrefsSave.get_UploadData —
    // forces telemetry opt-in to false.
    public static bool ReturnFalsePrefix(ref bool __result)
    {
        __result = false;
        return false;
    }

    // prefix on MegaCrit.Sts2.Core.Saves.GodotFileIo.CreateDirectory.
    // NullPlatformUtilStrategy's constructor calls CreateDirectory(".") which fails
    // on mobile because "." is not a valid absolute Godot path.
    // Skip paths that aren't valid Godot absolute paths (must contain "://").
    public static bool CreateDirectoryPrefix(GodotFileIo __instance, string directoryPath)
    {
        var fullPath = __instance.GetFullPath(directoryPath);
        if (!fullPath.Contains("://"))
            return false;
        return true;
    }
}
