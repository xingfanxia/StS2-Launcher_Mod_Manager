# Launcher stability proof

Status: **partial** on 2026-08-15. All locally executable proof below passes.
An ARM64 API 36 emulator is available, but no physical Android device is
connected, so the `GOAL.md` Device Matrix is not complete and the stability goal
must not be marked complete.

## Evidence and causal conclusions

The detailed phase-by-phase classification is in
[`STABILITY_FAILURE_MATRIX.md`](STABILITY_FAILURE_MATRIX.md). Its source corpus
is kept outside git at
`~/Library/Caches/StS2LauncherInvestigation/upstream-logs/`.

Confirmed launcher-controlled causes addressed here:

1. Shader warmup retained a large scene/resource graph, could repeat after a
   native/OOM death, could lose an early completion, and depended on Java process
   exit to terminate an await.
2. Android startup recursively deleted an unbounded texture cache on the
   Activity UI thread before the explanatory overlay appeared.
3. Android Back/parent teardown bypassed button-only completion in awaited
   picker, branch, conflict, and result dialogs.
4. Whole-launcher teardown left PLAY and Steam Guard waits pending; UI
   construction failure returned a half-initialized object to its caller.
5. The existing MemberRef tool did not validate newly added abstract interface
   slots (the issue #86 `TypeLoadException` class), and no independent check
   covered string reflection, Harmony targets, overload ambiguity, or transpiler
   IL calls.
6. Self-PID logcat cannot classify a process that has already died. Android 11+
   now reports the prior exit reason and bounded ANR trace from a daemon worker
   on the next boot, without blocking `Activity.onCreate()`.

The evidence also rules out BCL copy time as the cause in the captured runs and
separates post-background `QueuePresentKHR` surface teardown from the earlier
managed `TypeLoadException`. BaseLib/MonoMod native memory corruption and genuine
GPU/driver failures remain external until a device capture proves a
launcher-controlled boundary.

## Automated proof

The pinned container APK path now runs the focused checks automatically. The
latest run passed:

- `tools/stability-tests`: shader state/order, dialog lifecycle, UI-init and
  whole-launcher teardown contracts, and build-gate contracts.
- `tools/stability-tests-java`: prior-exit classification and atomic cache
  staging/background cleanup, including interrupted cleanup recovery.
- `tools/device-stability/tests/run.sh`: physical-device gating, package-state
  detection, bounded optional log capture, and a static no-mutation contract.
- `tools/memberref-audit/tests/run.sh`: a broken newer interface fixture is
  rejected and a virtual forward-compatible implementation passes.
- `tools/patch-target-audit/tests/run.sh`: present and IL-call rules pass;
  missing and ambiguous required targets fail before runtime.
- `tools/workshop-sync-tests`: all existing workshop synchronization cases pass.
- `dotnet build src/STS2Mobile/STS2Mobile.csproj -c Release`: 0 warnings,
  0 errors.
- Java/Android release compilation, Android lint vital, DEX, native packaging,
  and all 47 Gradle release tasks pass.

The actual v0.107.1 game DLL (Steam build id 23811903) produced:

```text
52 sts2-scoped MemberRefs
3 implemented game interfaces
0 missing
59 reflection/Harmony/IL rules
0 required failures
0 optional degradations
```

The DLL extracted back out of the final APK was audited again and produced the
same 52 / 3 / 0 result. A real v0.111 DLL is not locally available; the synthetic
new-interface fixture proves detection of the known added-slot failure class but
does not substitute for claiming v0.111 device support.

Reproduction command for the full build:

```sh
docker build --platform linux/amd64 -t sts2-launcher-build:goal docker
docker run --rm --platform linux/amd64 \
  -v "$PWD:/src:ro" \
  -v "/private/deps:/deps:ro" \
  -v "sts2-launcher-cache:/cache" \
  -v "/private/output:/out" \
  sts2-launcher-build:goal
```

## APK artifact

The last completed private build output is intentionally outside the
repository:

```text
~/Library/Caches/StS2LauncherBuildDeps/full/out-goal-proof2/StS2Launcher-v0.4.2.apk
SHA-256 d2fc9b9501b3bc96ffa7c69dbc9650559a37110b40294d586677cd2297cd7141
package  com.game.sts2launcher.modmanager
version  0.4.2 (339)
min/target SDK 24/35
```

`unzip -t` and `zipalign -c 4` pass. The artifact is unsigned because no
authorized launcher signing identity is available; no private game, FMOD, or
signing input is committed or uploaded.

A later repeat passed every focused test and both compatibility audits, then
made no progress for more than four minutes in Gradle
`stripMonoReleaseDebugSymbols`: its translated `llvm-strip` child was defunct
and the daemon threads were waiting in runtime mutexes. The ephemeral container
was stopped and that run is not counted as a build pass. This is consistent
with an amd64-on-Apple-Silicon translation stall; it produced no compiler or
launcher failure. The final device-tool changes were rerun independently and
pass, while the completed full APK above remains the build proof.

## Device proof still required

The local `mio_api36_pixel8` AVD boots as API 36 / ARM64 (`ranchu`) and confirms
that `ApplicationExitInfo` is queryable. A read-only preflight from
`tools/device-stability/capture.sh --require-physical` correctly records the
package as absent, classifies the AVD as an emulator, and exits 3. This is useful
tool validation, not physical-device proof.

The artifact cannot be installed because it is unsigned, and creating or using
a different signing identity requires new authorization under `GOAL.md`. The
next discriminating run must use an authorized, correctly signed artifact on an
ARM64 physical device and execute every Device Matrix row, especially:

- fresh install, upgrade with a large existing ETC2 cache, and a kill during
  background cache cleanup;
- first warmup, forced kill mid-scan, next-boot recovery, and warm-cache launch;
- Back/parent teardown on every awaited dialog, Steam Guard entry, fold/rotate,
  split screen, background/foreground, and launcher removal before PLAY;
- no mod, BaseLib only, a supported ordinary mod, and the smallest known
  incompatible mod set;
- online, offline, slow/drop network, cloud conflict, game branch switch, and
  stale assembly/PCK update;
- managed crash, native crash, LMK and ANR followed by next-boot
  `ApplicationExitInfo` verification; genuine first-frame Vulkan failure must be
  distinguished from surface teardown after `onPause`.

Until that matrix and a real newer supported game DLL are available, residual
device/driver/mod behavior remains `instrumented-awaiting-evidence` or
`external/unsupported`, not fixed.

## Upstream synchronization risk

Runtime changes are concentrated in small new helpers plus narrow call-site
wiring. No package identity, save path, credential format, renderer default, or
language-toggle code changed. Build enforcement is localized to
`scripts/build.sh`, `docker/build-apk.sh`, and the existing APK workflow. The
changes can be cherry-picked/reverted by failure class without a broad launcher
architecture rewrite.

As a direct portability check, the two runtime-fix commits cherry-pick cleanly
onto upstream `59a5b87` (v0.4.2). The compatibility/proof commits have no
upstream source-line conflicts; their only apply decisions are fork-owned CI,
Docker, and proof files that do not exist upstream. With this final focused
device-proof commit, the fork is 12 commits ahead and 0 behind that upstream
snapshot. No remote branch was changed.
