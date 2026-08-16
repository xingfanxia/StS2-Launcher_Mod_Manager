# Launcher stability proof

Status: **complete for launcher-controlled failure classes** on 2026-08-16.
The remaining cases below are explicitly external/unsupported or require a
different affected device/mod set; they are not hidden behind a claim that all
third-party or GPU failures can be fixed by the launcher.

Private logs, screenshots, proprietary game files, FMOD inputs, credentials,
and signing material remain outside git. The main evidence directories are:

- `~/Library/Caches/StS2LauncherInvestigation/upstream-logs/`
- `~/Library/Caches/StS2LauncherBuildDeps/full/`

## Causal conclusions

The detailed stage-by-stage classification is in
[`STABILITY_FAILURE_MATRIX.md`](STABILITY_FAILURE_MATRIX.md). Confirmed or
reproduced launcher-controlled causes addressed by this work are:

1. Shader warmup retained the complete loaded scene/material graph. The first
   physical run ended in Android `LOW_MEMORY` before its first count log. A
   first streaming revision still used typed generic loads on unrelated
   `.tres` files; failed casts threw before the returned native wrapper could be
   disposed and produced a second LMK.
2. PCK cache invalidation recursively deleted an unbounded tree on the Activity
   main thread. Active cache directories are now atomically staged and deleted
   on a daemon worker, with interrupted-cleanup recovery.
3. The native PCK-rebuild overlay swallowed touches but, after a startup-order
   change, was only hidden after `WaitForLaunch()`. This made PLAY both the
   prerequisite for and the input blocked by overlay dismissal.
4. Awaited pickers/dialogs, the PLAY wait, and Steam Guard wait had teardown
   paths that never completed their task. Launcher UI construction could also
   return a half-initialized object to a caller that would wait forever.
5. Cloud drains and conflict verification synchronously polled on the Godot
   thread for 5–300 seconds. They now run on workers with bounded/coalesced
   lifecycle and restart handling.
6. Calling a Godot singleton from the unmanaged .NET entrypoint could abort
   during Mono bootstrap. Android now establishes app-private XDG storage from
   Java before native/.NET startup; `ModEntry` only consumes the environment.
7. The previous MemberRef audit did not validate newly added abstract interface
   slots, the exact issue #86 `TypeLoadException` class. A second independent
   audit was also needed for string reflection, Harmony targets, overload
   ambiguity, and transpiler IL calls.
8. Self-PID logcat cannot diagnose a process after it dies. Android 11+ now
   reports the prior exit reason and a bounded ANR trace on the next boot,
   without blocking `Activity.onCreate()`.
9. A partial FMOD Java input JAR compiled and packaged but caused JNI error 28
   before `onCreate`. The build now validates the required Java helpers both in
   the input JAR and final DEX.

The evidence also rules out BCL copy time as the cause in the captured runs and
separates `QueuePresentKHR` after `onPause`/surface teardown from an earlier
managed failure. Genuine GPU-driver failures and arbitrary third-party
MonoMod/native corruption remain external until reproduced on the affected
fingerprint and smallest mod set.

## Automated and build proof

The pinned Docker APK build automatically ran and passed:

- 17 focused stability contracts, including bounded shader streaming and
  overlay-dismissal-before-PLAY ordering;
- previous-exit classifier and atomic startup-cache-wiper Java suites;
- the read-only physical-device capture harness tests;
- MemberRef/interface fixtures that reject a missing newer interface member;
- patch/reflection/IL fixtures that reject missing and ambiguous required
  targets;
- all Workshop synchronization tests;
- .NET Release publish, Android Java compile, lint-vital, DEX, native packaging,
  48 Gradle release tasks, and final FMOD DEX inspection;
- APK Signature Scheme v2 verification with one signer.

The actual supported game assemblies produced:

```text
public v0.107.1 / build 23811903:
  52 sts2-scoped MemberRefs
  3 implemented game interfaces
  0 missing
  59 reflection/Harmony/IL rules
  0 required failures

public-beta v0.111.0 / build 24724944:
  52 sts2-scoped MemberRefs
  3 implemented game interfaces
  0 missing
  59 reflection/Harmony/IL rules
  0 required failures
```

`git diff --check` passes. The modified C# files pass CSharpier 1.3.0; unrelated
upstream/fork files that predate this goal are not reformatted merely to make a
global formatter check clean.

## APK artifact

The final private, upgrade-compatible artifact is outside the repository:

```text
~/Library/Caches/StS2LauncherBuildDeps/full/out-goal-signed16/StS2Launcher-v0.4.2.apk
SHA-256 0f49e908a1e5923987a759f466eb98d8e6745e61ebb6bb7e8c2a63301c35afaa
package  com.game.sts2launcher.modmanager
version  0.4.2 (339)
min/target SDK 24/35
signature APK Signature Scheme v2
```

