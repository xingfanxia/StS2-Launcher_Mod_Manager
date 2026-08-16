#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

javac -d "$OUT" \
	"$ROOT/android/src/com/game/sts2launcher/modmanager/PreviousExitClassifier.java" \
	"$ROOT/android/src/com/game/sts2launcher/modmanager/StartupRecoveryJournal.java" \
	"$ROOT/android/src/com/game/sts2launcher/modmanager/StartupCacheWiper.java" \
	"$ROOT/tools/stability-tests-java/PreviousExitClassifierTest.java" \
	"$ROOT/tools/stability-tests-java/StartupRecoveryJournalTest.java" \
	"$ROOT/tools/stability-tests-java/StartupCacheWiperTest.java"
java -cp "$OUT" com.game.sts2launcher.modmanager.PreviousExitClassifierTest
java -cp "$OUT" com.game.sts2launcher.modmanager.StartupRecoveryJournalTest
java -cp "$OUT" com.game.sts2launcher.modmanager.StartupCacheWiperTest
