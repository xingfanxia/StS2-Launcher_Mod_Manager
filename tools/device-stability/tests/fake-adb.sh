#!/usr/bin/env bash
set -euo pipefail

if [ "${1:-}" = "-s" ]; then
    shift 2
fi

case "${1:-}" in
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
                echo "fake dumpsys ${*:2}"
                ;;
            *)
                echo "unexpected fake shell command: $*" >&2
                exit 2
                ;;
        esac
        ;;
    logcat)
        echo 'fake bounded logcat'
        ;;
    *)
        echo "unexpected fake adb command: $*" >&2
        exit 2
        ;;
esac
