using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace STS2Mobile.Launcher;

// One-shot process termination hooks for adb-driven update interruption tests.
// Only the debug-gated Android intent handler can create the app-private marker.
// Production update behavior is a single bounded file read and a no-op.
internal sealed class GameInstallFaultInjector
{
    internal const string MarkerName = ".debug_game_install_fault";

    private static readonly HashSet<string> AllowedPoints = new(StringComparer.Ordinal)
    {
        "after-staging-created",
        "after-file-verified",
        "after-depot-manifest-committed",
        "after-all-depots-verified",
        "after-pck-patched",
        "after-prepared",
        "after-active-retired",
        "after-staged-activated",
    };

    private readonly string _armedPoint;
    private int _triggered;

    private GameInstallFaultInjector(string armedPoint)
    {
        _armedPoint = armedPoint;
    }

    public static GameInstallFaultInjector Consume(string dataDirectory)
    {
        var marker = Path.Combine(dataDirectory, MarkerName);
        try
        {
            if (!File.Exists(marker) || new FileInfo(marker).Length > 128)
                return new GameInstallFaultInjector(null);
            var requested = File.ReadAllText(marker).Trim();
            File.Delete(marker);
            return new GameInstallFaultInjector(
                AllowedPoints.Contains(requested) ? requested : null
            );
        }
        catch
        {
            return new GameInstallFaultInjector(null);
        }
    }

    public void Hit(string point)
    {
        if (
            !string.Equals(_armedPoint, point, StringComparison.Ordinal)
            || Interlocked.Exchange(ref _triggered, 1) != 0
        )
        {
            return;
        }

        Console.Error.WriteLine($"[GameInstall/Fault] terminating at {point}");
        Environment.FailFast($"debug game-install fault: {point}");
    }

    public void Hit(GameInstallFaultPoint point) =>
        Hit(
            point switch
            {
                GameInstallFaultPoint.AfterPrepared => "after-prepared",
                GameInstallFaultPoint.AfterActiveRetired => "after-active-retired",
                GameInstallFaultPoint.AfterStagedActivated => "after-staged-activated",
                _ => throw new ArgumentOutOfRangeException(nameof(point)),
            }
        );
}
