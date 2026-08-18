using System;
using System.Collections.Generic;
using System.Threading;
using Godot;

namespace STS2Mobile.Launcher;

// Phase-0 metric validator. The Android bridge returns a mode only for a
// version name ending in -debug, so production does not attach a per-frame
// callback. The fixed sleep is intentionally a single controlled timing fault,
// not a product wait or pacing mechanism.
internal sealed class DebugFrameTimeProbe
{
    private const int ValidationTargetIntervals = 180;
    private const ulong LongCaptureUsec = 120_000_000;
    private const ulong ExtendedCaptureUsec = 300_000_000;
    private const ulong MenuCaptureUsec = 60_000_000;
    private const ulong MenuSettleUsec = 5_000_000;
    private const int InjectAtFrame = 60;
    private const int InjectedStallMs = 100;
    private const long SpikeThresholdUsec = 50_000;
    private const int MaxSpikeMarkers = 64;
    private const int InteractionSampleCount = 60;

    private readonly SceneTree _tree;
    private readonly string _mode;
    private readonly List<long> _intervals = new(20_000);
    private readonly long _frameBudgetUsec;
    private ulong _lastFrameUsec;
    private ulong _captureStartUsec;
    private int _frameCount;
    private bool _finished;
    private int _spikeMarkers;
    private string _segment = "full";
    private ulong _interactiveCandidateStartUsec;
    private int _interactiveCandidateHolderCount = -1;
    private long _interactiveCandidateCanvasCount = -1;
    private ulong _menuSettleStartUsec;
    private readonly List<long> _interactionIntervals = new(InteractionSampleCount);
    private string _interactionName;

    internal static bool IsGameCaptureActive { get; private set; }
    internal static bool ShouldAutoContinueRecoverySession { get; private set; }
    private static DebugFrameTimeProbe ActiveGameCapture { get; set; }

    private DebugFrameTimeProbe(SceneTree tree, string mode)
    {
        _tree = tree;
        _mode = mode;
        _frameBudgetUsec = ReadFrameBudgetUsec();
    }

    public static void TryStart(Node context, string point)
    {
        try
        {
            var app = LauncherModel.GetGodotApp();
            var mode = app == null ? "" : (string)app.Call("consumeDebugFrameProbe", point ?? "");
            if (
                mode != "control"
                && mode != "stall-100"
                && mode != "launcher-120"
                && mode != "game-120"
                && mode != "game-baseline-120"
                && mode != "game-baseline-safe-120"
                && mode != "game-baseline-partition-120"
                && mode != "game-quickrestart-baseline-partition-120"
                && mode != "game-quickrestart-partition-120"
                && mode != "game-safe-120"
                && mode != "game-safe-300"
                && mode != "game-partition-120"
                && mode != "game-menu-60"
                && mode != "game-menu-safe-60"
                && mode != "game-menu-partition-60"
            )
                return;

            var tree = context?.GetTree();
            if (tree == null)
                return;

            var probe = new DebugFrameTimeProbe(tree, mode);
            tree.ProcessFrame += probe.OnProcessFrame;
            IsGameCaptureActive = IsGameCapture(mode);
            ShouldAutoContinueRecoverySession = UsesDebugModRecovery(mode);
            if (IsGameCaptureActive)
                ActiveGameCapture = probe;
            string target = IsLongCapture(mode)
                ? $"{ReadCaptureTargetUsec(mode) / 1_000_000}s"
                : $"{ValidationTargetIntervals}frames";
            PatchHelper.Log(
                $"[FrameProbe] started mode={mode} point={point} target={target} "
                    + $"budget_us={probe._frameBudgetUsec}"
            );
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[FrameProbe] start failed: {ex.GetType().Name}");
        }
    }

    internal static void BeginGameplayInteractiveSegment()
    {
        DebugFrameTimeProbe probe = ActiveGameCapture;
        if (probe == null || probe._finished || probe._segment == "gameplay-interactive")
            return;

        probe.LogSummary("covered-load");
        probe._intervals.Clear();
        probe._lastFrameUsec = 0;
        probe._captureStartUsec = 0;
        probe._frameCount = 0;
        probe._spikeMarkers = 0;
        probe._segment = "gameplay-interactive";
        PatchHelper.Log(
            $"[FrameProbe] segment started mode={probe._mode} segment={probe._segment} "
                + $"target={ReadCaptureTargetUsec(probe._mode) / 1_000_000}s "
                + $"budget_us={probe._frameBudgetUsec}"
        );
    }

    internal static void BeginInteraction(string name)
    {
        DebugFrameTimeProbe probe = ActiveGameCapture;
        if (
            probe == null
            || probe._finished
            || probe._segment != "gameplay-interactive"
            || name != "map-open"
            || probe._interactionName != null
        )
            return;
        probe._interactionName = name;
        probe._interactionIntervals.Clear();
        PatchHelper.Log($"[InteractionProbe] started name={name}");
    }

