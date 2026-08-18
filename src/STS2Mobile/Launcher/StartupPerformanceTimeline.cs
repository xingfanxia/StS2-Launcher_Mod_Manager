using System;
using System.Text;

namespace STS2Mobile.Launcher;

internal enum StartupTimelineEventKind
{
    Began = 1,
    Progress = 2,
    Watchdog = 3,
    Terminal = 4,
}

internal enum StartupTimelineError
{
    None,
    InvalidTimestamp,
    StageAlreadyActive,
    NoActiveStage,
    WrongActiveStage,
    IllegalTransition,
    TerminalNotAllowed,
    ProgressNotSupported,
    InvalidProgress,
}

internal readonly record struct StartupTimelineEvent(
    long Sequence,
    StartupStageId Stage,
    StartupTimelineEventKind Kind,
    StartupStageTerminal Terminal,
    long TimestampUsec,
    long Done,
    long Total
);

internal readonly record struct StartupProgress(long Done, long Total)
{
    internal bool IsKnown => Total > 0;
}

// A bounded in-memory timeline for startup only. It accepts stable enums and
// numeric monotonic values, never arbitrary labels or user/device data. Progress
// updates remain available to UI callers, but only sparse milestones enter the
// event ring so a large warmup cannot create per-item allocations or file I/O.
internal sealed class StartupPerformanceTimeline
{
    private const long SparseProgressIntervalUsec = 1_000_000;
    private const int SparseProgressBucketCount = 20;
    private const int MaximumStageId = (int)StartupStageId.GameReady;

    private readonly StartupTimelineEvent[] _events;
    private int _head;
    private int _count;
    private long _sequence;
    private long _lastTimestampUsec = -1;
    private StartupStageId? _activeStage;
    private long _activeSinceUsec;
    private StartupStageId? _lastTerminalStage;
    private StartupProgress _progress;
    private long _lastProgressEventUsec = -1;
    private int _lastProgressBucket = -1;
    private bool _watchdogRecorded;
    private readonly long[] _terminalDurationUsec = new long[MaximumStageId + 1];
    private readonly bool[] _hasTerminalDuration = new bool[MaximumStageId + 1];

    internal StartupPerformanceTimeline(int capacity = 96)
    {
        if (capacity is < 8 or > 256)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _events = new StartupTimelineEvent[capacity];
    }

    internal StartupStageId? ActiveStage => _activeStage;
    internal StartupStageId? LastTerminalStage => _lastTerminalStage;
    internal StartupProgress CurrentProgress => _progress;
    internal int EventCount => _count;

    internal bool TryBegin(StartupStageId stage, long timestampUsec, out StartupTimelineError error)
    {
        if (!ValidateTimestamp(timestampUsec, out error))
            return false;
        if (_activeStage != null)
        {
            error = StartupTimelineError.StageAlreadyActive;
            return false;
        }

        var definition = StartupStageCatalog.Get(stage);
        if (
            _lastTerminalStage is { } previous
                ? !StartupStageCatalog.IsAllowedNext(previous, stage)
                : !definition.CanStartTimeline
        )
        {
            error = StartupTimelineError.IllegalTransition;
            return false;
        }

        _activeStage = stage;
        _activeSinceUsec = timestampUsec;
        _progress = default;
        _lastProgressEventUsec = -1;
        _lastProgressBucket = -1;
        _watchdogRecorded = false;
        Record(stage, StartupTimelineEventKind.Began, default, timestampUsec, 0, 0);
        error = StartupTimelineError.None;
        return true;
    }

    internal bool TryEnd(
        StartupStageId stage,
        StartupStageTerminal terminal,
        long timestampUsec,
        out StartupTimelineError error
    )
    {
        if (!ValidateTimestamp(timestampUsec, out error))
            return false;
        if (_activeStage == null)
        {
            error = StartupTimelineError.NoActiveStage;
            return false;
        }
        if (_activeStage != stage)
        {
            error = StartupTimelineError.WrongActiveStage;
            return false;
        }
        if (!StartupStageCatalog.Get(stage).Allows(terminal))
        {
            error = StartupTimelineError.TerminalNotAllowed;
            return false;
        }

        int stageIndex = (int)stage;
        long durationUsec = timestampUsec - _activeSinceUsec;
        _terminalDurationUsec[stageIndex] =
            long.MaxValue - _terminalDurationUsec[stageIndex] < durationUsec
                ? long.MaxValue
                : _terminalDurationUsec[stageIndex] + durationUsec;
        _hasTerminalDuration[stageIndex] = true;

        Record(
            stage,
            StartupTimelineEventKind.Terminal,
            terminal,
            timestampUsec,
            _progress.Done,
            _progress.Total
        );
        _activeStage = null;
        _lastTerminalStage = stage;
        error = StartupTimelineError.None;
        return true;
    }

    internal bool TrySkip(StartupStageId stage, long timestampUsec, out StartupTimelineError error)
    {
        if (!StartupStageCatalog.Get(stage).Allows(StartupStageTerminal.Skipped))
        {
            error = StartupTimelineError.TerminalNotAllowed;
            return false;
        }
        if (!TryBegin(stage, timestampUsec, out error))
            return false;
        if (TryEnd(stage, StartupStageTerminal.Skipped, timestampUsec, out error))
            return true;

        // Catalog validation should make this unreachable. Do not leave an
        // accidentally active stage if a future definition removes Skip.
        _activeStage = null;
        return false;
    }

