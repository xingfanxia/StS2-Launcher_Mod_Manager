#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
TESTS="$ROOT/tools/memberref-audit/tests"
CONFIG=Release

dotnet build "$TESTS/new-contract/Contract.csproj" -c "$CONFIG" --nologo >/dev/null
dotnet build "$TESTS/consumer-broken/Consumer.csproj" -c "$CONFIG" --nologo >/dev/null
dotnet build "$TESTS/consumer-compatible/Consumer.csproj" -c "$CONFIG" --nologo >/dev/null
dotnet build "$ROOT/tools/memberref-audit/audit.csproj" -c "$CONFIG" --nologo >/dev/null

AUDIT="$ROOT/tools/memberref-audit/bin/$CONFIG/net9.0/audit.dll"
TARGET="$TESTS/new-contract/bin/$CONFIG/net9.0/gamecontract.dll"

set +e
broken_output="$(dotnet "$AUDIT" \
	"$TESTS/consumer-broken/bin/$CONFIG/net9.0/consumer-broken.dll" \
	"$TARGET" gamecontract 2>&1)"
broken_status=$?
set -e

if [[ $broken_status -ne 1 ]] || ! grep -q 'MISSING_INTERFACE_SLOT.*CopyFile' <<<"$broken_output"; then
	echo "$broken_output" >&2
	echo "FAIL newer interface member was not detected" >&2
	exit 1
fi
echo "PASS newer interface member is detected before runtime"

dotnet "$AUDIT" \
	"$TESTS/consumer-compatible/bin/$CONFIG/net9.0/consumer-compatible.dll" \
	"$TARGET" gamecontract
echo "PASS virtual forward-compatibility member satisfies newer interface"
