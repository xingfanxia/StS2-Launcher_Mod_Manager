package com.game.sts2launcher.modmanager;

import java.io.ByteArrayOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

// Pure-Java startup-attempt state machine. GodotApp owns the Android
// SharedPreferences adapter; keeping lifecycle decisions here makes crash-loop
// behavior deterministic and testable without booting Android or Godot.
final class StartupRecoveryJournal {
	static final int SCHEMA_VERSION = 1;
	static final int RECOVERY_THRESHOLD = 2;
	static final long FAILURE_WINDOW_MS = 24L * 60L * 60L * 1_000L;
	private static final int MAX_ATTEMPTS = 8;
	private static final int MAX_STAGE_LENGTH = 48;
	private static final int MAX_FINGERPRINT_LENGTH = 64;
	private static final int MAX_CANDIDATE_LENGTH = 80;

	private StartupRecoveryJournal() {}

	static final class Attempt {
		final long id;
		final long startedAtMs;
		final long plannedAtMs;
		final String stage;
		final String fingerprint;
		final String modCandidate;
		final String lastSuccessfulMod;
		final boolean healthy;
		final boolean resolved;

		Attempt(
				long id,
				long startedAtMs,
				long plannedAtMs,
				String stage,
				String fingerprint,
				String modCandidate,
				String lastSuccessfulMod,
				boolean healthy,
				boolean resolved) {
			this.id = id;
			this.startedAtMs = startedAtMs;
			this.plannedAtMs = plannedAtMs;
			this.stage = stage;
			this.fingerprint = fingerprint;
			this.modCandidate = modCandidate;
			this.lastSuccessfulMod = lastSuccessfulMod;
			this.healthy = healthy;
			this.resolved = resolved;
		}

		Attempt withStage(String value) {
			return new Attempt(id, startedAtMs, plannedAtMs, value, fingerprint,
					modCandidate, lastSuccessfulMod, healthy, resolved);
		}

		Attempt withFingerprint(String value) {
			return new Attempt(id, startedAtMs, plannedAtMs, stage, value,
					modCandidate, lastSuccessfulMod, healthy, resolved);
		}

		Attempt withCandidate(String value) {
			return new Attempt(id, startedAtMs, plannedAtMs, stage, fingerprint,
					value, lastSuccessfulMod, healthy, resolved);
		}

		Attempt withSuccessfulMod(String value) {
			return new Attempt(id, startedAtMs, plannedAtMs, stage, fingerprint,
					"", value, healthy, resolved);
		}

		Attempt withPlannedAt(long value) {
			return new Attempt(id, startedAtMs, value, stage, fingerprint,
					modCandidate, lastSuccessfulMod, healthy, resolved);
		}

		Attempt withHealthy(String value) {
			return new Attempt(id, startedAtMs, plannedAtMs, value, fingerprint,
					"", lastSuccessfulMod, true, resolved);
		}

		Attempt withResolved() {
			return new Attempt(id, startedAtMs, plannedAtMs, stage, fingerprint,
					modCandidate, lastSuccessfulMod, healthy, true);
		}
	}

	static final class State {
		final long nextAttemptId;
		final List<Attempt> attempts;
		final String failureStage;
		final String failureFingerprint;
		final long failureAtMs;
		final int failureCount;
		final boolean recoveryPending;
		final String recoveryCandidate;
		final String recoveryReason;

		State(
				long nextAttemptId,
				List<Attempt> attempts,
				String failureStage,
				String failureFingerprint,
				long failureAtMs,
				int failureCount,
				boolean recoveryPending,
				String recoveryCandidate,
				String recoveryReason) {
			this.nextAttemptId = nextAttemptId;
			this.attempts = Collections.unmodifiableList(new ArrayList<>(attempts));
			this.failureStage = failureStage;
			this.failureFingerprint = failureFingerprint;
			this.failureAtMs = failureAtMs;
			this.failureCount = failureCount;
			this.recoveryPending = recoveryPending;
			this.recoveryCandidate = recoveryCandidate;
			this.recoveryReason = recoveryReason;
		}

