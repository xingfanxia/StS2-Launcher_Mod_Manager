#!/usr/bin/env bash
set -euo pipefail

PACKAGE_NAME="com.game.sts2launcher.modmanager"
ACTIVITY_NAME="com.game.sts2launcher.modmanager/.GodotApp"
DEVICE_SERIAL=""
OUTPUT_FILE=""
SCENARIO=""
ITERATIONS=1
PLAY_X=""
PLAY_Y=""
GAME_CONFIRM_X=""
GAME_CONFIRM_Y=""
GAME_CONFIRM_AFTER=8
LAUNCHER_BOUNDARY="ready"
DEBUG_STAGE_DELAY_SECONDS=0
TIMEOUT_SECONDS=120
MAX_THERMAL_STATUS=2
THERMAL_WAIT_SECONDS=600
ALLOW_DEVICE_ACTIONS=false
ADB_EXECUTABLE="${STS2_ADB_EXECUTABLE:-adb}"

usage() {
    cat <<'EOF'
Usage: run-matrix.sh --serial SERIAL --output FILE --scenario NAME [options]

Run a bounded physical-device stability scenario and write only sanitized TSV
evidence. Raw logcat, UI hierarchy, process IDs, device IDs, account data, mod
names, and paths are never written to the result.

Scenarios:
  cold-start    force-stop, launch, tap PLAY, and wait for game-ready
  cold-start-safe
                same path with the debug-only session Safe Mode override
  home-resume   press HOME, resume the activity, and require PID continuity
  rotate        alternate the two landscape rotations and require PID continuity

Options:
  --iterations N          Number of repetitions (default: 1)
  --play-x X --play-y Y   Required for cold-start
  --game-confirm-x X      Optional one-time game-owned startup dialog button
  --game-confirm-y Y      coordinate, tapped only if game-ready is still absent
  --game-confirm-after N  Seconds to wait before that optional tap (default: 8)
  --launcher-boundary B   ready or ui (default: ready). ui is for paired legacy APKs
  --debug-stage-delay-seconds N
                          Debug APK only: hold game-settings for 1-20 seconds
  --timeout SECONDS       Per-step timeout for cold-start (default: 120)
  --max-thermal-status N  Require Android thermal status at most N (default: 2)
  --thermal-wait-seconds N
                          Wait this long to cool before aborting (default: 600)
  --package NAME          Override the package name
  --activity COMPONENT    Override the launch component
  --adb PATH              adb executable (default: adb)
  --allow-device-actions  Required acknowledgement for force-stop/input/settings
  --help                  Show this help

The rotate scenario restores accelerometer_rotation and user_rotation even when
the run fails. This tool never installs, uninstalls, clears app data, edits app
private files, or changes network state.
EOF
}

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

is_positive_integer() {
    [[ "$1" =~ ^[1-9][0-9]*$ ]]
}

