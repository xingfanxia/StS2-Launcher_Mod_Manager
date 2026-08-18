#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
MATRIX_SCRIPT="$SCRIPT_DIR/../device-stability/run-matrix.sh"
PACKAGE_NAME="com.game.sts2launcher.modmanager"
ACTIVITY_NAME="com.game.sts2launcher.modmanager/.GodotApp"
BASELINE_APK=""
CANDIDATE_APK=""
DEVICE_SERIAL=""
OUTPUT_ROOT=""
PAIR_COUNT=30
PLAY_X=""
PLAY_Y=""
TIMEOUT_SECONDS=120
MIN_BATTERY=15
MAX_THERMAL_STATUS=2
MAX_START_THERMAL_STATUS=""
THERMAL_WAIT_SECONDS=600
ADB_EXECUTABLE="${STS2_ADB_EXECUTABLE:-adb}"
AAPT2_EXECUTABLE="${STS2_AAPT2_EXECUTABLE:-aapt2}"
APKSIGNER_EXECUTABLE="${STS2_APKSIGNER_EXECUTABLE:-apksigner}"
ALLOW_DEVICE_ACTIONS=false
ALLOW_APK_INSTALLS=false
RESUME=false
MUTATION_STARTED=false
RESTORE_COMPLETE=false
SUMMARY_VALUES_FILE=""
COMPLETED_ARMS=0

usage() {
    cat <<'EOF'
Usage: run-startup-ab.sh --baseline-apk FILE --candidate-apk FILE \
  --serial SERIAL --output DIRECTORY --play-x X --play-y Y [options]

Interleave upgrade-compatible baseline and candidate APK cold starts while
preserving app data. Every arm uses the common launcher-UI boundary, repeatedly
activates PLAY until the app acknowledges it, and requires game-ready.

Options:
  --pairs N                 A/B pair count, 1-30 (default: 30)
  --resume                  Continue only a valid prefix in an existing output
  --timeout SECONDS         Per-stage timeout (default: 120)
  --min-battery PERCENT     Refuse mutation below this level (default: 15)
  --max-thermal-status N    Require Android thermal status at most N (default: 2)
  --max-start-thermal-status N
                            Require this cooler status before each install
                            (default: same as --max-thermal-status)
  --thermal-wait-seconds N  Bounded pre-run cooling wait (default: 600)
  --adb PATH                adb executable (default: adb)
  --aapt2 PATH              aapt2 executable (default: aapt2)
  --apksigner PATH          apksigner executable (default: apksigner)
  --allow-device-actions    Required for force-stop, launch, and input
  --allow-apk-installs      Required for upgrade-installing both APKs
  --help                    Show this help

The runner validates the exact package and equal signer before touching the
device. It uses only `adb install -r -d`; it never uninstalls, clears app data,
changes settings, or reads saves, credentials, or mod files. Every post-install
exit path force-stops the app and upgrade-installs the candidate again.
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
        --baseline-apk)
            [ "$#" -ge 2 ] || fail "--baseline-apk needs a value"
            BASELINE_APK="$2"
            shift 2
            ;;
        --candidate-apk)
            [ "$#" -ge 2 ] || fail "--candidate-apk needs a value"
            CANDIDATE_APK="$2"
            shift 2
            ;;
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
        --pairs)
            [ "$#" -ge 2 ] || fail "--pairs needs a value"
            PAIR_COUNT="$2"
            shift 2
            ;;
        --resume)
            RESUME=true
            shift
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
        --thermal-wait-seconds)
            [ "$#" -ge 2 ] || fail "--thermal-wait-seconds needs a value"
            THERMAL_WAIT_SECONDS="$2"
            shift 2
            ;;
        --adb)
            [ "$#" -ge 2 ] || fail "--adb needs a value"
            ADB_EXECUTABLE="$2"
            shift 2
            ;;
        --aapt2)
            [ "$#" -ge 2 ] || fail "--aapt2 needs a value"
            AAPT2_EXECUTABLE="$2"
            shift 2
            ;;
        --apksigner)
            [ "$#" -ge 2 ] || fail "--apksigner needs a value"
            APKSIGNER_EXECUTABLE="$2"
            shift 2
            ;;
        --allow-device-actions)
            ALLOW_DEVICE_ACTIONS=true
            shift
            ;;
        --allow-apk-installs)
            ALLOW_APK_INSTALLS=true
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

