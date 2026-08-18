using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.TestSupport;

namespace STS2Mobile.Patches;

// NDeckViewScreen.ShowScreen creates a new card grid on every open, while its
// base close path hides and queues that grid for deletion. Retain exactly one
// warmed screen under the current run tree and reuse it for the same player;
// normal run-tree teardown still frees it and all of its subscriptions.
public static class DeckViewPerformancePatches
{
    private const string PileContentsChangedMethod = "OnPileContentsChanged";

    // These static field signatures must remain game-assembly-neutral. The same
    // STS2Mobile.dll boots the standalone launcher before sts2.dll is downloaded;
    // a closed generic over a game type makes Mono resolve sts2.dll while loading
    // this type, before the game-patch block can fail open.
    private static WeakReference<GodotObject> _cachedScreen;
    private static WeakReference<object> _cachedPlayer;
    private static readonly List<WeakReference<object>> _cachedCards = new();
    private static bool _invalidateAfterClose;

    public static void Apply(Harmony harmony)
    {
        var showScreen = AccessTools.Method(
            typeof(NDeckViewScreen),
            nameof(NDeckViewScreen.ShowScreen),
            new[] { typeof(Player) }
        );
        var afterClosed = AccessTools.Method(
            typeof(NDeckViewScreen),
            nameof(NDeckViewScreen.AfterCapstoneClosed),
            Type.EmptyTypes
        );
        var onPileContentsChanged = AccessTools.Method(
            typeof(NDeckViewScreen),
            PileContentsChangedMethod,
            Type.EmptyTypes
        );
        if (showScreen == null || afterClosed == null || onPileContentsChanged == null)
            return;

        harmony.Patch(
            showScreen,
            prefix: new HarmonyMethod(
                AccessTools.Method(typeof(DeckViewPerformancePatches), nameof(ShowScreenPrefix))
            ),
            postfix: new HarmonyMethod(
                AccessTools.Method(typeof(DeckViewPerformancePatches), nameof(ShowScreenPostfix))
            )
        );
        harmony.Patch(
            afterClosed,
            prefix: new HarmonyMethod(
                AccessTools.Method(
                    typeof(DeckViewPerformancePatches),
                    nameof(AfterCapstoneClosedPrefix)
                )
            )
        );
        harmony.Patch(
            onPileContentsChanged,
            prefix: new HarmonyMethod(
                AccessTools.Method(
                    typeof(DeckViewPerformancePatches),
                    nameof(OnPileContentsChangedPrefix)
                )
            )
        );
    }

    public static bool ShowScreenPrefix(Player player, ref NDeckViewScreen __result)
    {
        if (TestMode.IsOn)
            return true;

        if (
            _cachedScreen == null
            || !_cachedScreen.TryGetTarget(out GodotObject cachedObject)
            || cachedObject is not NDeckViewScreen cached
            || _cachedPlayer == null
            || !_cachedPlayer.TryGetTarget(out object cachedPlayer)
        )
        {
            ClearCachedScreen(queueFree: true);
            return true;
        }

        if (!GodotObject.IsInstanceValid(cached) || !cached.IsInsideTree())
        {
            ClearCachedScreen(queueFree: false);
            return true;
        }

        if (!ReferenceEquals(player, cachedPlayer))
        {
            // A hidden screen is no longer owned by the capstone close path.
            // Retire it before allowing the original method to create the new
            // player's screen. If it is still open, Open(new) will close and
            // free it through the original callback now that the cache is clear.
            ClearCachedScreen(queueFree: true);
            return true;
        }

        try
        {
            cached.Visible = true;
            NDebugAudioManager.Instance?.Play("map_open.mp3");
            NCapstoneContainer.Instance.Open(cached);
            __result = cached;
            return false;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[DeckViewCache] reuse degraded: {ex.GetType().Name}");
            if (GodotObject.IsInstanceValid(cached))
                cached.Visible = false;
            ClearCachedScreen(queueFree: true);
            return true;
        }
    }

    public static void ShowScreenPostfix(Player player, NDeckViewScreen __result)
    {
        if (TestMode.IsOn || __result == null || !GodotObject.IsInstanceValid(__result))
            return;

        // Harmony runs the postfix even when the reuse prefix suppresses the
        // original method. Do not detach and reattach an unchanged cache.
        if (
            _cachedScreen != null
            && _cachedScreen.TryGetTarget(out GodotObject cachedObject)
            && cachedObject is NDeckViewScreen cached
            && ReferenceEquals(cached, __result)
            && _cachedPlayer != null
            && _cachedPlayer.TryGetTarget(out object cachedPlayer)
            && ReferenceEquals(cachedPlayer, player)
        )
        {
            return;
        }

        ClearCachedScreen(queueFree: true);
        _cachedScreen = new WeakReference<GodotObject>(__result);
        _cachedPlayer = new WeakReference<object>(player);
        _invalidateAfterClose = false;
        __result.TreeExiting -= OnCachedScreenTreeExiting;
        __result.TreeExiting += OnCachedScreenTreeExiting;
        SubscribeToCachedCards(player);
    }

