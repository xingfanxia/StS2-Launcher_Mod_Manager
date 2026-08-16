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

## Phase 3 — crash-loop Safe Mode and mod isolation

### Grounded execution boundary

The matched game assembly was decompiled locally (the assembly itself and
decompiled output remain outside the repository). `ModManager.Initialize`
discovers and sorts mods, then calls the single private
`TryLoadMod(Mod)` method for each entry. That method contains DLL loading, PCK
mounting, attributed initializer calls, and fallback Harmony `PatchAll`, making
its prefix the last reliable launcher-owned boundary before third-party code can
damage the process. The new required patch-target rule proves this method is
present and unambiguous before an APK can build.

The prefix commits stage `mod-loading` and the stable manifest id as the current
candidate before third-party execution. A loaded-return postfix records the last
successful mod. Managed mod failures that the game catches do not masquerade as
success; native or delayed corruption can leave only a candidate, which every UI
explicitly describes as temporal evidence rather than a confirmed culprit.

### Recovery and data-safety contract

- The launcher waits for Android's asynchronous previous-exit reconciliation
  without blocking `Activity.onCreate`, and prevents PLAY input until that short
  check has a terminal result.
- Two same-stage/same-fingerprint actionable exits offer four explicit choices:
  session-only Safe Mode, session-only candidate exclusion, a deterministic
  dependency-closed half-set test, or an acknowledged normal launch. One exit
  still never changes behavior.
- The choice configures only a process-local path filter on the existing
  `IModManagerFileIo` wrapper. Directory enumeration, file enumeration,
  existence checks, and stream opens all consume the same filter. It performs no
  `Directory.Move`, rename, delete, mod-config write, save write, or credential
  access. Safe Mode also skips only this run's optional shader warmup.
- Safe Mode and bisection hide unmanaged root manifests because they cannot be
  isolated safely; candidate exclusion preserves unrelated root manifests and
  hides only the candidate's containing package. An unknown/stale candidate
  fails closed to Safe Mode instead of claiming an exclusion happened.
- After filtered startup reaches `game-ready`, a second dialog proves the menu
  was reached and offers either continuing the current session or a planned
  restart into normal mode. The next process starts with the unmodified real mod
  layout and normal policy.

The only persisted recovery fields remain the Phase 1 journal's bounded stage,
opaque fingerprint, exit class/count, and validated mod id. Full paths used by
the in-memory filter are neither journaled nor included in recovery logs.

### Focused and build evidence

- The focused regression first failed with no recovery policy/loader boundary,
  then passed strict recovery-payload parsing, torn/path-like rejection, normal,
  Safe Mode, candidate exclusion, unknown-candidate fallback, deterministic
  bisection, root-manifest behavior, and source contracts for pre-PLAY recovery,
  warmup bypass, menu success UI, candidate/success journaling, and zero mod-dir
  mutation.
- Existing previous-exit, journal, cache-staging, warmup, lifecycle, dialog,
  cloud and device-capture regressions remain green.
- Pinned amd64 Docker APK pipeline: C# publish succeeded, Java/JNI compiled, 47
  Gradle tasks passed, 53 game-scoped MemberRefs and 3 implemented interfaces
  were present, all 60 required patch/reflection rules passed (including
  `TryLoadMod`), and Workshop sync remained green. The unsigned artifact
  SHA-256 is
  `4a76c46fdddd8e0ac623c2c4ee683dbfceef606f40ec80f362f5f2fa62dd9dcb`.

Remaining final device gate: with a cumulative signed/debug-controllable build,
exercise two abrupt same-mod exits, the third-launch recovery dialog, Safe Mode,
candidate exclusion and half-set selection; hash the real `Mods/`,
`ModsDisabled/`, and mod-config state before/after; then restore normal startup
and verify a supported mod still loads. Managed failure, hang/ANR, immediate
native-like exit, and delayed-exit rows remain device-proof requirements rather
than completed claims.

## Phase 4 — transactional game update and interruption recovery

### Root cause and activation contract

The old downloader committed each verified file directly into the active
`game/` directory, then wrote each depot manifest independently. Branch changes
first recursively deleted both `game/` and `download_state/`. A process death in
either flow could therefore expose a new PCK with old assemblies, a manifest
claiming files that were not all present, or no usable old install at all.

Downloads now target sibling `game.staging/` exclusively. Unchanged files use
same-filesystem hard links where supported (copy fallback); every write path
replaces rather than mutates a shared inode, and the in-place PCK patch detaches
only when the download did not already replace that file. Depot manifests and
ids live inside the staged tree and use temp-file replacement. A force-fresh
branch switch still redownloads every target file, but it never deletes the
active version first.

After Steam verification and PCK patching, the staged completion marker records
the branch, build, depot manifest ids, PCK size/mtime/magic, complete top-level
game assembly set, and the PCK mtime that owns atlas-cache invalidation. Only a
matching marker may cross the directory commit point:

1. `game/` is atomically renamed to `game.rollback/`;
2. `game.staging/` is atomically renamed to `game/`;
3. Android repairs either rename window before selecting the PCK or copying any
   assembly.

The rollback remains until the new process reaches `game-ready`. Android writes
a one-shot validation-attempt marker before cache staging and assembly sync. If
that first startup dies before the healthy terminal stage, the next process
restores the last validated directory. A healthy startup publishes the new
branch and performs rollback/staging cleanup on a daemon thread. Invalid active
markers fall back to the launcher bootstrap instead of loading a suspect PCK.
Legacy complete installs remain accepted for the one-time migration path.

