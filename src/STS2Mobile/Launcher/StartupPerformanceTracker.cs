using System;
using Godot;

namespace STS2Mobile.Launcher;

internal readonly record struct StartupPerformanceSnapshot(
    StartupStageId? ActiveStage,
    StartupProgress Progress,
    long ActiveElapsedUsec,
    long TotalElapsedUsec,
    StartupWatchdogPolicy WatchdogPolicy
);

// Thread-safe facade around the pure bounded timeline. Startup call sites use
// this narrow API rather than sharing recovery-journal strings or UI objects.
// Changed may be raised from a continuation thread; Godot consumers must defer
// their tree mutation to the main thread.
internal static class StartupPerformanceTracker
{
    private static readonly object Gate = new();
    private static StartupPerformanceTimeline _timeline = new();
    private static long _activeSinceUsec;
    private static long _postPlaySinceUsec;

    internal static event Action Changed;

    internal static void BeginManagedStartup()
    {
        bool changed;
        lock (Gate)
        {
            _timeline = new StartupPerformanceTimeline();
            long now = NowUsec();
            changed = _timeline.TryBegin(StartupStageId.LauncherCreation, now, out var error);
            _activeSinceUsec = changed ? now : 0;
            _postPlaySinceUsec = 0;
            LogFailure("begin-managed", error, changed);
        }
        Notify(changed);
    }

    internal static void AdvanceTo(
        StartupStageId next,
        StartupStageTerminal previousTerminal = StartupStageTerminal.Completed
    )
    {
        bool changed = false;
        lock (Gate)
        {
            long now = NowUsec();
            if (_timeline.ActiveStage is { } active)
            {
                if (active == StartupStageId.UserWait && _postPlaySinceUsec == 0)
                    _postPlaySinceUsec = now;
                bool ended = _timeline.TryEnd(active, previousTerminal, now, out var endError);
                LogFailure("advance-end", endError, ended);
                if (!ended)
                    return;
                changed = true;
            }

            bool began = _timeline.TryBegin(next, now, out var beginError);
            LogFailure("advance-begin", beginError, began);
            if (began)
            {
                _activeSinceUsec = now;
                changed = true;
            }
        }
        Notify(changed);
    }

    internal static void CompleteAndSkip(
        StartupStageId skipped,
        StartupStageTerminal previousTerminal = StartupStageTerminal.Completed
    )
    {
        bool changed = false;
        lock (Gate)
        {
            long now = NowUsec();
            if (_timeline.ActiveStage is { } active)
            {
                if (active == StartupStageId.UserWait && _postPlaySinceUsec == 0)
                    _postPlaySinceUsec = now;
                bool ended = _timeline.TryEnd(active, previousTerminal, now, out var endError);
                LogFailure("skip-end", endError, ended);
                if (!ended)
                    return;
                changed = true;
            }

            bool skippedStage = _timeline.TrySkip(skipped, now, out var skipError);
            LogFailure("skip", skipError, skippedStage);
            changed |= skippedStage;
            _activeSinceUsec = 0;
        }
        Notify(changed);
    }

    internal static void EndActive(StartupStageTerminal terminal)
    {
        bool changed = false;
        lock (Gate)
        {
            if (_timeline.ActiveStage is not { } active)
                return;
            long now = NowUsec();
            if (active == StartupStageId.UserWait && _postPlaySinceUsec == 0)
                _postPlaySinceUsec = now;
            changed = _timeline.TryEnd(active, terminal, now, out var error);
            LogFailure("end-active", error, changed);
            if (changed)
                _activeSinceUsec = 0;
        }
        Notify(changed);
    }

    internal static void MarkGameReady()
    {
        bool changed = false;
        string durationSummary = "";
        lock (Gate)
        {
            long now = NowUsec();
            if (_timeline.ActiveStage is { } active)
            {
                bool ended = _timeline.TryEnd(
                    active,
                    StartupStageTerminal.Completed,
                    now,
                    out var endError
                );
                LogFailure("game-ready-end", endError, ended);
                if (!ended)
                    return;
                changed = true;
            }

            bool began = _timeline.TryBegin(StartupStageId.GameReady, now, out var beginError);
            LogFailure("game-ready-begin", beginError, began);
            if (!began)
                return;
            changed = true;

            bool completed = _timeline.TryEnd(
                StartupStageId.GameReady,
                StartupStageTerminal.Completed,
                now,
                out var completeError
            );
            LogFailure("game-ready-complete", completeError, completed);
            changed |= completed;
            _activeSinceUsec = 0;
            durationSummary = _timeline.EncodeTerminalDurations();
        }
        Notify(changed);
        if (changed)
        {
            PatchHelper.Log($"[StartupPerformance/Summary] {durationSummary}");
        }
    }

    internal static void ReportProgress(StartupStageId stage, long done, long total)
    {
        bool accepted;
        bool recorded;
        lock (Gate)
        {
            int eventCount = _timeline.EventCount;
            accepted = _timeline.TryReportProgress(stage, done, total, NowUsec(), out var error);
            recorded = accepted && _timeline.EventCount != eventCount;
            LogFailure("progress", error, accepted);
        }
        Notify(recorded);
    }

    internal static StartupPerformanceSnapshot GetSnapshot()
    {
        lock (Gate)
        {
            long now = NowUsec();
            StartupWatchdogPolicy policy = _timeline.CheckWatchdog(now);
            long elapsed =
                _timeline.ActiveStage != null && _activeSinceUsec > 0
                    ? Math.Max(0, now - _activeSinceUsec)
                    : 0;
            long totalElapsed =
                _postPlaySinceUsec > 0 ? Math.Max(0, now - _postPlaySinceUsec) : elapsed;
            return new StartupPerformanceSnapshot(
                _timeline.ActiveStage,
                _timeline.CurrentProgress,
                elapsed,
                totalElapsed,
                policy
            );
        }
    }

    internal static StartupTimelineEvent[] GetEvents()
    {
        lock (Gate)
            return _timeline.Snapshot();
    }

    internal static string EncodeSanitized()
    {
        lock (Gate)
            return _timeline.EncodeSanitized();
    }

    private static long NowUsec() => (long)Time.GetTicksUsec();

    private static void Notify(bool changed)
    {
        if (!changed)
            return;

        try
        {
            Callable.From(PersistSanitized).CallDeferred();
        }
        catch (Exception ex)
        {
            PatchHelper.Log(
                $"[StartupPerformance] failed to queue sanitized summary: {ex.GetType().Name}"
            );
        }
        Changed?.Invoke();
    }

    private static void PersistSanitized()
    {
        try
        {
            LauncherModel.GetGodotApp()?.Call("recordManagedStartupPerformance", EncodeSanitized());
        }
        catch (Exception ex)
        {
            PatchHelper.Log(
                $"[StartupPerformance] managed summary bridge failed: {ex.GetType().Name}"
            );
        }
    }

    private static void LogFailure(string operation, StartupTimelineError error, bool success)
    {
        if (!success)
            PatchHelper.Log($"[StartupPerformance] {operation} rejected: {error}");
    }
}
