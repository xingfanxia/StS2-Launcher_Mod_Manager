using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace STS2Mobile.Patches;

// Debug-only anonymous attribution for the sequential third-party mod loader.
// Repository-safe output contains only per-run ordinals, durations, and status.
internal static class DebugModLoadTimingPatches
{
    private const string ArmEnvironmentVariable = "STS2_DEBUG_MOD_LOAD_TIMING";
    private static bool _enabled;
    private static int _nextOrdinal;

    [ThreadStatic]
    private static int _currentOrdinal;
    [ThreadStatic]
    private static int _initializerIndex;
    [ThreadStatic]
    private static int _patchAllIndex;
    [ThreadStatic]
    private static int _patchAllDepth;
    [ThreadStatic]
    private static long _modStarted;
    [ThreadStatic]
    private static long _initializerUsec;
    [ThreadStatic]
    private static long _patchAllUsec;

    internal static void Apply(Harmony harmony)
    {
        _enabled = string.Equals(
            System.Environment.GetEnvironmentVariable(ArmEnvironmentVariable),
            "1",
            StringComparison.Ordinal
        );
        if (!_enabled)
            return;

        try
        {
            MethodInfo callInitializer = typeof(ModManager).GetMethod(
                "CallModInitializer",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(Type) },
                modifiers: null
            );
            if (callInitializer == null || callInitializer.ReturnType != typeof(bool))
                throw new MissingMethodException("ModManager.CallModInitializer(Type)");

            harmony.Patch(
                callInitializer,
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(DebugModLoadTimingPatches), nameof(InitializerPrefix))
                ),
                postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(DebugModLoadTimingPatches), nameof(InitializerPostfix))
                )
            );

            MethodInfo[] patchAllMethods = typeof(Harmony)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method =>
                    method.Name == nameof(Harmony.PatchAll)
                    && (
                        method.GetParameters().Length == 0
                        || method.GetParameters().Select(parameter => parameter.ParameterType)
                            .SequenceEqual(new[] { typeof(Assembly) })
                    )
                )
                .ToArray();
            if (patchAllMethods.Length == 0)
                throw new MissingMethodException("Harmony.PatchAll");

            var patchAllPrefix = new HarmonyMethod(
                AccessTools.Method(typeof(DebugModLoadTimingPatches), nameof(PatchAllPrefix))
            );
            var patchAllPostfix = new HarmonyMethod(
                AccessTools.Method(typeof(DebugModLoadTimingPatches), nameof(PatchAllPostfix))
            );
            foreach (MethodInfo patchAll in patchAllMethods)
                harmony.Patch(patchAll, prefix: patchAllPrefix, postfix: patchAllPostfix);

            PatchHelper.Log("[ModLoadProbe] installed");
        }
        catch (Exception ex)
        {
            _enabled = false;
            PatchHelper.Log($"[ModLoadProbe] unavailable error={ex.GetType().Name}");
        }
    }

    internal static void BeginMod()
    {
        if (!_enabled)
            return;
        _currentOrdinal = Interlocked.Increment(ref _nextOrdinal);
        _initializerIndex = 0;
        _patchAllIndex = 0;
        _patchAllDepth = 0;
        _initializerUsec = 0;
        _patchAllUsec = 0;
        _modStarted = Stopwatch.GetTimestamp();
    }

    internal static void EndMod(bool loaded)
    {
        if (!_enabled || _currentOrdinal == 0)
            return;
        PatchHelper.Log(
            $"[ModLoadProbe] item={_currentOrdinal} total_us={ElapsedUsec(_modStarted)} "
                + $"initializer_us={_initializerUsec} patchall_us={_patchAllUsec} "
                + $"initializer_count={_initializerIndex} patchall_count={_patchAllIndex} "
                + $"loaded={(loaded ? 1 : 0)}"
        );
        _currentOrdinal = 0;
        _modStarted = 0;
    }

    private static void InitializerPrefix(ref long __state)
    {
        if (!_enabled || _currentOrdinal == 0)
            return;
        _initializerIndex++;
        __state = Stopwatch.GetTimestamp();
    }

    private static void InitializerPostfix(bool __result, long __state)
    {
        if (__state == 0 || _currentOrdinal == 0)
            return;
        long durationUsec = ElapsedUsec(__state);
        _initializerUsec += durationUsec;
        PatchHelper.Log(
            $"[ModLoadProbe] initializer item={_currentOrdinal} index={_initializerIndex} "
                + $"duration_us={durationUsec} success={(__result ? 1 : 0)}"
        );
    }

    private static void PatchAllPrefix(ref long __state)
    {
        if (!_enabled || _currentOrdinal == 0)
            return;
        _patchAllDepth++;
        if (_patchAllDepth != 1)
            return;
        _patchAllIndex++;
        __state = Stopwatch.GetTimestamp();
    }

    private static void PatchAllPostfix(long __state)
    {
        if (!_enabled || _currentOrdinal == 0 || _patchAllDepth == 0)
            return;
        _patchAllDepth--;
        if (_patchAllDepth != 0 || __state == 0)
            return;
        long durationUsec = ElapsedUsec(__state);
        _patchAllUsec += durationUsec;
        PatchHelper.Log(
            $"[ModLoadProbe] patchall item={_currentOrdinal} index={_patchAllIndex} "
                + $"duration_us={durationUsec}"
        );
    }

    private static long ElapsedUsec(long start)
    {
        long elapsed = Stopwatch.GetTimestamp() - start;
        return elapsed <= 0 ? 0 : elapsed * 1_000_000 / Stopwatch.Frequency;
    }
}
