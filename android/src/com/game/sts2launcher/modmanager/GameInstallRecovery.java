package com.game.sts2launcher.modmanager;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

// Repairs the small rename windows of the C# game-directory transaction before
// Android selects a PCK or copies game assemblies. This class intentionally has
// no Android dependencies so the recovery decisions can run in a plain JVM test.
final class GameInstallRecovery {
	private static final String ACTIVE = "game";
	private static final String STAGING = "game.staging";
	private static final String ROLLBACK = "game.rollback";
	private static final String MARKER = ".launcher_install_complete";
	private static final String VALIDATION_ATTEMPT = ".launcher_install_validation_attempt";
	private static final String PCK = "SlayTheSpire2.pck";
	private static final int MAX_MARKER_BYTES = 64 * 1024;

	private GameInstallRecovery() {}

	static void recover(File dataDirectory) throws IOException {
		File active = new File(dataDirectory, ACTIVE);
		File staging = new File(dataDirectory, STAGING);
		File rollback = new File(dataDirectory, ROLLBACK);

		if (!active.isDirectory()) {
			if (isCompleteInstall(staging)) {
				moveDirectory(staging, active);
			} else if (rollback.isDirectory()) {
				moveDirectory(rollback, active);
			}
			return;
		}

		if (!rollback.isDirectory()) return;
		if (isCompleteInstall(active) && !new File(active, VALIDATION_ATTEMPT).isFile()) return;

		File failed = nextFailedPath(dataDirectory);
		moveDirectory(active, failed);
		try {
			moveDirectory(rollback, active);
		} catch (IOException restoreFailure) {
			// Best effort to leave the pre-call active tree in place if restoring
			// rollback fails for an unexpected filesystem reason.
			if (!active.exists()) failed.renameTo(active);
			throw restoreFailure;
		}
	}

	static boolean isActiveLaunchable(File dataDirectory) {
		File active = new File(dataDirectory, ACTIVE);
		File marker = new File(active, MARKER);
		return marker.isFile() ? isCompleteInstall(active) : hasRequiredFiles(active);
	}

	static void beginValidation(File dataDirectory) throws IOException {
		File active = new File(dataDirectory, ACTIVE);
		if (!new File(dataDirectory, ROLLBACK).isDirectory() || !isCompleteInstall(active)) return;
		File marker = new File(active, VALIDATION_ATTEMPT);
		if (!marker.createNewFile() && !marker.isFile()) {
			throw new IOException("Failed to record game install validation attempt");
		}
	}

	static void completeValidation(File dataDirectory) {
		File active = new File(dataDirectory, ACTIVE);
		deleteRecursively(new File(dataDirectory, STAGING));
		if (!isCompleteInstall(active)) return;

		String branch = readMarkerString(active, "Branch");
		new File(active, VALIDATION_ATTEMPT).delete();
		if (branch != null && branch.matches("[A-Za-z0-9._-]{1,64}")) {
			writeSelectedBranch(dataDirectory, branch);
		}
		deleteRecursively(new File(dataDirectory, ROLLBACK));
		File[] children = dataDirectory.listFiles();
		if (children == null) return;
		for (File child : children) {
			if (child.getName().startsWith("game.failed.")) deleteRecursively(child);
		}
	}

	private static boolean isCompleteInstall(File directory) {
		if (!hasRequiredFiles(directory)) return false;
		File marker = new File(directory, MARKER);
		if (!marker.isFile() || marker.length() <= 0 || marker.length() > MAX_MARKER_BYTES) {
			return false;
		}

		try {
			String json = readMarker(marker);
			if (!json.matches("(?s).*\\\"Schema\\\"\\s*:\\s*1(?:\\s*[,}]).*")) return false;
			if (!json.matches(
					"(?s).*\\\"TransactionId\\\"\\s*:\\s*\\\"[0-9a-fA-F]{32}\\\".*")) {
				return false;
			}
			long expectedPck = extractLong(json, "PckLength");
			long expectedAssembly = extractLong(json, "AssemblyLength");
			File assembly = findAssembly(directory);
			return expectedPck >= 4
					&& expectedAssembly > 0
					&& new File(directory, PCK).length() == expectedPck
					&& assembly != null
					&& assembly.length() == expectedAssembly;
		} catch (Exception ignored) {
			return false;
		}
	}