[ -n "$BASELINE_APK" ] || fail "--baseline-apk is required"
[ -n "$CANDIDATE_APK" ] || fail "--candidate-apk is required"
[ -f "$BASELINE_APK" ] || fail "baseline APK does not exist"
[ -f "$CANDIDATE_APK" ] || fail "candidate APK does not exist"
[ -n "$DEVICE_SERIAL" ] || fail "--serial is required"
[ -n "$OUTPUT_ROOT" ] || fail "--output is required"
RESULT_FILE="$OUTPUT_ROOT/startup-ab.tsv"
if [ "$RESUME" = true ]; then
    [ -d "$OUTPUT_ROOT" ] || fail "resume output does not exist"
    [ -f "$RESULT_FILE" ] || fail "resume evidence is missing"
else
    [ ! -e "$OUTPUT_ROOT" ] || fail "output already exists"
fi
is_positive_integer "$PAIR_COUNT" || fail "--pairs must be a positive integer"
[ "$PAIR_COUNT" -le 30 ] || fail "--pairs must not exceed 30"
is_positive_integer "$PLAY_X" || fail "--play-x is required"
is_positive_integer "$PLAY_Y" || fail "--play-y is required"
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
is_nonnegative_integer "$THERMAL_WAIT_SECONDS" \
    || fail "--thermal-wait-seconds must be a nonnegative integer"
[ "$ALLOW_DEVICE_ACTIONS" = true ] \
    || fail "--allow-device-actions is required for this mutating device test"
[ "$ALLOW_APK_INSTALLS" = true ] \
    || fail "--allow-apk-installs is required for upgrade installation"
[ -x "$MATRIX_SCRIPT" ] || fail "device stability matrix is unavailable"
command -v "$ADB_EXECUTABLE" >/dev/null 2>&1 || fail "adb not found"
command -v "$AAPT2_EXECUTABLE" >/dev/null 2>&1 || fail "aapt2 not found"
command -v "$APKSIGNER_EXECUTABLE" >/dev/null 2>&1 || fail "apksigner not found"

read_apk_package() {
    "$AAPT2_EXECUTABLE" dump badging "$1" 2>/dev/null \
        | sed -nE "s/^package: name='([^']+)'.*/\1/p" \
        | head -n 1
}

read_apk_signers() {
    "$APKSIGNER_EXECUTABLE" verify --print-certs "$1" 2>/dev/null \
        | sed -nE 's/^Signer #[0-9]+ certificate SHA-256 digest:[[:space:]]*([0-9A-Fa-f]{64})[[:space:]]*$/\1/p' \
        | tr '[:upper:]' '[:lower:]' \
        | LC_ALL=C sort -u \
        | paste -sd, -
}

baseline_package="$(read_apk_package "$BASELINE_APK")"
candidate_package="$(read_apk_package "$CANDIDATE_APK")"
[ "$baseline_package" = "$PACKAGE_NAME" ] || fail "baseline APK package mismatch"
[ "$candidate_package" = "$PACKAGE_NAME" ] || fail "candidate APK package mismatch"
baseline_signers="$(read_apk_signers "$BASELINE_APK")"
candidate_signers="$(read_apk_signers "$CANDIDATE_APK")"
[[ "$baseline_signers" =~ ^[0-9a-f]{64}(,[0-9a-f]{64})*$ ]] \
    || fail "baseline APK signer unavailable"
[[ "$candidate_signers" =~ ^[0-9a-f]{64}(,[0-9a-f]{64})*$ ]] \
    || fail "candidate APK signer unavailable"
[ "$baseline_signers" = "$candidate_signers" ] \
    || fail "baseline and candidate APK signers differ"

