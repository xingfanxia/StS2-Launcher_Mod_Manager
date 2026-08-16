using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher;

// Compiles shaders on first launch by collecting materials from resources and scenes,
// rendering them in a SubViewport, then writing a version marker to skip on future launches.
public class ShaderWarmupScreen : Control
{
    private const int WarmupVersion = 7;
    private const int BatchSize = 8;

    private readonly ShaderWarmupOperation _operation = new();
    private readonly ShaderWarmupState _state = new(OS.GetUserDataDir(), WarmupVersion);
    private float _scale;
    private Label _statusLabel;
    private Label _detailLabel;
    private ProgressBar _progressBar;
    private bool _inputGateHeld;

    public static bool NeedsWarmup()
    {
        try
        {
            var check = new ShaderWarmupState(OS.GetUserDataDir(), WarmupVersion).Check();
            PatchHelper.Log($"[ShaderWarmup] NeedsWarmup={check.NeedsWarmup} ({check.Reason})");
            if (check.RecoveredInterruptedAttempt)
                PatchHelper.Log(
                    "[ShaderWarmup] Previous attempt was interrupted; skipping this optional warmup version"
                );
            return check.NeedsWarmup;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[ShaderWarmup] NeedsWarmup check failed: {ex.Message}");
            // Warmup is an optimization. If durable state is unavailable, running
            // it would repeat on every boot and can create an unrecoverable loop.
            return false;
        }
    }

    public Task<bool> WaitForCompletion() => _operation.Completion;

