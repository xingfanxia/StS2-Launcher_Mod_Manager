#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PACKAGE_NAME="com.game.sts2launcher.modmanager"
ACTIVITY_NAME="com.game.sts2launcher.modmanager/.GodotApp"
DEVICE_SERIAL=""
OUTPUT_DIR=""
MODE=""
PLAY_X=""
PLAY_Y=""
RESUME_X=""
RESUME_Y=""
RESUME_AUTO=false
INTERACTION_SCRIPT="none"
DECK_X=""
DECK_Y=""
MAP_X=""
MAP_Y=""
PAUSE_MENU_X=""
PAUSE_MENU_Y=""
PAUSE_RESTART_X=""
PAUSE_RESTART_Y=""
DECK_CACHE_MUTATION_PROOF=false
QUICK_RESTART_METHOD_PROBE=false
MOD_LOAD_PROBE=false
MOD_PARTITION=""
STARTUP_TELEMETRY_PERSISTENCE="on"
TIMEOUT_SECONDS=360
MIN_BATTERY=15
MAX_THERMAL_STATUS=2
ALLOW_DEVICE_ACTIONS=false
ALLOW_SAVE_FIXTURE=false
ADB_EXECUTABLE="${STS2_ADB_EXECUTABLE:-adb}"

usage() {
    cat <<'EOF'
Usage: run-frame-capture.sh --serial SERIAL --output DIRECTORY --mode MODE [options]

Arm the debug-only Godot ProcessFrame probe and write sanitized numeric evidence.
No raw logcat, PID, serial, account, path, save content, or mod name is written.

Modes:
  control        180-frame metric control
  stall-100      180-frame controlled 100 ms stall
  launcher-120   120-second launcher interaction capture
  game-120       120-second real-game interaction capture
  game-baseline-120  Same capture with only gameplay performance fixes disabled
  game-baseline-safe-120  Baseline capture plus session-only no-mod Safe Mode
  game-baseline-partition-120
                         Baseline capture with a session-only mod partition
  game-quickrestart-baseline-partition-120
                         Quick Restart fix off, other gameplay fixes unchanged
  game-quickrestart-partition-120
                         Quick Restart fix on with the same mod partition
  game-safe-120  120-second session-only no-mod game capture
  game-safe-300  300-second session-only no-mod shader guardrail capture
  game-partition-120  120-second session-only mod-partition capture
  game-menu-60  60-second settled main-menu capture with normal mods
  game-menu-safe-60  60-second settled main-menu capture without mods
  game-menu-partition-60  60-second settled main-menu mod-partition capture

Options:
  --play-x X --play-y Y   Tap PLAY after launcher-ready (game modes only)
  --resume-x X --resume-y Y
                          After game-ready, tap Continue on a sacrificial save fixture
  --resume-auto           Locate the game-owned Continue action by content-free OCR
  --interaction-script S Fixed actions: none, deck-cycle, map-open,
                         quickrestart-short, quickrestart-hold, or
                         quickrestart-pause
  --deck-x X --deck-y Y   Deck button coordinates required by deck-cycle
  --map-x X --map-y Y     Map button coordinates required by map-open
  --pause-restart-x X --pause-restart-y Y
                          Quick Restart button coordinates in the pause menu
  --pause-menu-x X --pause-menu-y Y
                          Game pause button coordinates required by quickrestart-pause
  --deck-cache-mutation-proof
                          Reversibly prove hidden-cache obtain/remove/upgrade handling
  --quick-restart-method-probe
                          Add debug-only exact-mod call counters to Quick Restart modes
  --mod-load-probe       Add anonymous per-mod initializer/PatchAll timings
  --mod-partition I/N     Load only numeric partition I of N (partition mode only)
  --startup-telemetry-persistence on|off
                          Keep or suppress bounded startup-summary persistence
                          while retaining truthful progress UI (default: on)
  --timeout SECONDS       Overall capture timeout (default: 360)
  --min-battery PERCENT   Refuse all device mutations below this level (default: 15)
  --max-thermal-status N  Refuse to start above Android thermal status N (default: 2)
  --package NAME          Override package name
  --activity COMPONENT    Override launch component
  --adb PATH              adb executable (default: adb)
  --allow-device-actions  Required acknowledgement for force-stop/launch/input/log clear
  --allow-save-fixture    Required with --resume-x/y; acknowledges loading the active save
  --help                  Show this help

The installed version name must contain "-debug". The tool never installs,
uninstalls, clears app data, or changes renderer/settings/network. By default it
does not enter a save; the explicit resume option is only for a sacrificial
active-slot fixture selected and verified by the caller.
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

while [ "$#" -gt 0 ]; do
    case "$1" in
        --serial)
            [ "$#" -ge 2 ] || fail "--serial needs a value"
            DEVICE_SERIAL="$2"
            shift 2
            ;;
        --output)
            [ "$#" -ge 2 ] || fail "--output needs a value"
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --mode)
            [ "$#" -ge 2 ] || fail "--mode needs a value"
            MODE="$2"
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
        --pause-restart-x)
            [ "$#" -ge 2 ] || fail "--pause-restart-x needs a value"
            PAUSE_RESTART_X="$2"
            shift 2
            ;;
        --pause-restart-y)
            [ "$#" -ge 2 ] || fail "--pause-restart-y needs a value"
            PAUSE_RESTART_Y="$2"
            shift 2
            ;;
        --pause-menu-x)
            [ "$#" -ge 2 ] || fail "--pause-menu-x needs a value"
            PAUSE_MENU_X="$2"
            shift 2
            ;;
        --pause-menu-y)
            [ "$#" -ge 2 ] || fail "--pause-menu-y needs a value"
            PAUSE_MENU_Y="$2"
            shift 2
            ;;
        --deck-cache-mutation-proof)
            DECK_CACHE_MUTATION_PROOF=true
            shift
            ;;
        --quick-restart-method-probe)
            QUICK_RESTART_METHOD_PROBE=true
            shift
            ;;
        --mod-load-probe)
            MOD_LOAD_PROBE=true
            shift
            ;;
        --mod-partition)
            [ "$#" -ge 2 ] || fail "--mod-partition needs a value"
            MOD_PARTITION="$2"
            shift 2
            ;;
        --startup-telemetry-persistence)
            [ "$#" -ge 2 ] || fail "--startup-telemetry-persistence needs a value"
            STARTUP_TELEMETRY_PERSISTENCE="$2"
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
[ -n "$OUTPUT_DIR" ] || fail "--output is required"
[[ "$MODE" =~ ^(control|stall-100|launcher-120|game-120|game-baseline-120|game-baseline-safe-120|game-baseline-partition-120|game-quickrestart-baseline-partition-120|game-quickrestart-partition-120|game-safe-120|game-safe-300|game-partition-120|game-menu-60|game-menu-safe-60|game-menu-partition-60)$ ]] \
    || fail "unsupported mode: $MODE"