		Attempt findAttempt(long attemptId) {
			for (Attempt attempt : attempts) {
				if (attempt.id == attemptId) return attempt;
			}
			return null;
		}
	}

	static final class BeginResult {
		final State state;
		final long attemptId;

		BeginResult(State state, long attemptId) {
			this.state = state;
			this.attemptId = attemptId;
		}
	}

	static final class RecoveryRequest {
		final boolean pending;
		final String stage;
		final String modCandidate;
		final String reason;
		final int failureCount;

		RecoveryRequest(
				boolean pending,
				String stage,
				String modCandidate,
				String reason,
				int failureCount) {
			this.pending = pending;
			this.stage = stage;
			this.modCandidate = modCandidate;
			this.reason = reason;
			this.failureCount = failureCount;
		}
	}

	static final class DecodeResult {
		final boolean valid;
		final State state;

		DecodeResult(boolean valid, State state) {
			this.valid = valid;
			this.state = state;
		}
	}

	static State empty() {
		return new State(1L, Collections.emptyList(), "", "", 0L, 0,
				false, "", "");
	}

	static BeginResult beginAttempt(State source, long nowMs) {
		State state = source == null ? empty() : source;
		long id = Math.max(1L, state.nextAttemptId);
		List<Attempt> attempts = new ArrayList<>(state.attempts);
		while (attempts.size() >= MAX_ATTEMPTS) {
			int removable = -1;
			for (int i = 0; i < attempts.size(); i++) {
				if (attempts.get(i).resolved) {
					removable = i;
					break;
				}
			}
			attempts.remove(removable >= 0 ? removable : 0);
		}
		attempts.add(new Attempt(id, Math.max(0L, nowMs), 0L,
				"android-on-create", "unknown", "", "", false, false));
		State next = new State(id + 1L, attempts, state.failureStage,
				state.failureFingerprint, state.failureAtMs, state.failureCount,
				state.recoveryPending, state.recoveryCandidate, state.recoveryReason);
		return new BeginResult(next, id);
	}

	static State recordStage(State state, long attemptId, String stage) {
		return replaceAttempt(state, attemptId, Replacement.STAGE, sanitizeStage(stage));
	}

	static State setFingerprint(State state, long attemptId, String fingerprint) {
		return replaceAttempt(state, attemptId, Replacement.FINGERPRINT,
				sanitizeFingerprint(fingerprint));
	}

	static State recordModCandidate(State state, long attemptId, String modId) {
		return replaceAttempt(state, attemptId, Replacement.CANDIDATE,
				sanitizeCandidate(modId));
	}

	static State recordModSuccessful(State state, long attemptId, String modId) {
		return replaceAttempt(state, attemptId, Replacement.SUCCESSFUL_MOD,
				sanitizeCandidate(modId));
	}

	static State markPlannedExit(State state, long attemptId, long nowMs) {
		Attempt attempt = state == null ? null : state.findAttempt(attemptId);
		if (attempt == null) return state == null ? empty() : state;
		return replace(state, attemptId, attempt.withPlannedAt(Math.max(0L, nowMs)));
	}

	static State markHealthy(State state, long attemptId, String terminalStage) {
		State safe = state == null ? empty() : state;
		Attempt attempt = safe.findAttempt(attemptId);
		if (attempt == null) return safe;
		State updated = replace(safe, attemptId,
				attempt.withHealthy(sanitizeStage(terminalStage)));
		return clearFailure(updated);
	}

