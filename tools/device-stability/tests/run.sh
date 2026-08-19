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

grep -q '안전모드로계속' "$TOOL_DIR/audit-screenshot.swift" \
    || fail "screenshot Safe Mode locator does not support Korean"
grep -q 'language_selector_center_normalized' "$TOOL_DIR/audit-screenshot.swift" \
    || fail "screenshot audit cannot locate the Simplified Chinese selector"
grep -q -- '--require-chinese' "$TOOL_DIR/audit-screenshot.swift" \
    || fail "screenshot audit cannot require visible Simplified Chinese"
grep -q -- '--require-no-tofu' "$TOOL_DIR/audit-screenshot.swift" \
    || fail "screenshot audit cannot reject missing glyph boxes"
grep -q '选择游戏版本' "$TOOL_DIR/audit-screenshot.swift" \
    || fail "branch picker locator does not support Simplified Chinese"
grep -q '安全模式继续' "$TOOL_DIR/audit-screenshot.swift" \
    || fail "Safe Mode locator does not support Simplified Chinese"
grep -q 'automaticObservations' "$TOOL_DIR/audit-screenshot.swift" \
    || fail "Hangul residue check still relies on a forced-language OCR pass"

grep -q 'Launcher ready for PLAY' "$TOOL_DIR/run-matrix.sh" \
    || fail "matrix does not wait for the truthful PLAY-ready boundary"
if grep -q 's/.*(Launcher UI displayed).*/launcher-ready' "$TOOL_DIR/run-matrix.sh"; then
    fail "matrix still treats initial launcher rendering as PLAY readiness"
fi

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

COMMAND_LOG="$TEST_DIR/cold-start.commands"
set +e
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_PACKAGE_INSTALLED=1 \
    bash "$TOOL_DIR/run-matrix.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake-serial \
        --output "$TEST_DIR/cold-start.tsv" \
        --scenario cold-start \
        --iterations 2 \
        --play-x 10 \
        --play-y 20 \
        --timeout 2 \
        --allow-device-actions >/dev/null 2>&1
matrix_status=$?
set -e
[ "$matrix_status" -eq 0 ] || fail "cold-start matrix returned $matrix_status"
grep -q $'process_to_launcher_ms\tuser_wait_ms\tplay_to_game_ready_ms' \
    "$TEST_DIR/cold-start.tsv" || fail "cold-start matrix omitted split timing"
[ -f "$TEST_DIR/cold-start.tsv.stages.tsv" ] \
    || fail "cold-start matrix omitted stage timing evidence"
grep -q $'^2\tcold-start\t1\tpass\t2\t4\t-\t-\t-\t10' \
    "$TEST_DIR/cold-start.tsv.stages.tsv" \
    || fail "cold-start matrix did not parse bounded stage summaries"
[ "$(awk -F '\t' 'NR > 1 && $4 == "pass" && $5 == "game-ready" { n++ } END { print n+0 }' \
    "$TEST_DIR/cold-start.tsv")" -eq 2 ] || fail "cold-start matrix rows did not pass"
[ "$(grep -c 'force-stop' "$COMMAND_LOG")" -eq 4 ] \
    || fail "cold-start matrix did not stop each owned process after capture"

COMMAND_LOG="$TEST_DIR/safe-start.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_PACKAGE_INSTALLED=1 \
    bash "$TOOL_DIR/run-matrix.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake-serial \
        --output "$TEST_DIR/safe-start.tsv" \
        --scenario cold-start-safe \
        --iterations 1 \
        --play-x 10 \
        --play-y 20 \
        --timeout 2 \
        --allow-device-actions >/dev/null
grep -q $'^2\tcold-start-safe\t1\tpass\tgame-ready' "$TEST_DIR/safe-start.tsv" \
    || fail "session-only Safe Mode startup did not pass"
grep -Eq 'debug_frame_probe[[:space:]]+game-menu-safe-60' "$COMMAND_LOG" \
    || fail "Safe Mode startup did not arm the debug-only session override"

COMMAND_LOG="$TEST_DIR/delayed-start.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_PACKAGE_INSTALLED=1 \
    bash "$TOOL_DIR/run-matrix.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake-serial \
        --output "$TEST_DIR/delayed-start.tsv" \
        --scenario cold-start \
        --iterations 1 \
        --play-x 10 \
        --play-y 20 \
        --debug-stage-delay-seconds 20 \
        --timeout 2 \
        --allow-device-actions >/dev/null
grep -Eq 'debug_startup_stage_delay_seconds[[:space:]]+20' "$COMMAND_LOG" \
    || fail "startup matrix did not arm the bounded watchdog proof"

COMMAND_LOG="$TEST_DIR/release-delay.commands"
set +e
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_PACKAGE_INSTALLED=1 FAKE_VERSION_NAME=0.4.2 \
    bash "$TOOL_DIR/run-matrix.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake-serial \
        --output "$TEST_DIR/release-delay.tsv" \
        --scenario cold-start \
        --iterations 1 \
        --play-x 10 \
        --play-y 20 \
        --debug-stage-delay-seconds 20 \
        --timeout 2 \
        --allow-device-actions >/dev/null 2>&1
release_delay_status=$?
set -e
[ "$release_delay_status" -eq 1 ] \
    || fail "release startup-delay gate returned $release_delay_status"
[ ! -e "$TEST_DIR/release-delay.tsv" ] \
    || fail "release startup-delay gate wrote evidence"
if grep -Eq 'force-stop|am[[:space:]]+start|logcat[[:space:]]+-c' "$COMMAND_LOG"; then
    fail "release startup-delay gate allowed a mutating device command"
fi

COMMAND_LOG="$TEST_DIR/hot-matrix.commands"
set +e
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_PACKAGE_INSTALLED=1 FAKE_THERMAL_STATUS=3 \
    bash "$TOOL_DIR/run-matrix.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake-serial \
        --output "$TEST_DIR/hot-matrix.tsv" \
        --scenario cold-start \
        --iterations 1 \
        --play-x 10 \
        --play-y 20 \
        --max-thermal-status 2 \
        --thermal-wait-seconds 0 \
        --allow-device-actions >/dev/null 2>&1
hot_matrix_status=$?
set -e
[ "$hot_matrix_status" -eq 8 ] || fail "hot matrix returned $hot_matrix_status"
[ ! -e "$TEST_DIR/hot-matrix.tsv" ] || fail "hot matrix wrote evidence"
if grep -Eq 'force-stop|am[[:space:]]+start|logcat[[:space:]]+-c' "$COMMAND_LOG"; then
    fail "hot matrix allowed a mutating device command"
fi

echo "PASS: device stability capture tests"
