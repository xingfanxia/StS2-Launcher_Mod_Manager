# Performance follow-up proof

Status: complete (2026-08-18). This file records only sanitized aggregate evidence. Raw
device logs, screenshots, installed-mod inventory, binaries, and decompilation
remain outside the repository.

## Quick Restart sustained-jank owner

The controlled Vulkan combat partition identified Quick Restart v2.0.0 as the
owner, not BaseLib or the renderer:

| Closure | p99 | Frames over 2x budget / 120 s |
|---|---:|---:|
| BaseLib only | 18.807 ms | 4 |
| BaseLib + Quick Restart | 31.514 ms | 61 |

Read-only inspection of the exact installed assembly then found that the hidden
hold indicator continued processing while idle. Each idle frame called
`CanRestart`, reached two synchronous file-existence probes, and reset hidden UI
state.

The launcher compatibility boundary is deliberately exact: assembly name,
assembly version, MVID, SHA-256, external-mod provenance, declaring type, method
signature, field types, and instance property must all match. Unknown or updated
binaries retain their original behavior. The fix disables only the indicator's
idle processing and re-enables it at the mod's existing hold-input boundary.

One instrumented same-APK discriminating pair closed the mechanism:

| Arm | p99 | Frames over 2x | `_Process` calls | `CanRestart` calls | File probes |
|---|---:|---:|---:|---:|---:|
| Exact fix disabled | 31.551 ms | 58 | 6,922 | 6,922 | 13,844 |
| Exact fix enabled | 18.784 ms | 4 | 0 | 0 | 0 |

This is a 40.5% p99 improvement and a 93.1% reduction in over-2x frames. The
required three interleaved, uninstrumented, same-APK pairs then passed under the
same exact mod closure, normal automatic brightness, Vulkan, thermal 0–2, and a
start-temperature gate:

| Pair | Baseline p99 / over-2x | Fixed p99 / over-2x |
|---|---:|---:|
| 1 | 32.304 ms / 63 | 18.771 ms / 5 |
| 2 | 31.855 ms / 64 | 18.722 ms / 6 |
| 3 | 31.181 ms / 58 | 18.723 ms / 5 |
| Median | 31.855 ms / 63 | 18.723 ms / 5 |

Median p99 improved 41.2% and median over-2x density improved 92.1%. Median
p50/p95 changed from 16.711/22.897 ms to 16.677/17.643 ms. Every arm began at
thermal status 0, ended at status 2, and had zero mod-load errors.

Short release, full hold, one-shot room restart, reset/cancel, and pause-menu
restart behavior were also exercised on the current APK:

| Interaction | Process frames | Visible frames | Resets | Room restarts | Pause callbacks |
|---|---:|---:|---:|---:|---:|
| 250 ms press/release | 14 | 14 | 1 | 0 | 0 |
| Full hold | 117 | 116 | 1 | 1 | 0 |
| Pause-menu restart | 0 | 0 | 0 | 1 | 1 |

The short and full-hold runs stayed within thermal status 0–2. The pause-menu
run began after the device was already warm and ended at status 3, so its exact
callback counts are retained only as functional evidence, not performance
evidence. All three had zero mod-load errors, crashes, ANRs, and LMKs.

## First map opening

The real map tree already exists before opening, while upstream `Open` still owns
pause state, screen-stack state, hotkeys, signals, audio, and one-time animation.
Calling hidden `Open`/`Close` would therefore change behavior and was rejected.

Three fixed-interaction Safe Mode pairs tested the narrower hypothesis of exposing
only the existing server-side canvas item behind the opaque first-combat cover.
It did not call `Open` or change the Godot-visible lifecycle:

| Metric | Baseline median | Candidate median | Change |
|---|---:|---:|---:|
| First-map 60-frame p99/max | 118.123 ms | 34.350 ms | -70.9% |
| First-combat cover | 4.702 s | 4.708 s | +0.128% |
| Opens over 100 ms | 3/3 | 1/3 | -66.7% |

