# Performance baseline

Status: complete on the ARM64 reference device. Baseline, returning-user,
focused gameplay, supported-mod, warmup/on-demand, and instrumentation-overhead
A/B evidence is retained outside the repository as sanitized TSV.

This document records only sanitized aggregates. Raw logcat, Perfetto traces,
screenshots, device identifiers, account data, save contents, paths, and mod
names stay outside the repository.

## Reference conditions

- One connected ARM64 Android reference device, 60 Hz display budget
  (`16,667 us`).
- Public game branch, Vulkan renderer, unchanged resolution and game settings.
- The focused measurements below use the debug-only session Safe Mode so the
  real mod configuration and files remain unchanged.
- Existing stability baseline: commit `8176861`. Its 30 warm-cache cold starts
  reached `game-ready` 30/30, with 38,324 ms minimum, 41,255 ms nearest-rank
  p50, 43,657 ms p95, 44,055 ms maximum, and 41,169 ms mean.

## Canonical frame metric validation

The canonical metric is the monotonic interval between Godot
`SceneTree.ProcessFrame` callbacks. A debug-only `100 ms` single-frame sleep
was detected as an approximately `104 ms` interval. The control run remained
near a 60 Hz cadence (`p50 ~16.7 ms`, `p95 ~18.7 ms`, `max ~20.9 ms`) and did
not report the injected spike.

Android `gfxinfo` did not observe the Godot/Vulkan surface reliably and is not
used as a pass/fail source. Perfetto remains an attribution tool rather than the
canonical percentile source.

## Returning-user startup

The Goal stability baseline APK and exact final APK ran as 30 alternating A/B
pairs. Every arm started at thermal status 1, ended at status 1–2, retained one
PID, and reached `game-ready` with zero fatal, ANR, LMK, or surface error.

| Boundary | Baseline p50/p95/p99/max | Final p50/p95/p99/max | p50 change |
|---|---:|---:|---:|
| Process → launcher UI | 6.056 / 6.634 / 6.734 / 6.734 s | 6.060 / 6.701 / 6.958 / 6.958 s | +0.07% |
| Automated UI → PLAY acknowledgement | 0.421 / 1.059 / 1.068 / 1.068 s | 1.062 / 1.079 / 1.086 / 1.086 s | not an owned-work gate |
| PLAY → game-ready | 31.359 / 31.727 / 31.753 / 31.753 s | 21.739 / 22.397 / 22.685 / 22.685 s | -30.68% |
| Complete automated launch | 38.061 / 38.695 / 38.712 / 38.712 s | 28.797 / 29.702 / 30.388 / 30.388 s | -24.34% |

Total p95 improved `23.24%`, so the 10% p50 and no-more-than-5% p95 regression
gates both pass. A separate pre-logo/final 30-pair series localized the change:
`game-startup` p50 fell `19.032 → 10.022 s`, while anonymous mod-load p50
remained `11.468 → 11.527 s`.

Coverage and blind spots:

- The Godot interval includes main-thread work, waits, render synchronization,
  and process scheduling that delays the next callback.
- It does not alone identify CPU, GPU, I/O, GC, shader, mod, or scheduler
  ownership. Spike markers therefore include pipeline compilation counters and
  are correlated with bounded method timing or Perfetto evidence.
- Activity background time must be segmented separately; it is not a dropped
  frame.

## Real game measurements

All counts use strict thresholds and the `16,667 us` frame budget.

| Scenario | Samples | p50 | p95 | p99 | Max | >2x | >3x | >50 ms | >100 ms | >250 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Pre-fix load through first combat hand | 6,947 | 16.697 ms | 18.343 ms | 20.928 ms | 645.907 ms | 37 | 23 | 23 | 13 | 4 |
| Covered-load plus paced transitions | 3,466 | 16.735 ms | 18.655 ms | 27.657 ms | 660.742 ms | 27 | 21 | 21 | 12 | 4 |
| Revealed combat, idle, 120 s | 7,185 | 16.700 ms | 17.817 ms | 19.051 ms | 53.431 ms | 5 | 2 | 2 | 0 | 0 |
| Revealed combat with real UI interaction, 120 s | 7,086 | 16.687 ms | 18.162 ms | 19.214 ms | 771.357 ms | 16 | 9 | 9 | 3 | 2 |

