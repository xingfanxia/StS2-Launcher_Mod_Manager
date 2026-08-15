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