	static State reconcileExit(State source, int reason, long exitTimestampMs, long nowMs) {
		State state = source == null ? empty() : source;
		if (exitTimestampMs <= 0L) return state;

		Attempt match = null;
		List<Attempt> ordered = new ArrayList<>(state.attempts);
		ordered.sort(Comparator.comparingLong(a -> a.startedAtMs));
		for (int i = 0; i < ordered.size(); i++) {
			Attempt candidate = ordered.get(i);
			if (candidate.resolved || exitTimestampMs < candidate.startedAtMs) continue;
			long upperExclusive = i + 1 < ordered.size()
					? ordered.get(i + 1).startedAtMs
					: Long.MAX_VALUE;
			if (exitTimestampMs < upperExclusive) match = candidate;
		}
		if (match == null) return state;

		State resolved = replace(state, match.id, match.withResolved());
		boolean planned = PreviousExitClassifier.isPlannedExit(
				reason, exitTimestampMs, match.plannedAtMs);
		if (planned || !PreviousExitClassifier.isActionableFailure(reason)) {
			return clearFailure(resolved);
		}

		String stage = sanitizeStage(match.stage);
		String fingerprint = sanitizeFingerprint(match.fingerprint);
		boolean sameFailure = stage.equals(resolved.failureStage)
				&& fingerprint.equals(resolved.failureFingerprint)
				&& exitTimestampMs >= resolved.failureAtMs
				&& exitTimestampMs - resolved.failureAtMs <= FAILURE_WINDOW_MS;
		int count = sameFailure ? resolved.failureCount + 1 : 1;
		String candidate = !match.modCandidate.isEmpty()
				? match.modCandidate
				: match.lastSuccessfulMod;
		return new State(resolved.nextAttemptId, resolved.attempts, stage, fingerprint,
				exitTimestampMs, count, count >= RECOVERY_THRESHOLD, candidate,
				PreviousExitClassifier.reasonLabel(reason));
	}

	static RecoveryRequest getRecoveryRequest(State state) {
		State safe = state == null ? empty() : state;
		return new RecoveryRequest(safe.recoveryPending, safe.failureStage,
				safe.recoveryCandidate, safe.recoveryReason, safe.failureCount);
	}

	static State clearRecoveryRequest(State state) {
		return clearFailure(state == null ? empty() : state);
	}

	static String encode(State source) {
		State state = source == null ? empty() : source;
		StringBuilder out = new StringBuilder();
		out.append("V").append(SCHEMA_VERSION)
				.append('|').append(state.nextAttemptId)
				.append('|').append(state.failureCount)
				.append('|').append(state.failureAtMs)
				.append('|').append(state.recoveryPending ? '1' : '0')
				.append('|').append(hex(state.failureStage))
				.append('|').append(hex(state.failureFingerprint))
				.append('|').append(hex(state.recoveryCandidate))
				.append('|').append(hex(state.recoveryReason))
				.append('|').append(state.attempts.size());
		for (Attempt attempt : state.attempts) {
			out.append('\n').append('A')
					.append('|').append(attempt.id)
					.append('|').append(attempt.startedAtMs)
					.append('|').append(attempt.plannedAtMs)
					.append('|').append(attempt.healthy ? '1' : '0')
					.append('|').append(attempt.resolved ? '1' : '0')
					.append('|').append(hex(attempt.stage))
					.append('|').append(hex(attempt.fingerprint))
					.append('|').append(hex(attempt.modCandidate))
					.append('|').append(hex(attempt.lastSuccessfulMod));
		}
		return out.toString();
	}

