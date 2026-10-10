using System;
using System.Globalization;
using System.IO;
using Godot;

namespace STS2Mobile.Platform;

// Test hook, on with STS2_DEBUG_AUTOMATION=1: lets a host drive the UI without
// touching the device. The host drops one command into
// Documents/automation/command.txt and the app answers in result.txt:
//   shot <name>               saves the frame at half size as automation/<name>.jpg
//   tap <x> <y>               touches that point
//   drag <x1> <y1> <x2> <y2>  presses, moves over about half a second, releases
//   console <command>         runs a command of the game's developer console
// Coordinates are pixels of a shot, so what the host sees is what it points at.
internal static class IosAutomation
{
    private static string _directory;
    private static readonly System.Collections.Generic.List<Resource> _held = new();
    private static MegaCrit.Sts2.Core.DevConsole.DevConsole _console;
    private static double _nextPoll;
    private const int ShotWidth = 1376;
    private static float _shotScale = 2;
    private const int DragFrames = 30;
    private static Vector2? _release;
    private static Vector2 _dragFrom, _dragTo;
    private static int _dragFrame = -1;

    public static void StartIfRequested()
    {
        if (System.Environment.GetEnvironmentVariable("STS2_DEBUG_AUTOMATION") != "1")
            return;
        _directory = Path.Combine(IosPlatform.Documents, "automation");
        Directory.CreateDirectory(_directory);
        File.Delete(Path.Combine(_directory, "command.txt"));
        Callable.From(Attach).CallDeferred();
    }

