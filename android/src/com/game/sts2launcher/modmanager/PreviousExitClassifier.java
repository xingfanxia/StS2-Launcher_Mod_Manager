package com.game.sts2launcher.modmanager;

// Pure-Java classifier kept separate from GodotApp so exit-state behavior can
// be regression-tested without an Android runtime.
final class PreviousExitClassifier {
	static final int REASON_EXIT_SELF = 1;
	private static final long PLANNED_EXIT_WINDOW_MS = 120_000L;
	private static final long CLOCK_SKEW_TOLERANCE_MS = 5_000L;

	private PreviousExitClassifier() {}

	static boolean isPlannedExit(int reason, long exitTimestampMs, long plannedAtMs) {
		return reason == REASON_EXIT_SELF
				&& plannedAtMs > 0L
				&& exitTimestampMs >= plannedAtMs - CLOCK_SKEW_TOLERANCE_MS
				&& exitTimestampMs <= plannedAtMs + PLANNED_EXIT_WINDOW_MS;
	}

	static String reasonLabel(int reason) {
		switch (reason) {
			case 0: return "UNKNOWN";
			case 1: return "EXIT_SELF";
			case 2: return "SIGNALED";
			case 3: return "LOW_MEMORY";
			case 4: return "CRASH";
			case 5: return "CRASH_NATIVE";
			case 6: return "ANR";
			case 7: return "INITIALIZATION_FAILURE";
			case 8: return "PERMISSION_CHANGE";
			case 9: return "EXCESSIVE_RESOURCE_USAGE";
			case 10: return "USER_REQUESTED";
			case 11: return "USER_STOPPED";
			case 12: return "DEPENDENCY_DIED";
			case 13: return "OTHER";
			case 14: return "FREEZER";
			case 15: return "PACKAGE_STATE_CHANGE";
			case 16: return "PACKAGE_UPDATED";
			default: return "REASON_" + reason;
		}
	}
}
