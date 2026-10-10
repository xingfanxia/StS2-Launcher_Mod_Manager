using System;
using System.Diagnostics;
using System.Runtime;
using Godot;

namespace STS2Mobile.Platform;

// Diagnostic, on with STS2_FRAME_STATS=1: every five seconds logs frame pacing
// next to garbage-collection and JIT activity, so a stutter can be attributed.
internal static class IosFrameStats
{
    private const double WindowSeconds = 5;
    private static readonly double[] Frames = new double[4096];
    private static int _count;
    private static long _last;
    private static long _windowStart;
    private static int _gen0, _gen1, _gen2;
    private static TimeSpan _gcPause, _jitTime;
    private static long _jitMethods;

    private static System.Threading.Timer _memoryTimer;
    private static Rid _rootViewport;
    private static long _graphicsLogged;
    private static long _graphicsSampled;
    private static bool _watchStalls;
    private static long _lastLoggedFootprint;

    public static void StartIfRequested()
    {
        if (System.Environment.GetEnvironmentVariable("STS2_FRAME_STATS") != "1")
            return;
        Callable.From(Attach).CallDeferred();
        // Independent of the frame loop, so growth during a long stall is still seen.
        _memoryTimer = new System.Threading.Timer(
            _ =>
            {
                var footprint = IosNative.MemoryFootprint();
                if (Math.Abs(footprint - _lastLoggedFootprint) < 64L << 20)
                {
                    _graphicsSampled = IosNative.MemoryBreakdown().Graphics;
                    return;
                }
                _lastLoggedFootprint = footprint;
                var parts = IosNative.MemoryBreakdown();
                _graphicsSampled = parts.Graphics;
                PatchHelper.Log(
                    $"[Memory] footprint={footprint >> 20}MiB managed={GC.GetTotalMemory(false) >> 20}MiB "
                        + $"graphics={parts.Graphics >> 20} heap={parts.Heap >> 20} "
                        + $"anonymous={parts.Anonymous >> 20} compressed={parts.Compressed >> 20}"
                );
            },
            null,
            0,
            250
        );
    }