is_positive_integer "$TIMEOUT_SECONDS" || fail "--timeout must be a positive integer"
[[ "$STARTUP_TELEMETRY_PERSISTENCE" =~ ^(on|off)$ ]] \
    || fail "--startup-telemetry-persistence must be on or off"
is_positive_integer "$MIN_BATTERY" || fail "--min-battery must be a positive integer"
[ "$MIN_BATTERY" -le 100 ] || fail "--min-battery must not exceed 100"
is_nonnegative_integer "$MAX_THERMAL_STATUS" \
    || fail "--max-thermal-status must be an integer from 0 to 6"
[ "$MAX_THERMAL_STATUS" -le 6 ] \
    || fail "--max-thermal-status must be an integer from 0 to 6"
[[ "$PACKAGE_NAME" =~ ^[A-Za-z0-9._]+$ ]] || fail "invalid package name"
[[ "$ACTIVITY_NAME" =~ ^[A-Za-z0-9._]+/[A-Za-z0-9._]+$ ]] \
    || fail "invalid activity component"
[ "$ALLOW_DEVICE_ACTIONS" = true ] \
    || fail "--allow-device-actions is required for this mutating device test"
[ ! -e "$OUTPUT_DIR" ] || fail "output already exists: $OUTPUT_DIR"
if [ -n "$PLAY_X" ] || [ -n "$PLAY_Y" ]; then
    is_positive_integer "$PLAY_X" || fail "--play-x and --play-y must be provided together"
    is_positive_integer "$PLAY_Y" || fail "--play-x and --play-y must be provided together"
    [[ "$MODE" =~ ^game- ]] \
        || fail "PLAY coordinates require a game mode"
fi
if [ -n "$RESUME_X" ] || [ -n "$RESUME_Y" ]; then
    is_positive_integer "$RESUME_X" \
        || fail "--resume-x and --resume-y must be provided together"
    is_positive_integer "$RESUME_Y" \
        || fail "--resume-x and --resume-y must be provided together"
    [[ "$MODE" =~ ^game-(baseline-safe-|baseline-|safe-|partition-)?120$ \
            || "$MODE" == game-baseline-partition-120 \
            || "$MODE" == game-quickrestart-baseline-partition-120 \
            || "$MODE" == game-quickrestart-partition-120 \
            || "$MODE" == game-safe-300 ]] \
        || fail "resume coordinates require a gameplay capture mode"
    [ "$ALLOW_SAVE_FIXTURE" = true ] \
        || fail "--allow-save-fixture is required with resume coordinates"
    [ "$RESUME_AUTO" = false ] \
        || fail "--resume-auto cannot be combined with resume coordinates"
elif [ "$RESUME_AUTO" = true ]; then
    [[ "$MODE" =~ ^game-(baseline-safe-|baseline-|safe-|partition-)?120$ \
            || "$MODE" == game-baseline-partition-120 \
            || "$MODE" == game-quickrestart-baseline-partition-120 \
            || "$MODE" == game-quickrestart-partition-120 \
            || "$MODE" == game-safe-300 ]] \
        || fail "--resume-auto requires a gameplay capture mode"
    [ "$ALLOW_SAVE_FIXTURE" = true ] \
        || fail "--allow-save-fixture is required with --resume-auto"
    command -v swift >/dev/null 2>&1 \
        || fail "--resume-auto requires Swift/Vision on the host"
elif [ "$ALLOW_SAVE_FIXTURE" = true ]; then
    fail "--allow-save-fixture requires resume coordinates or --resume-auto"
