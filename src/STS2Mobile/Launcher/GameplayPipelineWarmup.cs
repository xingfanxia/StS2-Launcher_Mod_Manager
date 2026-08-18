using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher;

// Canvas pipelines compile synchronously the first time the real draw animation
// renders them. Resource scans and detached/fake cards cannot cover every variant
// used by the live hand. Keep the normal first-turn path intact, but present it
// under an explicit loading cover and reveal combat only after the real hand and
// its pipeline set have stopped changing.
internal static class GameplayPipelineWarmup
{
    private const int StableFrameWindowMs = 650;
    private const int MaximumWaitMs = 7_000;
    private const int DeckViewMaximumWaitMs = 4_000;
    private static bool _attemptedForProcess;

    // Read only by the debug frame probe so baseline and fixed captures begin
    // their interactive segment after the same real-hand-ready boundary. This
    // stays false outside the one covered first-combat warmup.
    internal static bool IsActive { get; private set; }

    internal static async Task CoverFirstHandAsync(
        NPlayerHand hand,
        Player player,
        Action startFirstTurn
    )
    {
        ArgumentNullException.ThrowIfNull(startFirstTurn);

        if (
            _attemptedForProcess
            || !OS.HasFeature("android")
            || hand == null
            || !hand.IsInsideTree()
            || hand.GetViewport() is SubViewport
        )
        {
            startFirstTurn();
            return;
        }
        _attemptedForProcess = true;
        var coveredTotal = Stopwatch.StartNew();

        CanvasLayer cover;
        try
        {
            cover = CreateOpaqueCover();
            hand.GetTree().Root.AddChild(cover);
            IsActive = true;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplayPipelineWarmup] cover unavailable: {ex.GetType().Name}");
            startFirstTurn();
            return;
        }

        // Starting the real turn is game behavior, not an optimization. Never
        // swallow or downgrade an exception from it.
        try
        {
            startFirstTurn();
        }
        catch
        {
            IsActive = false;
            cover.QueueFree();
            throw;
        }

