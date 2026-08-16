package com.game.sts2launcher.modmanager;

// Pure decision helper for the renderer recovery offer. It deliberately uses
// only the durable startup stage and Android's exit classification: log text
// such as QueuePresentKHR is diagnostic evidence, never a recovery trigger.
final class RendererRecoveryPolicy {
	private RendererRecoveryPolicy() {}

	static boolean shouldOffer(StartupRecoveryJournal.RecoveryRequest request) {
		if (request == null || !request.pending
				|| request.failureCount < StartupRecoveryJournal.RECOVERY_THRESHOLD
				|| !request.modCandidate.isEmpty()) return false;
		if (!("android-on-create".equals(request.stage)
				|| "launcher-creating".equals(request.stage)
				|| "launcher-awaiting-frame".equals(request.stage))) return false;
		return "SIGNALED".equals(request.reason)
				|| "CRASH_NATIVE".equals(request.reason)
				|| "INITIALIZATION_FAILURE".equals(request.reason);
	}
}
