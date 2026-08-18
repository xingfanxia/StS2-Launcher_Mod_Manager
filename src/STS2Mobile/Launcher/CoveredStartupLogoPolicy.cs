using System;
using System.Threading;

namespace STS2Mobile.Launcher;

// The launcher owns a full-screen progress surface from PLAY until the first
// game-ready frame. During that narrow window the game's logo animation is
// completely hidden, so playing it only extends an already-obscured startup.
// Keep this as a scoped process state rather than changing the user's persisted
// SkipIntroLogo preference.
internal static class CoveredStartupLogoPolicy
{
    private static int _activeScopeCount;

    internal static bool ShouldSkipLogo(bool gameAlreadyRequestedSkip) =>
        gameAlreadyRequestedSkip || Volatile.Read(ref _activeScopeCount) > 0;

    internal static IDisposable Enter(bool launcherSurfaceCoversStartup)
    {
        if (!launcherSurfaceCoversStartup)
            return NoopLease.Instance;

        Interlocked.Increment(ref _activeScopeCount);
        return new ActiveLease();
    }

    private sealed class ActiveLease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref _activeScopeCount);
        }
    }

    private sealed class NoopLease : IDisposable
    {
        internal static readonly NoopLease Instance = new();

        public void Dispose() { }
    }
}
