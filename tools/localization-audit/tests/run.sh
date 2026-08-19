#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PROJECT="tools/localization-audit/localization-audit.csproj"
FIXTURE="$REPO_ROOT/src/STS2Mobile/Launcher/Components/LocalizationAuditNegativeFixture.cs"
JAVA_FIXTURE="$REPO_ROOT/android/src/com/game/sts2launcher/modmanager/LocalizationAuditNegativeFixture.java"
OUTPUT_FILE="$(mktemp)"

cleanup() {
	rm -f "$FIXTURE" "$JAVA_FIXTURE" "$OUTPUT_FILE"
}
trap cleanup EXIT

cd "$REPO_ROOT"
dotnet run --project "$PROJECT"

expect_failure() {
	local expected="$1"
	if dotnet run --project "$PROJECT" >"$OUTPUT_FILE" 2>&1; then
		echo "ERROR: localization audit accepted a negative fixture" >&2
		exit 1
	fi
	grep -Fq "$expected" "$OUTPUT_FILE" || {
		echo "ERROR: localization audit failed for an unexpected reason" >&2
		cat "$OUTPUT_FILE" >&2
		exit 1
	}
}

printf '%s\n' \
    'namespace STS2Mobile.Launcher.Components;' \
    'internal static class LocalizationAuditNegativeFixture' \
    '{' \
	'    public const string VisibleLauncherText = "의도적으로 번역되지 않은 문장";' \
	'}' >"$FIXTURE"
expect_failure "untranslated launcher text"

printf '%s\n' \
	'namespace STS2Mobile.Launcher.Components;' \
	'internal static class LocalizationAuditNegativeFixture' \
	'{' \
	'    public static string VisibleLauncherText => Loc.Tr("새 중국어 누락 문장", "Missing Chinese sentence");' \
	'}' >"$FIXTURE"
expect_failure "invalid Loc.Tr Korean/English/zh-Hans path"

printf '%s\n' \
	'namespace STS2Mobile.Launcher.Components;' \
	'internal static class LocalizationAuditNegativeFixture' \
	'{' \
	'    public static string VisibleLauncherText => Loc.Tr("중국어 번체 혼입", "Traditional residue", "設定");' \
	'}' >"$FIXTURE"
expect_failure "invalid Loc.Tr Korean/English/zh-Hans path"

printf '%s\n' \
	'namespace STS2Mobile.Launcher.Components;' \
	'internal static class LocalizationAuditNegativeFixture' \
	'{' \
	'    public static object Build(float scale) => new StyledLabel("Untranslated launcher sentence", scale);' \
	'}' >"$FIXTURE"
expect_failure "English launcher text lacks Simplified Chinese"

rm -f "$FIXTURE"
printf '%s\n' \
	'package com.game.sts2launcher.modmanager;' \
	'final class LocalizationAuditNegativeFixture {' \
	'    String visible() { return nativeText("번역 누락", "Missing translation"); }' \
	'}' >"$JAVA_FIXTURE"
expect_failure "nativeText Korean argument lacks English/zh-Hans paths"

echo "PASS: Korean, English, missing-zh, Traditional-residue, and Android-native negative fixtures are rejected"