fi
case "$INTERACTION_SCRIPT" in
    none)
        [ -z "$DECK_X" ] && [ -z "$DECK_Y" ] \
            && [ -z "$MAP_X" ] && [ -z "$MAP_Y" ] \
            && [ -z "$PAUSE_MENU_X" ] && [ -z "$PAUSE_MENU_Y" ] \
            && [ -z "$PAUSE_RESTART_X" ] && [ -z "$PAUSE_RESTART_Y" ] \
            || fail "interaction coordinates require a matching script"
        ;;
    deck-cycle)
        [[ "$MODE" =~ ^game-(baseline-safe-|baseline-|safe-|partition-)?120$ \
                || "$MODE" == game-baseline-partition-120 \
                || "$MODE" == game-quickrestart-baseline-partition-120 \
                || "$MODE" == game-quickrestart-partition-120 ]] \
            || fail "deck-cycle requires a 120-second gameplay mode"
        { [ -n "$RESUME_X" ] || [ "$RESUME_AUTO" = true ]; } \
            || fail "deck-cycle requires the caller-verified resume fixture"
        is_positive_integer "$DECK_X" \
            || fail "deck-cycle requires --deck-x and --deck-y"
        is_positive_integer "$DECK_Y" \
            || fail "deck-cycle requires --deck-x and --deck-y"
        [ -z "$MAP_X" ] && [ -z "$MAP_Y" ] \
            && [ -z "$PAUSE_MENU_X" ] && [ -z "$PAUSE_MENU_Y" ] \
            && [ -z "$PAUSE_RESTART_X" ] && [ -z "$PAUSE_RESTART_Y" ] \
            || fail "map coordinates require --interaction-script map-open"
        ;;
    map-open)
        [[ "$MODE" =~ ^game-(baseline-safe-|baseline-|safe-|partition-)?120$ \
                || "$MODE" == game-baseline-partition-120 \
                || "$MODE" == game-quickrestart-baseline-partition-120 \
                || "$MODE" == game-quickrestart-partition-120 ]] \
            || fail "map-open requires a 120-second gameplay mode"
        { [ -n "$RESUME_X" ] || [ "$RESUME_AUTO" = true ]; } \
            || fail "map-open requires the caller-verified resume fixture"
        is_positive_integer "$MAP_X" || fail "map-open requires --map-x and --map-y"
        is_positive_integer "$MAP_Y" || fail "map-open requires --map-x and --map-y"
        [ -z "$DECK_X" ] && [ -z "$DECK_Y" ] \
            && [ -z "$PAUSE_MENU_X" ] && [ -z "$PAUSE_MENU_Y" ] \
            && [ -z "$PAUSE_RESTART_X" ] && [ -z "$PAUSE_RESTART_Y" ] \
            || fail "deck coordinates require --interaction-script deck-cycle"
        ;;
    quickrestart-short|quickrestart-hold)
        [ "$MODE" = "game-quickrestart-partition-120" ] \
            || fail "$INTERACTION_SCRIPT requires the optimized Quick Restart mode"
        [ "$QUICK_RESTART_METHOD_PROBE" = true ] \
            || fail "$INTERACTION_SCRIPT requires --quick-restart-method-probe"
        { [ -n "$RESUME_X" ] || [ "$RESUME_AUTO" = true ]; } \
            || fail "$INTERACTION_SCRIPT requires the caller-verified resume fixture"
        [ -z "$DECK_X" ] && [ -z "$DECK_Y" ] \
            && [ -z "$MAP_X" ] && [ -z "$MAP_Y" ] \
            && [ -z "$PAUSE_MENU_X" ] && [ -z "$PAUSE_MENU_Y" ] \
            && [ -z "$PAUSE_RESTART_X" ] && [ -z "$PAUSE_RESTART_Y" ] \
            || fail "$INTERACTION_SCRIPT does not accept coordinates"
        ;;
    quickrestart-pause)
        [ "$MODE" = "game-quickrestart-partition-120" ] \
            || fail "$INTERACTION_SCRIPT requires the optimized Quick Restart mode"
        [ "$QUICK_RESTART_METHOD_PROBE" = true ] \
            || fail "$INTERACTION_SCRIPT requires --quick-restart-method-probe"
        { [ -n "$RESUME_X" ] || [ "$RESUME_AUTO" = true ]; } \
            || fail "$INTERACTION_SCRIPT requires the caller-verified resume fixture"
        is_positive_integer "$PAUSE_RESTART_X" \
            || fail "$INTERACTION_SCRIPT requires pause restart coordinates"
        is_positive_integer "$PAUSE_RESTART_Y" \
            || fail "$INTERACTION_SCRIPT requires pause restart coordinates"
        is_positive_integer "$PAUSE_MENU_X" \
            || fail "$INTERACTION_SCRIPT requires pause menu coordinates"
        is_positive_integer "$PAUSE_MENU_Y" \
            || fail "$INTERACTION_SCRIPT requires pause menu coordinates"
        [ -z "$DECK_X" ] && [ -z "$DECK_Y" ] \
            && [ -z "$MAP_X" ] && [ -z "$MAP_Y" ] \
            || fail "$INTERACTION_SCRIPT accepts only pause restart coordinates"
        ;;
    *)
        fail "unsupported --interaction-script: $INTERACTION_SCRIPT"
        ;;
esac
if [ "$DECK_CACHE_MUTATION_PROOF" = true ]; then
    [ "$MODE" = "game-safe-120" ] \
        || fail "--deck-cache-mutation-proof requires game-safe-120"
    { [ -n "$RESUME_X" ] || [ "$RESUME_AUTO" = true ]; } \
        || fail "--deck-cache-mutation-proof requires the caller-verified resume fixture"
fi
if [ "$QUICK_RESTART_METHOD_PROBE" = true ]; then
    [ "$MODE" = "game-quickrestart-baseline-partition-120" ] \
        || [ "$MODE" = "game-quickrestart-partition-120" ] \
        || fail "--quick-restart-method-probe requires a Quick Restart mode"
fi
if [ "$MOD_LOAD_PROBE" = true ]; then
    [[ "$MODE" == game-* ]] || fail "--mod-load-probe requires a game mode"
fi
if [ "$MODE" = "game-partition-120" ] \
        || [ "$MODE" = "game-baseline-partition-120" ] \
        || [ "$MODE" = "game-quickrestart-baseline-partition-120" ] \
        || [ "$MODE" = "game-quickrestart-partition-120" ] \
        || [ "$MODE" = "game-menu-partition-60" ]; then
    [[ "$MOD_PARTITION" =~ ^([0-9]{1,2})/([0-9]{1,2})$ ]] \
        || fail "partition mode requires --mod-partition I/N"
    PARTITION_INDEX="${BASH_REMATCH[1]}"
    PARTITION_COUNT="${BASH_REMATCH[2]}"
    [ "$PARTITION_COUNT" -ge 2 ] && [ "$PARTITION_COUNT" -le 32 ] \
        && [ "$PARTITION_INDEX" -lt "$PARTITION_COUNT" ] \
        || fail "mod partition must satisfy 0 <= I < N <= 32"
elif [ -n "$MOD_PARTITION" ]; then
    fail "--mod-partition is only valid with a partition capture mode"
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

BATTERY_DUMP="$("${ADB[@]}" shell dumpsys battery 2>/dev/null | tr -d '\r')"
BATTERY_LEVEL="$(printf '%s\n' "$BATTERY_DUMP" \
    | sed -nE 's/^[[:space:]]*level:[[:space:]]*([0-9]+).*/\1/p' | head -n 1)"