    public static bool AfterCapstoneClosedPrefix(NDeckViewScreen __instance)
    {
        if (TestMode.IsOn)
            return true;

        if (
            _cachedScreen == null
            || !_cachedScreen.TryGetTarget(out GodotObject cachedObject)
            || cachedObject is not NDeckViewScreen cached
            || !ReferenceEquals(__instance, cached)
        )
        {
            return true;
        }

        if (_invalidateAfterClose)
        {
            // Upstream already refreshed a visible screen after a pile change,
            // or a card upgraded while it was open. Let the original close path
            // free it and rebuild current subscriptions on the next open.
            ClearCachedScreen(queueFree: false);
            return true;
        }

        try
        {
            // Exact retained-screen equivalent of NDeckViewScreen's original
            // override: preserve visibility and top-bar state, but omit only
            // NCardsViewScreen.QueueFreeSafely(). NCapstoneContainer already set
            // ProcessMode.Disabled before invoking this callback.
            __instance.Visible = false;
            NRun.Instance?.GlobalUi.TopBar.Deck.ToggleAnimState();
            return false;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[DeckViewCache] retain degraded: {ex.GetType().Name}");
            ClearCachedScreen(queueFree: false);
            return true;
        }
    }

    public static bool OnPileContentsChangedPrefix(NDeckViewScreen __instance)
    {
        if (TestMode.IsOn)
            return true;

        // The upstream event handler reconstructs every card holder even while
        // this retained screen is hidden. Drop the cache instead, so obtaining
        // or removing a card cannot add an invisible full-grid spike.
        if (
            _cachedScreen == null
            || !_cachedScreen.TryGetTarget(out GodotObject cachedObject)
            || cachedObject is not NDeckViewScreen cached
            || !ReferenceEquals(__instance, cached)
        )
        {
            return true;
        }

        if (__instance.Visible)
        {
            _invalidateAfterClose = true;
            return true;
        }

        try
        {
            ClearCachedScreen(queueFree: true);
            return false;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[DeckViewCache] invalidation degraded: {ex.GetType().Name}");
            return true;
        }
    }

    // Used only by the explicitly armed debug mutation proof. Keep product
    // ownership weak and expose no player/card identity or save content.
    internal static bool TryGetCachedScreenForDebug(Player player, out NDeckViewScreen screen)
    {
        screen = null;
        if (
            _cachedScreen == null
            || !_cachedScreen.TryGetTarget(out GodotObject cachedObject)
            || cachedObject is not NDeckViewScreen cached
        )
        {
            return false;
        }

        screen = cached;
        return GodotObject.IsInstanceValid(screen)
            && _cachedPlayer != null
            && _cachedPlayer.TryGetTarget(out object cachedPlayer)
            && ReferenceEquals(player, cachedPlayer);
    }

    private static void SubscribeToCachedCards(Player player)
    {
        DetachCachedCardSubscriptions();
        foreach (CardModel card in player.Deck.Cards)
        {
            card.Upgraded -= OnCachedCardUpgraded;
            card.Upgraded += OnCachedCardUpgraded;
            _cachedCards.Add(new WeakReference<object>(card));
        }
    }

    private static void DetachCachedCardSubscriptions()
    {
        foreach (WeakReference<object> weakCard in _cachedCards)
        {
            if (weakCard.TryGetTarget(out object cachedObject) && cachedObject is CardModel card)
                card.Upgraded -= OnCachedCardUpgraded;
        }
        _cachedCards.Clear();
    }

    private static void OnCachedCardUpgraded()
    {
        if (
            _cachedScreen == null
            || !_cachedScreen.TryGetTarget(out GodotObject cachedObject)
            || cachedObject is not NDeckViewScreen cached
            || !GodotObject.IsInstanceValid(cached)
        )
        {
            ClearCachedScreen(queueFree: false);
            return;
        }

        if (
            cached.Visible
            || ReferenceEquals(NCapstoneContainer.Instance.CurrentCapstoneScreen, cached)
        )
        {
            _invalidateAfterClose = true;
            return;
        }

        ClearCachedScreen(queueFree: true);
    }

    private static void OnCachedScreenTreeExiting()
    {
        ClearCachedScreen(queueFree: false);
    }

    private static void ClearCachedScreen(bool queueFree)
    {
        NDeckViewScreen cached = null;
        if (_cachedScreen != null && _cachedScreen.TryGetTarget(out GodotObject cachedObject))
        {
            cached = cachedObject as NDeckViewScreen;
        }

        _cachedScreen = null;
        _cachedPlayer = null;
        _invalidateAfterClose = false;
        DetachCachedCardSubscriptions();

        if (cached == null || !GodotObject.IsInstanceValid(cached))
            return;

        cached.TreeExiting -= OnCachedScreenTreeExiting;
        if (
            queueFree && !ReferenceEquals(NCapstoneContainer.Instance.CurrentCapstoneScreen, cached)
        )
        {
            cached.QueueFree();
        }
    }
}