The covered-load row is not presented as faster total work. It shows that the
known synchronous scene and first-hand compilation work remains expensive but
is kept behind an explicit, localized, indeterminate loading state. The
revealed-combat segment starts only after the real hand is stable.

### Spike ownership

1. The original first-hand frame rose from 48 to 62 Canvas pipeline
   compilations and took approximately `646 ms`. Managed card/hand calls around
   it were tens of milliseconds, not the full spike.
2. Perfetto showed the Godot main thread CPU-bound during a representative
   approximately `915 ms` transition spike. Vulkan present work was roughly
   `1-2 ms`; no thermal or file-I/O owner explained that spike.
3. The OpenGL diagnostic comparison was worse on the same device (maximum
   approximately `1.11 s`, seven frames over `250 ms`) than Vulkan (four frames
   over `250 ms`). Vulkan remains the default.
4. In the interactive run, first pause/settings open cost approximately
   `237 ms`, first map open approximately `80 ms`, first 23-card deck view open
   approximately `771 ms`, and deck close approximately `311 ms`.
5. Decompilation tied deck-open cost to the synchronous
   `NDeckViewScreen._Ready -> DisplayCards -> NCardGrid.SetCards -> InitGrid`
   path, which creates and updates the visible full-card grid on one main-thread
   continuation. The original `ShowScreen` creates a new screen on every open,
   and the original `AfterCapstoneClosed` queues that screen for deletion. Thus
   every open repeats the full construction. A weak-reference-only prototype
   was ineffective because its target was deleted on the next frame; the
   current patch instead retains one run-tree-owned screen and omits deletion
   only for that screen.
6. The matched game assembly confirms that `NRunSubmenuStack` already owns a
   one-instance lazy cache for the pause menu. Constructing that cached instance
   during the covered load uses the upstream lifecycle and does not open the
   submenu or replace game state.
7. The approximately `80 ms` first map open is not map generation: `SetMap`
   creates the live point/path tree before interaction. The first `Open` exposes
   that existing Canvas tree, but it also pauses combat, changes the hotkey and
   active-screen stacks, emits open/close signals, plays audio, and can consume
   the one-time start-of-act animation. A hidden open/close therefore is not a
   behavior-preserving prewarm and has not been added. The residual remains in
   the final device A/B so rendering exposure can be measured independently.

## Excluded experiments

- Hidden or detached representative cards, SubViewport rendering, root-view
  fake cards, and a cloned player hand did not eliminate the real first-hand
  pipeline delta. Those experiments were removed.
- Explicit managed GC did not improve the measured transition and was removed.
- OpenGL did not improve the same-device diagnostic comparison and is not a
  product default change.

## Additional guardrails

- Injected Android memory pressure caused optional shader warmup to defer in
  `3 ms`; the planned restart and on-demand startup reached `game-ready`
  without a crash loop. A 300-second on-demand gameplay run changed long-frame
  density by `+5.05%` versus the comparable current warm-cache path, inside the
  10% gate, with no frame over `100 ms`.
- Startup-summary persistence on/off 3x120-second medians differed by p50
  `+0.042%`, p95 `-0.235%`, p99 `+0.058%`, CPU `-0.144%`, and RSS `+1.584%`.
  This is below the 3% instrumentation-overhead limit and introduced no new
  long-frame cluster.
- Three 120-second supported-mod pairs passed in the 0–2 thermal band with no
  load error or frame over 100 ms. Median p95/p99 changed
  `17.643/18.767 → 17.607/18.741 ms`, inside the 10% regression guardrail for
  both the anonymous base-library dependency and ordinary supported-mod paths.

## Residual limits

- The historical baseline predates the independent per-stage timeline, so
  baseline per-stage percentiles are not available. The final series provides
  process-to-PLAY-ready, real user-wait, PLAY-to-game, and all 16 stage spans.
- One anonymous third-party mod group remains an external sustained-jank owner;
  the launcher provides session-only isolation and attribution rather than
  changing that mod.
- Results are reference-device evidence, not a universal ROM/GPU guarantee.