	static DecodeResult decode(String encoded) {
		if (encoded == null || encoded.isEmpty()) return new DecodeResult(true, empty());
		try {
			String[] lines = encoded.split("\\n", -1);
			String[] header = lines[0].split("\\|", -1);
			if (header.length != 10 || !("V" + SCHEMA_VERSION).equals(header[0])) {
				return invalidDecode();
			}
			long nextAttemptId = positiveLong(header[1]);
			int failureCount = nonNegativeInt(header[2]);
			long failureAtMs = nonNegativeLong(header[3]);
			boolean recoveryPending = parseBoolean(header[4]);
			String failureStage = unhex(header[5]);
			String failureFingerprint = unhex(header[6]);
			String recoveryCandidate = unhex(header[7]);
			String recoveryReason = unhex(header[8]);
			int attemptCount = nonNegativeInt(header[9]);
			if (attemptCount > MAX_ATTEMPTS || lines.length != attemptCount + 1) {
				return invalidDecode();
			}

			List<Attempt> attempts = new ArrayList<>();
			Set<Long> ids = new HashSet<>();
			for (int i = 1; i < lines.length; i++) {
				String[] fields = lines[i].split("\\|", -1);
				if (fields.length != 10 || !"A".equals(fields[0])) return invalidDecode();
				long id = positiveLong(fields[1]);
				if (!ids.add(id)) return invalidDecode();
				Attempt attempt = new Attempt(
						id,
						nonNegativeLong(fields[2]),
						nonNegativeLong(fields[3]),
						unhex(fields[6]),
						unhex(fields[7]),
						unhex(fields[8]),
						unhex(fields[9]),
						parseBoolean(fields[4]),
						parseBoolean(fields[5]));
				if (!attempt.stage.equals(sanitizeStage(attempt.stage))
						|| !attempt.fingerprint.equals(sanitizeFingerprint(attempt.fingerprint))
						|| !attempt.modCandidate.equals(sanitizeCandidate(attempt.modCandidate))
						|| !attempt.lastSuccessfulMod.equals(
								sanitizeCandidate(attempt.lastSuccessfulMod))) {
					return invalidDecode();
				}
				attempts.add(attempt);
			}
			if (recoveryPending && failureCount < RECOVERY_THRESHOLD) return invalidDecode();
			State state = new State(nextAttemptId, attempts,
					sanitizeStageOrEmpty(failureStage),
					sanitizeFingerprintOrEmpty(failureFingerprint), failureAtMs,
					failureCount, recoveryPending, sanitizeCandidate(recoveryCandidate),
					sanitizeReason(recoveryReason));
			return new DecodeResult(true, state);
		} catch (RuntimeException ex) {
			return invalidDecode();
		}
	}

	private enum Replacement {
		STAGE,
		FINGERPRINT,
		CANDIDATE,
		SUCCESSFUL_MOD
	}

	private static State replaceAttempt(
			State source, long attemptId, Replacement replacement, String value) {
		State state = source == null ? empty() : source;
		Attempt attempt = state.findAttempt(attemptId);
		if (attempt == null) return state;
		Attempt changed;
		switch (replacement) {
			case STAGE:
				changed = attempt.withStage(value);
				break;
			case FINGERPRINT:
				changed = attempt.withFingerprint(value);
				break;
			case CANDIDATE:
				changed = attempt.withCandidate(value);
				break;
			case SUCCESSFUL_MOD:
				changed = attempt.withSuccessfulMod(value);
				break;
			default:
				throw new IllegalStateException("Unhandled replacement " + replacement);
		}
		return replace(state, attemptId, changed);
	}

	private static State replace(State state, long attemptId, Attempt replacement) {
		List<Attempt> attempts = new ArrayList<>(state.attempts.size());
		for (Attempt attempt : state.attempts) {
			attempts.add(attempt.id == attemptId ? replacement : attempt);
		}
		return new State(state.nextAttemptId, attempts, state.failureStage,
				state.failureFingerprint, state.failureAtMs, state.failureCount,
				state.recoveryPending, state.recoveryCandidate, state.recoveryReason);
	}

	private static State clearFailure(State state) {
		return new State(state.nextAttemptId, state.attempts, "", "", 0L, 0,
				false, "", "");
	}

	private static String sanitizeStage(String value) {
		String sanitized = sanitizeAsciiToken(value, MAX_STAGE_LENGTH);
		return sanitized.isEmpty() ? "unknown" : sanitized;
	}

	private static String sanitizeStageOrEmpty(String value) {
		if (value == null || value.isEmpty()) return "";
		return sanitizeStage(value);
	}

