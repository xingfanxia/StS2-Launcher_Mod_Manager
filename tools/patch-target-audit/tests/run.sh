#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
FIXTURE="$ROOT/tools/memberref-audit/tests/new-contract"
TOOL="$ROOT/tools/patch-target-audit"
CONFIG=Release

dotnet build "$FIXTURE/Contract.csproj" -c "$CONFIG" --nologo >/dev/null
dotnet build "$TOOL/audit.csproj" -c "$CONFIG" --nologo >/dev/null

TARGET="$FIXTURE/bin/$CONFIG/net9.0/gamecontract.dll"
AUDIT="$TOOL/bin/$CONFIG/net9.0/patch-target-audit.dll"

dotnet "$AUDIT" "$TARGET" "$TOOL/tests/pass.tsv"
echo "PASS present, IL-shape, and optional-degradation rules"

for case_name in missing ambiguous; do
	set +e
	output="$(dotnet "$AUDIT" "$TARGET" "$TOOL/tests/$case_name.tsv" 2>&1)"
	status=$?
	set -e
	if [[ $status -ne 1 ]]; then
		echo "$output" >&2
		echo "FAIL $case_name target was not rejected" >&2
		exit 1
	fi
	echo "PASS $case_name target is rejected before runtime"
done