is_positive_integer "${BATTERY_LEVEL:-0}" || fail "battery level is unavailable"
if [ "$BATTERY_LEVEL" -lt "$MIN_BATTERY" ]; then
    echo "ERROR: battery ${BATTERY_LEVEL}% is below required ${MIN_BATTERY}%; no device action taken" >&2
    exit 5
fi

CURRENT_THERMAL_STATUS="$(read_thermal_status)"
if [[ "$CURRENT_THERMAL_STATUS" =~ ^[0-6]$ ]] \
    && [ "$CURRENT_THERMAL_STATUS" -gt "$MAX_THERMAL_STATUS" ]; then
    echo "ERROR: thermal status ${CURRENT_THERMAL_STATUS} exceeds allowed ${MAX_THERMAL_STATUS}; no device action taken" >&2
    exit 8
fi

PACKAGE_DUMP="$("${ADB[@]}" shell dumpsys package "$PACKAGE_NAME" 2>/dev/null | tr -d '\r')"
VERSION_NAME="$(printf '%s\n' "$PACKAGE_DUMP" \
    | sed -nE 's/.*versionName=([^[:space:]]+).*/\1/p' | head -n 1)"
[[ "$VERSION_NAME" == *-debug* ]] || {
    echo "ERROR: installed build is not a debug-probe build; no device action taken" >&2
    exit 6
}

mkdir -p "$OUTPUT_DIR"
EVENT_FILE="$OUTPUT_DIR/.events.tmp"
RSS_FILE="$OUTPUT_DIR/.rss.tmp"
CPU_FILE="$OUTPUT_DIR/.cpu.tmp"
AUTO_RESUME_SCREENSHOT="$OUTPUT_DIR/.resume-auto.tmp.png"
LOGCAT_CAPTURE_PID=""
RSS_CAPTURE_PID=""
DEVICE_SESSION_STARTED=false

now_ms() {
    ruby -e 'printf("%.0f\n", Process.clock_gettime(Process::CLOCK_MONOTONIC) * 1000)'
}

# Deterministic fake-device contracts do not need Android/Godot input settling.
# Keep every real-device delay unchanged; the focused test runner opts into this
# process-local shortcut explicitly.
input_settle_sleep() {
    if [ "${STS2_DEVICE_PERFORMANCE_TEST_FAST:-0}" = "1" ]; then
        return
    fi
    sleep "$1"
}

current_pid() {
    "${ADB[@]}" shell pidof "$PACKAGE_NAME" 2>/dev/null \
        | tr -d '\r' | awk '{print $1}'
}

read_battery_field() {
    local name="$1"
    "${ADB[@]}" shell dumpsys battery 2>/dev/null | tr -d '\r' \
        | sed -nE "s/^[[:space:]]*$name:[[:space:]]*([^[:space:]]+).*/\1/p" \
        | head -n 1
}

stop_captures() {
    if [ -n "$RSS_CAPTURE_PID" ]; then
        kill "$RSS_CAPTURE_PID" >/dev/null 2>&1 || true
        wait "$RSS_CAPTURE_PID" >/dev/null 2>&1 || true
        RSS_CAPTURE_PID=""
    fi
    if [ -n "$LOGCAT_CAPTURE_PID" ]; then
        kill "$LOGCAT_CAPTURE_PID" >/dev/null 2>&1 || true
        wait "$LOGCAT_CAPTURE_PID" >/dev/null 2>&1 || true
        LOGCAT_CAPTURE_PID=""
    fi
}

cleanup() {
    stop_captures
    if [ "$DEVICE_SESSION_STARTED" = true ]; then
        "${ADB[@]}" shell am force-stop "$PACKAGE_NAME" >/dev/null 2>&1 || true
        DEVICE_SESSION_STARTED=false
    fi
    [ ! -f "$EVENT_FILE" ] || unlink "$EVENT_FILE" >/dev/null 2>&1 || true
    [ ! -f "$RSS_FILE" ] || unlink "$RSS_FILE" >/dev/null 2>&1 || true
    [ ! -f "$CPU_FILE" ] || unlink "$CPU_FILE" >/dev/null 2>&1 || true
    [ ! -f "$AUTO_RESUME_SCREENSHOT" ] \
        || unlink "$AUTO_RESUME_SCREENSHOT" >/dev/null 2>&1 || true
}
trap cleanup EXIT

