#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
CAPTURE_SCRIPT="$SCRIPT_DIR/run-frame-capture.sh"
DEVICE_SERIAL=""
OUTPUT_ROOT=""
SCENARIOS="full,safe,0/2,1/2"
PHASE="menu"
RUNS=1
PLAY_X=""
PLAY_Y=""
RESUME_X=""
RESUME_Y=""
RESUME_AUTO=false
INTERACTION_SCRIPT="none"
QUICK_RESTART_METHOD_PROBE=false
MOD_LOAD_PROBE=false
DECK_X=""
DECK_Y=""
MAP_X=""
MAP_Y=""
TIMEOUT_SECONDS=180
MIN_BATTERY=15
MAX_THERMAL_STATUS=2
MAX_START_THERMAL_STATUS=""
MAX_START_BATTERY_DECI_C=""
THERMAL_WAIT_SECONDS=600
COOL_SCREEN_BETWEEN_RUNS=false
COOL_BRIGHTNESS=""
ADB_EXECUTABLE="${STS2_ADB_EXECUTABLE:-adb}"
ALLOW_DEVICE_ACTIONS=false
ALLOW_SAVE_FIXTURE=false

usage() {
    cat <<'EOF'
Usage: run-mod-jank-workflow.sh --serial SERIAL --output DIRECTORY [options]

Run a standardized, unattended mod-jank matrix. The default menu phase captures
60 settled seconds without entering a save. The explicit combat phase resumes a
caller-verified sacrificial fixture and captures 120 real-combat seconds.

Options:
  --scenarios LIST        Comma list: full,safe,I/N; combat also accepts
                          baseline,optimized,baseline-safe,optimized-safe,
                          baseline-I/N,optimized-I/N and
                          quickrestart-baseline-I/N,quickrestart-optimized-I/N
  --phase PHASE           menu or combat (default: menu)
  --runs COUNT            Repetitions per scenario (default: 1)
  --play-x X --play-y Y   Reference-device PLAY coordinates (required)
  --resume-x X --resume-y Y
                          Continue coordinates required by combat phase
  --resume-auto           Locate Continue safely for layouts that move it
  --interaction-script S Fixed combat actions: none, deck-cycle, or map-open
  --quick-restart-method-probe
                          Add exact-mod counters; Quick Restart scenarios only
  --mod-load-probe       Add anonymous per-mod initializer/PatchAll timings
  --deck-x X --deck-y Y   Deck button coordinates required by deck-cycle
  --map-x X --map-y Y     Map button coordinates required by map-open
  --timeout SECONDS       Per-capture timeout (default: 180)
  --min-battery PERCENT   Refuse device mutation below this level (default: 15)
  --max-thermal-status N  Reject captures above Android status N (default: 2)
  --max-start-thermal-status N
                          Require this cooler status before every arm
  --max-start-battery-deci-c N
                          Also require battery temperature at or below N
  --thermal-wait-seconds N
                          Bounded inter-arm cooling wait (default: 600)
  --cool-screen-between-runs
                          Turn off an insecure-keyguard display only while cooling
  --cool-brightness N     Dim only while cooling; restore capture brightness per arm
  --adb PATH              adb executable (default: adb)
  --allow-device-actions  Required acknowledgement for force-stop/launch/input
  --allow-save-fixture    Required in combat phase; acknowledges active-save load
  --help                  Show this help

The output is numeric/sanitized. Mod initializer failures are classified but
no mod name, path, account, device serial, save content, or raw log is stored.
EOF
}

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