    private void OnProcessFrame()
    {
        if (_finished)
            return;

        try
        {
            ulong nowUsec = Time.GetTicksUsec();
            if (_captureStartUsec == 0)
                _captureStartUsec = nowUsec;
            if (_lastFrameUsec != 0 && nowUsec > _lastFrameUsec)
            {
                long intervalUsec = (long)(nowUsec - _lastFrameUsec);
                _intervals.Add(intervalUsec);
                SampleInteraction(intervalUsec);
                if (
                    IsLongCapture(_mode)
                    && intervalUsec > SpikeThresholdUsec
                    && _spikeMarkers < MaxSpikeMarkers
                )
                {
                    _spikeMarkers++;
                    PatchHelper.Log(
                        $"[FrameProbe] spike elapsed_ms={(nowUsec - _captureStartUsec) / 1_000} "
                            + $"interval_us={intervalUsec} {ReadPipelineCompilationCounts()}"
                    );
                    LauncherModel
                        .GetGodotApp()
                        ?.Call(
                            "markDebugFrameSpike",
                            (long)(nowUsec - _captureStartUsec),
                            intervalUsec
                        );
                }
            }
            _lastFrameUsec = nowUsec;
            _frameCount++;

            if (TryBeginGameplayInteractiveSegment(nowUsec))
                return;
            if (TryBeginGameMenuIdleSegment(nowUsec))
                return;

            if (_mode == "stall-100" && _frameCount == InjectAtFrame)
            {
                PatchHelper.Log("[FrameProbe] inject begin stall_ms=100");
                Thread.Sleep(InjectedStallMs);
                PatchHelper.Log("[FrameProbe] inject end stall_ms=100");
            }

            bool complete = IsLongCapture(_mode)
                ? nowUsec - _captureStartUsec >= ReadCaptureTargetUsec(_mode)
                : _intervals.Count >= ValidationTargetIntervals;
            if (complete)
                Complete();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[FrameProbe] sample failed: {ex.GetType().Name}");
            Detach();
        }
    }

    private void Complete()
    {
        LogSummary(_segment);
        Detach();
    }

    private void LogSummary(string segment)
    {
        if (_intervals.Count == 0 || _captureStartUsec == 0 || _lastFrameUsec == 0)
            return;
        var summary = FrameTimeSummary.Create(_intervals, _frameBudgetUsec);
        ulong elapsedUsec = _lastFrameUsec - _captureStartUsec;
        PatchHelper.Log(
            $"[FrameProbe] summary mode={_mode} segment={segment} samples={summary.Count} "
                + $"elapsed_ms={elapsedUsec / 1_000} budget_us={summary.FrameBudgetUsec} "
                + $"p50_us={summary.P50Usec} p95_us={summary.P95Usec} "
                + $"p99_us={summary.P99Usec} max_us={summary.MaxUsec} "
                + $"over_1x={summary.Over1XBudget} over_2x={summary.Over2XBudget} "
                + $"over_3x={summary.Over3XBudget} "
                + $"max_consecutive_2x={summary.MaxConsecutiveOver2X} "
                + $"over_50ms={summary.Over50Ms} over_100ms={summary.Over100Ms} "
                + $"over_250ms={summary.Over250Ms}"
        );
        Patches.QuickRestartPerformanceCompatPatches.LogAndResetDebugSummary(segment);
    }

    private static bool IsLongCapture(string mode) =>
        mode == "launcher-120"
        || mode == "game-120"
        || mode == "game-baseline-120"
        || mode == "game-baseline-safe-120"
        || mode == "game-baseline-partition-120"
        || mode == "game-quickrestart-baseline-partition-120"
        || mode == "game-quickrestart-partition-120"
        || mode == "game-safe-120"
        || mode == "game-safe-300"
        || mode == "game-partition-120"
        || IsMenuCapture(mode);

    private static bool IsGameCapture(string mode) =>
        mode == "game-120"
        || mode == "game-baseline-120"
        || mode == "game-baseline-safe-120"
        || mode == "game-baseline-partition-120"
        || mode == "game-quickrestart-baseline-partition-120"
        || mode == "game-quickrestart-partition-120"
        || mode == "game-safe-120"
        || mode == "game-safe-300"
        || mode == "game-partition-120"
        || IsMenuCapture(mode);

    private static bool IsGameplayCapture(string mode) =>
        mode == "game-120"
        || mode == "game-baseline-120"
        || mode == "game-baseline-safe-120"
        || mode == "game-baseline-partition-120"
        || mode == "game-quickrestart-baseline-partition-120"
        || mode == "game-quickrestart-partition-120"
        || mode == "game-safe-120"
        || mode == "game-safe-300"
        || mode == "game-partition-120";

    private static bool IsMenuCapture(string mode) =>
        mode == "game-menu-60" || mode == "game-menu-safe-60" || mode == "game-menu-partition-60";

    private static bool UsesDebugModRecovery(string mode) =>
        mode == "game-baseline-safe-120"
        || mode == "game-baseline-partition-120"
        || mode == "game-quickrestart-baseline-partition-120"
        || mode == "game-quickrestart-partition-120"
        || mode == "game-safe-120"
        || mode == "game-safe-300"
        || mode == "game-partition-120"
        || mode == "game-menu-safe-60"
        || mode == "game-menu-partition-60";