    private static void Attach()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Callable.From(Attach).CallDeferred();
            return;
        }
        PatchHelper.Log("[Automation] host-driven UI commands enabled");
        tree.ProcessFrame += () => OnFrame(tree);
    }

    private static void OnFrame(SceneTree tree)
    {
        if (_release is { } position)
        {
            _release = null;
            Touch(position, pressed: false);
            return;
        }
        if (_dragFrame >= 0)
        {
            _dragFrame++;
            var previous = _dragFrom.Lerp(_dragTo, (_dragFrame - 1) / (float)DragFrames);
            var current = _dragFrom.Lerp(_dragTo, _dragFrame / (float)DragFrames);
            Input.ParseInputEvent(
                new InputEventScreenDrag { Index = 0, Position = current, Relative = current - previous }
            );
            if (_dragFrame == DragFrames)
            {
                _dragFrame = -1;
                _release = _dragTo;
            }
            return;
        }
        var now = Time.GetTicksMsec() / 1000.0;
        if (now < _nextPoll)
            return;
        _nextPoll = now + 0.25;
        var commandPath = Path.Combine(_directory, "command.txt");
        if (!File.Exists(commandPath))
            return;

        string result;
        try
        {
            var parts = File.ReadAllText(commandPath).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            File.Delete(commandPath);
            result = Run(tree, parts);
        }
        catch (Exception ex)
        {
            result = "error " + ex.Message;
        }
        File.WriteAllText(Path.Combine(_directory, "result.txt"), result + "\n");
        PatchHelper.Log($"[Automation] {result}");
    }

    private static string Run(SceneTree tree, string[] parts)
    {
        if (parts.Length == 2 && parts[0] == "shot" && parts[1].IndexOfAny(new[] { '/', '.' }) < 0)
        {
            var image = tree.Root.GetTexture().GetImage();
            // Shots are always 1376 wide, whatever the render resolution, and
            // taps are given in shot pixels.
            _shotScale = image.GetWidth() / (float)ShotWidth;
            image.Resize(ShotWidth, (int)(image.GetHeight() / _shotScale), Image.Interpolation.Bilinear);
            var error = image.SaveJpg(Path.Combine(_directory, parts[1] + ".jpg"), 0.8f);
            return error == Error.Ok
                ? $"shot {parts[1]} {image.GetWidth()}x{image.GetHeight()}"
                : $"error saving shot: {error}";
        }
        if (
            parts.Length == 3
            && parts[0] == "tap"
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
        )
        {
            var position = new Vector2(x, y) * CurrentScale(tree);
            Touch(position, pressed: true);
            _release = position;
            return $"tap {x} {y}";
        }
        if (parts.Length == 5 && parts[0] == "drag")
        {
            var values = new float[4];
            for (var i = 0; i < 4; i++)
            {
                if (!float.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    return "error bad drag coordinates";
            }
            _dragFrom = new Vector2(values[0], values[1]) * CurrentScale(tree);
            _dragTo = new Vector2(values[2], values[3]) * CurrentScale(tree);
            Touch(_dragFrom, pressed: true);
            _dragFrame = 0;
            return $"drag {values[0]} {values[1]} -> {values[2]} {values[3]}";
        }
        if (parts.Length >= 2 && parts[0] == "console")
        {
            // The game's own developer console (fight <encounter>, event <id>,
            // godmode, help ...), so a test can go straight to a situation.
            _console ??= new MegaCrit.Sts2.Core.DevConsole.DevConsole(true);
            var outcome = _console.ProcessCommand(string.Join(' ', parts, 1, parts.Length - 1));
            var message = (outcome.msg ?? "").Replace('\n', ' ');
            return $"console {(outcome.success ? "ok" : "failed")}: "
                + (message.Length > 1500 ? message.Substring(0, 1500) : message);
        }
        if (parts.Length == 2 && parts[0] == "load")
        {
            // Loads one resource and keeps it, to see what it costs in graphics memory.
            var before = IosNative.MemoryBreakdown().Graphics;
            var resource = ResourceLoader.Load(parts[1]);
            if (resource == null)
                return "error could not load " + parts[1];
            _held.Add(resource);
            var after = IosNative.MemoryBreakdown().Graphics;
            return $"load {parts[1]} ({resource.GetType().Name}): graphics {before >> 20} -> {after >> 20} MiB, holding {_held.Count}";
        }
        if (parts.Length == 2 && parts[0] == "loadset")
        {
            // Loads a game asset set one resource at a time and logs what each costs.
            System.Collections.Generic.IEnumerable<string> set = parts[1] switch
            {
                "mainmenu" => MegaCrit.Sts2.Core.Assets.AssetSets.MainMenuSet,
                "common" => MegaCrit.Sts2.Core.Assets.AssetSets.CommonAssets,
                _ => null,
            };
            if (set == null)
                return "error unknown set";
            var start = IosNative.MemoryBreakdown().Graphics;
            var peak = start;
            var count = 0;
            foreach (var path in set)
            {
                var before = IosNative.MemoryBreakdown().Graphics;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var resource = ResourceLoader.Load(path);
                if (resource != null)
                    _held.Add(resource);
                var after = IosNative.MemoryBreakdown().Graphics;
                peak = Math.Max(peak, after);
                count++;
                if (Math.Abs(after - before) >= 16L << 20 || watch.ElapsedMilliseconds >= 100)
                    PatchHelper.Log(
                        $"[Automation] loadset #{count} {path}: graphics {(after - before) >> 20:+0;-0} -> {after >> 20} MiB in {watch.ElapsedMilliseconds} ms"
                    );
            }
            return $"loadset {parts[1]}: {count} assets, graphics {start >> 20} -> {IosNative.MemoryBreakdown().Graphics >> 20} MiB, peak {peak >> 20}";
        }
        if (parts.Length == 1 && parts[0] == "drop")
        {
            var count = _held.Count;
            _held.Clear();
            return $"drop released {count} held resources, graphics {IosNative.MemoryBreakdown().Graphics >> 20} MiB";
        }
        if (parts.Length == 2 && parts[0] == "profile" && int.TryParse(parts[1], out var profileMs))
        {
            // Needs STS2_STALL_STACKS=1, which installs the sampling signal.
            IosNative.Profile(Math.Clamp(profileMs, 100, 10000), 2000);
            return $"profile started for {profileMs} ms; results follow in the log as [Profile] lines";
        }
        if (parts.Length == 1 && parts[0] == "gfx")
        {
            // What the frame is made of, for judging GPU cost.
            var root = tree.Root;
            var environment = FindEnvironment(root);
            return $"gfx window={root.Size} stretch={root.ContentScaleMode}/{root.ContentScaleAspect} base={root.ContentScaleSize} "
                + $"factor={root.ContentScaleFactor} hdr2d={root.UseHdr2D} msaa2d={root.Msaa2D} snap={root.Snap2DTransformsToPixel} "
                + $"scaling3d={root.Scaling3DScale} env={(environment == null ? "none" : $"glow={environment.GlowEnabled} bg={environment.BackgroundMode} tonemap={environment.TonemapMode} adjust={environment.AdjustmentEnabled}")} "
                + $"fps={Engine.GetFramesPerSecond()} draws={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}";
        }
        if (parts.Length == 3 && parts[0] == "set")
        {
            // Rendering switches for experiments; not persisted anywhere.
            var root = tree.Root;
            var on = parts[2] is "1" or "on" or "true";
            switch (parts[1])
            {
                case "stretch":
                    root.ContentScaleMode = parts[2] == "viewport" ? Window.ContentScaleModeEnum.Viewport : Window.ContentScaleModeEnum.CanvasItems;
                    break;
                case "hdr":
                    root.UseHdr2D = on;
                    break;
                case "glow":
                    if (FindEnvironment(root) is { } glowEnvironment)
                        glowEnvironment.GlowEnabled = on;
                    break;
                case "maxfps":
                    Engine.MaxFps = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    break;
                default:
                    return "error unknown setting";
            }
            return $"set {parts[1]} {parts[2]}";
        }
        if (parts.Length == 1 && parts[0] == "mem")
        {
            var memory = IosNative.MemoryBreakdown();
            return $"mem footprint={IosNative.MemoryFootprint() >> 20} graphics={memory.Graphics >> 20} heap={memory.Heap >> 20} "
                + $"textures={(long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) >> 20} "
                + $"video={(long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) >> 20} "
                + $"buffers={(long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed) >> 20}";
        }
        return "error unknown command";
    }

    private static float CurrentScale(SceneTree tree) => tree.Root.Size.X / (float)ShotWidth;

    private static Godot.Environment FindEnvironment(Node root)
    {
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is WorldEnvironment world && world.Environment != null)
                return world.Environment;
            foreach (var child in node.GetChildren())
                stack.Push(child);
        }
        return null;
    }

    private static void Touch(Vector2 position, bool pressed) =>
        Input.ParseInputEvent(
            new InputEventScreenTouch { Index = 0, Position = position, Pressed = pressed }
        );
}