is_nonnegative_integer() {
    [[ "$1" =~ ^[0-9]+$ ]]
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --serial)
            [ "$#" -ge 2 ] || fail "--serial needs a value"
            DEVICE_SERIAL="$2"
            shift 2
            ;;
        --output)
            [ "$#" -ge 2 ] || fail "--output needs a value"
            OUTPUT_FILE="$2"
            shift 2
            ;;
        --scenario)
            [ "$#" -ge 2 ] || fail "--scenario needs a value"
            SCENARIO="$2"
            shift 2
            ;;
        --iterations)
            [ "$#" -ge 2 ] || fail "--iterations needs a value"
            ITERATIONS="$2"
            shift 2
            ;;
        --play-x)
            [ "$#" -ge 2 ] || fail "--play-x needs a value"
            PLAY_X="$2"
            shift 2
            ;;
        --play-y)
            [ "$#" -ge 2 ] || fail "--play-y needs a value"
            PLAY_Y="$2"
            shift 2
            ;;
        --game-confirm-x)
            [ "$#" -ge 2 ] || fail "--game-confirm-x needs a value"
            GAME_CONFIRM_X="$2"
            shift 2
            ;;
        --game-confirm-y)
            [ "$#" -ge 2 ] || fail "--game-confirm-y needs a value"
            GAME_CONFIRM_Y="$2"
            shift 2
            ;;
        --game-confirm-after)
            [ "$#" -ge 2 ] || fail "--game-confirm-after needs a value"
            GAME_CONFIRM_AFTER="$2"
            shift 2
            ;;
        --launcher-boundary)
            [ "$#" -ge 2 ] || fail "--launcher-boundary needs a value"
            LAUNCHER_BOUNDARY="$2"
            shift 2
            ;;
        --debug-stage-delay-seconds)
            [ "$#" -ge 2 ] || fail "--debug-stage-delay-seconds needs a value"
            DEBUG_STAGE_DELAY_SECONDS="$2"
            shift 2
            ;;
        --timeout)
            [ "$#" -ge 2 ] || fail "--timeout needs a value"
            TIMEOUT_SECONDS="$2"
            shift 2
            ;;
        --max-thermal-status)
            [ "$#" -ge 2 ] || fail "--max-thermal-status needs a value"
            MAX_THERMAL_STATUS="$2"
            shift 2
            ;;
        --thermal-wait-seconds)
            [ "$#" -ge 2 ] || fail "--thermal-wait-seconds needs a value"
            THERMAL_WAIT_SECONDS="$2"
            shift 2
            ;;
        --package)
            [ "$#" -ge 2 ] || fail "--package needs a value"
            PACKAGE_NAME="$2"
            shift 2
            ;;
        --activity)
            [ "$#" -ge 2 ] || fail "--activity needs a value"
            ACTIVITY_NAME="$2"
            shift 2
            ;;
        --adb)
            [ "$#" -ge 2 ] || fail "--adb needs a value"
            ADB_EXECUTABLE="$2"
            shift 2
            ;;
        --allow-device-actions)
            ALLOW_DEVICE_ACTIONS=true
            shift
            ;;
        --help)
            usage
            exit 0
            ;;
        *)
            fail "Unknown argument: $1"
            ;;
    esac
done

[ -n "$DEVICE_SERIAL" ] || fail "--serial is required"
[ -n "$OUTPUT_FILE" ] || fail "--output is required"
[[ "$SCENARIO" =~ ^(cold-start|cold-start-safe|home-resume|rotate)$ ]] \
    || fail "unsupported scenario: $SCENARIO"
is_positive_integer "$ITERATIONS" || fail "--iterations must be a positive integer"
is_positive_integer "$TIMEOUT_SECONDS" || fail "--timeout must be a positive integer"
[[ "$MAX_THERMAL_STATUS" =~ ^[0-6]$ ]] \
    || fail "--max-thermal-status must be an integer from 0 to 6"
is_nonnegative_integer "$THERMAL_WAIT_SECONDS" \
    || fail "--thermal-wait-seconds must be a nonnegative integer"
is_positive_integer "$GAME_CONFIRM_AFTER" \
    || fail "--game-confirm-after must be a positive integer"
[[ "$LAUNCHER_BOUNDARY" =~ ^(ready|ui)$ ]] \
    || fail "--launcher-boundary must be ready or ui"
is_nonnegative_integer "$DEBUG_STAGE_DELAY_SECONDS" \
    || fail "--debug-stage-delay-seconds must be a nonnegative integer"
[ "$DEBUG_STAGE_DELAY_SECONDS" -le 20 ] \
    || fail "--debug-stage-delay-seconds must not exceed 20"
[[ "$PACKAGE_NAME" =~ ^[A-Za-z0-9._]+$ ]] || fail "invalid package name"
[[ "$ACTIVITY_NAME" =~ ^[A-Za-z0-9._]+/[A-Za-z0-9._]+$ ]] \
    || fail "invalid activity component"
[ "$ALLOW_DEVICE_ACTIONS" = true ] \
    || fail "--allow-device-actions is required for this mutating device test"
[ ! -e "$OUTPUT_FILE" ] || fail "output already exists: $OUTPUT_FILE"
STAGE_OUTPUT_FILE="${OUTPUT_FILE}.stages.tsv"
[ ! -e "$STAGE_OUTPUT_FILE" ] || fail "output already exists: $STAGE_OUTPUT_FILE"
if [ "$SCENARIO" = "cold-start" ] || [ "$SCENARIO" = "cold-start-safe" ]; then
    is_positive_integer "$PLAY_X" || fail "--play-x is required for cold-start"
    is_positive_integer "$PLAY_Y" || fail "--play-y is required for cold-start"
    if [ -n "$GAME_CONFIRM_X" ] || [ -n "$GAME_CONFIRM_Y" ]; then
        is_positive_integer "$GAME_CONFIRM_X" \
            || fail "--game-confirm-x and --game-confirm-y must be provided together"
        is_positive_integer "$GAME_CONFIRM_Y" \
            || fail "--game-confirm-x and --game-confirm-y must be provided together"
    fi