start_safe_log_capture() {
    : >"$EVENT_FILE"
    "${ADB[@]}" logcat -v brief 2>/dev/null \
        > >(LC_ALL=C sed -u -nE \
            -e 's/.*\[FrameProbe\] started mode=([a-z0-9-]+) point=([a-z0-9-]+) target=([^ ]+) budget_us=([0-9]+).*/started\t\1\t\2\t\3\t\4/p' \
            -e 's/.*\[FrameProbe\] segment started mode=([a-z0-9-]+) segment=([a-z0-9-]+) target=([^ ]+) budget_us=([0-9]+).*/started\t\1\t\2\t\3\t\4/p' \
            -e 's/.*\[FrameProbe\] spike elapsed_ms=([0-9]+) interval_us=([0-9]+) pipeline_canvas=([0-9]+) pipeline_draw=([0-9]+) pipeline_surface=([0-9]+) pipeline_mesh=([0-9]+) pipeline_specialization=([0-9]+).*/spike\t\1\t\2\t\3\t\4\t\5\t\6\t\7/p' \
            -e '/.*\[FrameProbe\] summary mode=(control|stall-100|launcher-120|game-120|game-baseline-120|game-baseline-safe-120|game-baseline-partition-120|game-quickrestart-baseline-partition-120|game-quickrestart-partition-120|game-safe-120|game-safe-300|game-partition-120|game-menu-60|game-menu-safe-60|game-menu-partition-60) segment=[a-z0-9-]+ samples=[0-9]+ elapsed_ms=[0-9]+ budget_us=[0-9]+ p50_us=[0-9]+ p95_us=[0-9]+ p99_us=[0-9]+ max_us=[0-9]+ over_1x=[0-9]+ over_2x=[0-9]+ over_3x=[0-9]+ max_consecutive_2x=[0-9]+ over_50ms=[0-9]+ over_100ms=[0-9]+ over_250ms=[0-9]+$/ { s/.*\[FrameProbe\] summary mode=/summary\t/; s/ segment=/\t/; s/ samples=/\t/; s/ elapsed_ms=/\t/; s/ budget_us=/\t/; s/ p50_us=/\t/; s/ p95_us=/\t/; s/ p99_us=/\t/; s/ max_us=/\t/; s/ over_1x=/\t/; s/ over_2x=/\t/; s/ over_3x=/\t/; s/ max_consecutive_2x=/\t/; s/ over_50ms=/\t/; s/ over_100ms=/\t/; s/ over_250ms=/\t/; p; }' \
            -e 's/.*\[QuickRestartProbe\] summary segment=([a-z0-9-]+) process_calls=([0-9]+) process_us=([0-9]+) can_restart_calls=([0-9]+) can_restart_us=([0-9]+) file_exists_calls=([0-9]+) reset_calls=([0-9]+) reset_us=([0-9]+).*/quick-restart\t\1\t\2\t\3\t\4\t\5\t\6\t\7\t\8/p' \
            -e 's/.*\[QuickRestartBehaviorProbe\] summary segment=([a-z0-9-]+) input_enable=([0-9]+) input_disable=([0-9]+) visible_frames=([0-9]+) restart_calls=([0-9]+) pause_calls=([0-9]+).*/quick-restart-behavior\t\1\t\2\t\3\t\4\t\5\t\6/p' \
            -e 's/.*\[ModLoadProbe\] item=([0-9]+) total_us=([0-9]+) initializer_us=([0-9]+) patchall_us=([0-9]+) initializer_count=([0-9]+) patchall_count=([0-9]+) loaded=([01]).*/mod-item\t\1\t\2\t\3\t\4\t\5\t\6\t\7/p' \
            -e 's/.*\[ModLoadProbe\] initializer item=([0-9]+) index=([0-9]+) duration_us=([0-9]+) success=([01]).*/mod-step\tinitializer\t\1\t\2\t\3\t\4/p' \
            -e 's/.*\[ModLoadProbe\] patchall item=([0-9]+) index=([0-9]+) duration_us=([0-9]+).*/mod-step\tpatchall\t\1\t\2\t\3\t1/p' \
            -e 's/.*\[InteractionProbe\] summary name=([a-z0-9-]+) samples=([0-9]+) p50_us=([0-9]+) p95_us=([0-9]+) p99_us=([0-9]+) max_us=([0-9]+) over_2x=([0-9]+) over_100ms=([0-9]+).*/interaction\t\1\t\2\t\3\t\4\t\5\t\6\t\7\t\8/p' \
            -e 's/.*\[GameplayPipelineWarmup\] cover summary elapsed_us=([0-9]+).*/covered-first-combat\t\1/p' \
            -e 's/.*\[FrameProbe\] sample failed: ([A-Za-z0-9_.-]+).*/error\t\1/p' \
            -e 's/.*\[DeckCacheProbe\] result obtain=([01]) remove=([01]) upgrade=([01]) restore=([01]) cleanup=([01]) error=([01]) pass=([01]).*/deck-cache\t\1\t\2\t\3\t\4\t\5\t\6\t\7/p' \
            -e 's/.*\[ERROR\] Exception thrown when calling mod initializer.*/mod-load-error/p' \
            -e 's/.*Launcher ready for PLAY.*/launcher-ready/p' \
            >>"$EVENT_FILE") &
    LOGCAT_CAPTURE_PID=$!
}

start_rss_capture() {
    local start_ms="$1"
    : >"$RSS_FILE"
    : >"$CPU_FILE"
    (
        while true; do
            local_pid="$(current_pid)"
            if [ -n "$local_pid" ]; then
                rss_kb="$("${ADB[@]}" shell cat "/proc/$local_pid/status" 2>/dev/null \
                    | tr -d '\r' | sed -nE 's/^VmRSS:[[:space:]]*([0-9]+).*/\1/p' \
                    | head -n 1)"
                if [[ "$rss_kb" =~ ^[0-9]+$ ]]; then
                    sample_elapsed_ms="$(( $(now_ms) - start_ms ))"
                    printf '%s\t%s\n' "$sample_elapsed_ms" "$rss_kb" >>"$RSS_FILE"
                    cpu_ticks="$("${ADB[@]}" shell cat "/proc/$local_pid/stat" 2>/dev/null \
                        | tr -d '\r' | awk 'NF >= 15 { print $14 + $15; exit }')"
                    if [[ "$cpu_ticks" =~ ^[0-9]+$ ]]; then
                        printf '%s\t%s\n' "$sample_elapsed_ms" "$cpu_ticks" >>"$CPU_FILE"
                    fi
                fi
            fi
            sleep 5
        done
    ) &
    RSS_CAPTURE_PID=$!
}

wait_for_event() {
    local pattern="$1"
    local timeout="$2"
    local deadline=$(( $(now_ms) + timeout * 1000 ))
    while [ "$(now_ms)" -lt "$deadline" ]; do
        if grep -qE "$pattern" "$EVENT_FILE" 2>/dev/null; then
            return 0
        fi
        sleep 0.25
    done
    return 1
}

START_BATTERY="$BATTERY_LEVEL"
START_TEMPERATURE="$(read_battery_field temperature)"
START_THERMAL="$(read_thermal_status)"
CAPTURE_START_MS="$(now_ms)"

"${ADB[@]}" logcat -c
start_safe_log_capture
"${ADB[@]}" shell am force-stop "$PACKAGE_NAME"
START_ARGS=(shell am start -W -n "$ACTIVITY_NAME" --es debug_frame_probe "$MODE")
START_ARGS+=(--es debug_startup_telemetry_persistence "$STARTUP_TELEMETRY_PERSISTENCE")
if [ "$DECK_CACHE_MUTATION_PROOF" = true ]; then
    START_ARGS+=(--es debug_deck_cache_mutation_probe 1)
