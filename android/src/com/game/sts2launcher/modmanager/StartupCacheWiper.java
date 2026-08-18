package com.game.sts2launcher.modmanager;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;

// Filesystem-only helper for keeping recursive cache deletion off Android's UI
// thread. Renaming within the same parent is fast and makes the stale cache
// invisible to Godot before background cleanup begins.
final class StartupCacheWiper {
	private StartupCacheWiper() {}

	static File stageForDeletion(File activeDir, String suffix) throws IOException {
		if (!activeDir.exists()) return null;
		File parent = activeDir.getParentFile();
		if (parent == null) throw new IOException("cache directory has no parent: " + activeDir);
		File staged = new File(parent, activeDir.getName() + ".stale-" + suffix);
		if (staged.exists()) {
			throw new IOException("staging path already exists: " + staged);
		}
		if (!activeDir.renameTo(staged)) {
			throw new IOException("failed to rename " + activeDir + " to " + staged);
		}
		return staged;
	}

	static List<File> findStagedSiblings(File activeDir) {
		List<File> matches = new ArrayList<>();
		File parent = activeDir.getParentFile();
		if (parent == null) return matches;
		String prefix = activeDir.getName() + ".stale-";
		File[] children = parent.listFiles();
		if (children == null) return matches;
		for (File child : children) {
			if (child.getName().startsWith(prefix)) matches.add(child);
		}
		return matches;
	}

	static int deleteRecursively(File file) {
		if (file == null || !file.exists()) return 0;
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
