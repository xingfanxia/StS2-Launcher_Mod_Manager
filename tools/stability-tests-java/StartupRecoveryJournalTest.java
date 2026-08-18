package com.game.sts2launcher.modmanager;

public final class StartupRecoveryJournalTest {
	private static int failures;

	public static void main(String[] args) {
		testTwoMatchingCrashesRequestRecovery();
		testDifferentStageOrFingerprintBreaksTheSequence();
		testPlannedAndUserExitsNeverCount();
		testBackgroundExitNeverCounts();
		testHealthyStartupClearsTheSequence();
		testExpiredFailureStartsAnew();
		testCodecRejectsTornState();
		testCandidateBoundaryRejectsPaths();

		if (failures != 0) {
			throw new AssertionError(failures + " startup-recovery journal test(s) failed");
		}
		System.out.println("All startup-recovery journal tests passed.");
	}

	private static void testTwoMatchingCrashesRequestRecovery() {
		StartupRecoveryJournal.State state = StartupRecoveryJournal.empty();
		StartupRecoveryJournal.BeginResult first =
				StartupRecoveryJournal.beginAttempt(state, 1_000L);
		state = StartupRecoveryJournal.setFingerprint(first.state, first.attemptId, "aabbcc11");
		state = StartupRecoveryJournal.recordStage(state, first.attemptId, "mod-loading");
		state = StartupRecoveryJournal.recordModCandidate(
				state, first.attemptId, "BaseLib");

		StartupRecoveryJournal.BeginResult second =
				StartupRecoveryJournal.beginAttempt(state, 2_000L);
		state = StartupRecoveryJournal.reconcileExit(
				second.state, PreviousExitClassifier.REASON_CRASH_NATIVE, 1_500L, 2_100L);
		check("first matching native crash is counted", state.failureCount == 1);
		check("one crash does not request recovery", !state.recoveryPending);

		state = StartupRecoveryJournal.setFingerprint(state, second.attemptId, "aabbcc11");
		state = StartupRecoveryJournal.recordStage(state, second.attemptId, "mod-loading");
		state = StartupRecoveryJournal.recordModCandidate(
				state, second.attemptId, "BaseLib");
		StartupRecoveryJournal.BeginResult third =
				StartupRecoveryJournal.beginAttempt(state, 3_000L);
		state = StartupRecoveryJournal.reconcileExit(
				third.state, PreviousExitClassifier.REASON_CRASH, 2_500L, 3_100L);

		StartupRecoveryJournal.RecoveryRequest request =
				StartupRecoveryJournal.getRecoveryRequest(state);
		check("second matching crash requests recovery", request.pending);
		check("recovery keeps normalized stage", "mod-loading".equals(request.stage));
		check("recovery reports only the candidate id", "BaseLib".equals(request.modCandidate));
		check("recovery preserves the terminal reason", "CRASH".equals(request.reason));
	}

	private static void testDifferentStageOrFingerprintBreaksTheSequence() {
		StartupRecoveryJournal.State state = crashOnce("cloud-sync", "1111aaaa", 10_000L);
		StartupRecoveryJournal.BeginResult next =
				StartupRecoveryJournal.beginAttempt(state, 12_000L);
		state = StartupRecoveryJournal.setFingerprint(next.state, next.attemptId, "2222bbbb");
		state = StartupRecoveryJournal.recordStage(state, next.attemptId, "cloud-sync");
		StartupRecoveryJournal.BeginResult after =
				StartupRecoveryJournal.beginAttempt(state, 13_000L);
		state = StartupRecoveryJournal.reconcileExit(
				after.state, PreviousExitClassifier.REASON_ANR, 12_500L, 13_100L);
		check("configuration change resets the sequence", state.failureCount == 1);
		check("configuration change does not request recovery", !state.recoveryPending);

		state = StartupRecoveryJournal.recordStage(state, after.attemptId, "shader-warmup");
		state = StartupRecoveryJournal.setFingerprint(state, after.attemptId, "2222bbbb");
		StartupRecoveryJournal.BeginResult finalAttempt =
				StartupRecoveryJournal.beginAttempt(state, 14_000L);
		state = StartupRecoveryJournal.reconcileExit(
				finalAttempt.state, PreviousExitClassifier.REASON_LOW_MEMORY, 13_500L, 14_100L);
		check("stage change also resets the sequence", state.failureCount == 1);
	}

	private static void testPlannedAndUserExitsNeverCount() {
		StartupRecoveryJournal.BeginResult first =
				StartupRecoveryJournal.beginAttempt(StartupRecoveryJournal.empty(), 20_000L);
		StartupRecoveryJournal.State state = StartupRecoveryJournal.recordStage(
				first.state, first.attemptId, "shader-warmup");
		state = StartupRecoveryJournal.markPlannedExit(state, first.attemptId, 20_500L);
		StartupRecoveryJournal.BeginResult second =
				StartupRecoveryJournal.beginAttempt(state, 21_000L);
		state = StartupRecoveryJournal.reconcileExit(
				second.state, PreviousExitClassifier.REASON_EXIT_SELF, 20_600L, 21_100L);
		check("planned restart never counts", state.failureCount == 0);

		state = StartupRecoveryJournal.recordStage(state, second.attemptId, "launcher-ready");
		StartupRecoveryJournal.BeginResult third =
				StartupRecoveryJournal.beginAttempt(state, 22_000L);
		state = StartupRecoveryJournal.reconcileExit(
				third.state, PreviousExitClassifier.REASON_USER_REQUESTED, 21_500L, 22_100L);
		check("user-requested exit never counts", state.failureCount == 0);
		check("non-actionable exits never request recovery", !state.recoveryPending);
	}

