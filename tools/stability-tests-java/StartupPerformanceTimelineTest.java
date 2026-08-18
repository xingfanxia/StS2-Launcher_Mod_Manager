package com.game.sts2launcher.modmanager;

public final class StartupPerformanceTimelineTest {
	private static int failures;

	public static void main(String[] args) {
		testClosedNativePath();
		testInvalidOrderAndTimestamps();
		testBoundedNumericEncoding();
		testEncodingRejectsDynamicPrivateFields();

		if (failures != 0) {
			throw new AssertionError(failures + " startup-performance timeline test(s) failed");
		}
		System.out.println("All startup-performance timeline tests passed.");
	}

	private static void testClosedNativePath() {
		StartupPerformanceTimeline timeline = new StartupPerformanceTimeline(16);
		check("android process begins",
				timeline.begin(StartupPerformanceTimeline.STAGE_ANDROID_PROCESS, 10));
		check("android process completes",
				timeline.end(StartupPerformanceTimeline.STAGE_ANDROID_PROCESS,
						StartupPerformanceTimeline.TERMINAL_COMPLETED, 20));
		check("install recovery can degrade",
				timeline.begin(StartupPerformanceTimeline.STAGE_INSTALL_RECOVERY, 30)
						&& timeline.end(StartupPerformanceTimeline.STAGE_INSTALL_RECOVERY,
								StartupPerformanceTimeline.TERMINAL_DEGRADED, 40));
		check("cache stage can truthfully skip",
				timeline.skip(StartupPerformanceTimeline.STAGE_CACHE_SYNC, 50));
		check("assembly stage completes",
				timeline.begin(StartupPerformanceTimeline.STAGE_ASSEMBLY_SYNC, 60)
						&& timeline.end(StartupPerformanceTimeline.STAGE_ASSEMBLY_SYNC,
								StartupPerformanceTimeline.TERMINAL_COMPLETED, 70));
		check("Godot bootstrap remains active for handoff",
				timeline.begin(StartupPerformanceTimeline.STAGE_GODOT_BOOTSTRAP, 80)
						&& timeline.activeStage()
						== StartupPerformanceTimeline.STAGE_GODOT_BOOTSTRAP);
		check("managed handoff closes Godot bootstrap",
				timeline.end(StartupPerformanceTimeline.STAGE_GODOT_BOOTSTRAP,
						StartupPerformanceTimeline.TERMINAL_COMPLETED, 90));
	}

	private static void testInvalidOrderAndTimestamps() {
		StartupPerformanceTimeline timeline = new StartupPerformanceTimeline(8);
		check("native timeline rejects mid-path root",
				!timeline.begin(StartupPerformanceTimeline.STAGE_CACHE_SYNC, 1));
		check("native timeline accepts real root",
				timeline.begin(StartupPerformanceTimeline.STAGE_ANDROID_PROCESS, 2));
		check("native timeline rejects overlapping stage",
				!timeline.begin(StartupPerformanceTimeline.STAGE_INSTALL_RECOVERY, 3));
		check("native timeline rejects wrong terminal stage",
				!timeline.end(StartupPerformanceTimeline.STAGE_INSTALL_RECOVERY,
						StartupPerformanceTimeline.TERMINAL_COMPLETED, 4));
		check("native timeline rejects backward clock",
				!timeline.end(StartupPerformanceTimeline.STAGE_ANDROID_PROCESS,
						StartupPerformanceTimeline.TERMINAL_COMPLETED, 1));
	}

	private static void testBoundedNumericEncoding() {
		StartupPerformanceTimeline timeline = new StartupPerformanceTimeline(8);
		long now = 1;
		for (int stage = StartupPerformanceTimeline.STAGE_ANDROID_PROCESS;
				stage <= StartupPerformanceTimeline.STAGE_GODOT_BOOTSTRAP;
				stage++) {
			check("bounded path begin " + stage, timeline.begin(stage, now++));
			check("bounded path end " + stage,
					timeline.end(stage, StartupPerformanceTimeline.TERMINAL_COMPLETED, now++));
		}
		check("native ring is bounded", timeline.eventCount() == 8);
		String encoded = timeline.encode();
		check("native schema is versioned", encoded.startsWith("v1\n"));
		check("native schema is numeric-only", encoded.matches("[v0-9|\\n]+"));
		check("native schema stays small", encoded.length() < 512);
	}

	private static void testEncodingRejectsDynamicPrivateFields() {
		check("valid managed summary is accepted",
				StartupPerformanceTimeline.isValidEncoding("v1\n1|6|1|0|100\n"));
		check("path field is rejected",
				!StartupPerformanceTimeline.isValidEncoding("v1\n/storage/private\n"));
		check("account field is rejected",
				!StartupPerformanceTimeline.isValidEncoding("v1\naccount@example.com\n"));
		check("control field is rejected",
				!StartupPerformanceTimeline.isValidEncoding("v1\n1|6|1|0|100\rsecret"));
		check("oversized payload is rejected",
				!StartupPerformanceTimeline.isValidEncoding("v1\n" + "1".repeat(8_192)));
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