ADB=("$ADB_EXECUTABLE" -s "$DEVICE_SERIAL")

device_is_unlocked() {
    local state
    state="$("${ADB[@]}" shell dumpsys trust 2>/dev/null | tr -d '\r')"
    printf '%s\n' "$state" \
        | grep -Eq '\(current\).*deviceLocked=(0|false)([^0-9A-Za-z]|$)'
}

read_battery_level() {
    "${ADB[@]}" shell dumpsys battery 2>/dev/null \
        | sed -nE 's/^[[:space:]]*level:[[:space:]]*([0-9]+).*/\1/p' \
        | head -n 1
}

"${ADB[@]}" get-state >/dev/null 2>&1 || fail "device is not ready"
abi_list="$("${ADB[@]}" shell getprop ro.product.cpu.abilist 2>/dev/null | tr -d '\r')"
[[ ",$abi_list," == *,arm64-v8a,* ]] || fail "physical ARM64 device required"
qemu="$("${ADB[@]}" shell getprop ro.kernel.qemu 2>/dev/null | tr -d '\r')"
[ "$qemu" != "1" ] || fail "physical device required"
"${ADB[@]}" shell pm path "$PACKAGE_NAME" 2>/dev/null | grep -q '^package:' \
    || fail "package must already be installed"
device_is_unlocked || {
        echo "ERROR: device must be manually unlocked before APK installation" >&2
        exit 4
    }
battery_level="$(read_battery_level)"
[[ "$battery_level" =~ ^[0-9]+$ ]] || fail "battery level unavailable"
[ "$battery_level" -ge "$MIN_BATTERY" ] || {
    echo "ERROR: battery is below the configured floor" >&2
    exit 5
}

read_thermal_status() {
    local thermal
    thermal="$("${ADB[@]}" shell cmd thermalservice get-current-thermal-status \
        2>/dev/null | tr -d '\r' \
        | sed -nE 's/^[[:space:]]*([0-6])[[:space:]]*$/\1/p' | head -n 1 || true)"
    if [[ "$thermal" =~ ^[0-6]$ ]]; then
        printf '%s' "$thermal"
        return
    fi
    "${ADB[@]}" shell dumpsys thermalservice 2>/dev/null | tr -d '\r' \
        | sed -nE 's/^[[:space:]]*Thermal Status:[[:space:]]*([0-6]).*/\1/p' \
        | head -n 1
}

wait_for_thermal_gate() {
    local started now thermal
    started="$(date +%s)"
    while true; do
        thermal="$(read_thermal_status)"
        if [[ "$thermal" =~ ^[0-6]$ ]] \
            && [ "$thermal" -le "$MAX_START_THERMAL_STATUS" ]; then
            return 0
        fi
        now="$(date +%s)"
        if [ $((now - started)) -ge "$THERMAL_WAIT_SECONDS" ]; then
            return 8
        fi
        if [ "${STS2_DEVICE_PERFORMANCE_TEST_FAST:-0}" = "1" ]; then
            return 8
        fi
        sleep 5
    done
}

set +e
wait_for_thermal_gate
thermal_gate_status=$?
set -e
[ "$thermal_gate_status" -eq 0 ] || {
    echo "ERROR: device did not reach the configured thermal band" >&2
    exit "$thermal_gate_status"
}

restore_candidate() {
    "${ADB[@]}" shell am force-stop "$PACKAGE_NAME" >/dev/null 2>&1 || true
    if "${ADB[@]}" install -r -d "$CANDIDATE_APK" >/dev/null 2>&1; then
        RESTORE_COMPLETE=true
        return 0
    fi
    return 1
}

