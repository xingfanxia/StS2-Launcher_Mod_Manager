#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TOOL_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
STABILITY_FAKE_ADB="$TOOL_DIR/../device-stability/tests/fake-adb.sh"
TEST_DIR="$(mktemp -d)"
trap 'rm -rf "$TEST_DIR"' EXIT
export STS2_DEVICE_PERFORMANCE_TEST_FAST=1

fail() {
    echo "FAIL: $*" >&2
    exit 1
}

bash -n "$TOOL_DIR/run-frame-capture.sh" \
    "$TOOL_DIR/run-mod-jank-workflow.sh" \
    "$TOOL_DIR/run-startup-ab.sh" \
    "$SCRIPT_DIR/fake-aapt2.sh" \
    "$SCRIPT_DIR/fake-apksigner.sh" \
    "$SCRIPT_DIR/fake-adb.sh"

touch "$TEST_DIR/baseline.apk" "$TEST_DIR/candidate.apk"

set +e
bash "$TOOL_DIR/run-startup-ab.sh" \
    --baseline-apk "$TEST_DIR/baseline.apk" \
    --candidate-apk "$TEST_DIR/candidate.apk" \
    --serial fake-serial \
    --output "$TEST_DIR/startup-no-install-approval" \
    --pairs 1 \
    --play-x 10 --play-y 20 \
    --aapt2 "$SCRIPT_DIR/fake-aapt2.sh" \
    --apksigner "$SCRIPT_DIR/fake-apksigner.sh" \
    --adb "$STABILITY_FAKE_ADB" \
    --allow-device-actions >/dev/null 2>&1
startup_install_gate_status=$?
set -e
[ "$startup_install_gate_status" -eq 1 ] \
    || fail "startup APK-install gate returned $startup_install_gate_status"
[ ! -e "$TEST_DIR/startup-no-install-approval" ] \
    || fail "startup APK-install gate wrote output"

COMMAND_LOG="$TEST_DIR/startup-ab.commands"
INSTALLED_STATE="$TEST_DIR/startup-ab.installed"
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_INSTALLED_APK_STATE="$INSTALLED_STATE" \
    FAKE_PACKAGE_INSTALLED=1 FAKE_DEVICE_LOCKED=0 \
    bash "$TOOL_DIR/run-startup-ab.sh" \
        --baseline-apk "$TEST_DIR/baseline.apk" \
        --candidate-apk "$TEST_DIR/candidate.apk" \
        --serial fake-serial \
        --output "$TEST_DIR/startup-ab" \
        --pairs 2 \
        --play-x 10 --play-y 20 \
        --timeout 2 \
        --thermal-wait-seconds 0 \
        --aapt2 "$SCRIPT_DIR/fake-aapt2.sh" \
        --apksigner "$SCRIPT_DIR/fake-apksigner.sh" \
        --adb "$STABILITY_FAKE_ADB" \
        --allow-device-actions \
        --allow-apk-installs >/dev/null
startup_order="$(awk -F '\t' 'NR > 1 { print $2 ":" $3 ":" $4 }' \
    "$TEST_DIR/startup-ab/startup-ab.tsv")"
[ "$startup_order" = "$(printf '1:1:baseline\n1:2:candidate\n2:1:candidate\n2:2:baseline')" ] \
    || fail "startup A/B did not alternate arm order"
[ "$(awk -F '\t' 'NR > 1 && $5 == "pass" && $6 == "game-ready" { n++ } END { print n+0 }' \
    "$TEST_DIR/startup-ab/startup-ab.tsv")" -eq 4 ] \
    || fail "startup A/B rows did not pass"
grep -q $'surface_error_count\tandroid_process_ms\tinstall_recovery_ms' \
    "$TEST_DIR/startup-ab/startup-ab.tsv" \
    || fail "startup A/B rows omitted per-stage evidence"
grep -q $'^1\tbaseline\t2\tlaunch_to_game_ready\t' \
    "$TEST_DIR/startup-ab/startup-summary.tsv" \
    || fail "startup A/B omitted baseline percentile summary"
grep -q $'^1\tcandidate\t2\tlaunch_to_game_ready\t' \
    "$TEST_DIR/startup-ab/startup-summary.tsv" \
    || fail "startup A/B omitted candidate percentile summary"
grep -q $'^1\tlaunch_to_game_ready\t' \
    "$TEST_DIR/startup-ab/startup-comparison.tsv" \
    || fail "startup A/B omitted comparison gate summary"
grep -q $'^1\tcandidate\tlauncher_creation\t2\t10\t10\t10\t10$' \
    "$TEST_DIR/startup-ab/startup-stage-summary.tsv" \
    || fail "startup A/B omitted candidate stage percentiles"
[ "$(grep -Ec '(^|[[:space:]])install[[:space:]]+-r[[:space:]]+-d' "$COMMAND_LOG")" -eq 5 ] \
    || fail "startup A/B did not install each arm and restore candidate"
[ "$(cat "$INSTALLED_STATE")" = "candidate.apk" ] \
    || fail "startup A/B did not leave the candidate installed"