is_positive_integer() {
    [[ "$1" =~ ^[1-9][0-9]*$ ]]
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
            OUTPUT_ROOT="$2"
            shift 2
            ;;
        --scenarios)
            [ "$#" -ge 2 ] || fail "--scenarios needs a value"
            SCENARIOS="$2"
            shift 2
            ;;
        --phase)
            [ "$#" -ge 2 ] || fail "--phase needs a value"
            PHASE="$2"
            shift 2
            ;;
        --runs)
            [ "$#" -ge 2 ] || fail "--runs needs a value"
            RUNS="$2"
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
        --resume-x)
            [ "$#" -ge 2 ] || fail "--resume-x needs a value"
            RESUME_X="$2"
            shift 2
            ;;
        --resume-y)
            [ "$#" -ge 2 ] || fail "--resume-y needs a value"
            RESUME_Y="$2"
            shift 2
            ;;
        --resume-auto)
            RESUME_AUTO=true
            shift
            ;;
        --interaction-script)
            [ "$#" -ge 2 ] || fail "--interaction-script needs a value"
            INTERACTION_SCRIPT="$2"
            shift 2
            ;;
        --quick-restart-method-probe)
            QUICK_RESTART_METHOD_PROBE=true
            shift
            ;;
        --mod-load-probe)
            MOD_LOAD_PROBE=true
            shift
            ;;
        --deck-x)
            [ "$#" -ge 2 ] || fail "--deck-x needs a value"
            DECK_X="$2"
            shift 2
            ;;
        --deck-y)
            [ "$#" -ge 2 ] || fail "--deck-y needs a value"
            DECK_Y="$2"
            shift 2
            ;;
        --map-x)
            [ "$#" -ge 2 ] || fail "--map-x needs a value"
            MAP_X="$2"
            shift 2
            ;;
        --map-y)
            [ "$#" -ge 2 ] || fail "--map-y needs a value"
            MAP_Y="$2"
            shift 2
            ;;
        --timeout)
            [ "$#" -ge 2 ] || fail "--timeout needs a value"
            TIMEOUT_SECONDS="$2"
            shift 2
            ;;
        --min-battery)
            [ "$#" -ge 2 ] || fail "--min-battery needs a value"
            MIN_BATTERY="$2"
            shift 2
            ;;
        --max-thermal-status)
            [ "$#" -ge 2 ] || fail "--max-thermal-status needs a value"
            MAX_THERMAL_STATUS="$2"
            shift 2
            ;;
        --max-start-thermal-status)
            [ "$#" -ge 2 ] || fail "--max-start-thermal-status needs a value"
            MAX_START_THERMAL_STATUS="$2"
            shift 2
            ;;
        --max-start-battery-deci-c)
            [ "$#" -ge 2 ] || fail "--max-start-battery-deci-c needs a value"
            MAX_START_BATTERY_DECI_C="$2"
            shift 2
            ;;
        --thermal-wait-seconds)
            [ "$#" -ge 2 ] || fail "--thermal-wait-seconds needs a value"
            THERMAL_WAIT_SECONDS="$2"
            shift 2
            ;;
        --cool-screen-between-runs)
            COOL_SCREEN_BETWEEN_RUNS=true
            shift
            ;;
        --cool-brightness)
            [ "$#" -ge 2 ] || fail "--cool-brightness needs a value"
            COOL_BRIGHTNESS="$2"
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
        --allow-save-fixture)
            ALLOW_SAVE_FIXTURE=true
            shift
            ;;
        --help)
            usage
            exit 0
            ;;
        *)
            fail "unknown argument: $1"
            ;;
    esac
done

[ -n "$DEVICE_SERIAL" ] || fail "--serial is required"
[ -n "$OUTPUT_ROOT" ] || fail "--output is required"
[[ "$PHASE" =~ ^(menu|combat)$ ]] || fail "--phase must be menu or combat"
is_positive_integer "$RUNS" || fail "--runs must be a positive integer"
[ "$RUNS" -le 10 ] || fail "--runs must not exceed 10"
is_positive_integer "$PLAY_X" || fail "--play-x is required"
is_positive_integer "$PLAY_Y" || fail "--play-y is required"
if [ "$PHASE" = "combat" ]; then
    if [ "$RESUME_AUTO" = true ]; then
        [ -z "$RESUME_X" ] && [ -z "$RESUME_Y" ] \
            || fail "--resume-auto cannot be combined with resume coordinates"
    else
        is_positive_integer "$RESUME_X" || fail "combat phase requires --resume-x"
        is_positive_integer "$RESUME_Y" || fail "combat phase requires --resume-y"
    fi
    [ "$ALLOW_SAVE_FIXTURE" = true ] \
        || fail "combat phase requires --allow-save-fixture"
