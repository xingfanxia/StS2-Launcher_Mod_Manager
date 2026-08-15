# Launcher stability proof

Status: **partial** on 2026-08-15. All locally executable proof below passes,
but no ARM64 Android device is connected, so the `GOAL.md` Device Matrix is not
complete and the stability goal must not be marked complete.

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

The private build output is intentionally outside the repository:

```text
~/Library/Caches/StS2LauncherBuildDeps/full/out-goal-proof/StS2Launcher-v0.4.2.apk
SHA-256 80f48e228d44568afc0fec443141b4af79396372eab361776f3721070c61590b
package  com.game.sts2launcher.modmanager
version  0.4.2 (339)
min/target SDK 24/35
```

`unzip -t` and `zipalign -c 4` pass. The artifact is unsigned because no
authorized launcher signing identity is available; no private game, FMOD, or
signing input is committed or uploaded.

## Device proof still required

`adb devices -l` and `adb mdns services` both return no devices. The next
discriminating run must therefore install a correctly signed artifact on an
ARM64 device and execute every Device Matrix row in `GOAL.md`, especially:

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
