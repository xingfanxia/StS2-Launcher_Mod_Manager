using System;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using STS2Mobile.Launcher;

namespace STS2Mobile.Patches;

// Debug-probe-only transition timing. The patches stay inert in production and
// outside an explicitly armed game capture. They time bounded transition calls
// so a long ProcessFrame interval can be assigned to scene/map/combat setup
// instead of guessing from adjacent preload log lines.
public static class DebugTransitionTimingPatches
{
    private const ulong LogThresholdUsec = 2_000;
    private const string ArmEnvironmentVariable = "STS2_DEBUG_TRANSITION_TIMING";

    private static readonly (string TypeName, string MethodName, int Arity)[] Targets =
    {
        ("MegaCrit.Sts2.Core.Runs.RunManager", "Launch", 0),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "GenerateMap", 0),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "LoadIntoLatestMapCoord", 1),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "EnterMapPointInternal", 4),
        ("MegaCrit.Sts2.Core.Runs.RunManager", "EnterRoomInternal", 2),
        ("MegaCrit.Sts2.Core.Nodes.NRun", "Create", 1),
        ("MegaCrit.Sts2.Core.Nodes.NRun", "_Ready", 0),
        ("MegaCrit.Sts2.Core.Nodes.NRun", "SetCurrentRoom", 1),
        ("MegaCrit.Sts2.Core.Nodes.NSceneContainer", "SetCurrentScene", 1),
        ("MegaCrit.Sts2.Core.Nodes.CommonUi.NGlobalUi", "Initialize", 1),
        ("MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen", "SetMap", 3),
        ("MegaCrit.Sts2.Core.Rooms.AbstractRoom", "FromSerializable", 2),
        ("MegaCrit.Sts2.Core.Rooms.CombatRoom", "EnterInternal", 2),
        ("MegaCrit.Sts2.Core.Combat.CombatState", "CreateCreature", 3),
        ("MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom", "Create", 2),
        ("MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom", "_Ready", 0),
        ("MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom", "CreateAllyNodes", 0),
        ("MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom", "CreateEnemyNodes", 0),
        ("MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom", "SetUpBackground", 1),
        ("MegaCrit.Sts2.Core.Nodes.Combat.NCreature", "Create", 1),
        ("MegaCrit.Sts2.Core.Combat.CombatManager", "SetUpCombat", 1),
        ("MegaCrit.Sts2.Core.Combat.CombatManager", "AfterCombatRoomLoaded", 0),
        ("MegaCrit.Sts2.Core.Combat.CombatManager", "StartCombatInternal", 0),
        ("MegaCrit.Sts2.Core.Combat.CombatManager", "StartTurn", 1),
        ("MegaCrit.Sts2.Core.Combat.CombatManager", "SetupPlayerTurn", 2),
        ("MegaCrit.Sts2.Core.Combat.CombatManager", "AfterCreatureAdded", 1),
        ("MegaCrit.Sts2.Core.Entities.Creatures.Creature", "AfterAddedToRoom", 0),
        ("MegaCrit.Sts2.Core.Models.MonsterModel", "RollMove", 1),
        ("MegaCrit.Sts2.Core.Commands.CardPileCmd", "Draw", 4),
        ("MegaCrit.Sts2.Core.Commands.CardPileCmd", "CreateCardNodeAndUpdateVisuals", 3),
        ("MegaCrit.Sts2.Core.Nodes.Cards.NCard", "Create", 2),
        ("MegaCrit.Sts2.Core.Nodes.Cards.NCard", "_Ready", 0),
        ("MegaCrit.Sts2.Core.Nodes.Cards.NCard", "Reload", 0),
        ("MegaCrit.Sts2.Core.Nodes.Cards.NCard", "UpdateVisuals", 2),
        ("MegaCrit.Sts2.Core.Nodes.Cards.Holders.NHandCardHolder", "Create", 2),
        ("MegaCrit.Sts2.Core.Nodes.Cards.Holders.NHandCardHolder", "_Ready", 0),
        ("MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand", "Add", 2),
        ("MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand", "AddCardHolder", 2),
        ("MegaCrit.Sts2.Core.Nodes.Combat.NCreature", "UpdateIntent", 1),
        ("MegaCrit.Sts2.Core.Nodes.TopBar.NTopBarDeckButton", "OnRelease", 0),
        ("MegaCrit.Sts2.Core.Nodes.TopBar.NTopBarMapButton", "OnRelease", 0),
        ("MegaCrit.Sts2.Core.Nodes.TopBar.NTopBarPauseButton", "OnRelease", 0),
        ("MegaCrit.Sts2.Core.Nodes.Screens.NDeckViewScreen", "ShowScreen", 1),
        ("MegaCrit.Sts2.Core.Nodes.Screens.NDeckViewScreen", "_Ready", 0),
        ("MegaCrit.Sts2.Core.Nodes.Screens.NDeckViewScreen", "DisplayCards", 0),
        ("MegaCrit.Sts2.Core.Nodes.Cards.NCardGrid", "SetCards", 4),
        ("MegaCrit.Sts2.Core.Nodes.Cards.NCardGrid", "InitGrid", 0),
        ("MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer", "Open", 1),
        ("MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer", "Close", 0),
    };

    public static void Apply(Harmony harmony)
    {
        // Harmony prefixes/postfixes still add trampoline cost even when their
        // bodies immediately return. GodotApp sets this process-local flag only
        // for an explicitly armed debug game capture, before .NET bootstrap.
        // Production and ordinary debug sessions therefore patch zero game
        // methods for transition attribution.
        if (
            !string.Equals(
                System.Environment.GetEnvironmentVariable(ArmEnvironmentVariable),
                "1",
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        Assembly gameAssembly = typeof(MegaCrit.Sts2.Core.Nodes.NGame).Assembly;
        var prefix = new HarmonyMethod(
            AccessTools.Method(typeof(DebugTransitionTimingPatches), nameof(TimingPrefix))
        );
        var postfix = new HarmonyMethod(
            AccessTools.Method(typeof(DebugTransitionTimingPatches), nameof(TimingPostfix))
        );

        foreach (var target in Targets)
        {
            try
            {
                Type type = gameAssembly.GetType(target.TypeName);
                MethodInfo method = type
                    ?.GetMethods(
                        BindingFlags.Public
                            | BindingFlags.NonPublic
                            | BindingFlags.Instance
                            | BindingFlags.Static
                    )
                    .SingleOrDefault(candidate =>
                        candidate.Name == target.MethodName
                        && candidate.GetParameters().Length == target.Arity
                    );
                if (method == null)
                {
                    PatchHelper.Log(
                        $"[FrameTrace] target unavailable {target.TypeName}.{target.MethodName}/{target.Arity}"
                    );
                    continue;
                }

                harmony.Patch(method, prefix: prefix, postfix: postfix);
            }
            catch (Exception ex)
            {
                PatchHelper.Log(
                    $"[FrameTrace] patch failed {target.TypeName}.{target.MethodName}: {ex.GetType().Name}"
                );
            }
        }
    }

    public static void TimingPrefix(MethodBase __originalMethod, out ulong __state)
    {
        if (
            __originalMethod?.DeclaringType?.Name == "NTopBarMapButton"
            && __originalMethod.Name == "OnRelease"
        )
            DebugFrameTimeProbe.BeginInteraction("map-open");
        __state = DebugFrameTimeProbe.IsGameCaptureActive ? Time.GetTicksUsec() : 0;
    }

    public static void TimingPostfix(MethodBase __originalMethod, ulong __state)
    {
        if (__state == 0 || !DebugFrameTimeProbe.IsGameCaptureActive)
            return;

        ulong nowUsec = Time.GetTicksUsec();
        if (nowUsec <= __state || nowUsec - __state < LogThresholdUsec)
            return;

        PatchHelper.Log(
            $"[FrameTrace] {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} duration_us={nowUsec - __state}"
        );
    }
}