    private static ulong ReadCaptureTargetUsec(string mode) =>
        mode == "game-safe-300" ? ExtendedCaptureUsec
        : IsMenuCapture(mode) ? MenuCaptureUsec
        : LongCaptureUsec;

    // The baseline deliberately bypasses the gameplay pacing/warmup patches,
    // so it cannot rely on GameplayPipelineWarmup to switch the capture from
    // covered load to real interaction. Observe the real hand instead. The
    // fixed path waits until its cover is gone; both modes therefore start the
    // measured segment only after the hand and canvas pipeline count have been
    // stable for the same interval.
    private bool TryBeginGameplayInteractiveSegment(ulong nowUsec)
    {
        if (!IsGameplayCapture(_mode) || _segment == "gameplay-interactive")
            return false;

        var hand = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.Ui?.Hand;
        if (
            GameplayPipelineWarmup.IsActive
            || hand == null
            || !hand.IsInsideTree()
            || hand.ActiveHolders.Count == 0
        )
        {
            ResetInteractiveCandidate();
            return false;
        }

        int holderCount = hand.ActiveHolders.Count;
        long canvasCount = (long)
            Performance.GetMonitor(Performance.Monitor.PipelineCompilationsCanvas);
        if (
            _interactiveCandidateStartUsec == 0
            || holderCount != _interactiveCandidateHolderCount
            || canvasCount != _interactiveCandidateCanvasCount
        )
        {
            _interactiveCandidateStartUsec = nowUsec;
            _interactiveCandidateHolderCount = holderCount;
            _interactiveCandidateCanvasCount = canvasCount;
            return false;
        }

        if (nowUsec - _interactiveCandidateStartUsec < 650_000)
            return false;

        BeginGameplayInteractiveSegment();
        ResetInteractiveCandidate();
        return true;
    }

    private bool TryBeginGameMenuIdleSegment(ulong nowUsec)
    {
        if (!IsMenuCapture(_mode) || _segment == "game-menu-idle")
            return false;

        if (_menuSettleStartUsec == 0)
        {
            _menuSettleStartUsec = nowUsec;
            return false;
        }
        if (nowUsec - _menuSettleStartUsec < MenuSettleUsec)
            return false;

        LogSummary("covered-load");
        _intervals.Clear();
        _lastFrameUsec = 0;
        _captureStartUsec = 0;
        _frameCount = 0;
        _spikeMarkers = 0;
        _segment = "game-menu-idle";
        PatchHelper.Log(
            $"[FrameProbe] segment started mode={_mode} segment={_segment} "
                + $"target={MenuCaptureUsec / 1_000_000}s budget_us={_frameBudgetUsec}"
        );
        return true;
    }

    private void ResetInteractiveCandidate()
    {
        _interactiveCandidateStartUsec = 0;
        _interactiveCandidateHolderCount = -1;
        _interactiveCandidateCanvasCount = -1;
    }

    private void SampleInteraction(long intervalUsec)
    {
        if (_interactionName == null)
            return;
        _interactionIntervals.Add(intervalUsec);
        if (_interactionIntervals.Count < InteractionSampleCount)
            return;

        var summary = FrameTimeSummary.Create(_interactionIntervals, _frameBudgetUsec);
        PatchHelper.Log(
            $"[InteractionProbe] summary name={_interactionName} samples={summary.Count} "
                + $"p50_us={summary.P50Usec} p95_us={summary.P95Usec} "
                + $"p99_us={summary.P99Usec} max_us={summary.MaxUsec} "
                + $"over_2x={summary.Over2XBudget} over_100ms={summary.Over100Ms}"
        );
        _interactionName = null;
        _interactionIntervals.Clear();
    }

    private static long ReadFrameBudgetUsec()
    {
        try
        {
            int screen = DisplayServer.WindowGetCurrentScreen();
            double refreshRate = DisplayServer.ScreenGetRefreshRate(screen);
            if (refreshRate >= 30 && refreshRate <= 240)
                return Math.Max(1, (long)Math.Round(1_000_000d / refreshRate));
        }
        catch
        {
            // The probe still validates monotonic intervals when display
            // telemetry is unavailable; the observed device default is 60 Hz.
        }

        return 16_667;
    }

    private static string ReadPipelineCompilationCounts()
    {
        try
        {
            return $"pipeline_canvas={(long)Performance.GetMonitor(Performance.Monitor.PipelineCompilationsCanvas)} "
                + $"pipeline_draw={(long)Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw)} "
                + $"pipeline_surface={(long)Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSurface)} "
                + $"pipeline_mesh={(long)Performance.GetMonitor(Performance.Monitor.PipelineCompilationsMesh)} "
                + $"pipeline_specialization={(long)Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSpecialization)}";
        }
        catch
        {
            return "pipeline_counts=unavailable";
        }
    }

    private void Detach()
    {
        if (_finished)
            return;
        _finished = true;
        if (IsGameCapture(_mode))
        {
            IsGameCaptureActive = false;
            ShouldAutoContinueRecoverySession = false;
            if (ReferenceEquals(ActiveGameCapture, this))
                ActiveGameCapture = null;
        }
        _tree.ProcessFrame -= OnProcessFrame;
    }
}