COMMAND_LOG="$TEST_DIR/startup-ab-resume.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_INSTALLED_APK_STATE="$INSTALLED_STATE" \
    FAKE_PACKAGE_INSTALLED=1 FAKE_DEVICE_LOCKED=0 \
    bash "$TOOL_DIR/run-startup-ab.sh" \
        --baseline-apk "$TEST_DIR/baseline.apk" \
        --candidate-apk "$TEST_DIR/candidate.apk" \
        --serial fake-serial \
        --output "$TEST_DIR/startup-ab" \
        --pairs 3 \
        --resume \
        --play-x 10 --play-y 20 \
        --timeout 2 \
        --thermal-wait-seconds 0 \
        --aapt2 "$SCRIPT_DIR/fake-aapt2.sh" \
        --apksigner "$SCRIPT_DIR/fake-apksigner.sh" \
        --adb "$STABILITY_FAKE_ADB" \
        --allow-device-actions \
        --allow-apk-installs >/dev/null
[ "$(awk 'END { print NR-1 }' "$TEST_DIR/startup-ab/startup-ab.tsv")" -eq 6 ] \
    || fail "startup A/B resume did not retain and extend valid arms"
grep -q $'^1\tcandidate\t3\tlaunch_to_game_ready\t' \
    "$TEST_DIR/startup-ab/startup-summary.tsv" \
    || fail "startup A/B resume did not refresh summaries"
[ "$(grep -Ec '(^|[[:space:]])install[[:space:]]+-r[[:space:]]+-d' "$COMMAND_LOG")" -eq 3 ] \
    || fail "startup A/B resume reran already completed arms"

COMMAND_LOG="$TEST_DIR/startup-signer-mismatch.commands"
set +e
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_SIGNER_MISMATCH=1 \
    FAKE_PACKAGE_INSTALLED=1 FAKE_DEVICE_LOCKED=0 \
    bash "$TOOL_DIR/run-startup-ab.sh" \
        --baseline-apk "$TEST_DIR/baseline.apk" \
        --candidate-apk "$TEST_DIR/candidate.apk" \
        --serial fake-serial \
        --output "$TEST_DIR/startup-signer-mismatch" \
        --pairs 1 \
        --play-x 10 --play-y 20 \
        --aapt2 "$SCRIPT_DIR/fake-aapt2.sh" \
        --apksigner "$SCRIPT_DIR/fake-apksigner.sh" \
        --adb "$STABILITY_FAKE_ADB" \
        --allow-device-actions \
        --allow-apk-installs >/dev/null 2>&1
startup_signer_status=$?
set -e
[ "$startup_signer_status" -eq 1 ] \
    || fail "startup signer mismatch returned $startup_signer_status"
[ ! -e "$TEST_DIR/startup-signer-mismatch" ] \
    || fail "startup signer mismatch wrote output"
if [ -e "$COMMAND_LOG" ] \
    && grep -Eq '(^|[[:space:]])install[[:space:]]|force-stop|logcat[[:space:]]+-c' "$COMMAND_LOG"; then
    fail "startup signer mismatch allowed device mutation"
fi

COMMAND_LOG="$TEST_DIR/startup-locked.commands"
set +e
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_PACKAGE_INSTALLED=1 FAKE_DEVICE_LOCKED=1 \
    bash "$TOOL_DIR/run-startup-ab.sh" \
        --baseline-apk "$TEST_DIR/baseline.apk" \
        --candidate-apk "$TEST_DIR/candidate.apk" \
        --serial fake-serial \
        --output "$TEST_DIR/startup-locked" \
        --pairs 1 \
        --play-x 10 --play-y 20 \
        --aapt2 "$SCRIPT_DIR/fake-aapt2.sh" \
        --apksigner "$SCRIPT_DIR/fake-apksigner.sh" \
        --adb "$STABILITY_FAKE_ADB" \
        --allow-device-actions \
        --allow-apk-installs >/dev/null 2>&1
startup_locked_status=$?
set -e
[ "$startup_locked_status" -eq 4 ] \
    || fail "startup locked-device gate returned $startup_locked_status"
[ ! -e "$TEST_DIR/startup-locked" ] || fail "startup locked-device gate wrote output"
if grep -Eq '(^|[[:space:]])install[[:space:]]|force-stop|logcat[[:space:]]+-c' "$COMMAND_LOG"; then
    fail "startup locked-device gate allowed device mutation"
fi

COMMAND_LOG="$TEST_DIR/startup-hot-start.commands"
set +e
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_PACKAGE_INSTALLED=1 FAKE_DEVICE_LOCKED=0 \
    FAKE_THERMAL_STATUS=2 \
    bash "$TOOL_DIR/run-startup-ab.sh" \
        --baseline-apk "$TEST_DIR/baseline.apk" \
        --candidate-apk "$TEST_DIR/candidate.apk" \
        --serial fake-serial \
        --output "$TEST_DIR/startup-hot-start" \
        --pairs 1 \
        --play-x 10 --play-y 20 \
        --max-thermal-status 2 \
        --max-start-thermal-status 1 \
        --thermal-wait-seconds 0 \
        --aapt2 "$SCRIPT_DIR/fake-aapt2.sh" \
        --apksigner "$SCRIPT_DIR/fake-apksigner.sh" \
        --adb "$STABILITY_FAKE_ADB" \
        --allow-device-actions \
        --allow-apk-installs >/dev/null 2>&1
startup_hot_start_status=$?
set -e
[ "$startup_hot_start_status" -eq 8 ] \
    || fail "startup cooler admission gate returned $startup_hot_start_status"
[ ! -e "$TEST_DIR/startup-hot-start" ] \
    || fail "startup cooler admission gate wrote output"
if grep -Eq '(^|[[:space:]])install[[:space:]]|force-stop|logcat[[:space:]]+-c' \
    "$COMMAND_LOG"; then
    fail "startup cooler admission gate allowed device mutation"