No private game/FMOD/signing input is committed or uploaded. An ARM-native
container experiment correctly failed because Android's `aapt2` is x86-64; a
QEMU experiment then exposed .NET emulation faults. The counted build used the
original pinned amd64 image under Docker Desktop Rosetta and exited 0.

## Physical ARM64 device matrix

The signed artifact was installed as an upgrade on a physical ARM64 foldable
device. No device identifier or account identifier is recorded here.

| Matrix row | Result |
|---|---|
| Fresh/upgrade | First install/standalone no-game flow and repeated signed upgrade installs preserved app data, login, saves, language preference, and downloaded game state. |
| No mod | Public v0.107.1 reached the early-access/main-menu screen; input dismissed the dialog. No fatal signal, ANR, or low-memory exit occurred. |
| BaseLib | BaseLib DLL/PCK loaded, 280 patches applied with 0 failed, its initializer completed, and the main menu loaded. |
| Ordinary mod | ModConfig v0.2.3 initialized and reached the main menu. |
| Incompatible mod | QuickRestart without BaseLib emitted the explicit missing-dependency diagnosis, skipped its assembly, kept the process alive, and reached the menu. |
| Online/cloud | Cache preload enumerated 210 cloud files; identical decisions and a cloud-only conflict path completed without destructive push. |
| Offline/degraded network | With Wi-Fi disabled, Steam timeout/retry was bounded and startup fell back to local saves. A real branch-list timeout recovered on retry after reconnect/backoff. Exact `tc netem` shaping was unavailable without root. |
| First warmup | Version 7 enumerated 2,580 loose resources plus 947 scenes and streamed 1,592 materials in 156.074 s. Peak RSS was 2,038,860 KiB and minimum system `MemAvailable` was 2,974,712 KiB; it completed/restarted with no LMK, fatal signal, or ANR. |
| Warm cache | The next PLAY logged `NeedsWarmup=False` and reached the menu. |
| PCK/Atlas update | public-beta→public staged 428 old entries; public→public-beta staged 161. Background cleanup completed, the fixed native overlay hid immediately after launcher initialization, and PLAY remained clickable. |
| Foreground/background | HOME for 10 seconds and resume retained the same PID, recreated the surface, recovered the full layout, and accepted input. |
| Rotation/configuration | Forced rotation changed display frames while retaining the PID; restoring accelerometer/user rotation returned the layout without fatal/ANR. |
| Branch/assembly compatibility | Both directions downloaded the selected Windows depot, forced controlled restart, recopied all matching game assemblies (32 for beta, 30 for public) while protecting 186 BCL files, and invalidated the PCK cache. |
| public runtime | ReleaseInfo resolved commit `59260271`, version v0.107.1; the ≤v0.107 save-path capability branch ran and main menu loaded. |
| public-beta runtime | ReleaseInfo resolved commit `41cef1ea`, version v0.111.0; the v0.108+ capability branch ran and main menu loaded in 38.38 s with no `TypeLoadException`/missing member/fatal/ANR. |

The device was returned to the public branch after beta proof. Wi-Fi and the
original rotation settings were restored. The originally active mod set is
restored during final handoff rather than left disabled for the test matrix.

## Residual and external cases

- A controller was not available, so the existing one-shot controller-map
  refill remains code/log verified rather than hardware verified.
- Split-screen, an external keyboard, a genuinely affected alternate GPU, and
  Android 7–10 logcat-only behavior require their corresponding devices.
- An exact two-sided divergent cloud conflict was not fabricated because that
  would mutate real saves; the cloud-only conflict, identical, online, offline,
  timeout, and retry paths were exercised.
- Interrupted depot download, native-crash/tombstone injection, and killing the
  final warmup revision mid-scan are destructive stress cases. Their recovery
  state machines are automated; ordinary device paths passed.
- Issue #87's third-party native/string corruption cannot be claimed fixed
  without the smallest reproducing mod set. The launcher now attributes and
  reports what it can, then supports safe disable/stash recovery.

These residuals do not leave a confirmed launcher-controlled permanent wait or
black-screen path unhandled.

## Upstream synchronization risk

The changes stay in narrow call sites and focused helpers/tests. The final
warmup change is confined to `ShaderWarmupScreen`, the discovered overlay cycle
is a small ordering correction in `LauncherPatches`, and the proof changes are
under `tools/` and `docs/`. Package identity, credential format, save layout,
renderer default, and the existing EN toggle are unchanged.

No remote branch, PR, release, or uploaded artifact was changed as part of this
goal. The local commits remain separable by failure class for cherry-pick,
revert, and future upstream synchronization.