elif [ -n "$RESUME_X" ] || [ -n "$RESUME_Y" ] \
        || [ "$RESUME_AUTO" = true ] || [ "$ALLOW_SAVE_FIXTURE" = true ]; then
    fail "resume/save-fixture options are only valid in combat phase"
fi
case "$INTERACTION_SCRIPT" in
    none)
        [ -z "$DECK_X" ] && [ -z "$DECK_Y" ] \
            && [ -z "$MAP_X" ] && [ -z "$MAP_Y" ] \
            || fail "interaction coordinates require a matching script"
        ;;
    deck-cycle)
        [ "$PHASE" = "combat" ] || fail "deck-cycle is only valid in combat phase"
        is_positive_integer "$DECK_X" \
            || fail "deck-cycle requires --deck-x and --deck-y"
        is_positive_integer "$DECK_Y" \
            || fail "deck-cycle requires --deck-x and --deck-y"
        [ -z "$MAP_X" ] && [ -z "$MAP_Y" ] \
            || fail "map coordinates require --interaction-script map-open"
        ;;
    map-open)
        [ "$PHASE" = "combat" ] || fail "map-open is only valid in combat phase"
        { [ -n "$RESUME_X" ] || [ "$RESUME_AUTO" = true ]; } \
            || fail "map-open requires the caller-verified resume fixture"
        is_positive_integer "$MAP_X" || fail "map-open requires --map-x and --map-y"
        is_positive_integer "$MAP_Y" || fail "map-open requires --map-x and --map-y"
        [ -z "$DECK_X" ] && [ -z "$DECK_Y" ] \
            || fail "deck coordinates require --interaction-script deck-cycle"
        ;;
    *)
        fail "--interaction-script must be none, deck-cycle, or map-open"
        ;;
esac
is_positive_integer "$TIMEOUT_SECONDS" || fail "--timeout must be a positive integer"
is_positive_integer "$MIN_BATTERY" || fail "--min-battery must be a positive integer"
[ "$MIN_BATTERY" -le 100 ] || fail "--min-battery must not exceed 100"
[[ "$MAX_THERMAL_STATUS" =~ ^[0-6]$ ]] \
    || fail "--max-thermal-status must be an integer from 0 to 6"
if [ -z "$MAX_START_THERMAL_STATUS" ]; then
    MAX_START_THERMAL_STATUS="$MAX_THERMAL_STATUS"
fi
[[ "$MAX_START_THERMAL_STATUS" =~ ^[0-6]$ ]] \
    || fail "--max-start-thermal-status must be an integer from 0 to 6"
[ "$MAX_START_THERMAL_STATUS" -le "$MAX_THERMAL_STATUS" ] \
    || fail "--max-start-thermal-status must not exceed --max-thermal-status"
[[ "$THERMAL_WAIT_SECONDS" =~ ^[0-9]+$ ]] \
    || fail "--thermal-wait-seconds must be a nonnegative integer"
if [ -n "$MAX_START_BATTERY_DECI_C" ]; then
    [[ "$MAX_START_BATTERY_DECI_C" =~ ^[1-9][0-9]{1,3}$ ]] \
        || fail "--max-start-battery-deci-c must be a positive deci-C integer"
fi
if [ -n "$COOL_BRIGHTNESS" ]; then
    [[ "$COOL_BRIGHTNESS" =~ ^[0-9]{1,4}$ ]] \
        && [ "$COOL_BRIGHTNESS" -le 4095 ] \
        || fail "--cool-brightness must be an integer from 0 to 4095"