fi

COMMAND_LOG="$TEST_DIR/startup-restore.commands"
INSTALLED_STATE="$TEST_DIR/startup-restore.installed"
set +e
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_INSTALLED_APK_STATE="$INSTALLED_STATE" \
    FAKE_FAIL_WHEN_INSTALLED=candidate.apk FAKE_PACKAGE_INSTALLED=1 \
    FAKE_DEVICE_LOCKED=0 \
    bash "$TOOL_DIR/run-startup-ab.sh" \
        --baseline-apk "$TEST_DIR/baseline.apk" \
        --candidate-apk "$TEST_DIR/candidate.apk" \
        --serial fake-serial \
        --output "$TEST_DIR/startup-restore" \
        --pairs 1 \
        --play-x 10 --play-y 20 \
        --timeout 1 \
        --thermal-wait-seconds 0 \
        --aapt2 "$SCRIPT_DIR/fake-aapt2.sh" \
        --apksigner "$SCRIPT_DIR/fake-apksigner.sh" \
        --adb "$STABILITY_FAKE_ADB" \
        --allow-device-actions \
        --allow-apk-installs >/dev/null 2>&1
startup_restore_status=$?
set -e
[ "$startup_restore_status" -eq 7 ] \
    || fail "startup failed-capture status was $startup_restore_status"
[ "$(cat "$INSTALLED_STATE")" = "candidate.apk" ] \
    || fail "startup failure did not restore candidate"
[ "$(grep -Ec 'install[[:space:]]+-r[[:space:]]+-d.*candidate.apk' "$COMMAND_LOG")" -ge 2 ] \
    || fail "startup failure did not attempt final candidate restoration"
[ ! -e "$TEST_DIR/startup-restore/startup-summary.tsv" ] \
    || fail "partial startup A/B emitted a passing summary"

COMMAND_LOG="$TEST_DIR/startup-invalid-resume.commands"
set +e
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_INSTALLED_APK_STATE="$INSTALLED_STATE" \
    FAKE_PACKAGE_INSTALLED=1 FAKE_DEVICE_LOCKED=0 \
    bash "$TOOL_DIR/run-startup-ab.sh" \
        --baseline-apk "$TEST_DIR/baseline.apk" \
        --candidate-apk "$TEST_DIR/candidate.apk" \
        --serial fake-serial \
        --output "$TEST_DIR/startup-restore" \
        --pairs 2 \
        --resume \
        --play-x 10 --play-y 20 \
        --timeout 1 \
        --thermal-wait-seconds 0 \
        --aapt2 "$SCRIPT_DIR/fake-aapt2.sh" \
        --apksigner "$SCRIPT_DIR/fake-apksigner.sh" \
        --adb "$STABILITY_FAKE_ADB" \
        --allow-device-actions \
        --allow-apk-installs >/dev/null 2>&1
startup_invalid_resume_status=$?
set -e
[ "$startup_invalid_resume_status" -eq 1 ] \
    || fail "invalid startup resume returned $startup_invalid_resume_status"
if grep -Eq '(^|[[:space:]])install[[:space:]]|force-stop|logcat[[:space:]]+-c' \
    "$COMMAND_LOG"; then
    fail "invalid startup resume allowed device mutation"
fi

grep -q 'Launcher ready for PLAY' "$TOOL_DIR/run-frame-capture.sh" \
    || fail "performance capture does not wait for the truthful PLAY-ready boundary"
if grep -q 's/.*Launcher UI displayed.*/launcher-ready' "$TOOL_DIR/run-frame-capture.sh"; then
    fail "performance capture still treats initial launcher rendering as PLAY readiness"
fi
grep -q -- '--locate-game-continue' "$TOOL_DIR/run-frame-capture.sh" \
    || fail "performance capture lacks safe dynamic Continue location"
grep -q 'game_continue_center_normalized' \
    "$TOOL_DIR/../device-stability/audit-screenshot.swift" \
    || fail "screenshot audit lacks content-free Continue coordinates"

set +e
bash "$TOOL_DIR/run-frame-capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/no-action" \
    --mode game-120 >/dev/null 2>&1
action_status=$?
set -e
[ "$action_status" -eq 1 ] || fail "device-action gate returned $action_status"
[ ! -e "$TEST_DIR/no-action" ] || fail "device-action gate wrote output"

COMMAND_LOG="$TEST_DIR/low-battery.commands"
set +e
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_BATTERY_LEVEL=1 \
    bash "$TOOL_DIR/run-frame-capture.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/low-battery" \
        --mode game-120 \
        --allow-device-actions >/dev/null 2>&1
battery_status=$?
set -e
[ "$battery_status" -eq 5 ] || fail "battery gate returned $battery_status"
[ ! -e "$TEST_DIR/low-battery" ] || fail "battery gate wrote output"
if grep -Eq 'force-stop|am[[:space:]]+start|logcat[[:space:]]+-c' "$COMMAND_LOG"; then
    fail "battery gate allowed a mutating device command"
fi

COMMAND_LOG="$TEST_DIR/high-thermal.commands"
set +e
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_THERMAL_CMD_UNSUPPORTED=1 \
    FAKE_THERMAL_STATUS=3 \
    bash "$TOOL_DIR/run-frame-capture.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/high-thermal" \
        --mode game-120 \
        --max-thermal-status 2 \
        --allow-device-actions >/dev/null 2>&1
