using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace STS2Mobile.Platform;

// The game fits the two sides of a fight to the screen once, when the room
// opens. Creatures that join later (a summon, an elite splitting into four) are
// placed without refitting, which a wide screen absorbs and a 4:3 screen does
// not: the last enemy ends up past the right edge. Refit whenever the set of
// creatures changes.
internal static class IosCombatLayout
{
    private static MethodInfo _adjust;

    public static void Apply(Harmony harmony)
    {
        try
        {
            _adjust = AccessTools.Method(typeof(NCombatRoom), "AdjustCreatureScaleForAspectRatio");
            if (_adjust == null)
            {
                PatchHelper.Log("[CombatLayout] AdjustCreatureScaleForAspectRatio not found; mid-fight refit unavailable");
                return;
            }
            var refit = new HarmonyMethod(typeof(IosCombatLayout), nameof(CreaturesChanged));
            foreach (var name in new[] { "AddCreature", "RemoveCreatureNode" })
            {
                var method = AccessTools.Method(typeof(NCombatRoom), name);
                if (method != null)
                    harmony.Patch(method, postfix: refit);
            }
            PatchHelper.Log("Patched NCombatRoom creature changes (iOS layout refit)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CombatLayout] could not be patched: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void CreaturesChanged(NCombatRoom __instance)
    {
        var room = __instance;
        // Deferred so the new creature's node and bounds exist before measuring.
        Callable
            .From(() =>
            {
                try
                {
                    if (GodotObject.IsInstanceValid(room) && room.IsInsideTree())
                        _adjust.Invoke(room, null);
                }
                catch (Exception ex)
                {
                    PatchHelper.Log($"[CombatLayout] refit failed: {ex.InnerException?.Message ?? ex.Message}");
                }
            })
            .CallDeferred();
    }
}
