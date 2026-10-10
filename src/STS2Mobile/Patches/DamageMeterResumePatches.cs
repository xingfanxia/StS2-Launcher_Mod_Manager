using System;
using Godot;
using MegaCrit.Sts2.Core.Runs;

namespace STS2Mobile.Patches;

// The DamageMeter mod hides its windows on the main menu and only shows them
// again when a combat is set up. A run that is continued into a reward screen,
// the map or any other room without a fight therefore has no meter until the
// next fight. Ask the mod for its windows as soon as a run starts or is loaded.
public static class DamageMeterResumePatches
{
    public static void Apply()
    {
        try
        {
            RunManager.Instance.RunStarted += _ => Callable.From(ShowWindows).CallDeferred();
            PatchHelper.Log("Subscribed RunManager.RunStarted (DamageMeter windows)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"FAILED RunManager.RunStarted: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ShowWindows()
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "DamageMeter")
                    continue;
                var show = assembly
                    .GetType("DamageMeter.Scripts.DamageMeterUI")
                    ?.GetMethod("CreateAndAttach", Type.EmptyTypes);
                if (show == null)
                {
                    PatchHelper.Log("[DamageMeter] DamageMeterUI.CreateAndAttach not found; windows wait for the next combat");
                    return;
                }
                show.Invoke(null, null);
                return;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[DamageMeter] could not show windows on run start: {ex.GetBaseException().Message}");
        }
    }
}