The result was not an overall win. Candidate pair 2 still had a 106.649 ms map
spike, and the paired sum of cover-time change plus later map-time change had a
median regression of 30.877 ms. Candidate pair 3 independently confirmed that
the exposure ran with its lifecycle invariant preserved, but added no canvas
pipeline (`canvas_delta=0`). The candidate and its dedicated debug modes were
therefore removed from runtime/source. Generic bounded map-interaction and cover
timing remain available through `game-safe-120`; the approximately 118 ms median
first-open residual is game-owned.

## Mod-loading owner loop

Debug-only anonymous spans measured each sequential mod item,
`CallModInitializer`, and outermost Harmony `PatchAll` across three complete menu
starts. Persistent output exposes only ordinals, durations, counts, and status;
it does not expose mod names, paths, accounts, saves, or raw logs.

| Anonymous owner | Median total | Median initializer | Median `PatchAll` |
|---|---:|---:|---:|
| Item 6 | 4.872 s | 4.870 s | 3 ms |
| Item 11 | 3.502 s | 3.498 s | 138 ms |
| Item 5 | 1.568 s | 1.563 s | 0 ms |
| Item 1 | 1.067 s | 0.994 s | 0 ms |

Across all 14 items, median item time summed to 11.849 s and initializer time to
11.657 s (98.38%). `PatchAll`, already nested inside initializer time, summed to
0.555 s. The theoretical maximum outside third-party initializers is only about
0.192 s (1.62%), below both startup meaningful thresholds even if it could be
eliminated completely.

Anonymous item 8 consistently returned initializer failure while the loader
continued and all 14 items reached loaded state. Private diagnosis attributed it
to invalid third-party IL, not the launcher timing probe; no identity is retained
here. There is no launcher-owned optimization candidate. Initializers were not
parallelized because their ordering and global patch/registration side effects
are part of the mod contract. The debug-only anonymous spans remain as bounded
observability; production does not arm them.

## Final guardrails

- Focused source, harness, Android lifecycle, managed stability, localization,
  and privacy contracts pass.
- The pinned signed debug APK builds, its sidecar hash matches, and it installs as
  the existing package identity.
- The standardized workflow rejects a locked device instead of misclassifying a
  lock-screen wait as a performance capture.
- Inter-arm gating checks both Android thermal status and an optional battery
  temperature ceiling. This avoids accepting a status-0 start while the device
  is still hot because of thermal hysteresis.
- Anonymous mod-load attribution is available through the standardized workflow
  and its fake-device contract test passes.
- The final focused workflow passed diff, performance-harness, Android lifecycle,
  managed stability, and all 751 Hangul-bearing source localization contracts.
- The pinned container passed compatibility target audits (132 game-scoped
  MemberRefs and 66 required patch/reflection rules), Java/Gradle/D8, FMOD DEX,
  workshop sync, signing, and APK verification. The final no-suffix APK is
  version `0.4.3` (340), SHA-256
  `f106676066cb81d2ca69470b8327cc35b1d4cdde226e8386271adaf94abd3398`.
- The installed production package retained the existing signing identity and
  rejected frame, Quick Restart, mod-load, and startup-delay debug intents.
- The installed production APK reached `game-ready` in 37.885 s with same-PID
  continuity and zero fatal, ANR, LMK, or surface errors. HOME/resume passed 3/3
  and rotation passed 3/3 with PID continuity; rotation settings were restored.
- The final EN launcher audit found 21 English lines, zero Hangul, and zero
  edge-clipped lines. Private screenshots were deleted immediately. The KR path
  remains covered by the same closed localization catalog and prior device
  watchdog evidence; no localization source changed in this follow-up.
- Vulkan remains the default: the measured Quick Restart and map sessions used
  Vulkan, normal production startup did not enter the one-shot OpenGL recovery
  path, and source still adds `gl_compatibility` only for an explicit one-shot
  recovery request (production rejects the debug override).
- A changed/untracked-file audit found no device serial, credentials, private
  keys, APK/DLL/PCK, screenshot, raw log/trace, or oversized evidence in the
  repository. The final worktree has no conflict with freshly fetched
  `upstream/main`; its merge-tree exactly equals the audited worktree tree.