fi
command -v "$ADB_EXECUTABLE" >/dev/null 2>&1 || fail "adb not found"

ADB=("$ADB_EXECUTABLE" -s "$DEVICE_SERIAL")
"${ADB[@]}" get-state >/dev/null 2>&1 || fail "device is not ready"
ABI_LIST="$("${ADB[@]}" shell getprop ro.product.cpu.abilist 2>/dev/null | tr -d '\r')"
[[ ",$ABI_LIST," == *,arm64-v8a,* ]] || fail "physical ARM64 device required"
QEMU="$("${ADB[@]}" shell getprop ro.kernel.qemu 2>/dev/null | tr -d '\r')"
[ "$QEMU" != "1" ] || fail "physical device required"
"${ADB[@]}" shell pm path "$PACKAGE_NAME" 2>/dev/null | grep -q '^package:' \
    || fail "package is not installed"
if [ "$SCENARIO" = "cold-start-safe" ] || [ "$DEBUG_STAGE_DELAY_SECONDS" -gt 0 ]; then
    PACKAGE_DUMP="$("${ADB[@]}" shell dumpsys package "$PACKAGE_NAME" 2>/dev/null \
        | tr -d '\r')"
    VERSION_NAME="$(printf '%s\n' "$PACKAGE_DUMP" \
        | sed -nE 's/.*versionName=([^[:space:]]+).*/\1/p' | head -n 1)"
    [[ "$VERSION_NAME" == *-debug* ]] \
        || fail "the requested scenario requires an installed debug-probe build"
fi

now_ms() {
    ruby -e 'printf("%.0f\n", Process.clock_gettime(Process::CLOCK_MONOTONIC) * 1000)'
}

read_thermal_status() {
    local status
    status="$("${ADB[@]}" shell cmd thermalservice get-current-thermal-status \
        2>/dev/null | tr -d '\r' \
        | sed -nE 's/^[[:space:]]*([0-6])[[:space:]]*$/\1/p' | head -n 1 || true)"
    if [[ "$status" =~ ^[0-6]$ ]]; then
        printf '%s' "$status"
        return
    fi
    status="$("${ADB[@]}" shell dumpsys thermalservice 2>/dev/null | tr -d '\r' \
        | sed -nE 's/^[[:space:]]*Thermal Status:[[:space:]]*([0-6]).*/\1/p' \
        | head -n 1 || true)"
    printf '%s' "${status:--}"
}

read_battery_temperature() {
    "${ADB[@]}" shell dumpsys battery 2>/dev/null | tr -d '\r' \
        | sed -nE 's/^[[:space:]]*temperature:[[:space:]]*([^[:space:]]+).*/\1/p' \
        | head -n 1
}

wait_for_thermal_gate() {
    local deadline=$(( $(now_ms) + THERMAL_WAIT_SECONDS * 1000 ))
    local announced=false
    local thermal
    while true; do
        thermal="$(read_thermal_status)"
        [[ "$thermal" =~ ^[0-6]$ ]] || {
            echo "ERROR: Android thermal status is unavailable; no device action taken" >&2
            return 9
        }
        if [ "$thermal" -le "$MAX_THERMAL_STATUS" ]; then
            return 0
        fi
        if [ "$announced" = false ]; then
            echo "WAIT: thermal status $thermal exceeds $MAX_THERMAL_STATUS; cooling before next run"
            announced=true
        fi
        if [ "$(now_ms)" -ge "$deadline" ]; then
            echo "ERROR: device did not cool below thermal status $MAX_THERMAL_STATUS" >&2
            return 8
        fi
        sleep 5
    done
}

current_pid() {
    "${ADB[@]}" shell pidof "$PACKAGE_NAME" 2>/dev/null | tr -d '\r' | awk '{print $1}'
}

