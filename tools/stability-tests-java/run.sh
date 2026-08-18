#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

javac -d "$OUT" \
	"$ROOT/android/src/com/game/sts2launcher/modmanager/PreviousExitClassifier.java" \
	"$ROOT/android/src/com/game/sts2launcher/modmanager/StartupRecoveryJournal.java" \
    "$ROOT/android/src/com/game/sts2launcher/modmanager/StartupPerformanceTimeline.java" \
    "$ROOT/android/src/com/game/sts2launcher/modmanager/PreviousExitReportGate.java" \
	"$ROOT/android/src/com/game/sts2launcher/modmanager/RendererRecoveryPolicy.java" \
	"$ROOT/android/src/com/game/sts2launcher/modmanager/StartupCacheWiper.java" \
	"$ROOT/android/src/com/game/sts2launcher/modmanager/GameInstallRecovery.java" \
	"$ROOT/tools/stability-tests-java/PreviousExitClassifierTest.java" \
	"$ROOT/tools/stability-tests-java/StartupRecoveryJournalTest.java" \
    "$ROOT/tools/stability-tests-java/StartupPerformanceTimelineTest.java" \
    "$ROOT/tools/stability-tests-java/PreviousExitReportGateTest.java" \
	"$ROOT/tools/stability-tests-java/RendererRecoveryPolicyTest.java" \
	"$ROOT/tools/stability-tests-java/StartupCacheWiperTest.java" \
	"$ROOT/tools/stability-tests-java/GameInstallRecoveryTest.java"
java -cp "$OUT" com.game.sts2launcher.modmanager.PreviousExitClassifierTest
java -cp "$OUT" com.game.sts2launcher.modmanager.StartupRecoveryJournalTest
java -cp "$OUT" com.game.sts2launcher.modmanager.StartupPerformanceTimelineTest
java -cp "$OUT" com.game.sts2launcher.modmanager.PreviousExitReportGateTest
java -cp "$OUT" com.game.sts2launcher.modmanager.RendererRecoveryPolicyTest
java -cp "$OUT" com.game.sts2launcher.modmanager.StartupCacheWiperTest
java -cp "$OUT" com.game.sts2launcher.modmanager.GameInstallRecoveryTest