fi
if [ "$QUICK_RESTART_METHOD_PROBE" = true ]; then
    START_ARGS+=(--es debug_quick_restart_method_probe 1)
fi
if [ "$MOD_LOAD_PROBE" = true ]; then
    START_ARGS+=(--es debug_mod_load_probe 1)
fi
if [ -n "$MOD_PARTITION" ]; then
    START_ARGS+=(--es debug_mod_partition "$MOD_PARTITION")
fi
"${ADB[@]}" "${START_ARGS[@]}" >/dev/null
DEVICE_SESSION_STARTED=true
start_rss_capture "$CAPTURE_START_MS"

if [ -n "$PLAY_X" ]; then
    wait_for_event '^launcher-ready$' 120 \
        || fail "launcher-ready was not observed before PLAY"
    # The ready marker is emitted only after startup recovery checks finish and
    # the launcher has entered its explicit user-input wait.
    input_settle_sleep 2
    # A short synthetic tap is only a hover on this touch-only Godot build.
    # Hold long enough for Godot to establish keyboard focus, then submit
    # three times: the first two advance hover/focus state and the third
    # activates PLAY. The launch overlay prevents later events from reaching
    # game UI if an earlier one already activated the disabled-in-flight button.
    "${ADB[@]}" shell input swipe "$PLAY_X" "$PLAY_Y" "$PLAY_X" "$PLAY_Y" 200
    input_settle_sleep 0.5
    "${ADB[@]}" shell input keyevent 66
    input_settle_sleep 0.5
    "${ADB[@]}" shell input keyevent 66
    input_settle_sleep 0.5
    "${ADB[@]}" shell input keyevent 66
elif [[ "$MODE" =~ ^game- ]]; then
    echo "Capture armed: tap PLAY and exercise the requested real game scenario."
fi

if [ -n "$RESUME_X" ] || [ "$RESUME_AUTO" = true ]; then
    wait_for_event "^started[[:space:]]${MODE}[[:space:]]game-ready[[:space:]]" 120 \
        || fail "game-ready probe was not observed before fixture resume"
    # The game menu is visible when the probe starts, but let it accept one
    # rendered input frame before activating the caller-verified fixture.
    input_settle_sleep 2
    if [ "$RESUME_AUTO" = true ]; then
        display_id="$("${ADB[@]}" shell dumpsys SurfaceFlinger --display-id 2>/dev/null \
            | tr -d '\r' | sed -nE 's/^Display[[:space:]]+([0-9]+).*/\1/p' \
            | head -n 1)"
        [[ "$display_id" =~ ^[0-9]+$ ]] \
            || fail "active physical display id is unavailable"
        "${ADB[@]}" exec-out screencap -d "$display_id" -p \
            >"$AUTO_RESUME_SCREENSHOT"
        dimensions="$(sips -g pixelWidth -g pixelHeight "$AUTO_RESUME_SCREENSHOT" \
            2>/dev/null | awk '/pixelWidth:/{w=$2}/pixelHeight:/{h=$2}END{print w "x" h}')"
        [[ "$dimensions" =~ ^([1-9][0-9]*)x([1-9][0-9]*)$ ]] \
            || fail "auto-resume screenshot dimensions are unavailable"
        resume_width="${BASH_REMATCH[1]}"
        resume_height="${BASH_REMATCH[2]}"
        center="$(swift "$SCRIPT_DIR/../device-stability/audit-screenshot.swift" \
            "$AUTO_RESUME_SCREENSHOT" --locate-game-continue \
            | sed -nE 's/^game_continue_center_normalized=([0-9.]+),([0-9.]+)$/\1,\2/p')"
        [[ "$center" =~ ^(0\.[0-9]+),(0\.[0-9]+)$ ]] \
            || fail "game Continue action could not be located safely"
        resume_tap_x="$(awk -v n="${BASH_REMATCH[1]}" -v size="$resume_width" \
            'BEGIN { printf "%.0f", n * size }')"
        resume_tap_y="$(awk -v n="${BASH_REMATCH[2]}" -v size="$resume_height" \
            'BEGIN { printf "%.0f", n * size }')"
        unlink "$AUTO_RESUME_SCREENSHOT"
    else
        resume_tap_x="$RESUME_X"
        resume_tap_y="$RESUME_Y"
    fi
    resume_succeeded=false
    for resume_attempt in 1 2; do
        # The game menu can expose the button before mod initialization has
        # finished accepting input. Establish touch focus, then submit once;
        # the opaque first-combat cover safely absorbs the submit if the touch
        # already activated Continue.
        "${ADB[@]}" shell input swipe \
            "$resume_tap_x" "$resume_tap_y" "$resume_tap_x" "$resume_tap_y" 200
        input_settle_sleep 0.5
        "${ADB[@]}" shell input keyevent 66
        if wait_for_event \
            "^started[[:space:]]${MODE}[[:space:]]gameplay-interactive[[:space:]]" 45; then
            resume_succeeded=true
            break
        fi
        echo "Fixture resume activation retry: $resume_attempt/2"
    done
    [ "$resume_succeeded" = true ] \
        || fail "real combat did not become interactive after bounded Continue activation"
    echo "Fixture resume confirmed at the real combat-hand boundary."
fi

if [ "$INTERACTION_SCRIPT" = "deck-cycle" ]; then
    wait_for_event "^started[[:space:]]${MODE}[[:space:]]gameplay-interactive[[:space:]]" 120 \
        || fail "real combat did not become interactive before deck-cycle"
    input_settle_sleep 2
    for _cycle in 1 2 3 4 5; do
        "${ADB[@]}" shell input tap "$DECK_X" "$DECK_Y"
        input_settle_sleep 2
        "${ADB[@]}" shell input keyevent 4
        input_settle_sleep 2
    done
    echo "Deck interaction cycle completed."
fi