    public void Initialize()
    {
        ZIndex = 100;
        // If the scene tree removes this screen during a lifecycle/configuration
        // teardown, release the caller instead of leaving its launch Task pending.
        TreeExiting += OnTreeExiting;
        _inputGateHeld = true;
        StartupInputGate.Enter(this);

        try
        {
            var vpSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
            SetAnchorsPreset(LayoutPreset.FullRect);
            Size = vpSize;
            BuildUI();
            _state.Begin();
            PatchHelper.Log("[ShaderWarmup] Screen initialized");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[ShaderWarmup] Initialization failed: {ex}");
            _operation.Complete(restartRequired: false);
            return;
        }

        Callable.From(RunWarmup).CallDeferred();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMGoBackRequest && _inputGateHeld)
            StartupInputGate.HandleBack();
    }

    private void OnTreeExiting()
    {
        _operation.Complete(restartRequired: false);
        if (!_inputGateHeld)
            return;

        _inputGateHeld = false;
        StartupInputGate.Exit(this);
    }

    private void BuildUI()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var vpSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
        _scale = Math.Max(vpSize.X, vpSize.Y) / 960f;

        var bg = new ScreenBackground();
        AddChild(bg);

        var panel = new StyledPanel(_scale, widthRatio: 0.5f);
        panel.UpdateSizeFromViewport(vpSize);
        AddChild(panel);

        _statusLabel = new StyledLabel("Compiling shaders...", _scale, fontSize: 20);
        panel.Content.AddChild(_statusLabel);

        _progressBar = new StyledProgressBar(_scale);
        _progressBar.MinValue = 0;
        _progressBar.MaxValue = 100;
        _progressBar.Value = 0;
        _progressBar.ShowPercentage = true;
        panel.Content.AddChild(_progressBar);

        _detailLabel = new StyledLabel("Enumerating resources...", _scale, fontSize: 12);
        _detailLabel.Modulate = new Color(0.7f, 0.7f, 0.7f);
        panel.Content.AddChild(_detailLabel);
    }

    private async void RunWarmup()
    {
        var sw = Stopwatch.StartNew();
        var batch = new List<(string path, Material material)>(BatchSize);
        SubViewport viewport = null;
        var outcome = ShaderWarmupOutcome.Completed;
        var outcomeReason = "all scheduled shaders were processed";
        BeginWarmupMemoryMonitoring();

        try
        {
            ThrowIfWarmupShouldDefer();
            _statusLabel.Text = "Scanning for shaders...";
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            // Enumerating paths is cheap. Loading the referenced resources is not:
            // a Material can retain a deep texture/shader graph even with IgnoreDeep.
            // Keep only strings here and stream the native resources through one
            // bounded render batch below.
            var looseResourcePaths = new List<string>();
            var seenResourcePaths = new HashSet<string>(StringComparer.Ordinal);
            CollectWarmupResourcePaths("res://", looseResourcePaths, seenResourcePaths);

            var scenePaths = new List<string>();
            CollectScenePaths("res://scenes", scenePaths);

            PatchHelper.Log(
                $"[ShaderWarmup] Found {looseResourcePaths.Count} loose material resources "
                    + $"and {scenePaths.Count} scenes to stream"
            );
            _statusLabel.Text = "Compiling shaders...";

            viewport = new SubViewport();
            viewport.Size = new Vector2I(64, 64);
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            viewport.TransparentBg = true;
            AddChild(viewport);

            using var whiteImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            whiteImage.SetPixel(0, 0, Colors.White);
            using var whiteTex = ImageTexture.CreateFromImage(whiteImage);

            var seenShaderKeys = new HashSet<string>(StringComparer.Ordinal);
            int totalSources = looseResourcePaths.Count + scenePaths.Count;
            int processedSources = 0;

            foreach (var path in looseResourcePaths)
            {
                try
                {
                    var material = LoadLooseMaterial(path);
                    if (material != null)
                        await QueueMaterialForWarmupAsync(
                            path,
                            material,
                            seenShaderKeys,
                            batch,
                            viewport,
                            whiteTex
                        );
                }
                catch (WarmupDeferredForMemoryException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    PatchHelper.Log($"[ShaderWarmup] Failed to load {path}: {ex.Message}");
                }

                processedSources++;
                await UpdateStreamingProgressAsync(
                    processedSources,
                    totalSources,
                    seenShaderKeys.Count
                );
            }

            foreach (var scenePath in scenePaths)
            {
                try
                {
                    using var packed = ResourceLoader.Load<PackedScene>(
                        scenePath,
                        null,
                        ResourceLoader.CacheMode.IgnoreDeep
                    );
                    if (packed != null)
                    {
                        var sceneMaterials = ExtractMaterialsFromSceneState(packed, scenePath);
                        for (
                            int materialIndex = 0;
                            materialIndex < sceneMaterials.Count;
                            materialIndex++
                        )
                        {
                            var (path, material) = sceneMaterials[materialIndex];
                            try
                            {
                                await QueueMaterialForWarmupAsync(
                                    path,
                                    material,
                                    seenShaderKeys,
                                    batch,
                                    viewport,
                                    whiteTex
                                );
                            }
                            catch
                            {
                                // QueueMaterialForWarmupAsync consumes the current
                                // entry even when its render batch throws. Release
                                // only entries we have not handed off yet.
                                for (
                                    int remaining = materialIndex + 1;
                                    remaining < sceneMaterials.Count;
                                    remaining++
                                )
                                    sceneMaterials[remaining].material.Dispose();
                                throw;
                            }
                        }
                    }
                }
                catch (WarmupDeferredForMemoryException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    PatchHelper.Log(
                        $"[ShaderWarmup] Failed to extract from {scenePath}: {ex.Message}"
                    );
                }

                processedSources++;
                await UpdateStreamingProgressAsync(
                    processedSources,
                    totalSources,
                    seenShaderKeys.Count
                );
            }

            if (batch.Count > 0)
                await FlushMaterialBatchAsync(batch, viewport, whiteTex);

            viewport.QueueFree();
            viewport = null;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            _progressBar.Value = 100;
            _statusLabel.Text = "Done!";
            _detailLabel.Text =
                $"Compiled {seenShaderKeys.Count} shaders in {sw.ElapsedMilliseconds}ms";
            PatchHelper.Log(
                $"[ShaderWarmup] Completed: {seenShaderKeys.Count} streamed materials "
                    + $"in {sw.ElapsedMilliseconds}ms"
            );

            await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        }
        catch (WarmupDeferredForMemoryException ex)
        {
            outcome = ShaderWarmupOutcome.DeferredMemoryPressure;
            outcomeReason = ex.Message;
            _statusLabel.Text = "Continuing with on-demand shaders...";
            _detailLabel.Text = "Warmup stopped to protect available memory.";
            PatchHelper.Log(
                $"[ShaderWarmup] deferred for memory safety after {sw.ElapsedMilliseconds}ms: {ex.Message}"
            );
        }
        catch (Exception ex)
        {
            outcome = ShaderWarmupOutcome.FailedButBypassed;
            outcomeReason = ex.GetType().Name;
            PatchHelper.Log($"[ShaderWarmup] Failed: {ex}");
        }
        finally
        {
            // FlushMaterialBatchAsync owns normal batch disposal. This path only
            // has work when collection/rendering threw between batches.
            ReleaseMaterials(batch);
            if (viewport != null && GodotObject.IsInstanceValid(viewport))
                viewport.QueueFree();
            EndWarmupMemoryMonitoring();
        }

        CompleteStateAndSignalRestart(outcome, outcomeReason);
    }

    private async Task QueueMaterialForWarmupAsync(
        string path,
        Material material,
        HashSet<string> seenShaderKeys,
        List<(string path, Material material)> batch,
        SubViewport viewport,
        ImageTexture whiteTex
    )
    {
        string shaderKey;
        try
        {
            shaderKey = GetShaderKey(material);
        }
        catch
        {
            material.Dispose();
            throw;
        }

        if (!seenShaderKeys.Add(shaderKey))
        {
            material.Dispose();
            return;
        }

        batch.Add((path, material));
        if (batch.Count >= BatchSize)
        {
            await FlushMaterialBatchAsync(batch, viewport, whiteTex);
            ThrowIfWarmupShouldDefer();
        }
    }

    private async Task FlushMaterialBatchAsync(
        List<(string path, Material material)> batch,
        SubViewport viewport,
        ImageTexture whiteTex
    )
    {
        var nodes = new List<Node>(batch.Count);
        try
        {
            foreach (var (path, material) in batch)
            {
                try
                {
                    var node = CreateWarmupNode(material, whiteTex);
                    if (node == null)
                        continue;

                    viewport.AddChild(node);
                    nodes.Add(node);
                }
                catch (Exception ex)
                {
                    PatchHelper.Log(
                        $"[ShaderWarmup] Failed to create node for {path}: {ex.Message}"
                    );
                }
            }

            // The first frame submits the draw; the second lets the rendering
            // backend finish compiling before the nodes and their materials leave.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        finally
        {
            foreach (var node in nodes)
            {
                if (GodotObject.IsInstanceValid(node))
                    node.QueueFree();
            }

            // QueueFree is deferred. Do not release a Material while a queued
            // Sprite/particle node can still submit it on this frame.
            if (nodes.Count > 0 && IsInsideTree())
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            ReleaseMaterials(batch);
        }
    }

    private async Task UpdateStreamingProgressAsync(int processed, int total, int shaderCount)
    {
        if (total > 0)
            _progressBar.Value = (double)processed / total * 99;
        _detailLabel.Text = $"Scanning {processed} / {Math.Max(total, 1)} · {shaderCount} shaders";

        // A long stretch of non-material scenes would otherwise run without the
        // batch flush yields above. Keep input/rendering responsive regardless.
        if (processed % 25 == 0)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ThrowIfWarmupShouldDefer();
        }
    }

    private static void BeginWarmupMemoryMonitoring()
    {
        try
        {
            LauncherModel.GetGodotApp()?.Call("beginWarmupMemoryMonitoring");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[ShaderWarmup] memory monitor start failed: {ex.Message}");
        }
    }

    private static void EndWarmupMemoryMonitoring()
    {
        try
        {
            LauncherModel.GetGodotApp()?.Call("endWarmupMemoryMonitoring");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[ShaderWarmup] memory monitor stop failed: {ex.Message}");
        }
    }

    private static void ThrowIfWarmupShouldDefer()
    {
        var decision = ShaderWarmupMemoryPolicy.Evaluate(ReadWarmupMemorySnapshot());
        if (decision.ShouldDefer)
            throw new WarmupDeferredForMemoryException(decision.Reason);
    }

    private static ShaderWarmupMemorySnapshot ReadWarmupMemorySnapshot()
    {
        try
        {
            var app = LauncherModel.GetGodotApp();
            if (app == null)
                return ShaderWarmupMemorySnapshot.Unavailable;

            var encoded = (string)app.Call("getWarmupMemorySnapshot");
            var fields = encoded?.Split('|');
            if (
                fields == null
                || fields.Length != 6
                || !int.TryParse(
                    fields[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var trimLevel
                )
                || (fields[1] != "0" && fields[1] != "1")
                || !long.TryParse(
                    fields[2],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var availableBytes
                )
                || !long.TryParse(
                    fields[3],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var lowMemoryThresholdBytes
                )
                || !long.TryParse(
                    fields[4],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var totalBytes
                )
                || !long.TryParse(
                    fields[5],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var processPssBytes
                )
            )
            {
                return ShaderWarmupMemorySnapshot.Unavailable;
            }

            return new ShaderWarmupMemorySnapshot(
                trimLevel,
                fields[1] == "1",
                availableBytes,
                lowMemoryThresholdBytes,
                totalBytes,
                processPssBytes
            );
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[ShaderWarmup] memory snapshot bridge failed: {ex.Message}");
            return ShaderWarmupMemorySnapshot.Unavailable;
        }
    }

    private static void ReleaseMaterials(List<(string path, Material material)> batch)
    {
        foreach (var (_, material) in batch)
            material.Dispose();
        batch.Clear();
    }

    private sealed class WarmupDeferredForMemoryException : Exception
    {
        public WarmupDeferredForMemoryException(string message)
            : base(message) { }
    }

    private void CompleteStateAndSignalRestart(ShaderWarmupOutcome outcome, string reason)
    {
        try
        {
            // Even a managed scan failure completes this optional warmup version.
            // The clean process after restart will compile missed shaders on demand
            // instead of entering a deterministic restart loop.
            _state.Complete(outcome, reason);
            _operation.Complete(restartRequired: true);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[ShaderWarmup] Failed to persist completion: {ex.Message}");
            // Without durable completion a restart would repeat the same warmup.
            _operation.Complete(restartRequired: false);
        }
    }

    private static Node CreateWarmupNode(Material mat, ImageTexture whiteTex)
    {
        if (mat is ParticleProcessMaterial particleMat)
        {
            var particles = new GpuParticles2D();
            particles.ProcessMaterial = particleMat;
            particles.Amount = 1;
            particles.Emitting = true;
            particles.OneShot = false;
            particles.Texture = whiteTex;
            return particles;
        }

        var sprite = new Sprite2D();
        sprite.Texture = whiteTex;
        sprite.Material = mat;
        return sprite;
    }

    private static string GetShaderKey(Material mat)
    {
        if (mat is ShaderMaterial sm && sm.Shader != null)
            return GetResourceKey(sm.Shader);
        if (mat is ParticleProcessMaterial)
            return $"particle#{mat.GetRid()}";
        return GetResourceKey(mat);
    }

    private static string GetResourceKey(Resource resource) =>
        string.IsNullOrEmpty(resource.ResourcePath)
            ? $"{resource.GetType().Name}#{resource.GetRid()}"
            : resource.ResourcePath;

    private static Material LoadLooseMaterial(string path)
    {
        // The text resource loader accepts many unrelated .tres base types.
        // ResourceLoader.Load<T> therefore can throw during its generated cast,
        // before the caller receives a wrapper it can dispose. Load the base
        // Resource instead so mismatches are also released deterministically.
        var resource = ResourceLoader.Load(path, null, ResourceLoader.CacheMode.IgnoreDeep);
        if (resource == null)
            return null;

        if (resource is Material material)
            return material;

        if (resource is Shader shader)
        {
            try
            {
                return new ShaderMaterial { Shader = shader };
            }
            finally
            {
                // ShaderMaterial owns a native reference after assignment.
                // Release the temporary loader wrapper immediately.
                shader.Dispose();
            }
        }

        resource.Dispose();
        return null;
    }

    private static void CollectWarmupResourcePaths(
        string dirPath,
        List<string> paths,
        HashSet<string> seenPaths
    )
    {
        try
        {
            using var dir = DirAccess.Open(dirPath);
            if (dir == null)
                return;

            dir.ListDirBegin();
            string fileName;
            while ((fileName = dir.GetNext()) != "")
            {
                if (fileName == "." || fileName == "..")
                    continue;

                var fullPath = $"{dirPath}/{fileName}";

                if (dir.CurrentIsDir())
                {
                    if (fileName == "debug")
                        continue;
                    CollectWarmupResourcePaths(fullPath, paths, seenPaths);
                    continue;
                }

                var cleanName = fileName.Replace(".remap", "");
                var cleanPath = $"{dirPath}/{cleanName}";

                if (
                    !cleanName.EndsWith(".tres")
                    && !cleanName.EndsWith(".gdshader")
                    && !cleanName.EndsWith(".material")
                )
                    continue;

                if (seenPaths.Contains(cleanPath) || !ResourceLoader.Exists(cleanPath))
                    continue;

                seenPaths.Add(cleanPath);
                paths.Add(cleanPath);
            }
            dir.ListDirEnd();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[ShaderWarmup] Failed to enumerate {dirPath}: {ex.Message}");
        }
    }

    private static void CollectScenePaths(string dirPath, List<string> paths)
    {
        try
        {
            using var dir = DirAccess.Open(dirPath);
            if (dir == null)
                return;

            dir.ListDirBegin();
            string fileName;
            while ((fileName = dir.GetNext()) != "")
            {
                if (fileName == "." || fileName == "..")
                    continue;

                var fullPath = $"{dirPath}/{fileName}";

                if (dir.CurrentIsDir())
                {
                    if (fileName == "debug")
                        continue;
                    CollectScenePaths(fullPath, paths);
                    continue;
                }

                var cleanName = fileName.Replace(".remap", "");
                if (!cleanName.EndsWith(".tscn"))
                    continue;

                var cleanPath = $"{dirPath}/{cleanName}";
                if (ResourceLoader.Exists(cleanPath))
                    paths.Add(cleanPath);
            }
            dir.ListDirEnd();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[ShaderWarmup] Failed to enumerate {dirPath}: {ex.Message}");
        }
    }

    private static List<(string path, Material material)> ExtractMaterialsFromSceneState(
        PackedScene packed,
        string scenePath
    )
    {
        var result = new List<(string path, Material material)>();
        var seenInstances = new HashSet<ulong>();
        var state = packed.GetState();
        int nodeCount = state.GetNodeCount();

        for (int n = 0; n < nodeCount; n++)
        {
            int propCount = state.GetNodePropertyCount(n);
            for (int p = 0; p < propCount; p++)
            {
                var propName = state.GetNodePropertyName(n, p).ToString();
                if (
                    propName != "material"
                    && propName != "process_material"
                    && propName != "surface_material_override/0"
                )
                    continue;

                try
                {
                    var val = state.GetNodePropertyValue(n, p);
                    if (val.Obj is Material mat)
                    {
                        if (seenInstances.Add(mat.GetInstanceId()))
                            result.Add(($"{scenePath}#node{n}#{propName}", mat));
                    }
                    else if (val.Obj is Shader shader)
                    {
                        if (seenInstances.Add(shader.GetInstanceId()))
                        {
                            var shaderMat = new ShaderMaterial { Shader = shader };
                            shader.Dispose();
                            result.Add(($"{scenePath}#node{n}#{propName}", shaderMat));
                        }
                    }
                }
                catch (Exception ex)
                {
                    PatchHelper.Log(
                        $"[ShaderWarmup] Failed to read property {propName} in {scenePath}: {ex.Message}"
                    );
                }
            }
        }

        return result;
    }
}
