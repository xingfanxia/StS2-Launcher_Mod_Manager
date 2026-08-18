#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET_IMAGE="${STS2_TEST_DOTNET_IMAGE:-mcr.microsoft.com/dotnet/sdk:9.0-noble@sha256:840c88158a49d65ab59451369988a0db6b420ab3ea7948a5e261c56a4ab3404d}"

usage() {
    cat <<'EOF'
Usage: tools/test-workflow.sh [focused]

Run the standard fast pre-APK gate:
  1. diff/whitespace validation
  2. device-performance harness contracts
  3. Android lifecycle/recovery Java contracts
  4. managed stability and localization contracts in a disposable container

This command does not build/install an APK or mutate a connected device.
EOF
}

case "${1:-focused}" in
    focused)
        ;;
    --help|-h)
        usage
        exit 0
        ;;
    *)
        usage >&2
        exit 1
        ;;
esac

case "$(uname -m)" in
    arm64|aarch64)
        CONTAINER_PLATFORM="linux/arm64"
        ;;
    x86_64|amd64)
        CONTAINER_PLATFORM="linux/amd64"
        ;;
    *)
        echo "ERROR: unsupported host architecture: $(uname -m)" >&2
        exit 1
        ;;
esac

run_step() {
    local label="$1"
    shift
    local started=$SECONDS
    echo "STEP: $label"
    "$@"
    echo "PASS: $label ($((SECONDS - started))s)"
}

run_managed_contracts() {
    command -v docker >/dev/null 2>&1 || {
        echo "ERROR: docker is required for the isolated managed contracts" >&2
        return 1
    }
    docker run --rm \
        --platform "$CONTAINER_PLATFORM" \
        -v "$ROOT:/src:ro" \
        -v /work \
        "$DOTNET_IMAGE" \
        bash -lc '
            set -euo pipefail
            mkdir -p /work/repo
            cd /src
            tar --exclude=.git --exclude="*/bin" --exclude="*/obj" -cf - . \
                | tar -xf - -C /work/repo
            cd /work/repo
            dotnet run --project tools/stability-tests/stability-tests.csproj
            bash tools/localization-audit/tests/run.sh
        '
}

cd "$ROOT"
run_step "diff check" git diff --check
run_step "device-performance contracts" bash tools/device-performance/tests/run.sh
run_step "Android lifecycle contracts" bash tools/stability-tests-java/run.sh
run_step "managed stability/localization contracts" run_managed_contracts

echo "PASS: focused pre-APK workflow"