fi
[ "$ALLOW_DEVICE_ACTIONS" = true ] \
    || fail "--allow-device-actions is required for this mutating device test"
[ ! -e "$OUTPUT_ROOT" ] || fail "output already exists: $OUTPUT_ROOT"
[ -x "$CAPTURE_SCRIPT" ] || fail "capture script is not executable"

IFS=',' read -r -a SCENARIO_LIST <<<"$SCENARIOS"
[ "${#SCENARIO_LIST[@]}" -gt 0 ] || fail "--scenarios must not be empty"

mkdir -p "$OUTPUT_ROOT"
WORKFLOW_FILE="$OUTPUT_ROOT/workflow.tsv"
printf 'format_version\tphase\tscenario\tpartition_index\tpartition_count\trun\tstatus\tsamples\telapsed_ms\tbudget_us\tp50_us\tp95_us\tp99_us\tmax_us\tover_2x\tover_3x\tmax_consecutive_2x\tover_50ms\tover_100ms\tover_250ms\tstart_battery_deci_c\tend_battery_deci_c\tstart_thermal_status\tend_thermal_status\tmod_load_error_count\n' >"$WORKFLOW_FILE"

INVALID_RUNS=0
CAPTURE_BRIGHTNESS_MODE=""
CAPTURE_BRIGHTNESS=""
BRIGHTNESS_STATE_ARMED=false

restore_capture_brightness() {
    [ "$BRIGHTNESS_STATE_ARMED" = true ] || return 0
    "$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell settings put system \
        screen_brightness "$CAPTURE_BRIGHTNESS" >/dev/null
    "$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell settings put system \
        screen_brightness_mode "$CAPTURE_BRIGHTNESS_MODE" >/dev/null
}

dim_for_cooling() {
    [ "$BRIGHTNESS_STATE_ARMED" = true ] || return 0
    "$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell settings put system \
        screen_brightness_mode 0 >/dev/null
    "$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell settings put system \
        screen_brightness "$COOL_BRIGHTNESS" >/dev/null
}

cleanup_workflow() {
    restore_capture_brightness || true
}
trap cleanup_workflow EXIT

if [ -n "$COOL_BRIGHTNESS" ]; then
    CAPTURE_BRIGHTNESS_MODE="$("$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell \
        settings get system screen_brightness_mode 2>/dev/null | tr -d '\r')"
    CAPTURE_BRIGHTNESS="$("$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell \
        settings get system screen_brightness 2>/dev/null | tr -d '\r')"
    [[ "$CAPTURE_BRIGHTNESS_MODE" =~ ^[01]$ ]] \
        || fail "capture brightness mode is unavailable"
    [[ "$CAPTURE_BRIGHTNESS" =~ ^[0-9]{1,4}$ ]] \
        || fail "capture brightness is unavailable"
    BRIGHTNESS_STATE_ARMED=true
    dim_for_cooling
fi

read_thermal_status() {
    local status
    status="$("$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell \
        cmd thermalservice get-current-thermal-status 2>/dev/null \
        | tr -d '\r' \
        | sed -nE 's/^[[:space:]]*([0-6])[[:space:]]*$/\1/p' \
        | head -n 1 || true)"
    if [[ "$status" =~ ^[0-6]$ ]]; then
        printf '%s' "$status"
        return
    fi
    "$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell dumpsys thermalservice 2>/dev/null \
        | tr -d '\r' \
        | sed -nE 's/^[[:space:]]*Thermal Status:[[:space:]]*([0-6]).*/\1/p' \
        | head -n 1
}

screen_is_awake() {
    "$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell dumpsys power 2>/dev/null \
        | tr -d '\r' | grep -q 'mWakefulness=Awake'
}

