using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using STS2Mobile.Patches;

namespace STS2Mobile.Launcher;

// Explicitly armed, debug-build-only proof for the retained deck screen. It
// exercises the same CardPile/CardModel events used by real obtain, remove and
// upgrade operations, verifies that the next screen is rebuilt from current
// state, and restores every in-memory mutation in a finally path. No card,
// profile, save, path, account, device or mod identity is logged.
internal static class DebugDeckCacheMutationProbe
{
    private static readonly FieldInfo CardsField = AccessTools.Field(
        typeof(NCardsViewScreen),
        "_cards"
    );
    private static readonly FieldInfo GridField = AccessTools.Field(
        typeof(NCardsViewScreen),
        "_grid"
    );

    internal static async Task TryRunAsync(Node context, Player player)
    {
        if (!DebugFrameTimeProbe.IsGameCaptureActive || !TryConsumeArm())
            return;

        bool obtain = false;
        bool remove = false;
        bool upgrade = false;
        bool restore = false;
        bool cleanup = false;
        bool error = false;
        CardModel temporary = null;
        CardModel upgradeTarget = null;
        int originalDeckCount = player?.Deck?.Cards?.Count ?? -1;

        try
        {
            if (
                context == null
                || player == null
                || originalDeckCount <= 0
                || !DeckViewPerformancePatches.TryGetCachedScreenForDebug(
                    player,
                    out NDeckViewScreen initialScreen
                )
            )
            {
                throw new InvalidOperationException("deck cache is not ready");
            }

            CardModel source = player.Deck.Cards[0];
            temporary = player.RunState.CloneCard(source);
            player.Deck.AddInternal(temporary);
            bool obtainInvalidated = !DeckViewPerformancePatches.TryGetCachedScreenForDebug(
                player,
                out _
            );
            NDeckViewScreen addedScreen = await OpenAndSettleAsync(context, player);
            IReadOnlyList<CardModel> addedCards = ReadCards(addedScreen);
            obtain =
                obtainInvalidated
                && !ReferenceEquals(initialScreen, addedScreen)
                && addedCards != null
                && addedCards.Count == originalDeckCount + 1
                && addedCards.Contains(temporary);
            await CloseAndSettleAsync(context, addedScreen);

            player.Deck.RemoveInternal(temporary);
            player.RunState.RemoveCard(temporary);
            temporary = null;
            bool removeInvalidated = !DeckViewPerformancePatches.TryGetCachedScreenForDebug(
                player,
                out _
            );
            NDeckViewScreen restoredAfterRemove = await OpenAndSettleAsync(context, player);
            IReadOnlyList<CardModel> restoredCards = ReadCards(restoredAfterRemove);
            NCardGrid restoredGrid = ReadGrid(restoredAfterRemove);
            upgradeTarget = restoredGrid?.CurrentlyDisplayedCards.FirstOrDefault(card =>
                card.IsUpgradable && !card.IsUpgraded
            );
            GodotObject oldCardNode =
                upgradeTarget == null ? null : restoredGrid.GetCardNode(upgradeTarget);
            remove =
                removeInvalidated
                && restoredCards != null
                && restoredCards.Count == originalDeckCount
                && upgradeTarget != null
                && !upgradeTarget.IsUpgraded
                && oldCardNode != null;
            await CloseAndSettleAsync(context, restoredAfterRemove);

            upgradeTarget.UpgradeInternal();
            upgradeTarget.FinalizeUpgradeInternal();
            bool upgradeInvalidated = !DeckViewPerformancePatches.TryGetCachedScreenForDebug(
                player,
                out _
            );
            NDeckViewScreen upgradedScreen = await OpenAndSettleAsync(context, player);
            NCardGrid upgradedGrid = ReadGrid(upgradedScreen);
            GodotObject upgradedCardNode = upgradedGrid?.GetCardNode(upgradeTarget);
            IReadOnlyList<CardModel> upgradedCards = ReadCards(upgradedScreen);
            upgrade =
                upgradeInvalidated
                && !ReferenceEquals(restoredAfterRemove, upgradedScreen)
                && upgradedCards != null
                && upgradedCards.Contains(upgradeTarget)
                && upgradeTarget.IsUpgraded
                && upgradedCardNode != null
                && !ReferenceEquals(oldCardNode, upgradedCardNode);
            await CloseAndSettleAsync(context, upgradedScreen);

            upgradeTarget.DowngradeInternal();
            bool downgradeInvalidated = !DeckViewPerformancePatches.TryGetCachedScreenForDebug(
                player,
                out _
            );
            NDeckViewScreen downgradedScreen = await OpenAndSettleAsync(context, player);
            NCardGrid downgradedGrid = ReadGrid(downgradedScreen);
            GodotObject downgradedCardNode = downgradedGrid?.GetCardNode(upgradeTarget);
            restore =
                downgradeInvalidated
                && !ReferenceEquals(upgradedScreen, downgradedScreen)
                && !upgradeTarget.IsUpgraded
                && downgradedCardNode != null
                && !ReferenceEquals(upgradedCardNode, downgradedCardNode);
            await CloseAndSettleAsync(context, downgradedScreen);
        }
        catch
        {
            error = true;
        }
        finally
        {
            cleanup = RestoreMutations(player, temporary, upgradeTarget, originalDeckCount);
        }

        bool pass = obtain && remove && upgrade && restore && cleanup && !error;
        PatchHelper.Log(
            $"[DeckCacheProbe] result obtain={Bit(obtain)} remove={Bit(remove)} "
                + $"upgrade={Bit(upgrade)} restore={Bit(restore)} "
                + $"cleanup={Bit(cleanup)} error={Bit(error)} pass={Bit(pass)}"
        );
    }