thermal_status=$?
set -e
[ "$thermal_status" -eq 8 ] || fail "thermal gate returned $thermal_status"
[ ! -e "$TEST_DIR/high-thermal" ] || fail "thermal gate wrote output"
if grep -Eq 'force-stop|am[[:space:]]+start|logcat[[:space:]]+-c' "$COMMAND_LOG"; then
    fail "thermal gate allowed a mutating device command"
fi

set +e
FAKE_VERSION_NAME=0.4.2 bash "$TOOL_DIR/run-frame-capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/release" \
    --mode game-120 \
    --allow-device-actions >/dev/null 2>&1
release_status=$?
set -e
[ "$release_status" -eq 6 ] || fail "release probe gate returned $release_status"
[ ! -e "$TEST_DIR/release" ] || fail "release probe gate wrote output"

COMMAND_LOG="$TEST_DIR/success.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" bash "$TOOL_DIR/run-frame-capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/success" \
    --mode game-120 \
    --timeout 2 \
    --allow-device-actions >/dev/null

grep -q $'^1\tgame-120\tgameplay-interactive\t7000\t120001' \
    "$TEST_DIR/success/summary.tsv" || fail "summary was not parsed"
grep -q $'^1\t250\t52000\t12\t3\t2\t1\t0$' \
    "$TEST_DIR/success/spikes.tsv" || fail "spike was not parsed"
grep -Eq $'^1\t[0-9]+\t456789$' "$TEST_DIR/success/rss.tsv" \
    || fail "RSS sample was not sanitized"
grep -q $'^1\ton\t0\t0$' "$TEST_DIR/success/instrumentation.tsv" \
    || fail "default telemetry-persistence state was not recorded"
grep -q $'^1\t80\t80\t295\t295\t0\t0\t-\t-\t0$' "$TEST_DIR/success/context.tsv" \
    || fail "device context was not captured"
[ "$(grep -c 'force-stop' "$COMMAND_LOG")" -eq 2 ] \
    || fail "successful capture did not stop its device session during cleanup"

COMMAND_LOG="$TEST_DIR/telemetry-off.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" bash "$TOOL_DIR/run-frame-capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/telemetry-off" \
    --mode game-120 \
    --startup-telemetry-persistence off \
    --timeout 2 \
    --allow-device-actions >/dev/null
grep -q $'^1\toff\t0\t0$' "$TEST_DIR/telemetry-off/instrumentation.tsv" \
    || fail "disabled telemetry-persistence state was not recorded"
grep -Eq 'debug_startup_telemetry_persistence[[:space:]]+off' "$COMMAND_LOG" \
    || fail "telemetry-persistence mode was not passed to the debug Activity"

FAKE_FRAME_MODE=game-baseline-120 bash "$TOOL_DIR/run-frame-capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/baseline" \
    --mode game-baseline-120 \
    --timeout 2 \
    --allow-device-actions >/dev/null
grep -q $'^1\tgame-baseline-120\tgameplay-interactive\t7000\t120001' \
    "$TEST_DIR/baseline/summary.tsv" || fail "same-APK baseline was not parsed"

FAKE_FRAME_MODE=game-baseline-safe-120 bash "$TOOL_DIR/run-frame-capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/baseline-safe" \
    --mode game-baseline-safe-120 \
    --resume-x 30 \
    --resume-y 40 \
    --allow-save-fixture \
    --timeout 2 \
    --allow-device-actions >/dev/null
grep -q $'^1\tgame-baseline-safe-120\tgameplay-interactive\t7000\t120001' \
    "$TEST_DIR/baseline-safe/summary.tsv" \
    || fail "same-APK Safe Mode baseline was not parsed"

FAKE_FRAME_MODE=game-baseline-partition-120 bash "$TOOL_DIR/run-frame-capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/baseline-partition" \
    --mode game-baseline-partition-120 \
    --mod-partition 1/4 \
    --resume-x 30 \
    --resume-y 40 \
    --allow-save-fixture \
    --timeout 2 \
    --allow-device-actions >/dev/null
grep -q $'^1\tgame-baseline-partition-120\tgameplay-interactive\t7000\t120001' \
    "$TEST_DIR/baseline-partition/summary.tsv" \
    || fail "same-APK partition baseline was not parsed"

COMMAND_LOG="$TEST_DIR/quickrestart-probe.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" \
    FAKE_FRAME_MODE=game-quickrestart-baseline-partition-120 \
    FAKE_QR_METHOD_PROBE=1 bash "$TOOL_DIR/run-frame-capture.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/quickrestart-probe" \
        --mode game-quickrestart-baseline-partition-120 \
        --mod-partition 1/4 \
        --resume-x 30 \
        --resume-y 40 \
        --quick-restart-method-probe \
        --allow-save-fixture \
        --timeout 2 \
        --allow-device-actions >/dev/null
grep -q $'^1\tgame-quickrestart-baseline-partition-120\tgameplay-interactive\t7000\t120001' \
    "$TEST_DIR/quickrestart-probe/summary.tsv" \
    || fail "Quick Restart same-APK baseline was not parsed"
grep -q $'^1\tgameplay-interactive\t7200\t120000\t7200\t90000\t7200\t7200\t30000$' \
    "$TEST_DIR/quickrestart-probe/quick-restart-probe.tsv" \
    || fail "Quick Restart method counters were not sanitized"
grep -q $'^1\tgameplay-interactive\t0\t0\t0\t0\t0$' \
    "$TEST_DIR/quickrestart-probe/quick-restart-behavior.tsv" \
    || fail "Quick Restart behavior counters were not sanitized"
