using System;

namespace STS2Mobile.Launcher;

internal readonly struct ShaderWarmupMemorySnapshot
{
    public ShaderWarmupMemorySnapshot(
        int trimLevel,
        bool systemLowMemory,
        long availableBytes,
        long lowMemoryThresholdBytes,
        long totalBytes,
        long processPssBytes
    )
    {
        TrimLevel = trimLevel;
        SystemLowMemory = systemLowMemory;
        AvailableBytes = availableBytes;
        LowMemoryThresholdBytes = lowMemoryThresholdBytes;
        TotalBytes = totalBytes;
        ProcessPssBytes = processPssBytes;
    }

    public static ShaderWarmupMemorySnapshot Unavailable => new(0, false, -1, -1, -1, -1);

    public int TrimLevel { get; }
    public bool SystemLowMemory { get; }
    public long AvailableBytes { get; }
    public long LowMemoryThresholdBytes { get; }
    public long TotalBytes { get; }
    public long ProcessPssBytes { get; }

    public bool HasTelemetry =>
        TrimLevel > 0
        || SystemLowMemory
        || AvailableBytes >= 0
        || LowMemoryThresholdBytes >= 0
        || TotalBytes >= 0
        || ProcessPssBytes >= 0;
}

internal readonly struct ShaderWarmupMemoryDecision
{
    public ShaderWarmupMemoryDecision(bool shouldDefer, string reason)
    {
        ShouldDefer = shouldDefer;
        Reason = reason;
    }

    public bool ShouldDefer { get; }
    public string Reason { get; }
}

// Converts Android's live memory signal into a deterministic warmup decision.
// Warmup is optional: preserving process/system headroom is always more valuable
// than precompiling one more shader that Godot can compile on demand later.
internal static class ShaderWarmupMemoryPolicy
{
    private const int TrimMemoryRunningLow = 10;
    private const long Mebibyte = 1024L * 1024L;
    private const long MinimumSystemReserve = 512L * Mebibyte;
    private const long MinimumProcessBudget = 768L * Mebibyte;
    private const long MaximumProcessBudget = 2304L * Mebibyte;

    public static ShaderWarmupMemoryDecision Evaluate(ShaderWarmupMemorySnapshot snapshot)
    {
        if (!snapshot.HasTelemetry)
            return Continue("Android memory telemetry unavailable");

        if (snapshot.TrimLevel >= TrimMemoryRunningLow)
            return Defer($"Android trim level {snapshot.TrimLevel}");

        if (snapshot.SystemLowMemory)
            return Defer("ActivityManager reports low memory");

        if (snapshot.TotalBytes > 0 && snapshot.ProcessPssBytes >= 0)
        {
            long processBudget = Math.Clamp(
                snapshot.TotalBytes / 3,
                MinimumProcessBudget,
                MaximumProcessBudget
            );
            if (snapshot.ProcessPssBytes >= processBudget)
            {
                return Defer(
                    $"process PSS {snapshot.ProcessPssBytes / Mebibyte} MiB reached "
                        + $"{processBudget / Mebibyte} MiB budget"
                );
            }
        }

        if (
            snapshot.AvailableBytes >= 0
            && snapshot.LowMemoryThresholdBytes >= 0
            && snapshot.TotalBytes > 0
        )
        {
            long reserve = Math.Max(MinimumSystemReserve, snapshot.TotalBytes / 8);
            long deferAt = snapshot.LowMemoryThresholdBytes + reserve;
            if (snapshot.AvailableBytes <= deferAt)
            {
                return Defer(
                    $"system available {snapshot.AvailableBytes / Mebibyte} MiB reached "
                        + $"{deferAt / Mebibyte} MiB safety floor"
                );
            }
        }

        return Continue("memory headroom is healthy");
    }

    private static ShaderWarmupMemoryDecision Continue(string reason) => new(false, reason);

    private static ShaderWarmupMemoryDecision Defer(string reason) => new(true, reason);
}