	private static void testHealthyStartupClearsTheSequence() {
		StartupRecoveryJournal.State state = crashOnce("game-startup", "1234abcd", 30_000L);
		StartupRecoveryJournal.BeginResult next =
				StartupRecoveryJournal.beginAttempt(state, 32_000L);
		state = StartupRecoveryJournal.markHealthy(next.state, next.attemptId, "game-ready");
		check("healthy startup clears failure count", state.failureCount == 0);
		check("healthy startup clears recovery request", !state.recoveryPending);
	}

	private static void testBackgroundExitNeverCounts() {
		StartupRecoveryJournal.State state = crashOnce(
				"launcher-awaiting-frame", "facefeed", 25_000L);
		StartupRecoveryJournal.BeginResult next =
				StartupRecoveryJournal.beginAttempt(state, 27_000L);
		state = StartupRecoveryJournal.setFingerprint(next.state, next.attemptId, "facefeed");
		state = StartupRecoveryJournal.recordStage(
				state, next.attemptId, "launcher-awaiting-frame");
		state = StartupRecoveryJournal.markForeground(state, next.attemptId, false);
		StartupRecoveryJournal.BeginResult after =
				StartupRecoveryJournal.beginAttempt(state, 28_000L);
		state = StartupRecoveryJournal.reconcileExit(
				after.state,
				PreviousExitClassifier.REASON_CRASH_NATIVE,
				27_500L,
				28_100L);
		check("background native exit clears the crash sequence", state.failureCount == 0);
		check("background native exit never requests recovery", !state.recoveryPending);
	}

	private static void testExpiredFailureStartsAnew() {
		StartupRecoveryJournal.State state = crashOnce("mod-loading", "9999aaaa", 40_000L);
		long later = 40_000L + StartupRecoveryJournal.FAILURE_WINDOW_MS + 1L;
		StartupRecoveryJournal.BeginResult next =
				StartupRecoveryJournal.beginAttempt(state, later);
		state = StartupRecoveryJournal.setFingerprint(next.state, next.attemptId, "9999aaaa");
		state = StartupRecoveryJournal.recordStage(state, next.attemptId, "mod-loading");
		StartupRecoveryJournal.BeginResult after =
				StartupRecoveryJournal.beginAttempt(state, later + 1_000L);
		state = StartupRecoveryJournal.reconcileExit(
				after.state,
				PreviousExitClassifier.REASON_CRASH,
				later + 500L,
				later + 1_100L);
		check("expired failure is not a crash loop", state.failureCount == 1);
		check("expired failure does not request recovery", !state.recoveryPending);
	}

	private static void testCodecRejectsTornState() {
		StartupRecoveryJournal.BeginResult begin =
				StartupRecoveryJournal.beginAttempt(StartupRecoveryJournal.empty(), 50_000L);
		StartupRecoveryJournal.State state = StartupRecoveryJournal.recordStage(
				begin.state, begin.attemptId, "cloud-sync");
		String encoded = StartupRecoveryJournal.encode(state);
		StartupRecoveryJournal.DecodeResult decoded = StartupRecoveryJournal.decode(encoded);
		check("complete journal round-trips", decoded.valid);
		check("round-trip preserves current stage",
				"cloud-sync".equals(decoded.state.findAttempt(begin.attemptId).stage));

		StartupRecoveryJournal.DecodeResult torn =
				StartupRecoveryJournal.decode(encoded.substring(0, encoded.length() / 2));
		check("torn journal is rejected", !torn.valid);
		check("torn journal fails closed without recovery", !torn.state.recoveryPending);
	}

	private static void testCandidateBoundaryRejectsPaths() {
		StartupRecoveryJournal.BeginResult begin =
				StartupRecoveryJournal.beginAttempt(StartupRecoveryJournal.empty(), 60_000L);
		StartupRecoveryJournal.State state = StartupRecoveryJournal.recordModCandidate(
				begin.state, begin.attemptId, "/storage/emulated/0/Mods/private");
		check("path-like mod candidate is rejected",
				"".equals(state.findAttempt(begin.attemptId).modCandidate));

		state = StartupRecoveryJournal.recordModCandidate(
				state, begin.attemptId, "한글 모드");
		check("unicode stable mod id remains displayable",
				"한글 모드".equals(state.findAttempt(begin.attemptId).modCandidate));
	}

	private static StartupRecoveryJournal.State crashOnce(
			String stage, String fingerprint, long baseTime) {
		StartupRecoveryJournal.BeginResult first =
				StartupRecoveryJournal.beginAttempt(StartupRecoveryJournal.empty(), baseTime);
		StartupRecoveryJournal.State state = StartupRecoveryJournal.setFingerprint(
				first.state, first.attemptId, fingerprint);
		state = StartupRecoveryJournal.recordStage(state, first.attemptId, stage);
		StartupRecoveryJournal.BeginResult second =
				StartupRecoveryJournal.beginAttempt(state, baseTime + 1_000L);
		return StartupRecoveryJournal.reconcileExit(
				second.state,
				PreviousExitClassifier.REASON_CRASH,
				baseTime + 500L,
				baseTime + 1_100L);
	}

	private static void check(String name, boolean condition) {
		if (condition) {
			System.out.println("PASS " + name);
			return;
		}
		failures++;
		System.err.println("FAIL " + name);
	}
}