grep -q $'^1\ton\t1\t0$' "$TEST_DIR/quickrestart-probe/instrumentation.tsv" \
    || fail "Quick Restart instrumentation state was not recorded"
grep -Eq 'debug_quick_restart_method_probe[[:space:]]+1' "$COMMAND_LOG" \
    || fail "Quick Restart method probe was not armed through the debug Activity"

COMMAND_LOG="$TEST_DIR/quickrestart-hold.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" \
    FAKE_FRAME_MODE=game-quickrestart-partition-120 \
    FAKE_QR_METHOD_PROBE=1 FAKE_QR_INPUT_ENABLE=1 FAKE_QR_INPUT_DISABLE=1 \
    FAKE_QR_VISIBLE_FRAMES=100 FAKE_QR_RESTART_CALLS=1 \
    bash "$TOOL_DIR/run-frame-capture.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/quickrestart-hold" \
        --mode game-quickrestart-partition-120 \
        --mod-partition 1/4 \
        --resume-x 30 \
        --resume-y 40 \
        --interaction-script quickrestart-hold \
        --quick-restart-method-probe \
        --allow-save-fixture \
        --timeout 2 \
        --allow-device-actions >/dev/null
grep -q $'^1\tgameplay-interactive\t1\t1\t100\t1\t0$' \
    "$TEST_DIR/quickrestart-hold/quick-restart-behavior.tsv" \
    || fail "Quick Restart hold behavior was not sanitized"
grep -Eq 'input[[:space:]]+keyevent[[:space:]]+--duration[[:space:]]+2500[[:space:]]+46' \
    "$COMMAND_LOG" || fail "Quick Restart hold did not use the fixed key duration"
grep -Eq 'input[[:space:]]+swipe[[:space:]]+30[[:space:]]+40[[:space:]]+30[[:space:]]+40[[:space:]]+200' \
    "$COMMAND_LOG" || fail "fixture resume did not establish touch focus"

COMMAND_LOG="$TEST_DIR/quickrestart-pause.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" \
    FAKE_FRAME_MODE=game-quickrestart-partition-120 \
    FAKE_QR_METHOD_PROBE=1 FAKE_QR_RESTART_CALLS=1 FAKE_QR_PAUSE_CALLS=1 \
    bash "$TOOL_DIR/run-frame-capture.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/quickrestart-pause" \
        --mode game-quickrestart-partition-120 \
        --mod-partition 1/4 \
        --resume-x 30 \
        --resume-y 40 \
        --interaction-script quickrestart-pause \
        --pause-menu-x 50 \
        --pause-menu-y 60 \
        --pause-restart-x 70 \
        --pause-restart-y 80 \
        --quick-restart-method-probe \
        --allow-save-fixture \
        --timeout 2 \
        --allow-device-actions >/dev/null
grep -q $'^1\tgameplay-interactive\t0\t0\t0\t1\t1$' \
    "$TEST_DIR/quickrestart-pause/quick-restart-behavior.tsv" \
    || fail "Quick Restart pause-button behavior was not sanitized"
grep -Eq 'input[[:space:]]+tap[[:space:]]+50[[:space:]]+60' "$COMMAND_LOG" \
    || fail "Quick Restart pause-button test did not open the pause menu"
grep -Eq 'input[[:space:]]+tap[[:space:]]+70[[:space:]]+80' "$COMMAND_LOG" \
    || fail "Quick Restart pause-button test did not use the fixed button coordinate"

COMMAND_LOG="$TEST_DIR/mod-load-probe.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_FRAME_MODE=game-menu-60 \
    FAKE_MOD_LOAD_PROBE=1 bash "$TOOL_DIR/run-frame-capture.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/mod-load-probe" \
        --mode game-menu-60 \
        --mod-load-probe \
        --timeout 2 \
        --allow-device-actions >/dev/null
grep -q $'^1\t1\t13000\t7000\t5000\t1\t1\t1$' \
    "$TEST_DIR/mod-load-probe/mod-load-items.tsv" \
    || fail "anonymous mod-load item timing was not sanitized"
grep -q $'^1\tinitializer\t1\t1\t7000\t1$' \
    "$TEST_DIR/mod-load-probe/mod-load-steps.tsv" \
    || fail "anonymous initializer timing was not sanitized"
grep -q $'^1\tpatchall\t1\t1\t5000\t1$' \
    "$TEST_DIR/mod-load-probe/mod-load-steps.tsv" \
    || fail "anonymous PatchAll timing was not sanitized"
grep -q $'^1\ton\t0\t1$' "$TEST_DIR/mod-load-probe/instrumentation.tsv" \
    || fail "mod-load instrumentation state was not recorded"
grep -Eq 'debug_mod_load_probe[[:space:]]+1' "$COMMAND_LOG" \
    || fail "mod-load probe was not armed through the debug Activity"

FAKE_FRAME_MODE=game-safe-300 bash "$TOOL_DIR/run-frame-capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/shader-guardrail" \
    --mode game-safe-300 \
    --resume-x 30 \
    --resume-y 40 \
    --allow-save-fixture \
    --timeout 2 \
    --allow-device-actions >/dev/null
grep -q $'^1\tgame-safe-300\tgameplay-interactive\t17500\t300001' \
    "$TEST_DIR/shader-guardrail/summary.tsv" \
    || fail "five-minute shader guardrail capture was not parsed"

