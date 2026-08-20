using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using STS2Mobile.Launcher;

namespace STS2Mobile.Patches;

// Redirects the game's built-in mod loader to scan AppPaths.ExternalModsDir
// (/storage/emulated/0/StS2LauncherMM/Mods) instead of the "mods" folder next
// to the game executable. As of sts2 v0.107.0 ModManager.Initialize is async
// (Task), so the compiler hoists the body — including Path.Combine(..., "mods")
// — into a generated MoveNext state machine and the old ldstr "mods"
// transpiler against the main body no longer matches.
//
// New approach (issue #45): prefix-swap the IModManagerFileIo argument with a
// wrapper that transparently redirects any path under "mods" to our external
// directory. The game's own scanner then walks the right folder without us
// touching its IL. The Steam-only enumerator is still short-circuited because
// Android has no Steamworks runtime.
public static class ModLoaderPatches
{
    public static void Apply(Harmony harmony)
    {
        PatchHelper.Patch(
            harmony,
            typeof(ModManager),
            "Initialize",
            prefix: PatchHelper.Method(typeof(ModLoaderPatches), nameof(InitializePrefix)),
            postfix: PatchHelper.Method(typeof(ModLoaderPatches), nameof(InitializePostfix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(ModManager),
            "TryLoadMod",
            prefix: PatchHelper.Method(typeof(ModLoaderPatches), nameof(TryLoadModPrefix)),
            postfix: PatchHelper.Method(typeof(ModLoaderPatches), nameof(TryLoadModPostfix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(ModManager),
            "CallModInitializer",
            transpiler: PatchHelper.Method(
                typeof(ModLoaderPatches),
                nameof(CallModInitializerTranspiler)
            )
        );
        PatchHelper.Patch(
            harmony,
            typeof(ModManager),
            "ReadSteamMods",
            prefix: PatchHelper.Method(typeof(ModLoaderPatches), nameof(ReadSteamModsPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(ModManager),
            "ReadModManifest",
            prefix: PatchHelper.Method(typeof(ModLoaderPatches), nameof(ReadModManifestPrefix))
        );
    }

    // Swap the fileIo argument the game just constructed for our redirecting
    // wrapper; the original Initialize body then continues unchanged. Using
    // `ref` keeps this signature-stable across the sync/void → async/Task
    // rewrite the game shipped in v0.107.0.
    public static bool InitializePrefix(ref IModManagerFileIo fileIo)
    {
        StartupRecoveryBridge.RecordStage("mod-discovery");
        StartupPerformanceTracker.AdvanceTo(StartupStageId.ModDiscovery);
        var originalFileIo = fileIo;
        fileIo = new ExternalModsFileIo(AppPaths.ExternalModsDir, originalFileIo);
        PatchHelper.Log(
            $"[Mods] Redirected ModManager.Initialize fileIo -> {AppPaths.ExternalModsDir}"
        );
        return true;
    }

    public static void InitializePostfix(Task __result)
    {
        if (__result == null)
        {
            StartupRecoveryBridge.RecordStage("game-startup");
            StartupPerformanceTracker.AdvanceTo(StartupStageId.GameStartup);
            return;
        }
        if (__result.IsCompleted)
        {
            if (__result.IsCompletedSuccessfully)
            {
                StartupRecoveryBridge.RecordStage("game-startup");
                StartupPerformanceTracker.AdvanceTo(StartupStageId.GameStartup);
            }
            else
            {
                StartupPerformanceTracker.EndActive(StartupStageTerminal.Failed);
            }
            return;
        }

        _ = __result.ContinueWith(
            completed =>
            {
                try
                {
                    Callable
                        .From(() =>
                        {
                            if (completed.IsCompletedSuccessfully)
                            {
                                StartupRecoveryBridge.RecordStage("game-startup");
                                StartupPerformanceTracker.AdvanceTo(StartupStageId.GameStartup);
                            }
                            else
                            {
                                StartupPerformanceTracker.EndActive(StartupStageTerminal.Failed);
                            }
                        })
                        .CallDeferred();
                }
                catch (Exception ex)
                {
                    PatchHelper.Log(
                        $"[StartupRecovery] mod completion bridge failed: {ex.Message}"
                    );
                }
            },
            TaskScheduler.Default
        );
    }

    // TryLoadMod is the last game-owned boundary before a third-party DLL/PCK,
    // initializer, or Harmony PatchAll executes. An abrupt native exit can leave
    // the candidate without a postfix; a normal loaded return records success.
    public static void TryLoadModPrefix(Mod mod)
    {
        DebugModLoadTimingPatches.BeginMod();
        var modId = mod?.manifest?.id;
        if (string.IsNullOrWhiteSpace(modId))
            return;
        StartupRecoveryBridge.RecordStage("mod-loading");
        StartupPerformanceTracker.AdvanceTo(StartupStageId.ModLoad);
        StartupRecoveryBridge.RecordModCandidate(modId);
    }

    public static void TryLoadModPostfix(Mod mod)
    {
        if (
            mod?.state == ModLoadState.Loaded
            && ModRuntimeCompatibility.IsIncompatible(mod.assembly)
        )
        {
            mod.state = ModLoadState.Failed;
            bool disabled = ModRuntimeCompatibility.TryDisableForFutureLaunch(
                mod.manifest?.id,
                mod.assembly
            );
            PatchHelper.Log(
                $"[ModCompat] Marked mod '{mod.manifest?.id}' failed for this run after "
                    + "Mono rejected its initializer IL; future-launch auto-disable="
                    + disabled
            );
        }
        bool loaded = mod?.state == ModLoadState.Loaded;
        DebugModLoadTimingPatches.EndMod(loaded);
        if (string.IsNullOrWhiteSpace(mod?.manifest?.id))
            return;
        if (loaded)
            StartupRecoveryBridge.RecordModSuccessful(mod.manifest.id);
        StartupPerformanceTracker.AdvanceTo(
            StartupStageId.ModDiscovery,
            loaded ? StartupStageTerminal.Completed : StartupStageTerminal.Degraded
        );
    }

    // The game catches initializer exceptions inside CallModInitializer, so the
    // outer TryLoadMod postfix cannot see why a DLL failed. Wrap the one reflection
    // invocation without changing its return/throw behavior. The wrapper attributes
    // InvalidProgramException to the exact mod class so the postfix can keep a
    // partially initialized assembly out of GetLoadedMods().
    public static IEnumerable<CodeInstruction> CallModInitializerTranspiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        var codes = new List<CodeInstruction>(instructions);
        var original = AccessTools.Method(
            typeof(MethodBase),
            nameof(MethodBase.Invoke),
            new[] { typeof(object), typeof(object[]) }
        );
        var replacement = AccessTools.Method(
            typeof(ModRuntimeCompatibility),
            nameof(ModRuntimeCompatibility.InvokeInitializer)
        );
        if (original == null || replacement == null)
        {
            PatchHelper.Log(
                "[ModCompat] Initializer invocation contract unavailable; fallback inactive"
            );
            return codes;
        }

        var matches = new List<int>();
        for (int i = 0; i < codes.Count; i++)
        {
            if (codes[i].operand is MethodInfo method && method == original)
                matches.Add(i);
        }

        if (matches.Count != 1)
        {
            PatchHelper.Log(
                $"[ModCompat] Expected one initializer invocation, found {matches.Count}; "
                    + "fallback inactive"
            );
            return codes;
        }

        var call = codes[matches[0]];
        call.opcode = OpCodes.Call;
        call.operand = replacement;
        PatchHelper.Log("[ModCompat] Mod initializer compatibility boundary installed");
        return codes;
    }

    // Skip the Steam-backed mod enumeration on Android (no Steamworks runtime).
    public static bool ReadSteamModsPrefix() => false;

    // The launcher's mod registry (mod_config.json, numeric "version" by design)
    // lives at the root of the redirected mods dir, and the game's scanner tries
    // every *.json in the tree as a mod manifest — logging a caught JsonException
    // for the registry on every launch (issue #71). Skip it before the parse.
    public static bool ReadModManifestPrefix(string filename, ref Mod __result)
    {
        if (
            string.Equals(
                Path.GetFileName(filename),
                "mod_config.json",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            __result = null;
            return false;
        }
        return true;
    }
}