cleanup() {
    local exit_status=$?
    trap - EXIT INT TERM
    if [ -n "$SUMMARY_VALUES_FILE" ] && [ -f "$SUMMARY_VALUES_FILE" ]; then
        unlink "$SUMMARY_VALUES_FILE" >/dev/null 2>&1 || true
    fi
    if [ "$MUTATION_STARTED" = true ] && [ "$RESTORE_COMPLETE" != true ]; then
        if ! restore_candidate; then
            echo "ERROR: candidate restoration failed" >&2
            if [ "$exit_status" -eq 0 ]; then
                exit_status=9
            fi
        fi
    fi
    exit "$exit_status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

mkdir -p "$OUTPUT_ROOT"
RESULT_HEADER='format_version\tpair\torder\tvariant\tstatus\tterminal\tprocess_to_ui_ms\tactivation_wait_ms\tplay_to_game_ready_ms\tlaunch_to_game_ready_ms\telapsed_ms\tstart_battery_deci_c\tend_battery_deci_c\tstart_thermal_status\tend_thermal_status\tpid_continuity\tfatal_count\tanr_count\tlmk_count\tsurface_error_count\tandroid_process_ms\tinstall_recovery_ms\tcache_sync_ms\tassembly_sync_ms\tgodot_bootstrap_ms\tlauncher_creation_ms\tlauncher_ready_ms\trecovery_choice_ms\tuser_wait_ms\tcloud_sync_ms\tshader_warmup_ms\tgame_settings_ms\tgame_startup_ms\tmod_discovery_ms\tmod_load_ms\tgame_ready_ms'
if [ "$RESUME" != true ]; then
    printf '%b\n' "$RESULT_HEADER" >"$RESULT_FILE"
elif [ "$(head -n 1 "$RESULT_FILE")" != "$(printf '%b' "$RESULT_HEADER")" ]; then
    fail "resume evidence schema mismatch"
fi

append_empty_stage_values() {
    local index
    for index in $(seq 1 16); do
        printf '\t-' >>"$RESULT_FILE"
    done
    printf '\n' >>"$RESULT_FILE"
}

validate_resume_prefix() {
    local row_number=0 pair order variant status terminal rest
    local expected_pair expected_order expected_variant
    while IFS=$'\t' read -r _format pair order variant status terminal rest; do
        row_number=$((row_number + 1))
        expected_pair=$(( (row_number - 1) / 2 + 1 ))
        expected_order=$(( (row_number - 1) % 2 + 1 ))
        if [ $((expected_pair % 2)) -eq 1 ]; then
            if [ "$expected_order" -eq 1 ]; then
                expected_variant="baseline"
            else
                expected_variant="candidate"
            fi
        elif [ "$expected_order" -eq 1 ]; then
            expected_variant="candidate"
        else
            expected_variant="baseline"
        fi
        [ "$pair" = "$expected_pair" ] && [ "$order" = "$expected_order" ] \
            && [ "$variant" = "$expected_variant" ] \
            || fail "resume evidence arm order mismatch"
        [ "$status" = "pass" ] && [ "$terminal" = "game-ready" ] \
            || fail "resume evidence contains a non-passing arm"
    done < <(tail -n +2 "$RESULT_FILE")
    [ "$row_number" -le $((PAIR_COUNT * 2)) ] \
        || fail "resume evidence exceeds requested pair count"
    COMPLETED_ARMS="$row_number"
}

if [ "$RESUME" = true ]; then
    validate_resume_prefix
fi

run_arm() {
    local pair="$1"
    local order="$2"
    local variant="$3"
    local apk="$4"
    local run_dir matrix_file matrix_status row total row_status stage_row
    local _format _scenario _iteration terminal attempt stage pid_continuity
    local process_to_ui_ms activation_wait_ms play_to_ready_ms elapsed_ms
    local start_temperature end_temperature start_thermal end_thermal
    local previous_exit recovery_pending fatal_count anr_count lmk_count surface_count
    local _stage_format _stage_scenario _stage_iteration _stage_status
    local -a stage_values

    set +e
    wait_for_thermal_gate
    local arm_thermal_status=$?
    set -e
    if [ "$arm_thermal_status" -ne 0 ]; then
        return "$arm_thermal_status"
    fi
    if ! device_is_unlocked; then
        return 4
    fi
    local arm_battery
    arm_battery="$(read_battery_level)"
    if ! [[ "$arm_battery" =~ ^[0-9]+$ ]] || [ "$arm_battery" -lt "$MIN_BATTERY" ]; then
        return 5
    fi

    MUTATION_STARTED=true
    if ! "${ADB[@]}" install -r -d "$apk" >/dev/null 2>&1; then
        printf '1\t%s\t%s\t%s\tinstall-failed\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-' \
            "$pair" "$order" "$variant" >>"$RESULT_FILE"
        append_empty_stage_values
        return 6
    fi

    printf -v run_dir '%s/pair-%02d-order-%d' "$OUTPUT_ROOT" "$pair" "$order"
    mkdir -p "$run_dir"
    matrix_file="$run_dir/matrix.tsv"
    set +e
    "$MATRIX_SCRIPT" \
        --adb "$ADB_EXECUTABLE" \
        --serial "$DEVICE_SERIAL" \
        --output "$matrix_file" \
        --scenario cold-start \
        --iterations 1 \
        --launcher-boundary ui \
        --play-x "$PLAY_X" \
        --play-y "$PLAY_Y" \
        --timeout "$TIMEOUT_SECONDS" \
        --max-thermal-status "$MAX_THERMAL_STATUS" \
        --thermal-wait-seconds 0 \
        --allow-device-actions >/dev/null 2>&1
    matrix_status=$?
    set -e

    [ -f "$matrix_file" ] || {
        printf '1\t%s\t%s\t%s\tcapture-failed\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-' \
            "$pair" "$order" "$variant" >>"$RESULT_FILE"
        append_empty_stage_values
        return 7
    }
    row="$(tail -n 1 "$matrix_file")"
    IFS=$'\t' read -r _format _scenario _iteration row_status terminal attempt stage \
        pid_continuity process_to_ui_ms activation_wait_ms play_to_ready_ms elapsed_ms \
        start_temperature end_temperature start_thermal end_thermal previous_exit \
        recovery_pending fatal_count anr_count lmk_count surface_count <<<"$row"
    total="-"
    if [[ "$process_to_ui_ms" =~ ^[0-9]+$ \
        && "$activation_wait_ms" =~ ^[0-9]+$ \
        && "$play_to_ready_ms" =~ ^[0-9]+$ ]]; then
        total=$((process_to_ui_ms + activation_wait_ms + play_to_ready_ms))
    fi
    stage_values=(- - - - - - - - - - - - - - - -)
    if [ -f "$matrix_file.stages.tsv" ]; then
        stage_row="$(tail -n 1 "$matrix_file.stages.tsv")"
        IFS=$'\t' read -r _stage_format _stage_scenario _stage_iteration _stage_status \
            stage_values[0] stage_values[1] stage_values[2] stage_values[3] \
            stage_values[4] stage_values[5] stage_values[6] stage_values[7] \
            stage_values[8] stage_values[9] stage_values[10] stage_values[11] \
            stage_values[12] stage_values[13] stage_values[14] stage_values[15] \
            <<<"$stage_row"
    fi
    printf '1\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s' \
        "$pair" "$order" "$variant" "${row_status:-capture-failed}" "${terminal:--}" \
        "${process_to_ui_ms:--}" "${activation_wait_ms:--}" "${play_to_ready_ms:--}" \
        "$total" "${elapsed_ms:--}" "${start_temperature:--}" "${end_temperature:--}" \
        "${start_thermal:--}" "${end_thermal:--}" "${pid_continuity:--}" \
        "${fatal_count:--}" "${anr_count:--}" "${lmk_count:--}" "${surface_count:--}" \
        >>"$RESULT_FILE"
    printf '\t%s' "${stage_values[@]}" >>"$RESULT_FILE"
    printf '\n' >>"$RESULT_FILE"

    if [ "$row_status" = "thermal-invalid" ]; then
        return 8
    fi
    if [ "$matrix_status" -ne 0 ] || [ "$row_status" != "pass" ] \
        || [ "$terminal" != "game-ready" ]; then
        return 7
    fi
    return 0
}

percentile_from_sorted() {
    local values_file="$1"
    local percentile="$2"
    local count rank
    count="$(wc -l <"$values_file" | tr -d ' ')"
    [ "$count" -gt 0 ] || {
        printf '-'
        return
    }
    rank=$(( (count * percentile + 99) / 100 ))
    sed -n "${rank}p" "$values_file"
}

write_summaries() {
    local summary_file="$OUTPUT_ROOT/startup-summary.tsv"
    local comparison_file="$OUTPUT_ROOT/startup-comparison.tsv"
    local stage_summary_file="$OUTPUT_ROOT/startup-stage-summary.tsv"
    local values_file variant metric column count p50 p95 p99 maximum
    local baseline_p50 candidate_p50 baseline_p95 candidate_p95
    local p50_bps p95_bps p50_gate p95_gate stage_index stage_name
    local -a metrics metric_columns stage_names
    metrics=(process_to_ui activation_wait play_to_game_ready launch_to_game_ready)
    metric_columns=(7 8 9 10)
    stage_names=(
        android_process install_recovery cache_sync assembly_sync godot_bootstrap
        launcher_creation launcher_ready recovery_choice user_wait cloud_sync
        shader_warmup game_settings game_startup mod_discovery mod_load game_ready
    )
    values_file="$(mktemp "$OUTPUT_ROOT/.startup-values.XXXXXX")"
    SUMMARY_VALUES_FILE="$values_file"

    printf 'format_version\tvariant\tsamples\tmetric\tp50_ms\tp95_ms\tp99_ms\tmax_ms\n' \
        >"$summary_file"
    for variant in baseline candidate; do
        for index in 0 1 2 3; do
            metric="${metrics[$index]}"
            column="${metric_columns[$index]}"
            awk -F '\t' -v variant="$variant" -v column="$column" '
                NR > 1 && $4 == variant && $5 == "pass" && $column ~ /^[0-9]+$/ {
                    print $column
                }
            ' "$RESULT_FILE" | LC_ALL=C sort -n >"$values_file"
            count="$(wc -l <"$values_file" | tr -d ' ')"
            [ "$count" -eq "$PAIR_COUNT" ] \
                || fail "startup summary sample count mismatch"
            p50="$(percentile_from_sorted "$values_file" 50)"
            p95="$(percentile_from_sorted "$values_file" 95)"
            p99="$(percentile_from_sorted "$values_file" 99)"
            maximum="$(tail -n 1 "$values_file")"
            printf '1\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
                "$variant" "$count" "$metric" "$p50" "$p95" "$p99" "$maximum" \
                >>"$summary_file"
        done
    done

    printf 'format_version\tmetric\tbaseline_p50_ms\tcandidate_p50_ms\tp50_improvement_basis_points\tp50_gate\tbaseline_p95_ms\tcandidate_p95_ms\tp95_change_basis_points\tp95_gate\n' \
        >"$comparison_file"
    for metric in "${metrics[@]}"; do
        baseline_p50="$(awk -F '\t' -v metric="$metric" \
            '$2 == "baseline" && $4 == metric { print $5 }' "$summary_file")"
        candidate_p50="$(awk -F '\t' -v metric="$metric" \
            '$2 == "candidate" && $4 == metric { print $5 }' "$summary_file")"
        baseline_p95="$(awk -F '\t' -v metric="$metric" \
            '$2 == "baseline" && $4 == metric { print $6 }' "$summary_file")"
        candidate_p95="$(awk -F '\t' -v metric="$metric" \
            '$2 == "candidate" && $4 == metric { print $6 }' "$summary_file")"
        p50_bps="-"
        p95_bps="-"
        p50_gate="unavailable"
        p95_gate="unavailable"
        if [ "$baseline_p50" -gt 0 ]; then
            p50_bps="$(awk -v baseline="$baseline_p50" -v candidate="$candidate_p50" \
                'BEGIN { printf "%.0f", (baseline - candidate) * 10000 / baseline }')"
            if [ "$p50_bps" -ge 1000 ]; then p50_gate="pass"; else p50_gate="fail"; fi
        fi
        if [ "$baseline_p95" -gt 0 ]; then
            p95_bps="$(awk -v baseline="$baseline_p95" -v candidate="$candidate_p95" \
                'BEGIN { printf "%.0f", (candidate - baseline) * 10000 / baseline }')"
            if [ "$p95_bps" -le 500 ]; then p95_gate="pass"; else p95_gate="fail"; fi
        fi
        printf '1\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
            "$metric" "$baseline_p50" "$candidate_p50" "$p50_bps" "$p50_gate" \
            "$baseline_p95" "$candidate_p95" "$p95_bps" "$p95_gate" \
            >>"$comparison_file"
    done

    printf 'format_version\tvariant\tstage\tsamples\tp50_ms\tp95_ms\tp99_ms\tmax_ms\n' \
        >"$stage_summary_file"
    for variant in baseline candidate; do
        for stage_index in $(seq 0 15); do
            stage_name="${stage_names[$stage_index]}"
            column=$((21 + stage_index))
            awk -F '\t' -v variant="$variant" -v column="$column" '
                NR > 1 && $4 == variant && $5 == "pass" && $column ~ /^[0-9]+$/ {
                    print $column
                }
            ' "$RESULT_FILE" | LC_ALL=C sort -n >"$values_file"
            count="$(wc -l <"$values_file" | tr -d ' ')"
            if [ "$count" -eq 0 ]; then
                printf '1\t%s\t%s\t0\t-\t-\t-\t-\n' "$variant" "$stage_name" \
                    >>"$stage_summary_file"
                continue
            fi
            p50="$(percentile_from_sorted "$values_file" 50)"
            p95="$(percentile_from_sorted "$values_file" 95)"
            p99="$(percentile_from_sorted "$values_file" 99)"
            maximum="$(tail -n 1 "$values_file")"
            printf '1\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
                "$variant" "$stage_name" "$count" "$p50" "$p95" "$p99" "$maximum" \
                >>"$stage_summary_file"
        done
    done

    unlink "$values_file"
    SUMMARY_VALUES_FILE=""
}

pair=1
arm_sequence=0
while [ "$pair" -le "$PAIR_COUNT" ]; do
    if [ $((pair % 2)) -eq 1 ]; then
        variants=(baseline candidate)
        apks=("$BASELINE_APK" "$CANDIDATE_APK")
    else
        variants=(candidate baseline)
        apks=("$CANDIDATE_APK" "$BASELINE_APK")
    fi
    for index in 0 1; do
        order=$((index + 1))
        arm_sequence=$((arm_sequence + 1))
        if [ "$arm_sequence" -le "$COMPLETED_ARMS" ]; then
            echo "SKIP: pair=$pair/$PAIR_COUNT order=$order already valid"
            continue
        fi
        echo "RUN: pair=$pair/$PAIR_COUNT order=$order variant=${variants[$index]}"
        set +e
        run_arm "$pair" "$order" "${variants[$index]}" "${apks[$index]}"
        arm_status=$?
        set -e
        if [ "$arm_status" -ne 0 ]; then
            case "$arm_status" in
                4)
                    echo "PARTIAL: device locked before the next startup arm" >&2
                    exit 4
                    ;;
                5)
                    echo "PARTIAL: battery fell below the configured floor" >&2
                    exit 5
                    ;;
                8)
                    echo "PARTIAL: thermal-invalid startup arm" >&2
                    exit 8
                    ;;
                *)
                    echo "PARTIAL: startup arm did not reach game-ready" >&2
                    exit 7
                    ;;
            esac
        fi
    done
    pair=$((pair + 1))
done

if ! restore_candidate; then
    echo "ERROR: final candidate restoration failed" >&2
    exit 9
fi

write_summaries

echo "PASS: $PAIR_COUNT interleaved startup A/B pair(s)"
