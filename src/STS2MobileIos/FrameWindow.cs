using System;
using System.Collections.Generic;

namespace STS2MobileIos;

// Engine-independent frame accounting, including suspension boundaries.
public sealed class FrameWindow
{
    private ulong? _previous;
    public List<ulong> Intervals { get; } = new(640);
    public ulong Elapsed { get; private set; }

    public void ResetClock() => _previous = null;

    public void Tick(ulong now)
    {
        if (_previous is ulong previous && now >= previous)
        {
            ulong interval = now - previous;
            if (interval > 0)
            {
                Intervals.Add(interval);
                Elapsed += interval;
            }
        }
        _previous = now;
    }

    public void ClearSample()
    {
        Intervals.Clear();
        Elapsed = 0;
    }

    public ulong Percentile(double percentile)
    {
        if (Intervals.Count == 0)
            return 0;
        var sorted = Intervals.ToArray();
        Array.Sort(sorted);
        return sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];
    }
}