    internal bool TryReportProgress(
        StartupStageId stage,
        long done,
        long total,
        long timestampUsec,
        out StartupTimelineError error
    )
    {
        if (!ValidateTimestamp(timestampUsec, out error))
            return false;
        if (_activeStage == null)
        {
            error = StartupTimelineError.NoActiveStage;
            return false;
        }
        if (_activeStage != stage)
        {
            error = StartupTimelineError.WrongActiveStage;
            return false;
        }
        if (StartupStageCatalog.Get(stage).ProgressKind == StartupProgressKind.Indeterminate)
        {
            error = StartupTimelineError.ProgressNotSupported;
            return false;
        }
        if (
            total <= 0
            || done < 0
            || done > total
            || (_progress.IsKnown && (done < _progress.Done || total != _progress.Total))
        )
        {
            error = StartupTimelineError.InvalidProgress;
            return false;
        }

        _progress = new StartupProgress(done, total);
        int bucket = (int)
            Math.Min(SparseProgressBucketCount, (double)done / total * SparseProgressBucketCount);
        bool isTerminalValue = done == total;
        bool intervalElapsed =
            _lastProgressEventUsec < 0
            || timestampUsec - _lastProgressEventUsec >= SparseProgressIntervalUsec;
        bool bucketAdvanced = bucket > _lastProgressBucket;
        if (isTerminalValue || (intervalElapsed && bucketAdvanced))
        {
            Record(stage, StartupTimelineEventKind.Progress, default, timestampUsec, done, total);
            _lastProgressEventUsec = timestampUsec;
            _lastProgressBucket = bucket;
        }

        error = StartupTimelineError.None;
        return true;
    }

    internal StartupWatchdogPolicy CheckWatchdog(long timestampUsec)
    {
        if (!ValidateTimestamp(timestampUsec, out _) || _activeStage == null)
            return StartupWatchdogPolicy.NoneForUserWait;

        var definition = StartupStageCatalog.Get(_activeStage.Value);
        if (
            definition.WatchdogAfterUsec == 0
            || timestampUsec - _activeSinceUsec < definition.WatchdogAfterUsec
        )
        {
            return StartupWatchdogPolicy.NoneForUserWait;
        }

        if (!_watchdogRecorded)
        {
            _watchdogRecorded = true;
            Record(
                definition.Id,
                StartupTimelineEventKind.Watchdog,
                default,
                timestampUsec,
                _progress.Done,
                _progress.Total
            );
        }
        return definition.WatchdogPolicy;
    }

    internal StartupTimelineEvent[] Snapshot()
    {
        var result = new StartupTimelineEvent[_count];
        for (int i = 0; i < _count; i++)
            result[i] = _events[(_head + i) % _events.Length];
        return result;
    }

    // Numeric-only, bounded, versioned summary suitable for app-private storage.
    // It deliberately has no API for arbitrary metadata or dynamic field names.
    internal string EncodeSanitized()
    {
        var builder = new StringBuilder(16 + _count * 32);
        builder.Append("v1\n");
        foreach (var item in Snapshot())
        {
            builder
                .Append(item.Sequence)
                .Append('|')
                .Append((int)item.Stage)
                .Append('|')
                .Append((int)item.Kind)
                .Append('|')
                .Append((int)item.Terminal)
                .Append('|')
                .Append(item.TimestampUsec)
                .Append('|')
                .Append(item.Done)
                .Append('|')
                .Append(item.Total)
                .Append('\n');
        }
        return builder.ToString();
    }

    // One fixed numeric aggregate per completed stage. Unlike the diagnostic
    // event ring, these totals survive ring eviction and repeated mod spans,
    // and remain short enough for one sanitized logcat line at game-ready.
    internal string EncodeTerminalDurations()
    {
        var builder = new StringBuilder(8 + MaximumStageId * 24);
        builder.Append("v2;");
        for (int stage = 1; stage <= MaximumStageId; stage++)
        {
            if (!_hasTerminalDuration[stage])
                continue;
            builder.Append(stage).Append('|').Append(_terminalDurationUsec[stage]).Append(';');
        }
        return builder.ToString();
    }

    private bool ValidateTimestamp(long timestampUsec, out StartupTimelineError error)
    {
        if (timestampUsec < 0 || timestampUsec < _lastTimestampUsec)
        {
            error = StartupTimelineError.InvalidTimestamp;
            return false;
        }
        _lastTimestampUsec = timestampUsec;
        error = StartupTimelineError.None;
        return true;
    }

    private void Record(
        StartupStageId stage,
        StartupTimelineEventKind kind,
        StartupStageTerminal terminal,
        long timestampUsec,
        long done,
        long total
    )
    {
        int index;
        if (_count < _events.Length)
        {
            index = (_head + _count) % _events.Length;
            _count++;
        }
        else
        {
            index = _head;
            _head = (_head + 1) % _events.Length;
        }

        _events[index] = new StartupTimelineEvent(
            ++_sequence,
            stage,
            kind,
            terminal,
            timestampUsec,
            done,
            total
        );
    }
}
