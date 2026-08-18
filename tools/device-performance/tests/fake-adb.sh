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
    get-state)
        echo device
        ;;
    logcat)
        if [ "${2:-}" = "-c" ]; then
            exit 0
        fi
        frame_mode="${FAKE_FRAME_MODE:-game-120}"
        emit_summary() {
            local mode="$1"
            local segment="gameplay-interactive"
            local target="120s"
            local samples=7000
            local elapsed=120001
            if [ "$mode" = "game-safe-300" ]; then
                samples=17500
                elapsed=300001
            elif [[ "$mode" == game-menu-* ]]; then
                segment="game-menu-idle"
                target="60s"
                samples=3500
                elapsed=60001
            fi
            cat <<EOF
I/Godot: [FrameProbe] started mode=$mode point=game-ready target=$target budget_us=16667
I/Godot: [FrameProbe] segment started mode=$mode segment=$segment target=$target budget_us=16667
I/Godot: [FrameProbe] spike elapsed_ms=250 interval_us=52000 pipeline_canvas=12 pipeline_draw=3 pipeline_surface=2 pipeline_mesh=1 pipeline_specialization=0 private=/do/not/copy
I/Godot: [FrameProbe] summary mode=$mode segment=$segment samples=$samples elapsed_ms=$elapsed budget_us=16667 p50_us=16680 p95_us=18100 p99_us=20100 max_us=52000 over_1x=1800 over_2x=3 over_3x=1 max_consecutive_2x=1 over_50ms=1 over_100ms=0 over_250ms=0
EOF
        }
        echo 'I/Godot: Launcher UI displayed.'
        echo 'I/Godot: Launcher ready for PLAY.'
        if [ "${FAKE_DECK_CACHE_PROOF:-0}" = "1" ]; then
            echo 'I/Godot: [DeckCacheProbe] result obtain=1 remove=1 upgrade=1 restore=1 cleanup=1 error=0 pass=1'
        fi
        if [ "$frame_mode" = "all-menu" ]; then
            emit_summary game-menu-60
            emit_summary game-menu-safe-60
            emit_summary game-menu-partition-60
        elif [ "$frame_mode" = "all-game" ]; then
            emit_summary game-120
            emit_summary game-baseline-120
            emit_summary game-baseline-safe-120
            emit_summary game-baseline-partition-120
            emit_summary game-safe-120
            emit_summary game-safe-300
            emit_summary game-partition-120
        elif [ "$frame_mode" = "all-quickrestart" ]; then
            emit_summary game-quickrestart-baseline-partition-120
            emit_summary game-quickrestart-partition-120
        else
            emit_summary "$frame_mode"
        fi
        echo 'I/Godot: [GameplayPipelineWarmup] cover summary elapsed_us=900000'
        if [ "${FAKE_QR_METHOD_PROBE:-0}" = "1" ]; then
            if [[ "$frame_mode" == *baseline* ]]; then
                echo 'I/Godot: [QuickRestartProbe] summary segment=gameplay-interactive process_calls=7200 process_us=120000 can_restart_calls=7200 can_restart_us=90000 file_exists_calls=7200 reset_calls=7200 reset_us=30000'
            else
                echo 'I/Godot: [QuickRestartProbe] summary segment=gameplay-interactive process_calls=0 process_us=0 can_restart_calls=0 can_restart_us=0 file_exists_calls=0 reset_calls=0 reset_us=0'
            fi
            echo "I/Godot: [QuickRestartBehaviorProbe] summary segment=gameplay-interactive input_enable=${FAKE_QR_INPUT_ENABLE:-0} input_disable=${FAKE_QR_INPUT_DISABLE:-0} visible_frames=${FAKE_QR_VISIBLE_FRAMES:-0} restart_calls=${FAKE_QR_RESTART_CALLS:-0} pause_calls=${FAKE_QR_PAUSE_CALLS:-0}"
        fi
        if [ "${FAKE_MOD_LOAD_PROBE:-0}" = "1" ]; then
            echo 'I/Godot: [ModLoadProbe] initializer item=1 index=1 duration_us=7000 success=1'
            echo 'I/Godot: [ModLoadProbe] patchall item=1 index=1 duration_us=5000'
            echo 'I/Godot: [ModLoadProbe] item=1 total_us=13000 initializer_us=7000 patchall_us=5000 initializer_count=1 patchall_count=1 loaded=1'
        fi
        if [ "${FAKE_INTERACTION_PROBE:-}" = "map-open" ]; then
            echo 'I/Godot: [InteractionProbe] summary name=map-open samples=60 p50_us=16690 p95_us=18000 p99_us=80000 max_us=80000 over_2x=1 over_100ms=0'
        fi
        if [ "${FAKE_MOD_LOAD_ERROR:-0}" = "1" ]; then
            echo 'E/Godot: [ERROR] Exception thrown when calling mod initializer of type Private.Name'
        fi
        ;;
    shell)
        shift
        case "${1:-}" in
            getprop)
                case "${2:-}" in
                    ro.product.cpu.abilist) echo arm64-v8a ;;
                    ro.kernel.qemu) echo 0 ;;
                esac
                ;;
            pm)
                [ "${2:-}" = "path" ] && echo 'package:/data/app/fake/base.apk'
                ;;
            dumpsys)
                case "${2:-}" in
                    battery)
                        echo "  level: ${FAKE_BATTERY_LEVEL:-80}"
                        echo "  temperature: ${FAKE_BATTERY_TEMPERATURE:-295}"
                        ;;
                    package)
                        echo "versionName=${FAKE_VERSION_NAME:-0.4.2-debug}"
                        ;;
                    thermalservice)
                        echo "Thermal Status: ${FAKE_THERMAL_STATUS:-0}"
                        ;;
                    trust)
                        echo 'User "fake" (id=0) (current): deviceLocked=0'
                        ;;
                    power)
                        echo '  mWakefulness=Awake'
                        ;;
                esac
                ;;
            cmd)
                if [ "${FAKE_THERMAL_CMD_UNSUPPORTED:-0}" = "1" ]; then
                    echo 'Unknown command: get-current-thermal-status' >&2
                    exit 1
                fi
                echo "${FAKE_THERMAL_STATUS:-0}"
                ;;
            pidof)
                echo 123
                ;;
            cat)
                if [[ "${2:-}" == */status ]]; then
                    echo 'VmRSS:     456789 kB'
                elif [[ "${2:-}" == */stat ]]; then
                    echo '123 (fake) S 0 0 0 0 0 0 0 0 0 0 100 20 0 0 0 0 0'
                fi
                ;;
            getconf)
                [ "${2:-}" = "CLK_TCK" ] && echo 100
                ;;
            settings)
                if [ "${2:-}" = "get" ] && [ "${4:-}" = "screen_brightness_mode" ]; then
                    echo 1
                elif [ "${2:-}" = "get" ] && [ "${4:-}" = "screen_brightness" ]; then
                    echo 1000
                elif [ "${2:-}" != "put" ]; then
                    echo "unexpected fake settings command: $*" >&2
                    exit 2
                fi
                ;;
            am|input)
                ;;
            *)
                echo "unexpected fake shell command: $*" >&2
                exit 2
                ;;
        esac
        ;;
    *)
        echo "unexpected fake adb command: $*" >&2
        exit 2
        ;;
esac
