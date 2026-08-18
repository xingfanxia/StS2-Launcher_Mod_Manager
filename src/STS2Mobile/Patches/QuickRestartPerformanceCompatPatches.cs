using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Godot;
using HarmonyLib;
using STS2Mobile.Launcher;

namespace STS2Mobile.Patches;

// Quick Restart v2.0.0 performs SaveManager.HasRunSave (ultimately a synchronous
// FileAccess.FileExists) and rewrites hidden progress-bar state from _Process on
// every frame, even while no restart key is held. Keep its node processing only
// for an active hold. This is exact-binary gated so any updated mod fails open.
internal static class QuickRestartPerformanceCompatPatches
{
    private const string IndicatorTypeName =
        "QuickRestart.QuickRestartCode.HoldProgressIndicator";
    private const string KeybindTypeName = "QuickRestart.QuickRestartCode.Keybind";
    private const string DisableEnvironmentVariable =
        "STS2_DISABLE_QUICK_RESTART_PERFORMANCE_FIX";
    private const string ProbeEnvironmentVariable = "STS2_DEBUG_QUICK_RESTART_PROBE";

    private static readonly object Gate = new();
    private static Harmony _harmony;
    private static bool _installed;
    private static bool _attempted;
    private static bool _patched;
    private static PropertyInfo _instanceProperty;
    private static FieldInfo _isHoldingField;
    private static FieldInfo _triggeredField;
    private static MethodInfo _hideAndResetMethod;
    private static MethodInfo _readyMethod;
    private static MethodInfo _keybindPostfixMethod;
    private static MethodInfo _processMethod;
    private static bool _probeInstalled;
    [ThreadStatic]
    private static int _canRestartDepth;
    private static long _processCalls;
    private static long _processUsec;
    private static long _canRestartCalls;
    private static long _canRestartUsec;
    private static long _fileExistsCalls;
    private static long _resetCalls;
    private static long _resetUsec;
    private static long _inputEnableCalls;
    private static long _inputDisableCalls;
    private static long _visibleProcessFrames;
    private static long _restartCalls;
    private static long _pauseRestartCalls;

    internal static void Install(Harmony harmony)
    {
        lock (Gate)
        {
            if (_installed)
                return;
            _installed = true;
            _harmony = harmony;
        }

        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            TryPatch(assembly);
        PatchHelper.Log("QuickRestartPerformanceCompat: exact-identity listener installed");
    }

