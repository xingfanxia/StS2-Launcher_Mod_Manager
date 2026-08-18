package com.game.sts2launcher.modmanager;

public final class PreviousExitReportGateTest {
	private static void assertTrue(boolean value, String label) {
		if (!value) throw new AssertionError(label);
		System.out.println("PASS " + label);
	}

	public static void main(String[] args) {
		PreviousExitReportGate queryFirst = new PreviousExitReportGate();
		assertTrue(queryFirst.claimQueryStart(), "query starts once");
		assertTrue(!queryFirst.claimQueryStart(), "duplicate query start rejected");
		assertTrue(!queryFirst.markQueryComplete(), "query waits for Activity readiness");
		assertTrue(queryFirst.markActivityReady(), "Activity claims query-first finalization");
		assertTrue(!queryFirst.markActivityReady(), "query-first finalization stays one-shot");

		PreviousExitReportGate activityFirst = new PreviousExitReportGate();
		assertTrue(activityFirst.claimQueryStart(), "activity-first query starts");
		assertTrue(!activityFirst.markActivityReady(), "Activity waits for query completion");
		assertTrue(activityFirst.markQueryComplete(), "query claims activity-first finalization");
		assertTrue(!activityFirst.markQueryComplete(), "activity-first finalization stays one-shot");
	}
}