if [ "$INTERACTION_SCRIPT" = "map-open" ]; then
    wait_for_event "^started[[:space:]]${MODE}[[:space:]]gameplay-interactive[[:space:]]" 120 \
        || fail "real combat did not become interactive before map-open"
    input_settle_sleep 2
    "${ADB[@]}" shell input tap "$MAP_X" "$MAP_Y"
    wait_for_event '^interaction[[:space:]]map-open[[:space:]]' 15 \
        || fail "map-open interaction summary was not observed"
    input_settle_sleep 2
    "${ADB[@]}" shell input keyevent 4
    echo "Map open/close interaction completed."
fi

if [ "$INTERACTION_SCRIPT" = "quickrestart-short" ] \
        || [ "$INTERACTION_SCRIPT" = "quickrestart-hold" ]; then
    wait_for_event "^started[[:space:]]${MODE}[[:space:]]gameplay-interactive[[:space:]]" 120 \
        || fail "real combat did not become interactive before $INTERACTION_SCRIPT"
    input_settle_sleep 2
    hold_ms=250
    if [ "$INTERACTION_SCRIPT" = "quickrestart-hold" ]; then
        hold_ms=2500
    fi
    "${ADB[@]}" shell input keyevent --duration "$hold_ms" 46
    input_settle_sleep 3
    echo "Quick Restart key interaction completed: $INTERACTION_SCRIPT"
fi

if [ "$INTERACTION_SCRIPT" = "quickrestart-pause" ]; then
    wait_for_event "^started[[:space:]]${MODE}[[:space:]]gameplay-interactive[[:space:]]" 120 \
        || fail "real combat did not become interactive before $INTERACTION_SCRIPT"
    input_settle_sleep 2
    "${ADB[@]}" shell input tap "$PAUSE_MENU_X" "$PAUSE_MENU_Y"
    input_settle_sleep 2
    "${ADB[@]}" shell input tap "$PAUSE_RESTART_X" "$PAUSE_RESTART_Y"
    input_settle_sleep 3
    echo "Quick Restart pause-menu interaction completed."
fi

if [ "$DECK_CACHE_MUTATION_PROOF" = true ]; then
    wait_for_event '^deck-cache[[:space:]]' 120 \
        || fail "deck cache mutation proof was not observed"
    grep -qE '^deck-cache[[:space:]]1[[:space:]]1[[:space:]]1[[:space:]]1[[:space:]]1[[:space:]]0[[:space:]]1$' \
        "$EVENT_FILE" || fail "deck cache mutation proof did not restore every mutation"
fi

SUMMARY_PATTERN='^summary[[:space:]]'
if [[ "$MODE" =~ ^game-(baseline-safe-|baseline-|safe-|partition-)?120$ \
        || "$MODE" == game-baseline-partition-120 \
        || "$MODE" == game-quickrestart-baseline-partition-120 \
        || "$MODE" == game-quickrestart-partition-120 \
        || "$MODE" == game-safe-300 ]]; then
    SUMMARY_PATTERN="^summary[[:space:]]${MODE}[[:space:]]gameplay-interactive[[:space:]]"
elif [[ "$MODE" =~ ^game-menu- ]]; then
    SUMMARY_PATTERN="^summary[[:space:]]${MODE}[[:space:]]game-menu-idle[[:space:]]"
fi
if ! wait_for_event "$SUMMARY_PATTERN" "$TIMEOUT_SECONDS"; then
    fail "frame summary was not observed within ${TIMEOUT_SECONDS}s"
fi
input_settle_sleep 0.5
stop_captures

if grep -q '^error[[:space:]]' "$EVENT_FILE"; then
    fail "the in-game frame sampler reported an error"
fi
if ! awk -F '\t' -v expected="$MODE" '
    $1 == "summary" && $2 == expected \
        && (expected !~ /^game-/ \
            || (expected ~ /^game-menu-/ && $3 == "game-menu-idle") \
            || (expected !~ /^game-menu-/ && $3 == "gameplay-interactive")) { found=1 }
    END { exit !found }
' \
    "$EVENT_FILE"; then
    fail "the observed summary did not match requested mode $MODE"
fi

printf 'format_version\tmode\tsegment\tsamples\telapsed_ms\tbudget_us\tp50_us\tp95_us\tp99_us\tmax_us\tover_1x\tover_2x\tover_3x\tmax_consecutive_2x\tover_50ms\tover_100ms\tover_250ms\n' \
    >"$OUTPUT_DIR/summary.tsv"
awk -F '\t' '$1 == "summary" { print "1\t" substr($0, index($0, "\t") + 1) }' \
    "$EVENT_FILE" >>"$OUTPUT_DIR/summary.tsv"

printf 'format_version\telapsed_ms\tinterval_us\tpipeline_canvas\tpipeline_draw\tpipeline_surface\tpipeline_mesh\tpipeline_specialization\n' \
    >"$OUTPUT_DIR/spikes.tsv"
awk -F '\t' '$1 == "spike" { print "1\t" substr($0, index($0, "\t") + 1) }' \
    "$EVENT_FILE" >>"$OUTPUT_DIR/spikes.tsv"

printf 'format_version\telapsed_ms\trss_kb\n' >"$OUTPUT_DIR/rss.tsv"
awk -F '\t' 'NF == 2 && $1 ~ /^[0-9]+$/ && $2 ~ /^[0-9]+$/ { print "1\t" $0 }' \
    "$RSS_FILE" >>"$OUTPUT_DIR/rss.tsv"

CLOCK_TICKS_PER_SECOND="$("${ADB[@]}" shell getconf CLK_TCK 2>/dev/null \
    | tr -d '\r' | sed -nE 's/^[[:space:]]*([1-9][0-9]*)[[:space:]]*$/\1/p' \
    | head -n 1)"
is_positive_integer "${CLOCK_TICKS_PER_SECOND:-0}" \
    || fail "device process clock frequency is unavailable"
printf 'format_version\telapsed_ms\tcpu_milli_percent\n' >"$OUTPUT_DIR/cpu.tsv"
awk -F '\t' -v hz="$CLOCK_TICKS_PER_SECOND" '
    $1 ~ /^[0-9]+$/ && $2 ~ /^[0-9]+$/ {
        if (have && $1 > previous_ms && $2 >= previous_ticks) {
            milli_percent = int((($2 - previous_ticks) * 100000000.0 \
                / (hz * ($1 - previous_ms))) + 0.5)
            print "1\t" $1 "\t" milli_percent
        }
        previous_ms=$1
        previous_ticks=$2
        have=1
    }