    private static void Attach()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Callable.From(Attach).CallDeferred();
            return;
        }
        PatchHelper.Log(
            $"[FrameStats] on: maxFps={Engine.MaxFps} vsync={DisplayServer.WindowGetVsyncMode()} "
                + $"refresh={DisplayServer.ScreenGetRefreshRate():F0} physics={Engine.PhysicsTicksPerSecond} "
                + $"gc={GCSettings.LatencyMode} server={GCSettings.IsServerGC}"
        );
        _rootViewport = tree.Root.GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_rootViewport, true);
        var footprintNow = IosNative.MemoryFootprint();
        PatchHelper.Log(
            $"[Memory] limit={(footprintNow + IosNative.MemoryAvailable()) >> 20}MiB "
                + $"(footprint {footprintNow >> 20} + available {IosNative.MemoryAvailable() >> 20})"
        );
        var formats = "";
        foreach (var feature in new[] { "s3tc", "bptc", "rgtc", "etc2", "astc" })
            formats += $" {feature}={RenderingServer.HasOsFeature(feature)}/{OS.HasFeature(feature)}";
        PatchHelper.Log($"[FrameStats] texture formats (renderer/os):{formats}");
        // How the renderer stores the pack's two desktop texture formats: kept
        // compressed, or expanded to RGBA (four to eight times the memory).
        foreach (var path in new[]
        {
            "res://animations/character_select/regent/characterselect_regent.png",
            "res://images/atlases/card_atlas_0.png",
        })
        {
            try
            {
                var before = IosNative.MemoryFootprint();
                if (ResourceLoader.Load(path) is not Texture2D texture)
                {
                    PatchHelper.Log($"[FrameStats] texture probe: {path} did not load");
                    continue;
                }
                var stored = RenderingServer.TextureGetFormat(texture.GetRid());
                PatchHelper.Log(
                    $"[FrameStats] texture probe: {path.GetFile()} {texture.GetWidth()}x{texture.GetHeight()} "
                        + $"stored as {stored}, footprint +{(IosNative.MemoryFootprint() - before) >> 20}MiB"
                );
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[FrameStats] texture probe failed for {path}: {ex.Message}");
            }
        }
        if (System.Environment.GetEnvironmentVariable("STS2_STALL_STACKS") == "1")
        {
            IosNative.WatchForStalls(300);
            _watchStalls = true;
        }
        _last = _windowStart = Stopwatch.GetTimestamp();
        try
        {
            Snapshot();
            tree.ProcessFrame += OnFrame;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[FrameStats] could not start: {ex}");
        }
    }

    private static void Snapshot()
    {
        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        _gen2 = GC.CollectionCount(2);
        _gcPause = Pause();
        _jitTime = JitTime();
        _jitMethods = JitMethods();
    }

    // Not every counter exists on every runtime; a missing one reads as zero.
    private static TimeSpan Pause()
    {
        try { return GC.GetTotalPauseDuration(); }
        catch (Exception) { return TimeSpan.Zero; }
    }

    private static TimeSpan JitTime()
    {
        try { return JitInfo.GetCompilationTime(); }
        catch (Exception) { return TimeSpan.Zero; }
    }

    private static long JitMethods()
    {
        try { return JitInfo.GetCompiledMethodCount(); }
        catch (Exception) { return 0; }
    }

    // Slow frames of the window, split by what else happened during the frame.
    private const double SlowFrameMs = 25;
    private static int _frameGc;
    private static long _frameArena;
    private static int _slowGc, _slowJit, _slowBoth, _slowOther;
    private static double _slowGcMs, _slowJitMs, _slowBothMs, _slowOtherMs;
    // Slow frames with neither: split by pipeline compilation and by script time.
    private static ulong _framePipelines;
    private static int _slowShader, _slowScript, _slowRender;
    private static double _slowShaderMs, _slowScriptMs, _slowRenderMs;

    private static ulong PipelineCompilations() =>
        RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsCanvas)
        + RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsMesh)
        + RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsSurface)
        + RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsDraw)
        + RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsSpecialization);

    // After a long frame: which particle systems are in the tree, heaviest first.
    private static void LogParticleSystems(double frameMs)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree)
                return;
            var found = new System.Collections.Generic.List<(double Work, string Text)>();
            var stack = new System.Collections.Generic.Stack<Node>();
            stack.Push(tree.Root);
            var nodes = 0;
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                nodes++;
                if (node is GpuParticles2D particles)
                {
                    var work = particles.Amount * (1 + particles.Preprocess * 60);
                    found.Add((work,
                        $"{particles.GetPath()} amount={particles.Amount} preprocess={particles.Preprocess:F1}s "
                            + $"lifetime={particles.Lifetime:F1} emitting={particles.Emitting} visible={particles.IsVisibleInTree()}"));
                }
                foreach (var child in node.GetChildren())
                    stack.Push(child);
            }
            found.Sort((a, b) => b.Work.CompareTo(a.Work));
            PatchHelper.Log($"[Stall] frame of {frameMs:F0} ms; {nodes} nodes, {found.Count} particle systems in the tree");
            for (var i = 0; i < Math.Min(12, found.Count); i++)
                PatchHelper.Log($"[Stall]   {found[i].Text}");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Stall] particle scan failed: {ex.Message}");
        }
    }

    private static void OnFrame()
    {
        if (_watchStalls)
            IosNative.StallTick();
        var now = Stopwatch.GetTimestamp();
        var frameMs = (now - _last) * 1000.0 / Stopwatch.Frequency;
        if (_count < Frames.Length)
            Frames[_count++] = frameMs;
        _last = now;

        // Graphics memory as the system charges it against what the renderer
        // itself accounts for, whenever it moves: a gap is memory held outside
        // the renderer's textures and buffers (drawables, driver staging).
        var graphicsNow = _graphicsSampled;
        if (Math.Abs(graphicsNow - _graphicsLogged) >= 96L << 20)
        {
            _graphicsLogged = graphicsNow;
            PatchHelper.Log(
                $"[Graphics] system={graphicsNow >> 20}MiB renderer: "
                    + $"textures={(long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) >> 20} "
                    + $"buffers={(long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed) >> 20} "
                    + $"video={(long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) >> 20} "
                    + $"frame={frameMs:F0}ms"
            );
        }

        if (_watchStalls && frameMs > 400)
            LogParticleSystems(frameMs);

        var gcNow = GC.CollectionCount(0);
        var arenaNow = IosNative.JitArenaStats().Used;
        var pipelinesNow = PipelineCompilations();
        if (frameMs > SlowFrameMs)
        {
            var collected = gcNow != _frameGc;
            var compiled = arenaNow != _frameArena;
            if (collected && compiled) { _slowBoth++; _slowBothMs += frameMs; }
            else if (collected) { _slowGc++; _slowGcMs += frameMs; }
            else if (compiled) { _slowJit++; _slowJitMs += frameMs; }
            else
            {
                _slowOther++;
                _slowOtherMs += frameMs;
                // The monitors describe the previous frame, the one just measured.
                var scriptMs =
                    (Performance.GetMonitor(Performance.Monitor.TimeProcess)
                        + Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess)) * 1000;
                if (pipelinesNow != _framePipelines) { _slowShader++; _slowShaderMs += frameMs; }
                else if (scriptMs > frameMs / 2) { _slowScript++; _slowScriptMs += frameMs; }
                else { _slowRender++; _slowRenderMs += frameMs; }
            }
        }
        _frameGc = gcNow;
        _frameArena = arenaNow;
        _framePipelines = pipelinesNow;
        var elapsed = (now - _windowStart) / (double)Stopwatch.Frequency;
        if (elapsed < WindowSeconds)
            return;

        var arena = IosNative.JitArenaStats();
        Array.Sort(Frames, 0, _count);
        var over20 = 0;
        var over50 = 0;
        for (var i = 0; i < _count; i++)
        {
            if (Frames[i] > 20)
                over20++;
            if (Frames[i] > 50)
                over50++;
        }
        PatchHelper.Log(
            $"[FrameStats] fps={_count / elapsed:F0} p50={Frames[_count / 2]:F1} "
                + $"p99={Frames[(int)(_count * 0.99)]:F1} max={Frames[_count - 1]:F1}ms "
                + $">20ms={over20} >50ms={over50} "
                + $"gc={GC.CollectionCount(0) - _gen0}/{GC.CollectionCount(1) - _gen1}/{GC.CollectionCount(2) - _gen2} "
                + $"gcPause={(Pause() - _gcPause).TotalMilliseconds:F0}ms "
                + $"jit={JitMethods() - _jitMethods} methods "
                + $"{(JitTime() - _jitTime).TotalMilliseconds:F0}ms "
                + $"heap={GC.GetTotalMemory(false) >> 20}MiB "
                + $"jitArena={arena.Used >> 20}/{arena.Size >> 20}MiB peak={arena.High >> 20} "
                + $"footprint={IosNative.MemoryFootprint() >> 20}MiB "
                + $"textures={(long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) >> 20}MiB "
                + $"video={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) >> 20}MiB "
                + $"static={OS.GetStaticMemoryUsage() >> 20}MiB "
                // Last frame's cost by stage, to tell a CPU-bound scene from a GPU-bound one.
                + $"script={Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:F1}ms "
                + $"renderCpu={RenderingServer.ViewportGetMeasuredRenderTimeCpu(_rootViewport) + RenderingServer.GetFrameSetupTimeCpu():F1}ms "
                + $"renderGpu={RenderingServer.ViewportGetMeasuredRenderTimeGpu(_rootViewport):F1}ms "
                + $"draws={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}"
        );
        if (_slowGc + _slowJit + _slowBoth + _slowOther > 0)
        {
            PatchHelper.Log(
                $"[FrameStats] slow frames >{SlowFrameMs}ms: gc={_slowGc} ({_slowGcMs:F0}ms) "
                    + $"jit={_slowJit} ({_slowJitMs:F0}ms) gc+jit={_slowBoth} ({_slowBothMs:F0}ms) "
                    + $"neither={_slowOther} ({_slowOtherMs:F0}ms) of which "
                    + $"shader={_slowShader} ({_slowShaderMs:F0}ms) script={_slowScript} ({_slowScriptMs:F0}ms) "
                    + $"render={_slowRender} ({_slowRenderMs:F0}ms)"
            );
        }
        _slowGc = _slowJit = _slowBoth = _slowOther = 0;
        _slowGcMs = _slowJitMs = _slowBothMs = _slowOtherMs = 0;
        _slowShader = _slowScript = _slowRender = 0;
        _slowShaderMs = _slowScriptMs = _slowRenderMs = 0;
        _count = 0;
        _windowStart = now;
        Snapshot();
    }
}