    private static bool TryConsumeArm()
    {
        try
        {
            var app = LauncherModel.GetGodotApp();
            return app != null && (bool)app.Call("consumeDebugDeckCacheMutationProbe");
        }
        catch
        {
            return false;
        }
    }

    private static async Task<NDeckViewScreen> OpenAndSettleAsync(Node context, Player player)
    {
        NDeckViewScreen screen = NDeckViewScreen.ShowScreen(player);
        if (screen == null)
            throw new InvalidOperationException("deck screen was not created");
        await NextFrameAsync(context);
        await NextFrameAsync(context);
        return screen;
    }

    private static async Task CloseAndSettleAsync(Node context, NDeckViewScreen screen)
    {
        if (ReferenceEquals(NCapstoneContainer.Instance?.CurrentCapstoneScreen, screen))
            NCapstoneContainer.Instance.Close();
        await NextFrameAsync(context);
    }

    private static async Task NextFrameAsync(Node context)
    {
        if (context == null || !context.IsInsideTree())
            throw new InvalidOperationException("game tree exited during deck proof");
        SceneTree tree = context.GetTree();
        await context.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private static IReadOnlyList<CardModel> ReadCards(NDeckViewScreen screen) =>
        CardsField?.GetValue(screen) as IReadOnlyList<CardModel>;

    private static NCardGrid ReadGrid(NDeckViewScreen screen) =>
        GridField?.GetValue(screen) as NCardGrid;

    private static bool RestoreMutations(
        Player player,
        CardModel temporary,
        CardModel upgradeTarget,
        int originalDeckCount
    )
    {
        try
        {
            if (NCapstoneContainer.Instance?.CurrentCapstoneScreen is NDeckViewScreen)
                NCapstoneContainer.Instance.Close();

            if (upgradeTarget?.IsUpgraded == true)
                upgradeTarget.DowngradeInternal();

            if (temporary != null)
            {
                if (player.Deck.Cards.Contains(temporary))
                    player.Deck.RemoveInternal(temporary);
                if (player.RunState.ContainsCard(temporary))
                    player.RunState.RemoveCard(temporary);
            }

            return player != null
                && player.Deck.Cards.Count == originalDeckCount
                && (upgradeTarget == null || !upgradeTarget.IsUpgraded)
                && (temporary == null || !player.RunState.ContainsCard(temporary));
        }
        catch
        {
            return false;
        }
    }

    private static int Bit(bool value) => value ? 1 : 0;
}