is_resumed() {
    "${ADB[@]}" shell dumpsys activity activities 2>/dev/null \
        | grep -q "topResumedActivity=.*$PACKAGE_NAME/"
}

EVENT_FILE="${OUTPUT_FILE}.events.tmp"
LOGCAT_CAPTURE_PID=""
MATRIX_OWNS_PROCESS=false

start_safe_log_capture() {
    : >"$EVENT_FILE"
    # Stream through the sanitizer so a busy device cannot wrap early startup
    # tokens out of logcat before the terminal state is reached. No raw line is
    # ever written to disk.
    "${ADB[@]}" logcat -v brief 2>/dev/null \
        > >(LC_ALL=C sed -u -nE \
            -e 's/.*\[StartupRecovery\] attempt=([0-9]+) stage=([a-z0-9-]+).*/attempt=\1 stage=\2/p' \
            -e 's/.*\[StartupRecovery\] healthy stage=([a-z0-9-]+).*/healthy=\1/p' \
            -e 's/.*\[StartupRecovery\] stage=([a-z0-9-]+).*/stage=\1/p' \
            -e 's/.*\[StartupRecovery\] reconciled reason=([A-Z_]+) failureCount=([0-9]+) recoveryPending=(true|false) stage=([a-z0-9-]*).*/exit=\1 failures=\2 recovery=\3 stage=\4/p' \
            -e 's/.*\[Recovery\] session action=([A-Za-z]+).*/recovery_action=\1/p' \
            -e 's/.*(Launcher ready for PLAY).*/launcher-ready/p' \
            -e 's/.*(Launcher UI displayed).*/launcher-ui/p' \
            -e 's/.*(User launched game, proceeding to startup).*/play-accepted/p' \
            -e 's/.*\[StartupPerformance\/NativeSummary\] (v1;[0-9|;]+).*/native_performance=\1/p' \
            -e 's/.*\[StartupPerformance\/Summary\] (v2;[0-9|;]+).*/managed_performance=\1/p' \
            >>"$EVENT_FILE") &
    LOGCAT_CAPTURE_PID=$!
}

stop_safe_log_capture() {
    if [ -n "$LOGCAT_CAPTURE_PID" ]; then
        kill "$LOGCAT_CAPTURE_PID" >/dev/null 2>&1 || true
        wait "$LOGCAT_CAPTURE_PID" >/dev/null 2>&1 || true
        LOGCAT_CAPTURE_PID=""
    fi
}

safe_logs() {
    [ -f "$EVENT_FILE" ] && LC_ALL=C sed -n '1,200p' "$EVENT_FILE"
}

count_pid_log_pattern() {
    local pid="$1"
    local pattern="$2"
    local count
    if [ -z "$pid" ]; then
        printf '0'
        return
    fi
    count="$("${ADB[@]}" logcat --pid "$pid" -d -v brief 2>/dev/null \
        | LC_ALL=C grep -E -c "$pattern" || true)"
    printf '%s' "${count:-0}"
}

wait_for_safe_token() {
    local pattern="$1"
    local timeout="${2:-$TIMEOUT_SECONDS}"
    local deadline=$(( $(now_ms) + timeout * 1000 ))
    while [ "$(now_ms)" -lt "$deadline" ]; do
        if safe_logs | grep -qE "$pattern"; then
            return 0
        fi
        sleep 0.25
    done
    return 1
}

latest_safe_value() {
    local prefix="$1"
    safe_logs | sed -n "s/^$prefix//p" | tail -n 1
}

activate_play_until_accepted() {
    local deadline=$(( $(now_ms) + 10000 ))
    while [ "$(now_ms)" -lt "$deadline" ]; do
        if safe_logs | grep -qE '^play-accepted$'; then
            return 0
        fi
        "${ADB[@]}" shell input tap "$PLAY_X" "$PLAY_Y"
        input_settle_sleep 0.25
        if safe_logs | grep -qE '^play-accepted$'; then
            return 0
        fi
        "${ADB[@]}" shell input swipe "$PLAY_X" "$PLAY_Y" "$PLAY_X" "$PLAY_Y" 200
        "${ADB[@]}" shell input keyevent 66
        "${ADB[@]}" shell input keyevent 66
        "${ADB[@]}" shell input keyevent 66
        input_settle_sleep 0.25
    done
    return 1
}

