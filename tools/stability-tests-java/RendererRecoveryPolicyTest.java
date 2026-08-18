package com.game.sts2launcher.modmanager;

public final class RendererRecoveryPolicyTest {
	private static int failures;

	public static void main(String[] args) {
		check("repeated pre-frame native crash offers compatibility",
				RendererRecoveryPolicy.shouldOffer(request(
						true, "launcher-awaiting-frame", "", "CRASH_NATIVE", 2)));
		check("one crash never offers compatibility",
				!RendererRecoveryPolicy.shouldOffer(request(
						false, "android-on-create", "", "CRASH_NATIVE", 1)));
		check("usable launcher frame excludes renderer recovery",
				!RendererRecoveryPolicy.shouldOffer(request(
						true, "launcher-ready", "", "CRASH_NATIVE", 2)));
		check("background-style LMK excludes renderer recovery",
				!RendererRecoveryPolicy.shouldOffer(request(
						true, "android-on-create", "", "LOW_MEMORY", 2)));
		check("ANR is not guessed to be a renderer failure",
				!RendererRecoveryPolicy.shouldOffer(request(
						true, "launcher-awaiting-frame", "", "ANR", 2)));
		check("mod candidate excludes renderer recovery",
				!RendererRecoveryPolicy.shouldOffer(request(
						true, "launcher-creating", "ExampleMod", "CRASH_NATIVE", 2)));

		if (failures != 0) {
			throw new AssertionError(failures + " renderer-recovery policy test(s) failed");
		}
		System.out.println("All renderer-recovery policy tests passed.");
	}

	private static StartupRecoveryJournal.RecoveryRequest request(
			boolean pending, String stage, String candidate, String reason, int count) {
		return new StartupRecoveryJournal.RecoveryRequest(
				pending, stage, candidate, reason, count);
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
