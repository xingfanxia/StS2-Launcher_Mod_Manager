# Launcher hardening proof

Status: **in progress**. This document records reproducible evidence for
`GOAL_STABILITY_HARDENING.md`; a green focused test is not used to claim the
later APK/device gates.

## Baseline

- Baseline commit: `f558761` (first-round stability proof is at parent
  `0525165`).
- Existing signed reference APK:
  `~/Library/Caches/StS2LauncherBuildDeps/full/out-goal-signed16/StS2Launcher-v0.4.2.apk`
- Reference SHA-256:
  `0f49e908a1e5923987a759f466eb98d8e6745e61ebb6bb7e8c2a63301c35afaa`
- Baseline focused checks passed on 2026-08-16:
  Java previous-exit/cache-wiper tests, C# stability contracts, Workshop sync
  tests, and the read-only device-capture harness.

## Phase 0 owner map

| Concern | Source of truth / owner | Risk boundary |
|---|---|---|
| Android process exit and planned restart | `GodotApp`, `PreviousExitClassifier` | `ApplicationExitInfo`, app-private `SharedPreferences` |
| Launcher → cloud → warmup → game handoff | `LauncherPatches.RunLauncherThenGame` | awaited stage transitions on the Godot process |
| Optional warmup attempt/completion | `ShaderWarmupState`, `ShaderWarmupScreen` | app-private atomic markers and native Godot resources |
| Game mod discovery/load | game `ModManager`, adapted by `ModLoaderPatches` | third-party assemblies execute in the game process |
| Mod exception attribution | `ModAssemblyRegistry`, `ModExceptionAttributionPatches` | best-effort evidence; delayed/native corruption is not attributable from a stack |
| Depot/version activation | `DepotDownloader`, `LauncherModel`, Java `setupAssemblies` | manifest, PCK and game-DLL consistency across process restart |
| Renderer startup | Android/Godot command line and render diagnostics | must distinguish first-frame failure from later surface teardown |
| KR/EN launcher text | `Loc`, `EnglishLocalization`, `LocalizedTextRegistry` | C#/Godot controls plus Android-native visible text |

## Phase 1 — durable attempt journal

### Root cause and contract

Previous-exit evidence, planned restart timestamps, warmup markers, launcher
stages, and mod attribution existed independently. There was no durable attempt
identity that could prove two abnormal exits occurred at the same stage with
the same relevant configuration. Therefore a launcher could diagnose one exit
but could not safely decide when to offer crash-loop recovery.

The new journal is owned by Android because Android owns process identity and
`ApplicationExitInfo`. C# sends only app-authored stage tokens, an opaque
configuration digest, and (in the mod-loading phase) a bounded mod id.

### Trust boundary

- Assets: Steam credentials, saves, user mod contents/configuration, account
  identity, device identity, and full filesystem paths.
- Actors: Android lifecycle/exit reporter, launcher C# workflow, game/mod code,
  and untrusted third-party mods running in-process.
- Entrypoints: the narrow Godot Java bridge methods `recordStartupStage`,
  `setStartupFingerprint`, `recordModCandidate`, `recordModSuccessful`, and
  `markStartupHealthy`.
- Persisted data: schema, attempt id/timestamps, normalized stage, opaque
  configuration hash, exit reason/count, and a path-rejecting 80-character mod
  id. No account, token, save body, device field, or full path is accepted.
- Failure behavior: a torn/invalid journal is discarded without requesting
  recovery; planned and user-requested exits never count; a successful startup
  clears the sequence; only two same-stage/same-fingerprint actionable exits
  within 24 hours request recovery.

### Focused evidence

- `tools/stability-tests-java/run.sh`: passes same/different stage and
  fingerprint, planned/user exit, healthy reset, expiry, atomic-codec/torn state,
  native crash/ANR/LMK classification, path rejection, and Unicode mod-id cases.
- `tools/stability-tests`: passes the Android↔C# ownership/source-order contract;
  `game-ready` is written only after awaited `GameStartup` success.
- `dotnet build src/STS2Mobile/STS2Mobile.csproj -c Release` in the pinned .NET 9
  container: 0 warnings, 0 errors.
- Pinned amd64 Docker APK pipeline: Java/Godot integration compiled, 47 Gradle
  tasks passed, both compatibility audits reported 0 failures, and the unsigned
  phase artifact SHA-256 was
  `dac26d5e93de2ec0cedf2b9b1c3f87cd139f482684d51cc1901ba6990cc7a748`.

Remaining final device gate: verify attempt/reconcile/healthy records with the
signed cumulative build on the physical device.

## Phase 2 — adaptive shader warmup

### Root cause and contract

Warmup v7 limited the live native material set to eight, but that bound did not
react to device pressure or cap the total process working set. On a lower-memory
device Android could therefore kill the first optional warmup before the
interrupted-attempt marker protected the next boot.

The warmup now owns a narrow Android monitor for only its lifetime. At the
initial boundary, every released material batch, and every 25 otherwise-yielding
sources it evaluates the highest `onTrimMemory` callback, `MemoryInfo.lowMemory`,
available memory relative to the LMK threshold, total device memory, and process
PSS. It defers when any reliable signal crosses the safety policy; unavailable
telemetry preserves the already batch-bounded path. The snapshot contains only
numeric memory fields and crosses no user-data or identity boundary.

Deferral is an explicit non-failure path. The current batch and viewport are
released, the result is atomically recorded as `DeferredMemoryPressure`, and a
clean process continues with normal on-demand shader compilation. Completed,
failed-but-bypassed, and interrupted outcomes are also distinct. Every terminal
outcome publishes the current warmup-version marker, so no optional failure can
become a repeated boot loop.

### Focused and build evidence

- The focused policy regression first failed with the provider absent, then
  passed healthy, trim-level, system-low, low-headroom, process-budget, and
  unavailable-telemetry cases. State tests pass all four terminal outcomes and
  prove deferred/failed/interrupted attempts do not rerun.
- Source contracts preserve the eight-material streaming/disposal bound and
  require the physical warmup path to begin, consume, and end Android monitoring
  while treating memory deferral separately from scan failure.
- `tools/stability-tests-java/run.sh` remains green for exit classification,
  the durable startup journal, and cache staging. The read-only device harness,
  MemberRef/interface audit, patch-target audit, and Workshop sync tests also
  pass in the pinned build pipeline.
- Pinned amd64 Docker APK pipeline: C# publish succeeded, Java/JNI compiled, 47
  Gradle tasks passed, 52 game-scoped MemberRefs and 3 implemented interfaces
  were present, all 59 required patch/reflection rules passed, and the unsigned
  artifact SHA-256 is
  `a145ad4fca525111a6fa8406147c1437f67101ddaf7fd3daea2de4b9fa96c9c5`.

Remaining final device gate: install the cumulative signed build once, exercise
both normal warmup and a controlled Android pressure callback, and prove the
process reaches the menu without Crash/ANR/LMK while preserving installed mods,
saves, login, branch, and language settings.
