#!/usr/bin/env bash
set -euo pipefail

PACKAGE_NAME="com.game.sts2launcher.modmanager"
DEVICE_SERIAL=""
OUTPUT_DIR=""
REQUIRE_PHYSICAL=false
REQUIRE_ARM64=false
INCLUDE_LOGCAT=false
ADB_EXECUTABLE="${STS2_ADB_EXECUTABLE:-adb}"

usage() {
    cat <<'EOF'
Usage: capture.sh --serial SERIAL --output DIRECTORY [options]

Create a bounded, read-only Android stability snapshot. This command never
installs, uninstalls, clears, stops, or launches the application.

Options:
  --package NAME       Android package (default: com.game.sts2launcher.modmanager)
  --require-physical   Exit 3 when the target is an emulator
  --require-arm64      Exit 4 when arm64-v8a is not in the ABI list
  --include-logcat     Include the last 4000 relevant log lines (may contain private data)
  --adb PATH           adb executable (default: adb)
  --help               Show this help
EOF
}

fail() {
    echo "ERROR: $*" >&2
    exit 1
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
        --package)
            [ "$#" -ge 2 ] || fail "--package needs a value"
            PACKAGE_NAME="$2"
            shift 2
            ;;
        --require-physical)
            REQUIRE_PHYSICAL=true
            shift
            ;;
        --require-arm64)
            REQUIRE_ARM64=true
            shift
            ;;
        --include-logcat)
            INCLUDE_LOGCAT=true
            shift
            ;;
        --adb)
            [ "$#" -ge 2 ] || fail "--adb needs a value"
            ADB_EXECUTABLE="$2"
            shift 2
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
[ -n "$OUTPUT_DIR" ] || fail "--output is required"
[[ "$PACKAGE_NAME" =~ ^[A-Za-z0-9._]+$ ]] || fail "Invalid package name: $PACKAGE_NAME"
[ ! -e "$OUTPUT_DIR" ] || fail "Output already exists: $OUTPUT_DIR"
command -v "$ADB_EXECUTABLE" >/dev/null 2>&1 || fail "adb not found: $ADB_EXECUTABLE"

ADB=("$ADB_EXECUTABLE" -s "$DEVICE_SERIAL")
"${ADB[@]}" get-state >/dev/null 2>&1 || fail "Device is not ready: $DEVICE_SERIAL"
mkdir -p "$OUTPUT_DIR"

capture() {
    local output_name="$1"
    shift
    {
        printf 'command:'
        printf ' %q' "$@"
        printf '\n\n'
        "$@"
    } >"$OUTPUT_DIR/$output_name" 2>&1 || true
}

get_prop() {
    "${ADB[@]}" shell getprop "$1" 2>/dev/null | tr -d '\r'
}

QEMU_PROPERTY="$(get_prop ro.kernel.qemu)"
BOOT_QEMU_PROPERTY="$(get_prop ro.boot.qemu)"
HARDWARE="$(get_prop ro.hardware)"
ABI_LIST="$(get_prop ro.product.cpu.abilist)"
DEVICE_KIND="physical"
if [ "$QEMU_PROPERTY" = "1" ] || [ "$BOOT_QEMU_PROPERTY" = "1" ] \
    || [[ "$DEVICE_SERIAL" == emulator-* ]] \
    || [[ "$HARDWARE" =~ ^(goldfish|ranchu|cutf_cvm)$ ]]; then
    DEVICE_KIND="emulator"
fi

PACKAGE_PATH="$("${ADB[@]}" shell pm path "$PACKAGE_NAME" 2>/dev/null | tr -d '\r' || true)"
PACKAGE_STATE="absent"
if [[ "$PACKAGE_PATH" == package:* ]]; then
    PACKAGE_STATE="installed"
fi

{
    echo "format_version=1"
    echo "serial=$DEVICE_SERIAL"
    echo "device_kind=$DEVICE_KIND"
    echo "package=$PACKAGE_NAME"
    echo "package_state=$PACKAGE_STATE"
    echo "manufacturer=$(get_prop ro.product.manufacturer)"
    echo "model=$(get_prop ro.product.model)"
    echo "device=$(get_prop ro.product.device)"
    echo "sdk=$(get_prop ro.build.version.sdk)"
    echo "abi=$ABI_LIST"
    echo "hardware=$HARDWARE"
    echo "egl=$(get_prop ro.hardware.egl)"
    echo "vulkan=$(get_prop ro.hardware.vulkan)"
    echo "qemu=$QEMU_PROPERTY"
    echo "boot_qemu=$BOOT_QEMU_PROPERTY"
    echo "captured_utc=$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
} >"$OUTPUT_DIR/summary.txt"

capture device-transport.txt "${ADB[@]}" get-serialno
capture device-state.txt "${ADB[@]}" get-state
capture package-path.txt "${ADB[@]}" shell pm path "$PACKAGE_NAME"
capture prior-exits.txt "${ADB[@]}" shell dumpsys activity exit-info "$PACKAGE_NAME"
capture activity-state.txt "${ADB[@]}" shell dumpsys activity activities "$PACKAGE_NAME"
if [ "$PACKAGE_STATE" = "installed" ]; then
    capture package-dump.txt "${ADB[@]}" shell dumpsys package "$PACKAGE_NAME"
    capture process-memory.txt "${ADB[@]}" shell dumpsys meminfo "$PACKAGE_NAME"
    capture frame-stats.txt "${ADB[@]}" shell dumpsys gfxinfo "$PACKAGE_NAME"
else
    printf 'Package absent; package-specific dump not requested.\n' >"$OUTPUT_DIR/package-dump.txt"
    printf 'Package absent; process memory not requested.\n' >"$OUTPUT_DIR/process-memory.txt"
    printf 'Package absent; frame stats not requested.\n' >"$OUTPUT_DIR/frame-stats.txt"
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cp "$SCRIPT_DIR/matrix-template.tsv" "$OUTPUT_DIR/device-matrix.tsv"

if [ "$INCLUDE_LOGCAT" = true ]; then
    capture logcat.txt "${ADB[@]}" logcat -d -t 4000 -v threadtime \
        'StS2Launcher:V' 'Godot:V' 'godot:V' 'AndroidRuntime:E' \
        'ActivityManager:W' 'libc:F' 'DEBUG:F' '*:S'
fi

echo "Snapshot: $OUTPUT_DIR"
echo "Device:   $DEVICE_KIND ($DEVICE_SERIAL)"
echo "Package:  $PACKAGE_STATE ($PACKAGE_NAME)"

if [ "$REQUIRE_PHYSICAL" = true ] && [ "$DEVICE_KIND" != "physical" ]; then
    echo "ERROR: physical device required; snapshot records an emulator" >&2
    exit 3
fi

if [ "$REQUIRE_ARM64" = true ] && [[ ",$ABI_LIST," != *,arm64-v8a,* ]]; then
    echo "ERROR: arm64-v8a required; device reports: $ABI_LIST" >&2
    exit 4
fi
