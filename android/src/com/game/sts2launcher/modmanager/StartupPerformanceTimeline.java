package com.game.sts2launcher.modmanager;

// Numeric-only, bounded native startup timeline. This is deliberately separate
// from StartupRecoveryJournal: recovery owns crash-loop decisions, while this
// class owns monotonic performance spans and native progress handoff.
final class StartupPerformanceTimeline {
	static final int STAGE_ANDROID_PROCESS = 1;
	static final int STAGE_INSTALL_RECOVERY = 2;
	static final int STAGE_CACHE_SYNC = 3;
	static final int STAGE_ASSEMBLY_SYNC = 4;
	static final int STAGE_GODOT_BOOTSTRAP = 5;

	static final int TERMINAL_COMPLETED = 1;
	static final int TERMINAL_SKIPPED = 2;
	static final int TERMINAL_DEGRADED = 3;
	static final int TERMINAL_FAILED = 4;
	static final int TERMINAL_RECOVERY = 5;

	private static final int EVENT_BEGAN = 1;
	private static final int EVENT_TERMINAL = 4;

	private final Event[] events;
	private int head;
	private int count;
	private long sequence;
	private long lastTimestampUsec = -1L;
	private int activeStage;
	private int lastTerminalStage;

	StartupPerformanceTimeline(int capacity) {
		if (capacity < 8 || capacity > 64) {
			throw new IllegalArgumentException("capacity");
		}
		events = new Event[capacity];
	}

	boolean begin(int stage, long timestampUsec) {
		if (!validTimestamp(timestampUsec) || activeStage != 0 || !validStage(stage)) {
			return false;
		}
		if (lastTerminalStage == 0) {
			if (stage != STAGE_ANDROID_PROCESS) return false;
		} else if (!allowedNext(lastTerminalStage, stage)) {
			return false;
		}
		activeStage = stage;
		record(stage, EVENT_BEGAN, 0, timestampUsec);
		return true;
	}

	boolean end(int stage, int terminal, long timestampUsec) {
		if (!validTimestamp(timestampUsec)
				|| activeStage != stage
				|| !validTerminal(terminal)) {
			return false;
		}
		record(stage, EVENT_TERMINAL, terminal, timestampUsec);
		activeStage = 0;
		lastTerminalStage = stage;
		return true;
	}

	boolean skip(int stage, long timestampUsec) {
		return begin(stage, timestampUsec) && end(stage, TERMINAL_SKIPPED, timestampUsec);
	}

	int activeStage() {
		return activeStage;
	}

	int eventCount() {
		return count;
	}

	String encode() {
		StringBuilder builder = new StringBuilder(8 + count * 24);
		builder.append("v1\n");
		for (int i = 0; i < count; i++) {
			Event event = events[(head + i) % events.length];
			builder.append(event.sequence).append('|')
					.append(event.stage).append('|')
					.append(event.kind).append('|')
					.append(event.terminal).append('|')
					.append(event.timestampUsec).append('\n');
		}
		return builder.toString();
	}

	static boolean isValidEncoding(String value) {
		if (value == null || value.length() > 8_192 || !value.startsWith("v1\n")) {
			return false;
		}
		for (int i = 0; i < value.length(); i++) {
			char current = value.charAt(i);
			if (current == 'v' || current == '|' || current == '\n'
					|| (current >= '0' && current <= '9')) {
				continue;
			}
			return false;
		}
		return true;
	}

	private boolean validTimestamp(long timestampUsec) {
		if (timestampUsec < 0 || timestampUsec < lastTimestampUsec) return false;
		lastTimestampUsec = timestampUsec;
		return true;
	}

	private void record(int stage, int kind, int terminal, long timestampUsec) {
		int index;
		if (count < events.length) {
			index = (head + count) % events.length;
			count++;
		} else {
			index = head;
			head = (head + 1) % events.length;
		}
		events[index] = new Event(++sequence, stage, kind, terminal, timestampUsec);
	}

	private static boolean validStage(int stage) {
		return stage >= STAGE_ANDROID_PROCESS && stage <= STAGE_GODOT_BOOTSTRAP;
	}

	private static boolean validTerminal(int terminal) {
		return terminal >= TERMINAL_COMPLETED && terminal <= TERMINAL_RECOVERY;
	}

	private static boolean allowedNext(int current, int next) {
		return next == current + 1;
	}

	private static final class Event {
		final long sequence;
		final int stage;
		final int kind;
		final int terminal;
		final long timestampUsec;

		Event(long sequence, int stage, int kind, int terminal, long timestampUsec) {
			this.sequence = sequence;
			this.stage = stage;
			this.kind = kind;
			this.terminal = terminal;
			this.timestampUsec = timestampUsec;
		}
	}
}