	private static String sanitizeFingerprint(String value) {
		String sanitized = sanitizeAsciiToken(value, MAX_FINGERPRINT_LENGTH);
		return sanitized.isEmpty() ? "unknown" : sanitized;
	}

	private static String sanitizeFingerprintOrEmpty(String value) {
		if (value == null || value.isEmpty()) return "";
		return sanitizeFingerprint(value);
	}

	private static String sanitizeAsciiToken(String value, int maxLength) {
		if (value == null) return "";
		String trimmed = value.trim().toLowerCase(java.util.Locale.ROOT);
		if (trimmed.length() == 0 || trimmed.length() > maxLength) return "";
		for (int i = 0; i < trimmed.length(); i++) {
			char ch = trimmed.charAt(i);
			if (!((ch >= 'a' && ch <= 'z')
					|| (ch >= '0' && ch <= '9')
					|| ch == '-' || ch == '_' || ch == '.')) return "";
		}
		return trimmed;
	}

	private static String sanitizeCandidate(String value) {
		if (value == null) return "";
		String trimmed = value.trim();
		if (trimmed.isEmpty() || trimmed.length() > MAX_CANDIDATE_LENGTH
				|| trimmed.indexOf('/') >= 0 || trimmed.indexOf('\\') >= 0
				|| trimmed.indexOf(':') >= 0) return "";
		for (int i = 0; i < trimmed.length(); i++) {
			if (Character.isISOControl(trimmed.charAt(i))) return "";
		}
		return trimmed;
	}

	private static String sanitizeReason(String value) {
		String sanitized = sanitizeAsciiToken(value, 48);
		return sanitized.isEmpty() ? "" : sanitized.toUpperCase(java.util.Locale.ROOT);
	}

	private static String hex(String value) {
		byte[] bytes = (value == null ? "" : value).getBytes(StandardCharsets.UTF_8);
		char[] encoded = new char[bytes.length * 2];
		final char[] alphabet = "0123456789abcdef".toCharArray();
		for (int i = 0; i < bytes.length; i++) {
			int n = bytes[i] & 0xff;
			encoded[i * 2] = alphabet[n >>> 4];
			encoded[i * 2 + 1] = alphabet[n & 0x0f];
		}
		return new String(encoded);
	}

	private static String unhex(String encoded) {
		if (encoded == null || (encoded.length() & 1) != 0 || encoded.length() > 1_024) {
			throw new IllegalArgumentException("invalid hex field");
		}
		ByteArrayOutputStream bytes = new ByteArrayOutputStream(encoded.length() / 2);
		for (int i = 0; i < encoded.length(); i += 2) {
			int hi = Character.digit(encoded.charAt(i), 16);
			int lo = Character.digit(encoded.charAt(i + 1), 16);
			if (hi < 0 || lo < 0) throw new IllegalArgumentException("invalid hex digit");
			bytes.write((hi << 4) | lo);
		}
		return new String(bytes.toByteArray(), StandardCharsets.UTF_8);
	}

	private static boolean parseBoolean(String value) {
		if ("1".equals(value)) return true;
		if ("0".equals(value)) return false;
		throw new IllegalArgumentException("invalid boolean");
	}

	private static long positiveLong(String value) {
		long parsed = Long.parseLong(value);
		if (parsed <= 0L) throw new IllegalArgumentException("expected positive long");
		return parsed;
	}

	private static long nonNegativeLong(String value) {
		long parsed = Long.parseLong(value);
		if (parsed < 0L) throw new IllegalArgumentException("expected non-negative long");
		return parsed;
	}

	private static int nonNegativeInt(String value) {
		int parsed = Integer.parseInt(value);
		if (parsed < 0) throw new IllegalArgumentException("expected non-negative int");
		return parsed;
	}

	private static DecodeResult invalidDecode() {
		return new DecodeResult(false, empty());
	}
}