device_is_unlocked() {
    "$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell dumpsys trust 2>/dev/null \
        | tr -d '\r' \
        | grep -Eq '\(current\).*deviceLocked=(0|false)([^0-9A-Za-z]|$)'
}

wait_for_start_thermal() {
    local started now thermal_value battery_deci_c temperature_ready
    started="$(date +%s)"
    dim_for_cooling
    while true; do
        thermal_value="$(read_thermal_status)"
        battery_deci_c="$("$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell \
            dumpsys battery 2>/dev/null | tr -d '\r' \
            | sed -nE 's/^[[:space:]]*temperature:[[:space:]]*([0-9]+).*/\1/p' \
            | head -n 1)"
        temperature_ready=true
        if [ -n "$MAX_START_BATTERY_DECI_C" ]; then
            if ! [[ "$battery_deci_c" =~ ^[0-9]+$ ]] \
                || [ "$battery_deci_c" -gt "$MAX_START_BATTERY_DECI_C" ]; then
                temperature_ready=false
            fi
        fi
        if [[ "$thermal_value" =~ ^[0-6]$ ]] \
            && [ "$thermal_value" -le "$MAX_START_THERMAL_STATUS" ] \
            && [ "$temperature_ready" = true ]; then
            if [ "$COOL_SCREEN_BETWEEN_RUNS" = true ] && ! screen_is_awake; then
                "$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell input keyevent 26 \
                    >/dev/null 2>&1
                sleep 2
            fi
            device_is_unlocked || return 4
            restore_capture_brightness
            return 0
        fi
        now="$(date +%s)"
        if [ $((now - started)) -ge "$THERMAL_WAIT_SECONDS" ]; then
            return 8
        fi
        if [ "$COOL_SCREEN_BETWEEN_RUNS" = true ] && screen_is_awake; then
            "$ADB_EXECUTABLE" -s "$DEVICE_SERIAL" shell input keyevent 26 \
                >/dev/null 2>&1
        fi
        if [ "${STS2_DEVICE_PERFORMANCE_TEST_FAST:-0}" = "1" ]; then
            return 8
        fi
        sleep 5
    done
}

