using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using STS2Mobile.Launcher;

namespace STS2Mobile.Patches;

// LaunchMainMenu still loads all main-menu essentials when skipLogo=true; it
// only omits the logo scene and its timed animation. Fail open if upstream
// changes the private method so game startup remains available, just slower.
internal static class CoveredStartupLogoPatches
{
    internal static void Apply(Harmony harmony)
    {
        try
        {
            var target = AccessTools.DeclaredMethod(
                typeof(NGame),
                "LaunchMainMenu",
                new[] { typeof(bool) }
            );
            if (target == null)
            {
                PatchHelper.Log(
                    "Covered startup logo optimization inactive: NGame.LaunchMainMenu(bool) not found"
                );
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    typeof(CoveredStartupLogoPatches),
                    nameof(LaunchMainMenuPrefix)
                )
            );
            PatchHelper.Log("Patched NGame.LaunchMainMenu(bool) for covered startup");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Covered startup logo optimization inactive: {ex.Message}");
        }
    }

    public static void LaunchMainMenuPrefix(ref bool skipLogo)
    {
        bool shouldSkip = CoveredStartupLogoPolicy.ShouldSkipLogo(skipLogo);
        if (!skipLogo && shouldSkip)
            PatchHelper.Log("Skipping logo animation hidden by launcher startup progress");
        skipLogo = shouldSkip;
    }
}