input_settle_sleep() {
    if [ "${STS2_DEVICE_PERFORMANCE_TEST_FAST:-0}" = "1" ]; then
        return
    fi
    sleep "$1"
}

stage_duration_ms() {
    local encoded="$1"
    local stage="$2"
    if [ -z "$encoded" ]; then
        printf '-'
        return
    fi
    if [[ "$encoded" == v2\;* ]]; then
        printf '%s' "$encoded" | tr ';' '\n' | awk -F '|' -v stage="$stage" '
            $1 == stage && $2 ~ /^[0-9]+$/ { duration=$2 }
            END {
                if (duration ~ /^[0-9]+$/)
                    printf "%d", int(duration / 1000)
                else
                    printf "-"
            }
        '
        return
    fi
    printf '%s' "$encoded" | tr ';' '\n' | awk -F '|' -v stage="$stage" '
        $2 == stage && $3 == 1 { started=$5 }
        $2 == stage && $3 == 4 { ended=$5 }
        END {
            if (started ~ /^[0-9]+$/ && ended ~ /^[0-9]+$/ && ended >= started)
                printf "%d", int((ended - started) / 1000)
            else
                printf "-"
        }
    '
}

OLD_ACCELEROMETER=""
OLD_ROTATION=""
restore_rotation() {
    stop_safe_log_capture
    if [ "$MATRIX_OWNS_PROCESS" = true ]; then
        "${ADB[@]}" shell am force-stop "$PACKAGE_NAME" >/dev/null 2>&1 || true
        MATRIX_OWNS_PROCESS=false
    fi
    if [ -f "$EVENT_FILE" ]; then
        unlink "$EVENT_FILE" >/dev/null 2>&1 || true
    fi
    if [ -n "$OLD_ACCELEROMETER" ]; then
        "${ADB[@]}" shell settings put system accelerometer_rotation \
            "$OLD_ACCELEROMETER" >/dev/null 2>&1 || true
    fi
    if [ -n "$OLD_ROTATION" ]; then
        "${ADB[@]}" shell settings put system user_rotation \
            "$OLD_ROTATION" >/dev/null 2>&1 || true
    fi
}
trap restore_rotation EXIT

if [ "$SCENARIO" = "rotate" ]; then
    OLD_ACCELEROMETER="$("${ADB[@]}" shell settings get system accelerometer_rotation \
        2>/dev/null | tr -d '\r')"
    OLD_ROTATION="$("${ADB[@]}" shell settings get system user_rotation \
        2>/dev/null | tr -d '\r')"
    "${ADB[@]}" shell settings put system accelerometer_rotation 0 >/dev/null
fi

set +e
wait_for_thermal_gate
thermal_gate_status=$?
set -e
[ "$thermal_gate_status" -eq 0 ] || exit "$thermal_gate_status"

mkdir -p "$(dirname "$OUTPUT_FILE")"
printf 'format_version\tscenario\titeration\tstatus\tterminal\tattempt\tstage\tpid_continuity\tprocess_to_launcher_ms\tuser_wait_ms\tplay_to_game_ready_ms\telapsed_ms\tstart_battery_deci_c\tend_battery_deci_c\tstart_thermal_status\tend_thermal_status\tprevious_exit\trecovery_pending\tfatal_count\tanr_count\tlmk_count\tsurface_error_count\n' \
    >"$OUTPUT_FILE"
printf 'format_version\tscenario\titeration\tstatus\tandroid_process_ms\tinstall_recovery_ms\tcache_sync_ms\tassembly_sync_ms\tgodot_bootstrap_ms\tlauncher_creation_ms\tlauncher_ready_ms\trecovery_choice_ms\tuser_wait_ms\tcloud_sync_ms\tshader_warmup_ms\tgame_settings_ms\tgame_startup_ms\tmod_discovery_ms\tmod_load_ms\tgame_ready_ms\n' \
    >"$STAGE_OUTPUT_FILE"