COMMAND_LOG="$TEST_DIR/deck-cycle.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_FRAME_MODE=game-safe-120 \
    bash "$TOOL_DIR/run-frame-capture.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/deck-cycle" \
        --mode game-safe-120 \
        --resume-x 30 \
        --resume-y 40 \
        --interaction-script deck-cycle \
        --deck-x 50 \
        --deck-y 60 \
        --allow-save-fixture \
        --timeout 2 \
        --allow-device-actions >/dev/null
[ "$(grep -Ec 'input[[:space:]]+tap[[:space:]]+50[[:space:]]+60' "$COMMAND_LOG")" -eq 5 ] \
    || fail "deck cycle did not execute five deterministic opens"
[ "$(grep -Ec 'input[[:space:]]+keyevent[[:space:]]+4' "$COMMAND_LOG")" -eq 5 ] \
    || fail "deck cycle did not execute five deterministic closes"

COMMAND_LOG="$TEST_DIR/map-open.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_FRAME_MODE=game-safe-120 \
    FAKE_INTERACTION_PROBE=map-open bash "$TOOL_DIR/run-frame-capture.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/map-open" \
        --mode game-safe-120 \
        --resume-x 30 \
        --resume-y 40 \
        --interaction-script map-open \
        --map-x 70 \
        --map-y 80 \
        --allow-save-fixture \
        --timeout 2 \
        --allow-device-actions >/dev/null
grep -q $'^1\tmap-open\t60\t16690\t18000\t80000\t80000\t1\t0$' \
    "$TEST_DIR/map-open/interactions.tsv" \
    || fail "map-open interaction timing was not sanitized"
grep -q $'^1\t900000$' "$TEST_DIR/map-open/covered-first-combat.tsv" \
    || fail "covered first-combat timing was not sanitized"
[ "$(grep -Ec 'input[[:space:]]+tap[[:space:]]+70[[:space:]]+80' "$COMMAND_LOG")" -eq 1 ] \
    || fail "map-open did not execute one deterministic open"
[ "$(grep -Ec 'input[[:space:]]+keyevent[[:space:]]+4' "$COMMAND_LOG")" -eq 1 ] \
    || fail "map-open did not execute one deterministic close"

COMMAND_LOG="$TEST_DIR/deck-cache-mutation.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_FRAME_MODE=game-safe-120 \
    FAKE_DECK_CACHE_PROOF=1 bash "$TOOL_DIR/run-frame-capture.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/deck-cache-mutation" \
        --mode game-safe-120 \
        --resume-x 30 \
        --resume-y 40 \
        --deck-cache-mutation-proof \
        --allow-save-fixture \
        --timeout 2 \
        --allow-device-actions >/dev/null
grep -q $'^1\t1\t1\t1\t1\t1\t0\t1$' \
    "$TEST_DIR/deck-cache-mutation/deck-cache-mutation.tsv" \
    || fail "reversible deck cache mutation proof was not sanitized"
grep -Eq 'debug_deck_cache_mutation_probe[[:space:]]+1' "$COMMAND_LOG" \
    || fail "deck cache mutation proof was not armed through the debug Activity"

FAKE_FRAME_MODE=game-partition-120 bash "$TOOL_DIR/run-frame-capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/partition" \
    --mode game-partition-120 \
    --mod-partition 1/4 \
    --resume-x 30 \
    --resume-y 40 \
    --allow-save-fixture \
    --timeout 2 \
    --allow-device-actions >/dev/null
grep -q $'^1\t80\t80\t295\t295\t0\t0\t1\t4\t0$' \
    "$TEST_DIR/partition/context.tsv" || fail "numeric mod partition was not recorded"

set +e
bash "$TOOL_DIR/run-frame-capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/unapproved-save-fixture" \
    --mode game-120 \
    --resume-x 30 \
    --resume-y 40 \
    --allow-device-actions >/dev/null 2>&1
save_fixture_status=$?
set -e
[ "$save_fixture_status" -eq 1 ] || fail "save-fixture gate returned $save_fixture_status"
[ ! -e "$TEST_DIR/unapproved-save-fixture" ] \
    || fail "save-fixture gate wrote output"

FAKE_FRAME_MODE=game-menu-partition-60 FAKE_MOD_LOAD_ERROR=1 \
    bash "$TOOL_DIR/run-frame-capture.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/menu-load-error" \
        --mode game-menu-partition-60 \
        --mod-partition 1/4 \
        --timeout 2 \
        --allow-device-actions >/dev/null
grep -q $'^1\t80\t80\t295\t295\t0\t0\t1\t4\t1$' \
    "$TEST_DIR/menu-load-error/context.tsv" || fail "mod load error was not classified"

set +e
FAKE_FRAME_MODE=all-menu bash "$TOOL_DIR/run-mod-jank-workflow.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/workflow" \
    --scenarios full,safe,1/4 \
    --runs 1 \
    --play-x 10 \
    --play-y 20 \
    --timeout 2 \
    --allow-device-actions >/dev/null
workflow_status=$?
set -e
[ "$workflow_status" -eq 0 ] || fail "workflow returned $workflow_status"
[ "$(wc -l <"$TEST_DIR/workflow/workflow.tsv" | tr -d ' ')" -eq 4 ] \
    || fail "workflow did not record all scenarios"
grep -q $'^2\tmenu\tpartition-1-of-4\t1\t4\t1\tpass\t3500\t60001' \
    "$TEST_DIR/workflow/workflow.tsv" || fail "workflow partition result was not recorded"

