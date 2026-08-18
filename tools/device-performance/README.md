# Android game-frame performance evidence

`run-frame-capture.sh` is the reproducible device harness for the canonical
metric in `GOAL_PERFORMANCE_OBSERVABILITY.md`: monotonic intervals between
Godot `SceneTree.ProcessFrame` callbacks. It consumes only the app's bounded
debug probe and never treats `gfxinfo` as authoritative for Godot/Vulkan.

The tool force-stops and launches the app, so it requires
`--allow-device-actions`. It refuses to act below the configured battery floor
or when the installed build is not an explicitly suffixed `-debug` probe build.
Every exit path also force-stops the capture-owned app process so a completed or
failed run cannot keep heating the device. It never installs/uninstalls, clears
app data, changes network/settings/renderer, or accesses saves and mods.

Example for a real interactive game capture:

```sh
tools/device-performance/run-frame-capture.sh \
  --serial "$ANDROID_SERIAL" \
  --output "/private/evidence/game-final-1" \
  --mode game-120 \
  --play-x 676 \
  --play-y 1405 \
  --allow-device-actions
```

After PLAY, perform the fixed interaction script for 120 seconds. Each output
directory contains only numeric `summary.tsv`, `spikes.tsv`, `rss.tsv`, and
`context.tsv` files. It contains no raw logcat, PID, serial, account, path, save
content, or mod name. Raw Perfetto traces, when required for owner attribution,
remain outside the repository and outside this sanitized harness.

For a caller-verified sacrificial slot that is already saved at the first combat
turn, the harness can also remove the last manual navigation step. It waits for
the real `game-ready` probe, taps the visible Continue action once, and starts
the measured segment only after a real combat hand is stable:

```sh
tools/device-performance/run-frame-capture.sh \
  --serial "$ANDROID_SERIAL" \
  --output "/private/evidence/partition-0-combat" \
  --mode game-partition-120 \
  --mod-partition 0/2 \
  --play-x 676 --play-y 1405 \
  --resume-x 960 --resume-y 1430 \
  --allow-save-fixture \
  --allow-device-actions
```

The resume option is deliberately separate from the ordinary device-action
gate because entering the active save may trigger normal game autosave behavior.
Verify the active profile/slot before using it; it never selects or edits a slot
and must not be pointed at a user-owned proof-exempt save.

After validating candidate partitions in the menu phase, run the entire combat
pair with the same aggregator instead of invoking each capture manually:

```sh
tools/device-performance/run-mod-jank-workflow.sh \
  --serial "$ANDROID_SERIAL" \
  --output "/private/evidence/combat-bisect" \
  --phase combat \
  --scenarios 6/8,7/8 \
  --play-x 676 --play-y 1405 \
  --resume-x 960 --resume-y 1430 \
  --timeout 300 \
  --allow-save-fixture \
  --allow-device-actions
```

The same combat runner also alternates the paired same-APK gameplay modes. The
`baseline` scenario changes exactly one process-local variable: it disables the
gameplay performance patches while retaining the same APK and instrumentation;
`optimized` keeps those patches active. Scenario order is preserved for every
repetition, so this command runs baseline→optimized three times without manual
relaunch/navigation:

```sh
tools/device-performance/run-mod-jank-workflow.sh \
  --serial "$ANDROID_SERIAL" \
  --output "/private/evidence/gameplay-paired" \
  --phase combat \
  --scenarios baseline,optimized \
  --runs 3 \
  --play-x 676 --play-y 1405 \
  --resume-x 960 --resume-y 1430 \
  --timeout 300 \
  --allow-save-fixture \
  --allow-device-actions
```

Both capture entry and aggregated results enforce Android thermal status 0–2 by
default. A hotter device is rejected before mutation, and a run that crosses
the limit is retained as `thermal-invalid` rather than counted as A/B proof.

If the ordinary pair reports any anonymous third-party initializer failure,
use the composable no-mod pair instead of comparing contaminated samples:

```sh
tools/device-performance/run-mod-jank-workflow.sh \
  --serial "$ANDROID_SERIAL" \
  --output "/private/evidence/gameplay-paired-safe" \
  --phase combat \
  --scenarios baseline-safe,optimized-safe \
  --runs 3 \
  --play-x 676 --play-y 1405 \
  --resume-x 960 --resume-y 1300 \
  --timeout 300 \
  --allow-save-fixture \
  --allow-device-actions
```

