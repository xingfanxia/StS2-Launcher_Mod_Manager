using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2Mobile.Launcher;

namespace STS2Mobile.Patches;

// Desktop loads several main-thread-only Godot scenes back-to-back in one
// continuation. On Android that creates 0.7-1.4 second ProcessFrame gaps. Keep
// the same operation order and state mutations, but give the renderer an
// opportunity to present between independent scene/map/combat setup steps.
public static class GameLoadFramePacingPatches
{
    public static void Apply(Harmony harmony)
    {
        var loadRun = AccessTools.Method(
            typeof(NGame),
            "LoadRun",
            new[] { typeof(RunState), typeof(SerializableRoom) }
        );
        if (loadRun != null)
        {
            harmony.Patch(
                loadRun,
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(GameLoadFramePacingPatches), nameof(LoadRunPrefix))
                )
            );
        }

        var startCombat = AccessTools.Method(
            typeof(CombatRoom),
            "StartCombat",
            new[] { typeof(IRunState) }
        );
        if (startCombat != null)
        {
            harmony.Patch(
                startCombat,
                prefix: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(GameLoadFramePacingPatches),
                        nameof(StartCombatPrefix)
                    )
                )
            );
        }
    }

    public static bool LoadRunPrefix(
        NGame __instance,
        RunState runState,
        SerializableRoom preFinishedRoom,
        ref Task __result
    )
    {
        __result = LoadRunFramePaced(__instance, runState, preFinishedRoom);
        return false;
    }

    private static async Task LoadRunFramePaced(
        NGame game,
        RunState runState,
        SerializableRoom preFinishedRoom
    )
    {
        await PreloadManager.LoadRunAssets(runState.Players.Select(player => player.Character));
        await PreloadManager.LoadActAssets(runState.Act);

        RunManager.Instance.Launch();
        await NextFrame(game);

        NRun runNode = NRun.Create(runState);
        await NextFrame(game);

        game.RootSceneContainer.SetCurrentScene(runNode);
        await NextFrame(game);

        await RunManager.Instance.GenerateMap();
        await NextFrame(game);

        AbstractRoom restoredRoom = AbstractRoom.FromSerializable(preFinishedRoom, runState);
        await RunManager.Instance.LoadIntoLatestMapCoord(restoredRoom);

        if (RunManager.Instance.MapDrawingsToLoad != null)
        {
            NRun.Instance.GlobalUi.MapScreen.Drawings.LoadDrawings(
                RunManager.Instance.MapDrawingsToLoad
            );
            RunManager.Instance.MapDrawingsToLoad = null;
        }
    }

    public static bool StartCombatPrefix(
        CombatRoom __instance,
        IRunState runState,
        ref Task __result
    )
    {
        __result = StartCombatFramePaced(__instance, runState);
        return false;
    }

    private static async Task StartCombatFramePaced(CombatRoom room, IRunState runState)
    {
        if (!room.Encounter.HaveMonstersBeenGenerated)
            room.Encounter.GenerateMonstersWithSlots(room.CombatState.RunState);

        if (room.ShouldCreateCombat)
            await PreloadManager.LoadRoomCombatAssets(
                room.Encounter,
                runState ?? NullRunState.Instance
            );

        await NextFrame(NGame.Instance);

        foreach (var (monsterModel, slot) in room.Encounter.MonstersWithSlots)
        {
            monsterModel.AssertMutable();
            if (room.ShouldCreateCombat)
            {
                var creature = room.CombatState.CreateCreature(
                    monsterModel,
                    CombatSide.Enemy,
                    slot
                );
                room.CombatState.AddCreature(creature);
            }

            room.CombatState.RunState.CurrentMapPointHistoryEntry?.Rooms.Last()
                .MonsterIds.Add(monsterModel.Id);
        }

        if (room.ShouldCreateCombat)
        {
            NCombatRoom combatNode = NCombatRoom.Create(room, CombatRoomMode.ActiveCombat);
            await NextFrame(NGame.Instance);
            NRun.Instance?.SetCurrentRoom(combatNode);
        }
        else
        {
            NCombatRoom.Instance?.TransitionToActiveCombat(room);
        }

        await NextFrame(NGame.Instance);
        CombatManager.Instance.SetUpCombat(room.CombatState);
        await NextFrame(NGame.Instance);

        if (runState != null)
            await Hook.AfterRoomEntered(runState, room);

        await GameplayPipelineWarmup.CoverFirstHandAsync(
            NCombatRoom.Instance?.Ui?.Hand,
            LocalContext.GetMe(room.CombatState),
            CombatManager.Instance.AfterCombatRoomLoaded
        );
    }

    private static async Task NextFrame(Node owner)
    {
        SceneTree tree = owner?.GetTree();
        if (tree != null)
            await owner.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}
