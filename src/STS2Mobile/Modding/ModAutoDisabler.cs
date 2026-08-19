using System;
using System.Linq;

namespace STS2Mobile.Modding;

// Persists the narrow, proven-incompatible case by moving the owning top-level
// mod folder to ModsDisabled/. Folder location remains the source of truth, so
// this is the same reversible operation exposed by the Mod Hub Disable button.
internal static class ModAutoDisabler
{
    internal static ModAutoDisableResult TryDisable(string modId, string assemblyLocation)
    {
        if (string.IsNullOrWhiteSpace(modId))
            return ModAutoDisableResult.Failed("The rejected mod has no stable manifest id.");

        var matches = ModScanner.FindEnabledById(modId);
        var selectedTopLevelDir = ModAutoDisablePolicy.SelectTopLevelDirectory(
            modId,
            assemblyLocation,
            AppPaths.ExternalModsDir,
            matches.Select(m => new ModAutoDisableCandidate(
                m.Id,
                m.TopLevelDir,
                ModScanner.ContainsOnlyManifestId(m.TopLevelDir, modId)
            ))
        );
        if (selectedTopLevelDir == null)
        {
            return ModAutoDisableResult.Failed(
                "The owning enabled mod folder could not be resolved uniquely."
            );
        }

        var info = matches.FirstOrDefault(m =>
            ModAutoDisablePolicy.PathsEqual(m.TopLevelDir, selectedTopLevelDir)
        );
        if (info == null)
            return ModAutoDisableResult.Failed("The owning mod folder changed during detection.");
        if (!ModScanner.ContainsOnlyManifestId(info.TopLevelDir, modId))
        {
            return ModAutoDisableResult.Failed(
                "The owning folder changed or contains multiple bundled mods."
            );
        }

        var (ok, error) = ModStasher.Disable(info);
        if (!ok)
            return ModAutoDisableResult.Failed(error ?? "The mod folder could not be moved.");

        // The folder move is authoritative. Best-effort reconciliation makes the
        // disabled state visible in every launcher tab immediately on next mount.
        try
        {
            ModConfig.Load().Reconcile(ModScanner.Scan());
        }
        catch (Exception ex)
        {
            PatchHelper.Log(
                $"[ModCompat] Auto-disable registry reconciliation degraded: {ex.GetType().Name}"
            );
        }

        return ModAutoDisableResult.Succeeded(selectedTopLevelDir);
    }
}

internal readonly record struct ModAutoDisableResult(
    bool Disabled,
    string TopLevelDir,
    string Error
)
{
    internal static ModAutoDisableResult Succeeded(string topLevelDir) =>
        new(true, topLevelDir, null);

    internal static ModAutoDisableResult Failed(string error) => new(false, null, error);
}