### Fault and focused evidence

- The pure C# transaction regression covers partial staging plus interruption
  after prepare, after active retirement, and after staged activation. Every
  recovery exposes matching old/old or new/new PCK/DLL contents, rejects a
  changed assembly set, and retains rollback until explicit validation.
- The pure JVM recovery regression covers the Android rename window, invalid
  activated marker, first-start failure rollback, legacy migration, bootstrap
  fallback, validated branch publication, and post-healthy cleanup.
- Debug-version-only intent hooks cover staging creation, first verified file,
  depot-manifest commit, all-depot verification, PCK patch, marker preparation,
  both directory renames, Android pre/post recovery, cache staging, and assembly
  sync. Values are allowlisted, bounded, app-private, and one-shot.
- The destructive pre-download `WipeGameFiles` flow and early selected-branch
  publication were removed. Download failure/cancellation resets pending branch
  state while the old active directory remains untouched.
- Pinned amd64 Docker APK pipeline passed the C#/Java/device-capture focused
  suites, 53 game-scoped MemberRefs, 3 implemented interfaces, all 60 required
  patch/reflection rules, C# publish, Java/JNI compilation, 47 Gradle tasks, and
  Workshop sync. The unsigned artifact SHA-256 is
  `18daf31e4a04015d8871a083c2150434ae9ffa54ccc7b358c475020ca87ce248`.

Remaining final device gate: use the cumulative signed debug-controllable APK
for public↔public-beta and at least three representative process-death points;
prove the recovered marker, PCK, full assembly set and selected branch are one
complete old or new tuple, then verify mods, saves and login remain unchanged.

## Phase 5 — renderer compatibility recovery

### Capability result

Godot's Android command line was first exposed behind a debug-version-only
intent override. The reference ARM64 device then started the same game PCK with
`--rendering-method gl_compatibility --rendering-driver opengl3`. Runtime output
reported OpenGL ES 3.2, the launcher reached its visible PLAY surface, the user
handoff completed, ordinary third-party mods loaded, and the game reached the
interactive main menu in about 86 seconds. A subsequent cold start without the
override reported the normal Vulkan API and reached the launcher again.

This satisfies only the capability question. The reference device is not a
known affected GPU, so it does not prove that Compatibility fixes a reported
driver fault. Compatibility also logged that one particle sub-emitter feature
is unsupported; it remains an explicit recovery mode rather than the default.
The implementation follows Godot 4.5's documented Android Compatibility
requirements and command-line selection, and diagnostics now query
`RenderingServer` for the actual runtime method/driver instead of repeating
project defaults:

- <https://docs.godotengine.org/en/4.5/about/system_requirements.html>
- <https://docs.godotengine.org/en/4.5/classes/class_projectsettings.html>

### Decision and rollback contract

- Vulkan remains the default and no renderer setting is changed persistently.
- The launcher records `launcher-awaiting-frame`, yields one Godot process
  frame, and only then records `launcher-ready`. Renderer recovery is eligible
  only after two matching journal failures in `android-on-create`,
  `launcher-creating`, or `launcher-awaiting-frame`.
- The eligible exit classes are deliberately narrow: signaled, native crash,
  or initialization failure. LMK, ANR, managed/mod evidence, one-off failures,
  and every post-usable-frame stage are rejected.
- `onPause` persists foreground=false before Godot tears down the Android
  Surface. Any later native or LMK exit therefore resets rather than increments
  the startup sequence. Driver log text such as `QueuePresentKHR` is never a
  decision input.
- Android presents the recovery suggestion without requiring a Godot-rendered
  dialog. Acceptance stores one boolean, performs a planned restart, and
  consumes/removes that boolean before the next renderer initialization. A
  Compatibility session exposes a launcher dialog to restart immediately with
  Vulkan or continue; either way, the following launch defaults to Vulkan.
- The native dialog reads only the bounded launcher language preference and has
  paired Korean/English copy. It does not inspect credentials, saves, mods,
  account identity, device identifiers, or paths outside app-private storage.

### Focused, build, and device evidence

- Pure JVM tests pass the repeated pre-frame native case and reject one crash,
  `launcher-ready`, LMK, ANR, and a mod candidate. Journal tests additionally
  prove that a background native exit cannot accumulate or request recovery.
- C# source contracts require the first-frame stage ordering, consume-once
  preference, actual Vulkan restore UI, and absence of driver-log matching.
- `STS2Mobile.csproj` compiled against the matched Godot 4.5 assemblies with 0
  warnings and 0 errors. The pinned amd64 pipeline passed all C#/Java/device
  focused suites, 53 game-scoped MemberRefs, 3 implemented interfaces, all 60
  required patch/reflection rules, Java/JNI compilation, 47 Gradle tasks, and
  Workshop sync. The unsigned artifact SHA-256 is
  `89aab953cb67da86509a2b5d9930fec6764e19d141a4f7a293d5bda163ef89a8`.
- Sanitized reference-device observation: Compatibility reported OpenGL ES 3.2
  and reached the game menu with mods; the no-override cold start reported
  Vulkan 1.3.284 and reached `Launcher UI displayed`. No account, device serial,
  private path, save content, or full log is stored in the repository.

Remaining cumulative device gate: install the final signed build once and
exercise the native repeated-pre-frame recovery offer, one-shot Compatibility
restart, explicit Vulkan restore, and HOME/resume exclusion together with the
Phase 7 matrix. Until an affected GPU supplies equivalent evidence, renderer
driver remediation remains unclaimed.
