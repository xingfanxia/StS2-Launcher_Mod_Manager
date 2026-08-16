#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TOOL_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
TEST_DIR="$(mktemp -d)"
trap 'rm -rf "$TEST_DIR"' EXIT

fail() {
    echo "FAIL: $*" >&2
    exit 1
}

bash -n "$TOOL_DIR/capture.sh" "$TOOL_DIR/run-matrix.sh" "$SCRIPT_DIR/fake-adb.sh"

set +e
bash "$TOOL_DIR/run-matrix.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake-serial \
    --output "$TEST_DIR/should-not-exist.tsv" \
    --scenario home-resume >/dev/null 2>&1
action_gate_status=$?
set -e
[ "$action_gate_status" -eq 1 ] || fail "device-action gate returned $action_gate_status"
[ ! -e "$TEST_DIR/should-not-exist.tsv" ] || fail "device-action gate wrote evidence"

set +e
FAKE_QEMU=1 bash "$TOOL_DIR/capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial emulator-5554 \
    --output "$TEST_DIR/emulator" \
    --require-physical >/dev/null 2>&1
emulator_status=$?
set -e
[ "$emulator_status" -eq 3 ] || fail "emulator gate returned $emulator_status, expected 3"
grep -qx 'device_kind=emulator' "$TEST_DIR/emulator/summary.txt" \
    || fail "emulator was not classified"

FAKE_PACKAGE_INSTALLED=1 bash "$TOOL_DIR/capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake-serial \
    --output "$TEST_DIR/physical" \
    --require-physical \
    --require-arm64 \
    --include-logcat >/dev/null
grep -qx 'device_kind=physical' "$TEST_DIR/physical/summary.txt" \
    || fail "physical device was not classified"
grep -qx 'package_state=installed' "$TEST_DIR/physical/summary.txt" \
    || fail "installed package was not detected"
grep -q 'fake bounded logcat' "$TEST_DIR/physical/logcat.txt" \
    || fail "bounded logcat was not captured"
[ "$(tail -n +2 "$TEST_DIR/physical/device-matrix.tsv" | wc -l | tr -d ' ')" -ge 20 ] \
    || fail "device matrix is incomplete"

set +e
FAKE_ABI_LIST=x86_64 bash "$TOOL_DIR/capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake-x86 \
    --output "$TEST_DIR/x86" \
    --require-arm64 >/dev/null 2>&1
abi_status=$?
set -e
[ "$abi_status" -eq 4 ] || fail "ARM64 gate returned $abi_status, expected 4"

if grep -Eq '(^|[[:space:]])(install|uninstall|clear|force-stop)([[:space:]]|$)|logcat[[:space:]]+-c' \
    "$TOOL_DIR/capture.sh"; then
    fail "capture tool contains a mutating adb operation"
fi

if grep -Eq '(^|[[:space:]])(install|uninstall|pm[[:space:]]+clear)([[:space:]]|$)' \
    "$TOOL_DIR/run-matrix.sh"; then
    fail "matrix tool contains an install, uninstall, or app-data clear operation"
fi

echo "PASS: device stability capture tests"
