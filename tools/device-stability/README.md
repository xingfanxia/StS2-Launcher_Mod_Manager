# Android device stability evidence

`capture.sh` creates a bounded, read-only snapshot for one Android device. It
does not install, uninstall, clear data, force-stop, or launch the package. The
generated `device-matrix.tsv` is a run ledger for the Device Matrix in
`GOAL.md`; fill each row with `pass`, `fail`, `unsupported`, or `not-run` and a
path to the corresponding before/after snapshots.

Before a real-device proof run:

```sh
tools/device-stability/capture.sh \
  --serial "$ANDROID_SERIAL" \
  --output "/private/evidence/before" \
  --require-physical \
  --require-arm64
```

Run the scenario manually, preserving the existing application data, then take
another snapshot with a new output directory. Add `--include-logcat` only when
the device owner accepts that filtered Android logs can still contain private
data. The log capture is bounded to 4000 lines and intentionally omits a full
bugreport.

Exit code 3 means the serial is an emulator. An emulator is useful for checking
API behavior, but cannot satisfy the ARM64 physical-device proof in `GOAL.md`.
An absent package is reported rather than installed: installing the unsigned
local APK requires an authorized signing identity, and fresh-install or data
destruction requires separate approval.

Run the deterministic fake-device tests with:

```sh
bash tools/device-stability/tests/run.sh
```

## Sanitized repeated scenarios

`run-matrix.sh` runs the repetitive cold-start, HOME/resume, or landscape
rotation rows. Unlike `capture.sh`, this is an explicitly mutating test tool: it
uses force-stop/input for cold starts and temporarily changes rotation settings
for the rotation scenario. It therefore requires `--allow-device-actions` and
restores both rotation settings on exit. It never installs/uninstalls, clears
app data, changes the network, or edits app-private files.

The output is a versioned TSV containing only scenario/iteration, terminal
state, bounded startup attempt/stage tokens, PID continuity (yes/no), split
process→launcher, user-wait, PLAY→game-ready and total elapsed time, numeric
temperature/thermal status, system exit classification, recovery-pending state, and counts of
fatal/ANR/LMK/surface-error lines. It deliberately does not save raw logcat,
actual PIDs, the device serial, UI dumps, account text, mod names, or paths.

```sh
tools/device-stability/run-matrix.sh \
  --serial "$ANDROID_SERIAL" \
  --output "/private/evidence/cold-start.tsv" \
  --scenario cold-start \
  --iterations 30 \
  --play-x 676 \
  --play-y 1405 \
  --game-confirm-x 1240 \
  --game-confirm-y 1920 \
  --allow-device-actions
```

PLAY coordinates are deliberately explicit because Godot Controls are not
available through Android's accessibility hierarchy. Validate them once on the
current display configuration before a long run. A cold-start row passes only
when the same process reaches the app-authored `game-ready` terminal stage.
The optional game-confirm coordinate handles a game-owned first-run dialog: it
is tapped only if `game-ready` has not appeared after the configured grace
period, and is skipped on normal launches that already reached the terminal.

The matrix rejects Android thermal status above 2 before mutation and waits up
to ten minutes for the device to cool between repetitions. Each cold-start row
force-stops its capture-owned process after collecting evidence, including on
teardown, so the finished game cannot keep heating the reference device. A row
that ends beyond the configured thermal ceiling is retained as
`thermal-invalid`, not counted as performance proof.

`audit-screenshot.swift` performs content-free OCR checks. It reports only line
counts, Hangul/edge-clipping counts, and (when explicitly requested) normalized
centers for the Safe Mode action, Compatibility action, or complete branch
picker. It never prints recognized text. The locator modes let a device test
wait for the real dynamic UI instead of guessing with a fixed network delay:

```sh
swift tools/device-stability/audit-screenshot.swift screenshot.png \
  --require-no-hangul \
  --require-no-tofu \
  --require-chinese \
  --locate-language-selector \
  --locate-branch-picker
```

For the launcher language proof, `--require-chinese` requires at least one
recognized CJK line and `--locate-language-selector` returns only the normalized
center of `简体中文`; recognized text itself is never printed. The unforced plus
high-confidence script-residue check avoids classifying Han glyphs as Korean, while
`--require-no-tofu` rejects common OCR representations of missing-glyph boxes.
