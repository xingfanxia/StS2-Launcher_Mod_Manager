using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace STS2Mobile.Platform;

// Mono compiles a method the first time it runs, so the first card of a kind,
// the first enemy move or the first visit to a screen each stall the frame
// while dozens of methods are compiled. A background thread compiles the game
// assembly ahead of time instead, combat code first, and stops before the
// fixed code arena gets tight.
internal static class IosJitWarmup
{
    // Share of the code arena the warm-up may fill; the rest stays for mods and play.
    private const double ArenaBudget = 0.75;

    private static readonly string[] Priority =
    {
        "MegaCrit.Sts2.Core.Commands",
        "MegaCrit.Sts2.Core.Combat",
        "MegaCrit.Sts2.Core.GameActions",
        "MegaCrit.Sts2.Core.Hooks",
        "MegaCrit.Sts2.Core.Entities",
        "MegaCrit.Sts2.Core.Nodes.Combat",
        "MegaCrit.Sts2.Core.Nodes.Cards",
        "MegaCrit.Sts2.Core.Models",
        "MegaCrit.Sts2.Core.Nodes",
        "MegaCrit.Sts2.Core.Runs",
        "MegaCrit.Sts2.Core.Map",
    };

    // Never reached on a device, or only from developer tools.
    private static readonly string[] Skipped =
    {
        "MegaCrit.Sts2.Core.DevConsole",
        "MegaCrit.Sts2.Core.Debug",
        "MegaCrit.Sts2.Core.AutoSlay",
    };

    public static void Start()
    {
        if (System.Environment.GetEnvironmentVariable("STS2_NO_JIT_WARMUP") == "1")
            return;
        new Thread(Run)
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "JitWarmup",
        }.Start();
    }

    private static int Rank(Type type)
    {
        var name = type.Namespace ?? "";
        for (var i = 0; i < Priority.Length; i++)
        {
            if (name.StartsWith(Priority[i], StringComparison.Ordinal))
                return i;
        }
        return Priority.Length;
    }

    private static void Run()
    {
        try
        {
            // A method that touches a class whose constructor has not run yet is
            // left alone, so later passes pick up what the game has initialized since.
            foreach (var delay in new[] { 3, 20, 30, 60, 120 })
            {
                Thread.Sleep(delay * 1000);
                if (!Pass())
                    break;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[JitWarmup] failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Type[] TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return Array.FindAll(ex.Types, type => type != null);
        }
        catch (Exception)
        {
            return Array.Empty<Type>();
        }
    }

    // Returns false when the arena budget is reached.
    private static bool Pass()
    {
        {
            // The game first, then the engine bindings, this launcher and every mod
            // loaded so far (mods arrive after the first pass).
            var gameTypes = TypesOf(typeof(MegaCrit.Sts2.Core.Nodes.NGame).Assembly);
            Array.Sort(gameTypes, (a, b) => Rank(a).CompareTo(Rank(b)));
            var all_types = new List<Type>(gameTypes);
            all_types.AddRange(TypesOf(typeof(Godot.GodotObject).Assembly));
            all_types.AddRange(TypesOf(typeof(IosJitWarmup).Assembly));
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string location;
                try
                {
                    location = assembly.Location;
                }
                catch (Exception)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(location)
                    && location.StartsWith(AppPaths.ExternalModsDir, StringComparison.Ordinal))
                    all_types.AddRange(TypesOf(assembly));
            }
            var types = all_types.ToArray();

            var arena = IosNative.JitArenaStats();
            var startUsed = arena.Used;
            var limit = (long)(arena.Size * ArenaBudget);
            var started = Stopwatch.GetTimestamp();
            int prepared = 0, failed = 0;
            const BindingFlags all =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var stoppedEarly = false;

            foreach (var type in types)
            {
                if (type.IsInterface || type.ContainsGenericParameters)
                    continue;
                var space = type.Namespace ?? "";
                if (Array.Exists(Skipped, skip => space.StartsWith(skip, StringComparison.Ordinal)))
                    continue;

                var methods = new List<MethodBase>();
                try
                {
                    methods.AddRange(type.GetMethods(all));
                    methods.AddRange(type.GetConstructors(all));
                }
                catch (Exception)
                {
                    failed++;
                    continue;
                }
                foreach (var method in methods)
                {
                    if (method.IsAbstract || method.ContainsGenericParameters
                        || (method.Attributes & MethodAttributes.PinvokeImpl) != 0
                        || (method.MethodImplementationFlags
                            & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) != 0)
                        continue;
                    try
                    {
                        // PrepareMethod does nothing on Mono, and asking for the entry
                        // point also runs the class constructor, which would start the
                        // game's static state early and off the main thread.
                        if (IosNative.CompileOnly(method.MethodHandle))
                            prepared++;
                        else
                            failed++;
                    }
                    catch (Exception)
                    {
                        failed++;
                    }
                    if (prepared % 256 == 0)
                    {
                        if (IosNative.JitArenaStats().Used > limit)
                        {
                            stoppedEarly = true;
                            break;
                        }
                        Thread.Sleep(1);
                    }
                }
                if (stoppedEarly)
                    break;
            }

            var seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
            arena = IosNative.JitArenaStats();
            PatchHelper.Log(
                $"[JitWarmup] {(stoppedEarly ? "stopped at the arena budget" : "pass finished")}: "
                    + $"{prepared} methods ready, {failed} left for first use, {seconds:F1} s, "
                    + $"code arena {startUsed >> 20} -> {arena.Used >> 20} of {arena.Size >> 20} MiB"
            );
            return !stoppedEarly;
        }
    }
}