        try
        {
            await WaitForStableFirstHandAsync(hand);
            await WarmDeckViewAsync(hand, player);
            await DebugDeckCacheMutationProbe.TryRunAsync(hand, player);
        }
        catch (Exception ex)
        {
            // A future scene change may degrade the cover, but must not block
            // combat or change card state.
            PatchHelper.Log($"[GameplayPipelineWarmup] cover degraded: {ex.GetType().Name}");
        }
        finally
        {
            PatchHelper.Log(
                $"[GameplayPipelineWarmup] cover summary elapsed_us="
                    + $"{coveredTotal.ElapsedTicks * 1_000_000 / Stopwatch.Frequency}"
            );
            if (GodotObject.IsInstanceValid(cover))
                cover.QueueFree();
            IsActive = false;
            if (hand.IsInsideTree())
            {
                await hand.ToSignal(hand.GetTree(), SceneTree.SignalName.ProcessFrame);
                DebugFrameTimeProbe.BeginGameplayInteractiveSegment();
            }
        }
    }

    private static async Task WarmDeckViewAsync(NPlayerHand hand, Player player)
    {
        if (player == null || !hand.IsInsideTree())
            return;

        var total = Stopwatch.StartNew();
        var stable = Stopwatch.StartNew();
        long canvasBefore = ReadCanvasPipelineCount();
        long lastCanvasCount = canvasBefore;
        NDeckViewScreen screen = null;

        try
        {
            await PrimeCachedRunSubmenusAsync(hand);

            // Use the exact real capstone screen. DeckViewPerformancePatches
            // retains this instance, so the user's first later open reuses the
            // already-built card grid instead of synchronously creating it.
            screen = NDeckViewScreen.ShowScreen(player);
            if (screen == null)
                return;

            while (total.ElapsedMilliseconds < DeckViewMaximumWaitMs && hand.IsInsideTree())
            {
                SceneTree tree = hand.GetTree();
                await hand.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

                long canvasCount = ReadCanvasPipelineCount();
                if (canvasCount != lastCanvasCount)
                {
                    lastCanvasCount = canvasCount;
                    stable.Restart();
                    continue;
                }

                if (stable.ElapsedMilliseconds < StableFrameWindowMs)
                    continue;

                await hand.ToSignal(
                    RenderingServer.Singleton,
                    RenderingServer.SignalName.FramePostDraw
                );
                break;
            }
        }
        finally
        {
            var currentScreen = NCapstoneContainer.Instance?.CurrentCapstoneScreen;
            if (currentScreen == screen || (screen == null && currentScreen is NDeckViewScreen))
            {
                NCapstoneContainer.Instance.Close();
            }
        }

        if (hand.IsInsideTree())
        {
            await hand.ToSignal(hand.GetTree(), SceneTree.SignalName.ProcessFrame);
            await hand.ToSignal(
                RenderingServer.Singleton,
                RenderingServer.SignalName.FramePostDraw
            );
        }

        PatchHelper.Log(
            $"[GameplayPipelineWarmup] deck view ready in {total.ElapsedMilliseconds}ms "
                + $"canvas_delta={lastCanvasCount - canvasBefore}"
        );
    }

    private static async Task PrimeCachedRunSubmenusAsync(NPlayerHand hand)
    {
        var total = Stopwatch.StartNew();
        NRun.Instance?.GlobalUi?.SubmenuStack?.Stack?.GetSubmenuType<NPauseMenu>();
        if (hand.IsInsideTree())
            await hand.ToSignal(hand.GetTree(), SceneTree.SignalName.ProcessFrame);
        PatchHelper.Log(
            $"[GameplayPipelineWarmup] pause menu cached in {total.ElapsedMilliseconds}ms"
        );
    }

    private static async Task WaitForStableFirstHandAsync(NPlayerHand hand)
    {
        var total = Stopwatch.StartNew();
        var stable = Stopwatch.StartNew();
        int lastHolderCount = -1;
        long lastCanvasCount = ReadCanvasPipelineCount();
        long canvasBefore = lastCanvasCount;
        bool sawRealCard = false;

        while (total.ElapsedMilliseconds < MaximumWaitMs && hand.IsInsideTree())
        {
            SceneTree tree = hand.GetTree();
            await hand.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            int holderCount = hand.ActiveHolders.Count;
            long canvasCount = ReadCanvasPipelineCount();
            if (holderCount > 0)
                sawRealCard = true;

            if (!sawRealCard || holderCount != lastHolderCount || canvasCount != lastCanvasCount)
            {
                lastHolderCount = holderCount;
                lastCanvasCount = canvasCount;
                stable.Restart();
                continue;
            }

            if (stable.ElapsedMilliseconds < StableFrameWindowMs)
                continue;

            await hand.ToSignal(
                RenderingServer.Singleton,
                RenderingServer.SignalName.FramePostDraw
            );
            PatchHelper.Log(
                $"[GameplayPipelineWarmup] real hand ready in {total.ElapsedMilliseconds}ms "
                    + $"cards={holderCount} canvas_delta={canvasCount - canvasBefore}"
            );
            return;
        }

        PatchHelper.Log(
            $"[GameplayPipelineWarmup] cover timed out after {total.ElapsedMilliseconds}ms"
        );
    }

    private static CanvasLayer CreateOpaqueCover()
    {
        var layer = new CanvasLayer { Layer = 100 };

        var background = new ScreenBackground();
        layer.AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        center.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(center);

        var status = new Label
        {
            Text = Loc.Tr("게임 렌더링 준비 중…", "Preparing gameplay rendering…"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        status.AddThemeFontSizeOverride("font_size", 26);
        status.AddThemeColorOverride("font_color", Colors.White);
        center.AddChild(status);

        return layer;
    }

    private static long ReadCanvasPipelineCount() =>
        (long)Performance.GetMonitor(Performance.Monitor.PipelineCompilationsCanvas);
}
