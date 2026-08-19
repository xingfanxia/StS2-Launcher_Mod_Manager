# Performance TODO

## Launcher time-to-interactive

Observed on PKH110 (Android 16, Vulkan) with launcher `0.4.5-modcompat-qa` through `qa8`:

- Android activity, recovery, cache check, and assembly sync: about 0.8 seconds.
- Godot/Vulkan/Mono/game bootstrap before the launcher becomes interactive: about 14.9 seconds.
- Earlier runs measured 14.9 seconds normally, about 71 seconds immediately after an APK replacement, and 17 seconds on the following cold launch.
- Final QA8 measured 85.46 seconds for the replacement launch and 83.84 seconds for a later cold restart of the same APK. This disproves the narrower "post-install only" explanation; the slow path can recur without package replacement. A Perfetto CPU/I/O/scheduler trace is still required before assigning a root cause.

### Option A: defer game-only Harmony patches

Keep the current in-process launcher architecture, but measure every `ModEntry.Apply` patch group and move only patches that are provably unnecessary before PLAY into the post-PLAY path.

- Expected impact: small to medium; bounded by the measured patch time, not the full 14.9-second engine bootstrap.
- Advantages: lower implementation risk and lower future upstream conflict.
- Required proof: per-group timings, cold-start A/B runs, no missed early hooks, and unchanged crash/recovery behavior.

### Option B: standalone Android launcher activity

Show a lightweight native Android launcher before starting the Godot activity, then hand the selected launch/session state to the game process. Godot may optionally prewarm in the background only after lifecycle and memory behavior are proven safe.

- Expected impact: large improvement to perceived launcher startup; the launcher can become interactive without waiting for Godot/.NET.
- Limitation: total time to the game remains close to the existing bootstrap unless work is safely overlapped after the launcher appears.
- Costs: duplicate/native UI surface, Steam/session handoff, update/mod-manager ownership, activity/process lifecycle, back-stack behavior, and materially higher upstream merge conflict.
- Required proof: process/activity state contract, credential boundary review, low-memory/background tests, install/update/recovery tests, and measured time-to-launcher plus time-to-game-ready.

### Next investigation

1. Add native markers around Vulkan device creation, PCK/project load, Mono initialization, managed assembly loading, `ModEntry.Apply`, and first scene creation.
2. Add per-patch-group timings in `ModEntry.Apply` and run cold-start A/B tests before moving any patch.
3. Capture both a fast and an 80+ second cold launch with Perfetto, plus one APK-replacement launch, to separate CPU, page-fault/I/O, package-manager work, lock contention, and idle/wait time.
4. Decide between Option A and Option B from measured time-to-interactive gain, implementation risk, and upstream conflict—not from the current aggregate 14.9-second span.
