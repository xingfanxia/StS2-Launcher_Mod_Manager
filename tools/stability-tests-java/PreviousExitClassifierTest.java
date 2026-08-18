package com.game.sts2launcher.modmanager;

public final class PreviousExitClassifierTest {
	private static int failures;

	public static void main(String[] args) {
		check("native crash is actionable",
				"CRASH_NATIVE".equals(PreviousExitClassifier.reasonLabel(5)));
		check("low-memory kill is actionable",
				"LOW_MEMORY".equals(PreviousExitClassifier.reasonLabel(3)));
		check("ANR is actionable",
				"ANR".equals(PreviousExitClassifier.reasonLabel(6)));
		check("native crash participates in crash-loop recovery",
				PreviousExitClassifier.isActionableFailure(
						PreviousExitClassifier.REASON_CRASH_NATIVE));
		check("ANR participates in crash-loop recovery",
				PreviousExitClassifier.isActionableFailure(PreviousExitClassifier.REASON_ANR));
		check("user stop never participates in crash-loop recovery",
				!PreviousExitClassifier.isActionableFailure(
						PreviousExitClassifier.REASON_USER_STOPPED));
		check("planned self-exit is recognized",
				PreviousExitClassifier.isPlannedExit(1, 10_050L, 10_000L));
		check("stale planned marker does not mask a crash",
				!PreviousExitClassifier.isPlannedExit(1, 200_001L, 10_000L));
		check("an exit before the marker is not claimed by a later restart",
				!PreviousExitClassifier.isPlannedExit(1, 0L, 10_000L));
		check("crash reason is never masked by planned marker",
				!PreviousExitClassifier.isPlannedExit(4, 10_050L, 10_000L));

		if (failures != 0) {
			throw new AssertionError(failures + " previous-exit classifier test(s) failed");
		}
		System.out.println("All previous-exit classifier tests passed.");
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
