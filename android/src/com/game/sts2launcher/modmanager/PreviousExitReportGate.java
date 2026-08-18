package com.game.sts2launcher.modmanager;

// Coordinates the previous-process query with Activity creation. The query may
// run while Godot bootstraps, but anything that can present Android UI must wait
// until GodotActivity.onCreate has returned. Either side may complete first and
// exactly one caller receives permission to finalize the report.
final class PreviousExitReportGate {
	private boolean queryStarted;
	private boolean queryComplete;
	private boolean activityReady;
	private boolean finalizationClaimed;

	synchronized boolean claimQueryStart() {
		if (queryStarted) return false;
		queryStarted = true;
		return true;
	}

	synchronized boolean markQueryComplete() {
		queryComplete = true;
		return claimFinalizationIfReady();
	}

	synchronized boolean markActivityReady() {
		activityReady = true;
		return claimFinalizationIfReady();
	}

	private boolean claimFinalizationIfReady() {
		if (!queryComplete || !activityReady || finalizationClaimed) return false;
		finalizationClaimed = true;
		return true;
	}
}
