using System;
using System.Globalization;
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace STS2MobileIos.Patches;

public static class PerformancePatches
{
    private static readonly FrameWindow Window = new();
    private static bool _started, _suspended;
    private static StreamWriter _output;
    private static string _scene;

    public static void EnterTreePostfix(Node __instance)
    {
        MobileUi.Install(__instance);
        if (_started || !Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--sts2-profile"))
            return;
        _started = true;
        try
        {
            var directory = Path.Combine(OS.GetUserDataDir(), "benchmarks");
            Directory.CreateDirectory(directory);
            var filename = $"frames-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.jsonl";
            _output = new StreamWriter(Path.Combine(directory, filename)) { AutoFlush = true };
            string renderer = RenderingServer.GetCurrentRenderingMethod();
            string driver = RenderingServer.GetCurrentRenderingDriverName();
            var size = __instance.GetTree().Root.Size;
            _output.WriteLine($"{{\"schema\":2,\"event\":\"start\",\"utc_ms\":{UtcMilliseconds()}," +
                $"\"renderer\":\"{renderer}\",\"driver\":\"{driver}\",\"width\":{size.X},\"height\":{size.Y}}}");
            PatchHelper.Log($"[Performance] capture=user://benchmarks/{filename} " +
                $"renderer={renderer} driver={driver} window={size}");
            var tree = __instance.GetTree();
            tree.ProcessFrame += () =>
            {
                if (_suspended || tree.Paused)
                {
                    Window.ResetClock();
                    return;
                }
                string scene = NGame.Instance?.CurrentRunNode != null ? "run" :
                    NGame.Instance?.MainMenu != null ? "menu" : "loading";
                _scene = _scene == null || _scene == scene ? scene : "mixed";
                Window.Tick(Time.GetTicksUsec());
                if (Window.Elapsed >= 5_000_000)
                    Flush();
            };
            __instance.TreeExiting += () =>
            {
                Flush();
                _output?.Dispose();
                _output = null;
            };
        }
        catch (Exception ex)
        {
            _output?.Dispose();
            _output = null;
            PatchHelper.Log($"[Performance] Cannot start capture: {ex.Message}");
        }
    }

    public static void SetSuspended(bool suspended)
    {
        MobileUi.SetSuspended(suspended);
        if (!_started)
            return;
        Flush();
        Window.ResetClock();
        _suspended = suspended;
        Write($"{{\"event\":\"{(suspended ? "background" : "foreground")}\",\"utc_ms\":{UtcMilliseconds()}}}");
    }

    private static long UtcMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static void Flush()
    {
        if (Window.Intervals.Count == 0)
            return;
        double fps = Window.Intervals.Count * 1_000_000.0 / Window.Elapsed;
        ulong p95 = Window.Percentile(0.95), p99 = Window.Percentile(0.99), worst = Window.Percentile(1);
        int stalls = Window.Intervals.FindAll(value => value > 50_000).Count;
        Write(FormattableString.Invariant($"{{\"event\":\"frames\",\"utc_ms\":{UtcMilliseconds()},\"scene\":\"{_scene}\",\"fps_limit\":{Engine.MaxFps},\"managed_bytes\":{GC.GetTotalMemory(false)},\"frame_us\":[{string.Join(",", Window.Intervals)}]}}"));
        PatchHelper.Log(string.Format(CultureInfo.InvariantCulture,
            "[Performance] fps={0:F1} p95_ms={1:F1} p99_ms={2:F1} worst_ms={3:F1} stalls_over_50ms={4} scene={5}",
            fps, p95 / 1000.0, p99 / 1000.0, worst / 1000.0, stalls, _scene));
        Window.ClearSample();
        _scene = null;
    }

    private static void Write(string line)
    {
        try
        {
            _output?.WriteLine(line);
        }
        catch (IOException ex)
        {
            _output?.Dispose();
            _output = null;
            PatchHelper.Log($"[Performance] Capture stopped: {ex.Message}");
        }
    }
}