	private static boolean hasRequiredFiles(File directory) {
		return directory.isDirectory()
				&& hasPckMagic(new File(directory, PCK))
				&& findAssembly(directory) != null;
	}

	private static boolean hasPckMagic(File pck) {
		if (!pck.isFile() || pck.length() < 4) return false;
		try (FileInputStream input = new FileInputStream(pck)) {
			return input.read() == 0x47
					&& input.read() == 0x44
					&& input.read() == 0x50
					&& input.read() == 0x43;
		} catch (IOException ignored) {
			return false;
		}
	}

	private static File findAssembly(File directory) {
		File[] children = directory.listFiles();
		if (children == null) return null;
		for (File child : children) {
			if (!child.isDirectory() || !child.getName().startsWith("data_")) continue;
			File assembly = new File(child, "sts2.dll");
			if (assembly.isFile() && assembly.length() > 0) return assembly;
		}
		return null;
	}

	private static long extractLong(String json, String field) {
		Matcher matcher = Pattern.compile(
				"\\\"" + Pattern.quote(field) + "\\\"\\s*:\\s*(\\d+)").matcher(json);
		return matcher.find() ? Long.parseLong(matcher.group(1)) : -1L;
	}

	private static String readMarkerString(File directory, String field) {
		File marker = new File(directory, MARKER);
		if (!marker.isFile() || marker.length() <= 0 || marker.length() > MAX_MARKER_BYTES) {
			return null;
		}
		try {
			String json = readMarker(marker);
			Matcher matcher = Pattern.compile("\\\"" + Pattern.quote(field)
					+ "\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"").matcher(json);
			return matcher.find() ? matcher.group(1) : null;
		} catch (IOException ignored) {
			return null;
		}
	}

	private static void writeSelectedBranch(File dataDirectory, String branch) {
		File selected = new File(dataDirectory, "selected_branch");
		File temporary = new File(dataDirectory, "selected_branch.tmp");
		try (FileOutputStream output = new FileOutputStream(temporary, false)) {
			output.write(branch.getBytes(StandardCharsets.UTF_8));
		} catch (IOException ignored) {
			temporary.delete();
			return;
		}
		if (selected.exists() && !selected.delete()) {
			temporary.delete();
			return;
		}
		if (!temporary.renameTo(selected)) temporary.delete();
	}

	private static String readMarker(File marker) throws IOException {
		byte[] bytes = new byte[(int) marker.length()];
		int offset = 0;
		try (FileInputStream input = new FileInputStream(marker)) {
			while (offset < bytes.length) {
				int read = input.read(bytes, offset, bytes.length - offset);
				if (read < 0) throw new IOException("Unexpected end of install marker");
				offset += read;
			}
		}
		return new String(bytes, StandardCharsets.UTF_8);
	}

	private static File nextFailedPath(File dataDirectory) {
		long suffix = System.currentTimeMillis();
		File candidate;
		do {
			candidate = new File(dataDirectory, "game.failed." + suffix++);
		} while (candidate.exists());
		return candidate;
	}

	private static void moveDirectory(File source, File destination) throws IOException {
		if (!source.renameTo(destination)) {
			throw new IOException("Failed to move " + source.getName() + " to "
					+ destination.getName());
		}
	}

	private static int deleteRecursively(File file) {
		if (!file.exists()) return 0;
		int deleted = 0;
		if (file.isDirectory()) {
			File[] children = file.listFiles();
			if (children != null) {
				for (File child : children) deleted += deleteRecursively(child);
			}
		}
		if (file.delete()) deleted++;
		return deleted;
	}
}
