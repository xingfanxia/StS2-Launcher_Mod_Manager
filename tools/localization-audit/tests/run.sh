#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PROJECT="tools/localization-audit/localization-audit.csproj"
FIXTURE="$REPO_ROOT/src/STS2Mobile/Launcher/Components/LocalizationAuditNegativeFixture.cs"
OUTPUT_FILE="$(mktemp)"

cleanup() {
    rm -f "$FIXTURE" "$OUTPUT_FILE"
}
trap cleanup EXIT

cd "$REPO_ROOT"
dotnet run --project "$PROJECT"

printf '%s\n' \
    'namespace STS2Mobile.Launcher.Components;' \
    'internal static class LocalizationAuditNegativeFixture' \
    '{' \
    '    public const string VisibleLauncherText = "의도적으로 번역되지 않은 문장";' \
    '}' >"$FIXTURE"

if dotnet run --project "$PROJECT" >"$OUTPUT_FILE" 2>&1; then
    echo "ERROR: localization audit accepted an untranslated launcher fixture" >&2
    exit 1
fi

grep -Fq "untranslated launcher text" "$OUTPUT_FILE" || {
    echo "ERROR: localization audit failed for an unexpected reason" >&2
    exit 1
}

echo "PASS: untranslated launcher fixture is rejected"