    private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args) =>
        TryPatch(args.LoadedAssembly);

    private static void TryPatch(Assembly assembly)
    {
        if (assembly == null || assembly.IsDynamic)
            return;
        lock (Gate)
        {
            if (_patched)
                return;
        }

        try
        {
            AssemblyName name = assembly.GetName();
            if (!string.Equals(name.Name, QuickRestartPerformancePolicy.AssemblyName, StringComparison.Ordinal))
                return;
            if (!ModAssemblyRegistry.IsModAssembly(assembly))
            {
                PatchHelper.Log("QuickRestartPerformanceCompat: candidate is not an external mod; skipped");
                return;
            }
            if (string.IsNullOrWhiteSpace(assembly.Location) || !File.Exists(assembly.Location))
            {
                PatchHelper.Log("QuickRestartPerformanceCompat: candidate has no hashable location; skipped");
                return;
            }

            string sha256;
            using (var stream = File.OpenRead(assembly.Location))
                sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (
                !QuickRestartPerformancePolicy.MatchesIdentity(
                    name.Name,
                    name.Version,
                    assembly.ManifestModule.ModuleVersionId,
                    sha256,
                    isExternalModAssembly: true
                )
            )
            {
                PatchHelper.Log("QuickRestartPerformanceCompat: unknown Quick Restart binary; skipped");
                return;
            }

            lock (Gate)
            {
                if (_attempted)
                    return;
                _attempted = true;
            }

            Type indicatorType = assembly.GetType(IndicatorTypeName, throwOnError: false);
            Type keybindType = assembly.GetType(KeybindTypeName, throwOnError: false);
            MethodInfo ready = ExactInstanceMethod(indicatorType, "_Ready", Type.EmptyTypes);
            MethodInfo process = ExactInstanceMethod(indicatorType, "_Process", new[] { typeof(double) });
            MethodInfo keybindPostfix = ExactStaticMethod(
                keybindType,
                "Postfix",
                new[] { typeof(InputEvent) }
            );
            PropertyInfo instanceProperty = indicatorType?.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );
            FieldInfo isHoldingField = keybindType?.GetField(
                "IsHolding",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );
            FieldInfo triggeredField = keybindType?.GetField(
                "Triggered",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );
            MethodInfo hideAndReset = ExactInstanceMethod(
                indicatorType,
                "HideAndReset",
                Type.EmptyTypes
            );
            if (
                ready == null
                || ready.ReturnType != typeof(void)
                || process == null
                || process.ReturnType != typeof(void)
                || keybindPostfix == null
                || keybindPostfix.ReturnType != typeof(void)
                || instanceProperty?.PropertyType != indicatorType
                || instanceProperty.GetMethod == null
                || !instanceProperty.GetMethod.IsStatic
                || isHoldingField?.FieldType != typeof(bool)
                || triggeredField?.FieldType != typeof(bool)
                || hideAndReset == null
                || hideAndReset.ReturnType != typeof(void)
            )
            {
                PatchHelper.Log("QuickRestartPerformanceCompat: target contract mismatch; skipped");
                return;
            }

            lock (Gate)
            {
                if (_patched)
                    return;
                _instanceProperty = instanceProperty;
                _isHoldingField = isHoldingField;
                _triggeredField = triggeredField;
                _hideAndResetMethod = hideAndReset;
                _readyMethod = ready;
                _keybindPostfixMethod = keybindPostfix;
                _processMethod = process;

                if (!IsFixDisabled())
                {
                    _harmony.Patch(
                        ready,
                        postfix: new HarmonyMethod(
                            AccessTools.Method(typeof(QuickRestartPerformanceCompatPatches), nameof(ReadyPostfix))
                        )
                    );
                    _harmony.Patch(
                        keybindPostfix,
                        postfix: new HarmonyMethod(
                            AccessTools.Method(typeof(QuickRestartPerformanceCompatPatches), nameof(InputPostfix))
                        )
                    );
                    _harmony.Patch(
                        process,
                        postfix: new HarmonyMethod(
                            AccessTools.Method(typeof(QuickRestartPerformanceCompatPatches), nameof(ProcessPostfix))
                        )
                    );
                }
                _patched = true;
            }

            PatchHelper.Log(
                IsFixDisabled()
                    ? "QuickRestartPerformanceCompat: exact v2.0.0 matched; fix disabled for A/B baseline"
                    : "QuickRestartPerformanceCompat: exact v2.0.0 matched; idle processing disabled"
            );
            TryInstallDebugProbe(assembly, process, hideAndReset);
        }
        catch (Exception ex)
        {
            // Compatibility code must never prevent the mod or the game from loading.
            TryRemoveLifecyclePatches();
            PatchHelper.Log(
                $"QuickRestartPerformanceCompat: fail-open ({ex.GetType().Name})"
            );
        }
    }

    private static MethodInfo ExactInstanceMethod(Type type, string name, Type[] parameters) =>
        type?.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameters,
            modifiers: null
        );

    private static MethodInfo ExactStaticMethod(Type type, string name, Type[] parameters) =>
        type?.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameters,
            modifiers: null
        );

    private static void ReadyPostfix(Node __instance) => __instance?.SetProcess(false);

    private static void InputPostfix()
    {
        try
        {
            var indicator = _instanceProperty?.GetValue(null) as Node;
            if (indicator == null)
                return;

            var action = QuickRestartPerformancePolicy.AfterInput(
                ReadBool(_isHoldingField),
                ReadBool(_triggeredField)
            );
            if (action == QuickRestartProcessAction.Enable)
            {
                if (_probeInstalled)
                    Interlocked.Increment(ref _inputEnableCalls);
                indicator.SetProcess(true);
                return;
            }

            if (
                action == QuickRestartProcessAction.ResetAndDisable
                && indicator.IsProcessing()
            )
                _hideAndResetMethod?.Invoke(indicator, null);
            if (_probeInstalled)
                Interlocked.Increment(ref _inputDisableCalls);
            indicator.SetProcess(false);
        }
        catch
        {
            // Fail open during input; the mod's original postfix already ran.
        }
    }

    private static void ProcessPostfix(Node __instance)
    {
        try
        {
            if (
                QuickRestartPerformancePolicy.AfterProcess(
                    ReadBool(_isHoldingField),
                    ReadBool(_triggeredField)
                ) == QuickRestartProcessAction.Disable
            )
                __instance?.SetProcess(false);
        }
        catch
        {
            // Fail open after the mod's original frame callback.
        }
    }

    private static bool ReadBool(FieldInfo field) => field?.GetValue(null) is true;

    private static bool IsFixDisabled() =>
        string.Equals(
            System.Environment.GetEnvironmentVariable(DisableEnvironmentVariable),
            "1",
            StringComparison.Ordinal
        );

    internal static void LogAndResetDebugSummary(string segment)
    {
        if (!_probeInstalled)
            return;
        PatchHelper.Log(
            $"[QuickRestartProbe] summary segment={SanitizeSegment(segment)} "
                + $"process_calls={Interlocked.Exchange(ref _processCalls, 0)} "
                + $"process_us={Interlocked.Exchange(ref _processUsec, 0)} "
                + $"can_restart_calls={Interlocked.Exchange(ref _canRestartCalls, 0)} "
                + $"can_restart_us={Interlocked.Exchange(ref _canRestartUsec, 0)} "
                + $"file_exists_calls={Interlocked.Exchange(ref _fileExistsCalls, 0)} "
                + $"reset_calls={Interlocked.Exchange(ref _resetCalls, 0)} "
                + $"reset_us={Interlocked.Exchange(ref _resetUsec, 0)}"
        );
        PatchHelper.Log(
            $"[QuickRestartBehaviorProbe] summary segment={SanitizeSegment(segment)} "
                + $"input_enable={Interlocked.Exchange(ref _inputEnableCalls, 0)} "
                + $"input_disable={Interlocked.Exchange(ref _inputDisableCalls, 0)} "
                + $"visible_frames={Interlocked.Exchange(ref _visibleProcessFrames, 0)} "
                + $"restart_calls={Interlocked.Exchange(ref _restartCalls, 0)} "
                + $"pause_calls={Interlocked.Exchange(ref _pauseRestartCalls, 0)}"
        );
    }

    private static void TryInstallDebugProbe(
        Assembly assembly,
        MethodInfo process,
        MethodInfo hideAndReset
    )
    {
        if (
            !string.Equals(
                System.Environment.GetEnvironmentVariable(ProbeEnvironmentVariable),
                "1",
                StringComparison.Ordinal
            )
        )
            return;

        try
        {
            Type restarterType = assembly.GetType(
                "QuickRestart.QuickRestartCode.Restarter",
                throwOnError: false
            );
            Type pauseMenuButtonPatchType = restarterType?.GetNestedType(
                "PauseMenuButtonPatch",
                BindingFlags.Public | BindingFlags.NonPublic
            );
            MethodInfo canRestart = ExactStaticMethod(restarterType, "CanRestart", Type.EmptyTypes);
            MethodInfo restart = ExactStaticMethod(
                restarterType,
                "RestartRoomAsync",
                Type.EmptyTypes
            );
            MethodInfo pauseRestart = ExactStaticMethod(
                pauseMenuButtonPatchType,
                "OnPressed",
                Type.EmptyTypes
            );
            MethodInfo fileExists = AccessTools.Method(
                typeof(Godot.FileAccess),
                nameof(Godot.FileAccess.FileExists),
                new[] { typeof(string) }
            );
            if (
                canRestart == null
                || canRestart.ReturnType != typeof(bool)
                || restart == null
                || restart.ReturnType != typeof(System.Threading.Tasks.Task)
                || pauseRestart == null
                || pauseRestart.ReturnType != typeof(void)
                || fileExists == null
            )
            {
                PatchHelper.Log("[QuickRestartProbe] target mismatch; probe inactive");
                return;
            }

            _harmony.Patch(
                process,
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(QuickRestartPerformanceCompatPatches), nameof(ProcessProbePrefix))
                ),
                postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(QuickRestartPerformanceCompatPatches), nameof(ProcessProbePostfix))
                )
            );
            _harmony.Patch(
                canRestart,
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(QuickRestartPerformanceCompatPatches), nameof(CanRestartProbePrefix))
                ),
                postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(QuickRestartPerformanceCompatPatches), nameof(CanRestartProbePostfix))
                )
            );
            _harmony.Patch(
                fileExists,
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(QuickRestartPerformanceCompatPatches), nameof(FileExistsProbePrefix))
                )
            );
            _harmony.Patch(
                hideAndReset,
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(QuickRestartPerformanceCompatPatches), nameof(ResetProbePrefix))
                ),
                postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(QuickRestartPerformanceCompatPatches), nameof(ResetProbePostfix))
                )
            );
            _harmony.Patch(
                restart,
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(QuickRestartPerformanceCompatPatches), nameof(RestartProbePrefix))
                )
            );
            _harmony.Patch(
                pauseRestart,
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(QuickRestartPerformanceCompatPatches), nameof(PauseRestartProbePrefix))
                )
            );
            _probeInstalled = true;
            PatchHelper.Log("[QuickRestartProbe] installed");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[QuickRestartProbe] unavailable error={ex.GetType().Name}");
        }
    }

    private static void ProcessProbePrefix(ref long __state)
    {
        Interlocked.Increment(ref _processCalls);
        __state = Stopwatch.GetTimestamp();
    }

    private static void ProcessProbePostfix(Node __instance, long __state)
    {
        Interlocked.Add(ref _processUsec, ElapsedUsec(__state));
        if (__instance is CanvasItem canvasItem && canvasItem.Visible)
            Interlocked.Increment(ref _visibleProcessFrames);
    }

    private static void CanRestartProbePrefix(ref long __state)
    {
        Interlocked.Increment(ref _canRestartCalls);
        _canRestartDepth++;
        __state = Stopwatch.GetTimestamp();
    }

    private static void CanRestartProbePostfix(long __state)
    {
        Interlocked.Add(ref _canRestartUsec, ElapsedUsec(__state));
        if (_canRestartDepth > 0)
            _canRestartDepth--;
    }

    private static void FileExistsProbePrefix()
    {
        if (_canRestartDepth > 0)
            Interlocked.Increment(ref _fileExistsCalls);
    }

    private static void ResetProbePrefix(ref long __state)
    {
        Interlocked.Increment(ref _resetCalls);
        __state = Stopwatch.GetTimestamp();
    }

    private static void ResetProbePostfix(long __state) =>
        Interlocked.Add(ref _resetUsec, ElapsedUsec(__state));

    private static void RestartProbePrefix() => Interlocked.Increment(ref _restartCalls);

    private static void PauseRestartProbePrefix() =>
        Interlocked.Increment(ref _pauseRestartCalls);

    private static long ElapsedUsec(long start)
    {
        long elapsed = Stopwatch.GetTimestamp() - start;
        return elapsed <= 0 ? 0 : elapsed * 1_000_000 / Stopwatch.Frequency;
    }

    private static string SanitizeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return "unknown";
        foreach (char character in segment)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character == '-'))
                return "unknown";
        }
        return segment;
    }

    private static void TryRemoveLifecyclePatches()
    {
        try
        {
            MethodInfo readyPostfix = AccessTools.Method(
                typeof(QuickRestartPerformanceCompatPatches),
                nameof(ReadyPostfix)
            );
            MethodInfo inputPostfix = AccessTools.Method(
                typeof(QuickRestartPerformanceCompatPatches),
                nameof(InputPostfix)
            );
            MethodInfo processPostfix = AccessTools.Method(
                typeof(QuickRestartPerformanceCompatPatches),
                nameof(ProcessPostfix)
            );
            if (_readyMethod != null && readyPostfix != null)
                _harmony.Unpatch(_readyMethod, readyPostfix);
            if (_keybindPostfixMethod != null && inputPostfix != null)
                _harmony.Unpatch(_keybindPostfixMethod, inputPostfix);
            if (_processMethod != null && processPostfix != null)
                _harmony.Unpatch(_processMethod, processPostfix);
        }
        catch { }
    }
}