`game-baseline-safe-120` combines the same one-variable performance baseline
with session-only Safe Mode; `game-safe-120` is its optimized peer. Neither
mode changes persistent mod enablement.

`game-safe-300` is the bounded five-minute no-mod gameplay capture used after
the optional shader warmup has deferred to on-demand compilation. It uses the
same sanitized percentile/spike/RSS/thermal outputs and requires an explicitly
approved sacrificial save fixture to enter real gameplay:

```sh
tools/device-performance/run-frame-capture.sh \
  --serial "$ANDROID_SERIAL" \
  --output "/private/evidence/shader-on-demand-300" \
  --mode game-safe-300 \
  --play-x 675 --play-y 1405 \
  --resume-x 960 --resume-y 1430 \
  --timeout 480 \
  --allow-save-fixture \
  --allow-device-actions
```

For a repeatable deck-screen load instead of an idle combat sample, add the
bounded `deck-cycle` script. It waits for the real stable-hand marker, then
opens and closes the deck exactly five times before completing the same
120-second capture. Coordinates are explicit because they belong to the
reference device/layout rather than the product:

```sh
  --interaction-script deck-cycle \
  --deck-x 2290 --deck-y 65
```

The script never plays a card, ends a turn, changes a save, or guesses that a
tap succeeded from timing alone; the gameplay segment and summary still have
to satisfy the in-app probe contract or the capture fails.

Fixed Continue coordinates are valid only for the exact game-menu layout that
was checked by the caller. Safe Mode and dependency-bearing partitions can move
that row. Use `--resume-auto` with `--allow-save-fixture` for mixed-layout
matrices: the runner captures a temporary private screenshot, returns only the
normalized center of the exact game-owned Continue label through the existing
Vision audit, rejects edge/dialog candidates, taps once, and immediately
deletes the screenshot. It never accepts an Abandon or confirmation label.

Use `game-safe-120` for the session-only no-mod comparison. `control` and
`stall-100` validate the metric; `launcher-120` measures launcher interaction.
For the paired gameplay A/B, alternate `game-baseline-120` and `game-120` in
the same debug APK. Baseline mode changes exactly one process-local variable:
it skips frame-pacing/first-hand/deck-cache patches while retaining identical
instrumentation and all other launcher/game state. The production APK
deliberately rejects every debug-probe intent.

Supported-mod regression pairs use `baseline-I/N,optimized-I/N` in the combat
workflow. Both arms expose the same anonymous numeric partition plus its real
dependency closure; the baseline arm changes only the existing process-local
gameplay-performance switch. Persistent enablement and mod files are untouched.

For the telemetry-overhead A/B, pass
`--startup-telemetry-persistence on|off`. Both arms retain the same startup
stage state, KR/EN progress surface, frame probe, and gameplay behavior; `off`
suppresses only the bounded native/managed summary writes. Each capture records
the selected arm in `instrumentation.tsv`, per-process RSS in `rss.tsv`, and
five-second CPU intervals as integer milli-percent in `cpu.tsv`.

## Fast standardized mod-jank workflow

Before navigating a sacrificial save into combat for every candidate group,
run the settled-main-menu matrix. An explicit debug menu capture automatically
continues its session-only Safe Mode/partition confirmation, waits five seconds
for the menu to settle, and captures 60 seconds. It does not read or change a
save, persistent mod enablement, renderer, language, or network setting.

```sh
tools/device-performance/run-mod-jank-workflow.sh \
  --serial "$ANDROID_SERIAL" \
  --output "/private/evidence/mod-jank-scan" \
  --scenarios full,safe,0/2,1/2 \
  --runs 1 \
  --play-x 676 \
  --play-y 1405 \
  --allow-device-actions
```

`workflow.tsv` classifies each run as `pass`, `mod-load-error`, or
`capture-failed` and contains only numeric timing/thermal data plus anonymous
partition coordinates. A main-menu candidate is promoted to the slower combat
workflow only after its spike cadence matches the already proven combat trace;
the menu scan alone is not evidence that gameplay is fixed.