run=1
while [ "$run" -le "$RUNS" ]; do
    for scenario in "${SCENARIO_LIST[@]}"; do
        mode=""
        partition=""
        partition_index="-"
        partition_count="-"
        scenario_name=""
        case "$scenario" in
            baseline)
                [ "$PHASE" = "combat" ] \
                    || fail "baseline scenario is only valid in combat phase"
                mode="game-baseline-120"
                scenario_name="baseline"
                ;;
            baseline-safe)
                [ "$PHASE" = "combat" ] \
                    || fail "baseline-safe scenario is only valid in combat phase"
                mode="game-baseline-safe-120"
                scenario_name="baseline-safe"
                ;;
            optimized)
                [ "$PHASE" = "combat" ] \
                    || fail "optimized scenario is only valid in combat phase"
                mode="game-120"
                scenario_name="optimized"
                ;;
            optimized-safe)
                [ "$PHASE" = "combat" ] \
                    || fail "optimized-safe scenario is only valid in combat phase"
                mode="game-safe-120"
                scenario_name="optimized-safe"
                ;;
            full)
                if [ "$PHASE" = "menu" ]; then
                    mode="game-menu-60"
                else
                    mode="game-120"
                fi
                scenario_name="full"
                ;;
            safe)
                if [ "$PHASE" = "menu" ]; then
                    mode="game-menu-safe-60"
                else
                    mode="game-safe-120"
                fi
                scenario_name="safe"
                ;;
            *)
                if [[ "$scenario" =~ ^quickrestart-(baseline|optimized)-([0-9]{1,2})/([0-9]{1,2})$ ]]; then
                    [ "$PHASE" = "combat" ] \
                        || fail "Quick Restart scenarios are combat-only"
                    variant="${BASH_REMATCH[1]}"
                    partition_index="${BASH_REMATCH[2]}"
                    partition_count="${BASH_REMATCH[3]}"
                    [ "$partition_count" -ge 2 ] && [ "$partition_count" -le 32 ] \
                        && [ "$partition_index" -lt "$partition_count" ] \
                        || fail "invalid Quick Restart scenario: $scenario"
                    if [ "$variant" = "baseline" ]; then
                        mode="game-quickrestart-baseline-partition-120"
                    else
                        mode="game-quickrestart-partition-120"
                    fi
                    partition="${partition_index}/${partition_count}"
                    scenario_name="quickrestart-${variant}-${partition_index}-of-${partition_count}"
                elif [[ "$scenario" =~ ^(baseline|optimized)-([0-9]{1,2})/([0-9]{1,2})$ ]]; then
                    [ "$PHASE" = "combat" ] \
                        || fail "paired partition scenarios are combat-only"
                    variant="${BASH_REMATCH[1]}"
                    partition_index="${BASH_REMATCH[2]}"
                    partition_count="${BASH_REMATCH[3]}"
                    [ "$partition_count" -ge 2 ] && [ "$partition_count" -le 32 ] \
                        && [ "$partition_index" -lt "$partition_count" ] \
                        || fail "invalid partition scenario: $scenario"
                    if [ "$variant" = "baseline" ]; then
                        mode="game-baseline-partition-120"
                    else
                        mode="game-partition-120"
                    fi
                    partition="${partition_index}/${partition_count}"
                    scenario_name="${variant}-partition-${partition_index}-of-${partition_count}"
                elif [[ "$scenario" =~ ^([0-9]{1,2})/([0-9]{1,2})$ ]]; then
                    partition_index="${BASH_REMATCH[1]}"
                    partition_count="${BASH_REMATCH[2]}"
                    [ "$partition_count" -ge 2 ] && [ "$partition_count" -le 32 ] \
                        && [ "$partition_index" -lt "$partition_count" ] \
                        || fail "invalid partition scenario: $scenario"
                    if [ "$PHASE" = "menu" ]; then
                        mode="game-menu-partition-60"
                    else
                        mode="game-partition-120"
                    fi
                    partition="$scenario"
                    scenario_name="partition-${partition_index}-of-${partition_count}"
                else
                    fail "invalid scenario: $scenario"
                fi
                ;;
        esac

        printf -v run_label '%02d' "$run"
        capture_dir="$OUTPUT_ROOT/${scenario_name}-run-${run_label}"
        capture_args=(
            --adb "$ADB_EXECUTABLE"
            --serial "$DEVICE_SERIAL"
            --output "$capture_dir"
            --mode "$mode"
            --play-x "$PLAY_X"
            --play-y "$PLAY_Y"
            --timeout "$TIMEOUT_SECONDS"
            --min-battery "$MIN_BATTERY"
            --max-thermal-status "$MAX_THERMAL_STATUS"
            --allow-device-actions
        )
        if [ -n "$partition" ]; then
            capture_args+=(--mod-partition "$partition")
        fi
        if [ "$QUICK_RESTART_METHOD_PROBE" = true ]; then
            [[ "$scenario_name" == quickrestart-* ]] \
                || fail "--quick-restart-method-probe requires only Quick Restart scenarios"
            capture_args+=(--quick-restart-method-probe)
        fi
        if [ "$MOD_LOAD_PROBE" = true ]; then
            capture_args+=(--mod-load-probe)
        fi
        if [ "$PHASE" = "combat" ]; then
            if [ "$RESUME_AUTO" = true ]; then
                capture_args+=(--resume-auto --allow-save-fixture)
            else
                capture_args+=(
                    --resume-x "$RESUME_X"
                    --resume-y "$RESUME_Y"
                    --allow-save-fixture
                )
            fi
        fi
        if [ "$INTERACTION_SCRIPT" = "deck-cycle" ]; then
            capture_args+=(
                --interaction-script deck-cycle
                --deck-x "$DECK_X"
                --deck-y "$DECK_Y"
            )
        elif [ "$INTERACTION_SCRIPT" = "map-open" ]; then
            capture_args+=(
                --interaction-script map-open
                --map-x "$MAP_X"
                --map-y "$MAP_Y"
            )
        fi

        echo "RUN: $scenario_name $run/$RUNS"
        set +e
        wait_for_start_thermal
        thermal_gate_status=$?
        set -e
        if [ "$thermal_gate_status" -eq 4 ]; then
            echo "ABORT: device must be manually unlocked before the next arm" >&2
            exit 4
        elif [ "$thermal_gate_status" -ne 0 ]; then
            echo "ABORT: device did not reach the requested thermal/temperature gate within ${THERMAL_WAIT_SECONDS}s" >&2
            exit 8
        fi
        set +e
        "$CAPTURE_SCRIPT" "${capture_args[@]}"
        capture_status=$?
        set -e
        dim_for_cooling

        if [ "$capture_status" -ne 0 ]; then
            if [ "$capture_status" -eq 8 ]; then
                echo "ABORT: device exceeded the thermal gate; cool it before resuming with a new output directory" >&2
                exit 8
            fi
            INVALID_RUNS=$((INVALID_RUNS + 1))
            printf '2\t%s\t%s\t%s\t%s\t%s\tcapture-failed\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\n' \
                "$PHASE" "$scenario_name" "$partition_index" "$partition_count" "$run" \
                >>"$WORKFLOW_FILE"
            continue
        fi

        IFS=$'\t' read -r _ observed_mode segment samples elapsed_ms budget_us \
            p50_us p95_us p99_us max_us _ over_2x over_3x max_consecutive_2x \
            over_50ms over_100ms over_250ms \
            < <(awk -F '\t' -v expected="$mode" \
                'NR > 1 && $2 == expected { line=$0 } END { print line }' \
                "$capture_dir/summary.tsv")
        IFS=$'\t' read -r _ _ _ start_temperature end_temperature \
            start_thermal end_thermal _ _ mod_load_errors \
            < <(tail -n 1 "$capture_dir/context.tsv")

        result="pass"
        if [ "${mod_load_errors:-0}" -gt 0 ]; then
            result="mod-load-error"
            INVALID_RUNS=$((INVALID_RUNS + 1))
        elif ! [[ "$start_thermal" =~ ^[0-6]$ && "$end_thermal" =~ ^[0-6]$ ]]; then
            result="thermal-unavailable"
            INVALID_RUNS=$((INVALID_RUNS + 1))
        elif [ "$start_thermal" -gt "$MAX_THERMAL_STATUS" ] \
            || [ "$end_thermal" -gt "$MAX_THERMAL_STATUS" ]; then
            result="thermal-invalid"
            INVALID_RUNS=$((INVALID_RUNS + 1))
        fi
        printf '2\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
            "$PHASE" "$scenario_name" "$partition_index" "$partition_count" "$run" "$result" \
            "$samples" "$elapsed_ms" "$budget_us" "$p50_us" "$p95_us" "$p99_us" \
            "$max_us" "$over_2x" "$over_3x" "$max_consecutive_2x" "$over_50ms" \
            "$over_100ms" "$over_250ms" "$start_temperature" "$end_temperature" \
            "$start_thermal" "$end_thermal" "$mod_load_errors" \
            >>"$WORKFLOW_FILE"
        echo "RESULT: $scenario_name run=$run status=$result p99_us=$p99_us over_2x=$over_2x"
    done
    run=$((run + 1))
done

if [ "$INVALID_RUNS" -ne 0 ]; then
    echo "PARTIAL: workflow completed with $INVALID_RUNS invalid run(s); see $WORKFLOW_FILE" >&2
    exit 7
fi

echo "PASS: standardized mod-jank workflow written to $WORKFLOW_FILE"