' "$CPU_FILE" >>"$OUTPUT_DIR/cpu.tsv"

printf 'format_version\tstartup_telemetry_persistence\tquick_restart_method_probe\tmod_load_probe\n1\t%s\t%s\t%s\n' \
    "$STARTUP_TELEMETRY_PERSISTENCE" \
    "$([ "$QUICK_RESTART_METHOD_PROBE" = true ] && printf 1 || printf 0)" \
    "$([ "$MOD_LOAD_PROBE" = true ] && printf 1 || printf 0)" \
    >"$OUTPUT_DIR/instrumentation.tsv"

if [ "$DECK_CACHE_MUTATION_PROOF" = true ]; then
    printf 'format_version\tobtain\tremove\tupgrade\trestore\tcleanup\terror\tpass\n' \
        >"$OUTPUT_DIR/deck-cache-mutation.tsv"
    awk -F '\t' '$1 == "deck-cache" { print "1\t" substr($0, index($0, "\t") + 1) }' \
        "$EVENT_FILE" >>"$OUTPUT_DIR/deck-cache-mutation.tsv"
fi
if [ "$QUICK_RESTART_METHOD_PROBE" = true ]; then
    printf 'format_version\tsegment\tprocess_calls\tprocess_us\tcan_restart_calls\tcan_restart_us\tfile_exists_calls\treset_calls\treset_us\n' \
        >"$OUTPUT_DIR/quick-restart-probe.tsv"
    awk -F '\t' '$1 == "quick-restart" { print "1\t" substr($0, index($0, "\t") + 1) }' \
        "$EVENT_FILE" >>"$OUTPUT_DIR/quick-restart-probe.tsv"
    grep -q $'^1\tgameplay-interactive\t' "$OUTPUT_DIR/quick-restart-probe.tsv" \
        || fail "Quick Restart method summary was not observed"
    printf 'format_version\tsegment\tinput_enable\tinput_disable\tvisible_frames\trestart_calls\tpause_calls\n' \
        >"$OUTPUT_DIR/quick-restart-behavior.tsv"
    awk -F '\t' '$1 == "quick-restart-behavior" { print "1\t" substr($0, index($0, "\t") + 1) }' \
        "$EVENT_FILE" >>"$OUTPUT_DIR/quick-restart-behavior.tsv"
    grep -q $'^1\tgameplay-interactive\t' "$OUTPUT_DIR/quick-restart-behavior.tsv" \
        || fail "Quick Restart behavior summary was not observed"
fi
if [ "$MOD_LOAD_PROBE" = true ]; then
    printf 'format_version\titem\ttotal_us\tinitializer_us\tpatchall_us\tinitializer_count\tpatchall_count\tloaded\n' \
        >"$OUTPUT_DIR/mod-load-items.tsv"
    awk -F '\t' '$1 == "mod-item" { print "1\t" substr($0, index($0, "\t") + 1) }' \
        "$EVENT_FILE" >>"$OUTPUT_DIR/mod-load-items.tsv"
    printf 'format_version\tkind\titem\tindex\tduration_us\tsuccess\n' \
        >"$OUTPUT_DIR/mod-load-steps.tsv"
    awk -F '\t' '$1 == "mod-step" { print "1\t" substr($0, index($0, "\t") + 1) }' \
        "$EVENT_FILE" >>"$OUTPUT_DIR/mod-load-steps.tsv"
    [ "$(wc -l <"$OUTPUT_DIR/mod-load-items.tsv" | tr -d ' ')" -gt 1 ] \
        || fail "mod-load item timings were not observed"
fi
if [ "$INTERACTION_SCRIPT" = "map-open" ]; then
    printf 'format_version\tname\tsamples\tp50_us\tp95_us\tp99_us\tmax_us\tover_2x\tover_100ms\n' \
        >"$OUTPUT_DIR/interactions.tsv"
    awk -F '\t' '$1 == "interaction" { print "1\t" substr($0, index($0, "\t") + 1) }' \
        "$EVENT_FILE" >>"$OUTPUT_DIR/interactions.tsv"
    grep -q $'^1\tmap-open\t' "$OUTPUT_DIR/interactions.tsv" \
        || fail "map-open interaction evidence was not sanitized"
    printf 'format_version\telapsed_us\n' \
        >"$OUTPUT_DIR/covered-first-combat.tsv"
    awk -F '\t' '$1 == "covered-first-combat" { print "1\t" $2 }' \
        "$EVENT_FILE" >>"$OUTPUT_DIR/covered-first-combat.tsv"
    [ "$(wc -l <"$OUTPUT_DIR/covered-first-combat.tsv" | tr -d ' ')" -eq 2 ] \
        || fail "one covered first-combat duration was not observed"
fi

END_BATTERY="$(read_battery_field level)"
END_TEMPERATURE="$(read_battery_field temperature)"
END_THERMAL="$(read_thermal_status)"
MOD_LOAD_ERROR_COUNT="$(grep -c '^mod-load-error$' "$EVENT_FILE" || true)"
printf 'format_version\tstart_battery_percent\tend_battery_percent\tstart_battery_deci_c\tend_battery_deci_c\tstart_thermal_status\tend_thermal_status\tmod_partition_index\tmod_partition_count\tmod_load_error_count\n' \
    >"$OUTPUT_DIR/context.tsv"
printf '1\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$START_BATTERY" "${END_BATTERY:--}" "${START_TEMPERATURE:--}" \
    "${END_TEMPERATURE:--}" "$START_THERMAL" "$END_THERMAL" \
    "${PARTITION_INDEX:--}" "${PARTITION_COUNT:--}" "$MOD_LOAD_ERROR_COUNT" \
    >>"$OUTPUT_DIR/context.tsv"

echo "PASS: sanitized frame evidence written to $OUTPUT_DIR"
