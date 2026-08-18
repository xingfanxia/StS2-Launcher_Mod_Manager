#!/usr/bin/env bash
set -euo pipefail

if [ -n "${FAKE_COMMAND_LOG:-}" ]; then
    printf '%q ' "$@" >>"$FAKE_COMMAND_LOG"
    printf '\n' >>"$FAKE_COMMAND_LOG"
fi

if [ "${1:-}" = "-s" ]; then
    shift 2
fi

case "${1:-}" in
    install)
        if [ -n "${FAKE_INSTALLED_APK_STATE:-}" ]; then
            basename "${*: -1}" >"$FAKE_INSTALLED_APK_STATE"
        fi
        echo Success
        ;;
    get-state)
        echo device
        ;;
    get-serialno)
        echo fake-serial
        ;;
    devices)
        echo 'List of devices attached'
        echo "fake-serial device product:fake model:Fake_Device device:fake transport_id:1"
        ;;
    shell)
        shift
        case "${1:-}" in
            getprop)
                case "${2:-}" in
                    ro.kernel.qemu) echo "${FAKE_QEMU:-0}" ;;
                    ro.boot.qemu) echo "${FAKE_BOOT_QEMU:-0}" ;;
                    ro.hardware) echo "${FAKE_HARDWARE:-physical_board}" ;;
                    ro.product.manufacturer) echo Example ;;
                    ro.product.model) echo 'Proof Device' ;;
                    ro.product.device) echo proof_device ;;
                    ro.build.version.sdk) echo 35 ;;
                    ro.product.cpu.abilist) echo "${FAKE_ABI_LIST:-arm64-v8a}" ;;
                    ro.hardware.egl) echo fake_egl ;;
                    ro.hardware.vulkan) echo fake_vulkan ;;
                esac
                ;;
            pm)
                if [ "${2:-}" = "path" ] && [ "${FAKE_PACKAGE_INSTALLED:-0}" = "1" ]; then
                    echo 'package:/data/app/fake/base.apk'
                fi
                ;;
            dumpsys)
                case "${2:-}" in
                    battery)
                        echo '  level: 80'
                        echo '  temperature: 295'
                        ;;
                    thermalservice)
                        echo "Thermal Status: ${FAKE_THERMAL_STATUS:-0}"
                        ;;
                    package)
                        echo "versionName=${FAKE_VERSION_NAME:-0.4.2-debug}"
                        ;;
                    trust)
                        echo " User \"owner\" (id=0) (current): deviceLocked=${FAKE_DEVICE_LOCKED:-0}"
                        ;;
                    activity)
                        echo 'topResumedActivity=fake com.game.sts2launcher.modmanager/.GodotApp'
                        ;;
                    *)
                        echo "fake dumpsys ${*:2}"
                        ;;
                esac
                ;;
            cmd)
                echo "${FAKE_THERMAL_STATUS:-0}"
                ;;
            pidof)
                echo 123
                ;;
            am|input)
                ;;
            settings)
                if [ "${2:-}" = "get" ]; then
                    echo 0
                fi
                ;;
            *)
                echo "unexpected fake shell command: $*" >&2
                exit 2
                ;;
        esac
        ;;
    logcat)
        if printf '%s\n' "$*" | grep -q -- '-c'; then
            exit 0
        fi
        if printf '%s\n' "$*" | grep -q -- '--pid'; then
            exit 0
        fi
        if printf '%s\n' "$*" | grep -q -- '-d'; then
            echo 'fake bounded logcat'
            exit 0
        fi
        cat <<'EOF'
I/Godot: [StartupRecovery] attempt=1 stage=launcher-creating
I/Godot: Launcher UI displayed.
I/Godot: Launcher ready for PLAY.
I/Godot: User launched game, proceeding to startup.
I/Godot: [StartupPerformance/NativeSummary] v1;1|1|1|0|1000;2|1|4|1|3000;3|2|1|0|3000;4|2|4|1|7000;
I/Godot: [StartupPerformance/Summary] v2;6|10000;16|0;
EOF
        installed_apk=""
        if [ -n "${FAKE_INSTALLED_APK_STATE:-}" ] && [ -f "$FAKE_INSTALLED_APK_STATE" ]; then
            installed_apk="$(cat "$FAKE_INSTALLED_APK_STATE")"
        fi
        if [ -z "${FAKE_FAIL_WHEN_INSTALLED:-}" ] \
            || [ "$installed_apk" != "$FAKE_FAIL_WHEN_INSTALLED" ]; then
            echo 'I/Godot: [StartupRecovery] healthy stage=game-ready'
        fi
        ;;
    *)
        echo "unexpected fake adb command: $*" >&2
        exit 2
        ;;
esac