FAKE_FRAME_MODE=all-game bash "$TOOL_DIR/run-mod-jank-workflow.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/combat-workflow" \
    --phase combat \
    --scenarios baseline,optimized,safe,1/4 \
    --runs 1 \
    --play-x 10 \
    --play-y 20 \
    --resume-x 30 \
    --resume-y 40 \
    --timeout 2 \
    --allow-save-fixture \
    --allow-device-actions >/dev/null
grep -q $'^2\tcombat\tpartition-1-of-4\t1\t4\t1\tpass\t7000\t120001' \
    "$TEST_DIR/combat-workflow/workflow.tsv" \
    || fail "combat workflow result was not recorded"
grep -q $'^2\tcombat\tbaseline\t-\t-\t1\tpass\t7000\t120001' \
    "$TEST_DIR/combat-workflow/workflow.tsv" \
    || fail "same-APK baseline workflow result was not recorded"
grep -q $'^2\tcombat\toptimized\t-\t-\t1\tpass\t7000\t120001' \
    "$TEST_DIR/combat-workflow/workflow.tsv" \
    || fail "optimized workflow result was not recorded"

FAKE_FRAME_MODE=all-game bash "$TOOL_DIR/run-mod-jank-workflow.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/paired-partition-workflow" \
    --phase combat \
    --scenarios baseline-1/4,optimized-1/4 \
    --runs 1 \
    --play-x 10 \
    --play-y 20 \
    --resume-x 30 \
    --resume-y 40 \
    --timeout 2 \
    --allow-save-fixture \
    --allow-device-actions >/dev/null
grep -q $'^2\tcombat\tbaseline-partition-1-of-4\t1\t4\t1\tpass\t7000\t120001' \
    "$TEST_DIR/paired-partition-workflow/workflow.tsv" \
    || fail "paired partition baseline was not recorded"
grep -q $'^2\tcombat\toptimized-partition-1-of-4\t1\t4\t1\tpass\t7000\t120001' \
    "$TEST_DIR/paired-partition-workflow/workflow.tsv" \
    || fail "paired partition optimization was not recorded"

FAKE_FRAME_MODE=all-quickrestart bash "$TOOL_DIR/run-mod-jank-workflow.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/quickrestart-workflow" \
    --phase combat \
    --scenarios quickrestart-baseline-1/4,quickrestart-optimized-1/4 \
    --runs 1 \
    --play-x 10 \
    --play-y 20 \
    --resume-x 30 \
    --resume-y 40 \
    --timeout 2 \
    --allow-save-fixture \
    --allow-device-actions >/dev/null
grep -q $'^2\tcombat\tquickrestart-baseline-1-of-4\t1\t4\t1\tpass\t7000\t120001' \
    "$TEST_DIR/quickrestart-workflow/workflow.tsv" \
    || fail "Quick Restart baseline workflow result was not recorded"
grep -q $'^2\tcombat\tquickrestart-optimized-1-of-4\t1\t4\t1\tpass\t7000\t120001' \
    "$TEST_DIR/quickrestart-workflow/workflow.tsv" \
    || fail "Quick Restart optimized workflow result was not recorded"

FAKE_FRAME_MODE=game-menu-60 FAKE_MOD_LOAD_PROBE=1 \
    bash "$TOOL_DIR/run-mod-jank-workflow.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/mod-load-workflow" \
        --phase menu \
        --scenarios full \
        --runs 1 \
        --play-x 10 \
        --play-y 20 \
        --mod-load-probe \
        --timeout 2 \
        --allow-device-actions >/dev/null
grep -q $'^1\t1\t13000\t7000\t5000\t1\t1\t1$' \
    "$TEST_DIR/mod-load-workflow/full-run-01/mod-load-items.tsv" \
    || fail "workflow did not retain anonymous mod-load attribution"

COMMAND_LOG="$TEST_DIR/safe-paired-workflow.commands"
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_FRAME_MODE=all-game \
    bash "$TOOL_DIR/run-mod-jank-workflow.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/safe-paired-combat-workflow" \
    --phase combat \
    --scenarios baseline-safe,optimized-safe \
    --runs 1 \
    --play-x 10 \
    --play-y 20 \
    --resume-x 30 \
    --resume-y 40 \
    --interaction-script deck-cycle \
    --deck-x 50 \
    --deck-y 60 \
    --timeout 2 \
    --allow-save-fixture \
    --allow-device-actions >/dev/null
grep -q $'^2\tcombat\tbaseline-safe\t-\t-\t1\tpass\t7000\t120001' \
    "$TEST_DIR/safe-paired-combat-workflow/workflow.tsv" \
    || fail "Safe Mode baseline workflow result was not recorded"
grep -q $'^2\tcombat\toptimized-safe\t-\t-\t1\tpass\t7000\t120001' \
    "$TEST_DIR/safe-paired-combat-workflow/workflow.tsv" \
    || fail "Safe Mode optimized workflow result was not recorded"
[ "$(grep -Ec 'input[[:space:]]+tap[[:space:]]+50[[:space:]]+60' "$COMMAND_LOG")" -eq 10 ] \
    || fail "paired workflow did not propagate the deterministic deck cycle"

FAKE_FRAME_MODE=all-game bash "$TOOL_DIR/run-mod-jank-workflow.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/paired-combat-workflow" \
    --phase combat \
    --scenarios baseline,optimized \
    --runs 2 \
    --play-x 10 \
    --play-y 20 \
    --resume-x 30 \
    --resume-y 40 \
    --timeout 2 \
    --allow-save-fixture \
    --allow-device-actions >/dev/null