FAILURES=0
for iteration in $(seq 1 "$ITERATIONS"); do
    wait_for_thermal_gate || exit $?
    start_ms="$(now_ms)"
    start_temperature="$(read_battery_temperature)"
    start_thermal="$(read_thermal_status)"
    status="pass"
    terminal=""
    attempt="-"
    stage="-"
    pid_continuity="-"
    previous_exit="none"
    recovery_pending="false"
    observed_pid=""
    process_to_launcher_ms="-"
    user_wait_ms="-"
    play_to_game_ready_ms="-"

    "${ADB[@]}" logcat -c
    start_safe_log_capture

    case "$SCENARIO" in
        cold-start|cold-start-safe)
            "${ADB[@]}" shell am force-stop "$PACKAGE_NAME"
            START_ARGS=(shell am start -W -n "$ACTIVITY_NAME")
            if [ "$SCENARIO" = "cold-start-safe" ]; then
                START_ARGS+=(--es debug_frame_probe game-menu-safe-60)
            fi
            if [ "$DEBUG_STAGE_DELAY_SECONDS" -gt 0 ]; then
                START_ARGS+=(
                    --es debug_startup_stage_delay_seconds "$DEBUG_STAGE_DELAY_SECONDS"
                )
            fi
            "${ADB[@]}" "${START_ARGS[@]}" >/dev/null
            MATRIX_OWNS_PROCESS=true
            launch_pid="$(current_pid)"
            observed_pid="$launch_pid"
            if [ "$LAUNCHER_BOUNDARY" = "ui" ]; then
                launcher_pattern='^launcher-ui$'
            else
                launcher_pattern='^launcher-ready$'
            fi
            if ! wait_for_safe_token "$launcher_pattern"; then
                status="fail"
                terminal="launcher-timeout"
            else
                launcher_ms="$(now_ms)"
                process_to_launcher_ms=$((launcher_ms - start_ms))
                if ! activate_play_until_accepted; then
                    status="fail"
                    terminal="play-not-accepted"
                else
                    play_ms="$(now_ms)"
                    user_wait_ms=$((play_ms - launcher_ms))
                    if [ -n "$GAME_CONFIRM_X" ] \
                        && ! wait_for_safe_token '^healthy=game-ready$' "$GAME_CONFIRM_AFTER"; then
                        "${ADB[@]}" shell input tap "$GAME_CONFIRM_X" "$GAME_CONFIRM_Y"
                    fi
                    if safe_logs | grep -qE '^healthy=game-ready$' \
                        || wait_for_safe_token '^healthy=game-ready$'; then
                        ready_ms="$(now_ms)"
                        play_to_game_ready_ms=$((ready_ms - play_ms))
                        terminal="game-ready"
                    else
                        status="fail"
                        terminal="game-ready-timeout"
                    fi
                fi
            fi
            end_pid="$(current_pid)"
            if [ -n "$launch_pid" ] && [ "$launch_pid" = "$end_pid" ]; then
                pid_continuity="yes"
            else
                pid_continuity="no"
                status="fail"
            fi
            attempt="$(latest_safe_value 'attempt=' | awk '{print $1}')"
            stage="$(latest_safe_value 'healthy=')"
            [ -n "$attempt" ] || attempt="-"
            [ -n "$stage" ] || stage="-"
            ;;
        home-resume)
            before_pid="$(current_pid)"
            observed_pid="$before_pid"
            [ -n "$before_pid" ] || fail "home-resume requires a running process"
            "${ADB[@]}" shell input keyevent KEYCODE_HOME
            sleep 0.5
            "${ADB[@]}" shell am start -W -n "$ACTIVITY_NAME" >/dev/null
            sleep 1
            after_pid="$(current_pid)"
            if [ "$before_pid" = "$after_pid" ] && is_resumed; then
                pid_continuity="yes"
                terminal="resumed"
                stage="resume"
            else
                pid_continuity="no"
                terminal="resume-failed"
                stage="resume"
                status="fail"
            fi
            ;;
        rotate)
            before_pid="$(current_pid)"
            observed_pid="$before_pid"
            [ -n "$before_pid" ] || fail "rotate requires a running process"
            if [ $((iteration % 2)) -eq 1 ]; then
                rotation=1
            else
                rotation=3
            fi
            "${ADB[@]}" shell settings put system user_rotation "$rotation" >/dev/null
            sleep 1
            after_pid="$(current_pid)"
            if [ "$before_pid" = "$after_pid" ] && is_resumed; then
                pid_continuity="yes"
                terminal="configuration-restored"
                stage="rotation"
            else
                pid_continuity="no"
                terminal="configuration-failed"
                stage="rotation"
                status="fail"
            fi
            ;;
    esac

    exit_summary="$(latest_safe_value 'exit=')"
    if [ -n "$exit_summary" ]; then
        previous_exit="$(printf '%s' "$exit_summary" | awk '{print $1}')"
        recovery_pending="$(printf '%s' "$exit_summary" \
            | sed -nE 's/.*recovery=(true|false).*/\1/p')"
        [ -n "$recovery_pending" ] || recovery_pending="false"
    fi
    native_performance="$(latest_safe_value 'native_performance=')"
    managed_performance="$(latest_safe_value 'managed_performance=')"
    stage_status="pass"
    if [ -z "$native_performance" ] || [ -z "$managed_performance" ]; then
        stage_status="unavailable"
    fi

    stop_safe_log_capture
    fatal_count="$(count_pid_log_pattern "$observed_pid" 'FATAL EXCEPTION|Fatal signal')"
    anr_count="$(count_pid_log_pattern "$observed_pid" 'ANR in |Input dispatching timed out')"
    lmk_count="$(count_pid_log_pattern "$observed_pid" 'lowmemorykiller|lmkd')"
    surface_error_count="$(count_pid_log_pattern "$observed_pid" 'QueuePresentKHR|VK_ERROR_SURFACE_LOST_KHR')"
    if [ "$fatal_count" -gt 0 ] || [ "$anr_count" -gt 0 ] || [ "$lmk_count" -gt 0 ]; then
        status="fail"
    fi
    elapsed_ms=$(( $(now_ms) - start_ms ))
    end_temperature="$(read_battery_temperature)"
    end_thermal="$(read_thermal_status)"
    if ! [[ "$end_thermal" =~ ^[0-6]$ ]] \
        || [ "$end_thermal" -gt "$MAX_THERMAL_STATUS" ]; then
        status="thermal-invalid"
    fi

    if { [ "$SCENARIO" = "cold-start" ] || [ "$SCENARIO" = "cold-start-safe" ]; } \
        && [ "$MATRIX_OWNS_PROCESS" = true ]; then
        "${ADB[@]}" shell am force-stop "$PACKAGE_NAME" >/dev/null 2>&1 || true
        MATRIX_OWNS_PROCESS=false
    fi

    printf '2\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
        "$SCENARIO" "$iteration" "$status" "$terminal" "$attempt" "$stage" \
        "$pid_continuity" "$process_to_launcher_ms" "$user_wait_ms" \
        "$play_to_game_ready_ms" "$elapsed_ms" "${start_temperature:--}" \
        "${end_temperature:--}" "$start_thermal" "$end_thermal" \
        "$previous_exit" "$recovery_pending" \
        "$fatal_count" "$anr_count" "$lmk_count" "$surface_error_count" \
        >>"$OUTPUT_FILE"

    stage_values=()
    for stage_id in 1 2 3 4 5; do
        stage_values+=("$(stage_duration_ms "$native_performance" "$stage_id")")
    done
    for stage_id in 6 7 8 9 10 11 12 13 14 15 16; do
        stage_values+=("$(stage_duration_ms "$managed_performance" "$stage_id")")
    done
    printf '2\t%s\t%s\t%s' "$SCENARIO" "$iteration" "$stage_status" \
        >>"$STAGE_OUTPUT_FILE"
    printf '\t%s' "${stage_values[@]}" >>"$STAGE_OUTPUT_FILE"
    printf '\n' >>"$STAGE_OUTPUT_FILE"

    if [ "$status" != "pass" ]; then
        FAILURES=$((FAILURES + 1))
    fi
    echo "$SCENARIO $iteration/$ITERATIONS: $status ($terminal, ${elapsed_ms}ms)"
done

restore_rotation
OLD_ACCELEROMETER=""
OLD_ROTATION=""

if [ "$FAILURES" -gt 0 ]; then
    echo "FAIL: $FAILURES/$ITERATIONS rows failed; sanitized evidence: $OUTPUT_FILE" >&2
    exit 2
fi

echo "PASS: $ITERATIONS/$ITERATIONS rows; sanitized evidence: $OUTPUT_FILE"
