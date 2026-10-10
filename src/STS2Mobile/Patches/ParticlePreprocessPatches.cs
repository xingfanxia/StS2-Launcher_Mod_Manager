using System;
using Godot;

namespace STS2Mobile.Patches;

// A particle system with a preprocess time is simulated for that whole time, in
// fixed steps, on the frame it first appears. The main menu's cloud layers ask
// for 400 seconds, which is thousands of steps (each a small GPU upload) inside
// one frame. Long preprocess times get a coarser simulation step; rendering
// still interpolates between steps, so slow effects look the same.
public static class ParticlePreprocessPatches
{
    // Preprocess longer than this is treated as "already drifting for a while".
    private const double LongPreprocessSeconds = 20;

    // Simulation steps allowed for the preprocess of one system.
    private const double StepBudget = 600;

    public static void Apply() => Callable.From(Attach).CallDeferred();

    private static void Attach()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Callable.From(Attach).CallDeferred();
            return;
        }
        tree.NodeAdded += OnNodeAdded;
        PatchHelper.Log("[Particles] long-preprocess limiter attached");
    }

    private static void OnNodeAdded(Node node)
    {
        if (node is not GpuParticles2D particles || particles.Preprocess <= LongPreprocessSeconds)
            return;
        try
        {
            // 0 means "every rendered frame", which the engine preprocesses at 30 steps a second.
            int current = particles.FixedFps > 0 ? particles.FixedFps : 30;
            int wanted = Math.Max(1, (int)Math.Ceiling(StepBudget / particles.Preprocess));
            if (wanted >= current)
                return;
            particles.FixedFps = wanted;
            particles.Interpolate = true;
            PatchHelper.Log(
                $"[Particles] {particles.Name}: {particles.Preprocess:F0} s preprocess, simulation step {current} -> {wanted} per second"
            );
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Particles] could not adjust {node.Name}: {ex.Message}");
        }
    }
}