paired_order="$(awk -F '\t' 'NR > 1 { print $3 ":" $6 }' \
    "$TEST_DIR/paired-combat-workflow/workflow.tsv")"
[ "$paired_order" = "$(printf 'baseline:1\noptimized:1\nbaseline:2\noptimized:2')" ] \
    || fail "paired workflow did not alternate scenarios within each repetition"

COMMAND_LOG="$TEST_DIR/hot-workflow.commands"
set +e
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_THERMAL_CMD_UNSUPPORTED=1 \
    FAKE_THERMAL_STATUS=3 FAKE_FRAME_MODE=all-game \
    bash "$TOOL_DIR/run-mod-jank-workflow.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/hot-workflow" \
        --phase combat \
        --scenarios baseline,optimized \
        --runs 1 \
        --play-x 10 \
        --play-y 20 \
        --resume-x 30 \
        --resume-y 40 \
        --timeout 2 \
        --max-thermal-status 2 \
        --allow-save-fixture \
        --allow-device-actions >/dev/null 2>&1
hot_workflow_status=$?
set -e
[ "$hot_workflow_status" -eq 8 ] \
    || fail "hot workflow did not preserve the thermal gate exit"
[ ! -e "$TEST_DIR/hot-workflow/baseline-run-01" ] \
    || fail "hot workflow created a capture directory"
if grep -Eq 'force-stop|am[[:space:]]+start|logcat[[:space:]]+-c' "$COMMAND_LOG"; then
    fail "hot workflow allowed a mutating device command"
fi

COMMAND_LOG="$TEST_DIR/hot-battery-workflow.commands"
set +e
FAKE_COMMAND_LOG="$COMMAND_LOG" FAKE_BATTERY_TEMPERATURE=400 \
    FAKE_FRAME_MODE=all-game STS2_DEVICE_PERFORMANCE_TEST_FAST=1 \
    bash "$TOOL_DIR/run-mod-jank-workflow.sh" \
        --adb "$SCRIPT_DIR/fake-adb.sh" \
        --serial fake \
        --output "$TEST_DIR/hot-battery-workflow" \
        --phase combat \
        --scenarios baseline,optimized \
        --runs 1 \
        --play-x 10 --play-y 20 --resume-x 30 --resume-y 40 \
        --max-start-battery-deci-c 300 \
        --cool-brightness 10 \
        --thermal-wait-seconds 0 --timeout 2 \
        --allow-save-fixture --allow-device-actions >/dev/null 2>&1
hot_battery_workflow_status=$?
set -e
[ "$hot_battery_workflow_status" -eq 8 ] \
    || fail "hot battery workflow did not preserve the temperature gate exit"
[ ! -e "$TEST_DIR/hot-battery-workflow/baseline-run-01" ] \
    || fail "hot battery workflow created a capture directory"
grep -Eq 'settings[[:space:]]+put[[:space:]]+system[[:space:]]+screen_brightness[[:space:]]+10' \
    "$COMMAND_LOG" || fail "cooling brightness was not applied"
grep -Eq 'settings[[:space:]]+put[[:space:]]+system[[:space:]]+screen_brightness[[:space:]]+1000' \
    "$COMMAND_LOG" || fail "capture brightness was not restored"

set +e
bash "$TOOL_DIR/run-frame-capture.sh" \
    --adb "$SCRIPT_DIR/fake-adb.sh" \
    --serial fake \
    --output "$TEST_DIR/missing-partition" \
    --mode game-partition-120 \
    --allow-device-actions >/dev/null 2>&1
partition_status=$?
set -e
[ "$partition_status" -eq 1 ] || fail "missing mod partition returned $partition_status"
[ ! -e "$TEST_DIR/missing-partition" ] || fail "invalid partition wrote output"

if grep -REn 'private=|fake|/do/not/copy|versionName|pid|serial' \
    "$TEST_DIR/success" "$TEST_DIR/baseline" "$TEST_DIR/baseline-safe" \
    "$TEST_DIR/partition" "$TEST_DIR/quickrestart-probe" "$TEST_DIR/quickrestart-hold" \
    "$TEST_DIR/quickrestart-pause" \
    "$TEST_DIR/mod-load-probe" \
    "$TEST_DIR/map-open" \
    "$TEST_DIR/menu-load-error" "$TEST_DIR/workflow" "$TEST_DIR/combat-workflow" \
    "$TEST_DIR/quickrestart-workflow" "$TEST_DIR/mod-load-workflow" \
    "$TEST_DIR/safe-paired-combat-workflow" \
    "$TEST_DIR/paired-combat-workflow"; then
    fail "sanitized output contains raw or identifying fields"
fi

if grep -Eq '(^|[[:space:]])(install|uninstall|pm[[:space:]]+clear)([[:space:]]|$)' \
    "$TOOL_DIR/run-frame-capture.sh"; then
    fail "performance harness contains an install, uninstall, or app-data clear operation"
fi

if grep -Eq '(^|[[:space:]\"])uninstall([[:space:]\"]|$)|pm[[:space:]]+clear' \
    "$TOOL_DIR/run-startup-ab.sh"; then
    fail "startup A/B contains uninstall or app-data clear"
fi

if grep -REn 'private=|fake|/do/not/copy|versionName|pid=|serial=|\.apk' \
    "$TEST_DIR/startup-ab" "$TEST_DIR/startup-restore"; then
    fail "startup A/B output contains a raw or identifying field"
fi

echo "PASS: device performance harness tests"