Use this fixed testing ladder instead of rebuilding and manually navigating
after every edit:

1. Run `tools/test-workflow.sh focused` after each harness/runtime edit. It
   executes the host, Java, managed, localization, and whitespace contracts in
   a disposable native-architecture environment without touching the device.
2. Run the pinned APK pipeline once after the focused contracts are green, then
   upgrade-install that one signed candidate without clearing app data.
3. Use `--scenarios safe,0/2` as the short device smoke check. Expand to
   `full,safe,0/2,1/2` only for diagnosis, and continue partitioning only the
   valid suspect side.
4. Promote a candidate to the 120-second real-combat capture only when the menu
   scan or an existing combat trace provides a concrete hypothesis. Final proof
   still uses three comparable 120-second runs.

The workflow exits `0` when every sample is valid and `7` when the matrix
finished but one or more samples were classified as a mod load error or capture
failure. Precondition/infrastructure failures remain ordinary nonzero failures.

Run the deterministic contract tests with:

```sh
bash tools/device-performance/tests/run.sh
```

Or run the complete focused pre-APK gate with:

```sh
tools/test-workflow.sh focused
```

## Interleaved startup APK A/B

Use `run-startup-ab.sh` only after the candidate diff and focused checks are
stable. It validates both APKs as this launcher's exact package and requires an
identical complete signer set before any device mutation. The phone must already
be manually unlocked. Each arm uses upgrade install (`adb install -r -d`), so app
data, Steam login, saves, persistent mod configuration, language, and renderer
remain untouched; every failure path force-stops the app and installs the
candidate again.

The runner alternates baseline→candidate in odd pairs and candidate→baseline in
even pairs, uses the common `Launcher UI displayed` boundary supported by both
APKs, and keeps the automated UI→PLAY acknowledgement interval in the total.
This avoids incorrectly calling baseline recovery work “user wait.” It writes
only numeric/sanitized matrices and never stores raw logcat, device identifiers,
APK paths, account data, save content, or mod names.

```sh
tools/device-performance/run-startup-ab.sh \
  --baseline-apk "/private/evidence/baseline.apk" \
  --candidate-apk "/private/evidence/candidate.apk" \
  --serial "$ANDROID_SERIAL" \
  --output "/private/evidence/startup-ab" \
  --pairs 30 \
  --play-x 676 --play-y 1405 \
  --max-start-thermal-status 1 \
  --aapt2 "$ANDROID_HOME/build-tools/35.0.0/aapt2" \
  --apksigner "$ANDROID_HOME/build-tools/35.0.0/apksigner" \
  --allow-device-actions \
  --allow-apk-installs
```

`startup-ab.tsv` reports process→launcher UI, automated activation wait,
PLAY→game-ready, their complete launch→game-ready sum, thermal state, and bounded
crash/ANR/LMK/surface counts, plus every available numeric startup stage. Before
each install, the runner waits for the configured thermal band and rechecks both
manual unlock and battery; the inner capture never performs a long blind cooling
wait after an APK has been installed.

The optional `--max-start-thermal-status` can enforce a cooler admission band
than the accepted result band. For example, start at status 0–1 while retaining
otherwise-valid status-2 endings; this avoids spending a full arm that begins
at status 2 and crosses into invalid status 3.

If a pre-install unlock, battery, or cooling precondition stops a long matrix,
rerun the exact command with `--resume`. Resume validates the existing schema,
alternating arm order, and `pass/game-ready` terminal for every retained row,
then starts at the first missing arm. A captured failure or thermal-invalid row
is deliberately not resumable in place; use a new evidence directory so an
invalid sample cannot be overwritten or cherry-picked away.

Successful completion also writes `startup-summary.tsv` with nearest-rank
p50/p95/p99/max, `startup-comparison.tsv` with the 10% p50 and 5% p95 gates in
basis points, and `startup-stage-summary.tsv`. A legacy APK that lacks a bounded
stage summary is reported with `samples=0` and `-`, never filled from guessed
timestamps. A thermal-invalid arm exits `8`; any non-terminal arm exits `7`.
Neither is counted as performance proof.
