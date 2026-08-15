package com.game.sts2launcher.modmanager;

import java.io.File;
import java.nio.file.Files;

public final class StartupCacheWiperTest {
	public static void main(String[] args) throws Exception {
		File root = Files.createTempDirectory("sts2-cache-wiper-").toFile();
		try {
			File imported = new File(root, "etc2_cache/.godot/imported");
			File nested = new File(imported, "nested/material.cache");
			if (!nested.getParentFile().mkdirs() || !nested.createNewFile()) {
				throw new AssertionError("failed to create test cache");
			}

			File staged = StartupCacheWiper.stageForDeletion(imported, "test-run");
			check("active cache path disappears immediately", !imported.exists());
			check("staged tree retains files until async cleanup", staged != null && staged.exists());
			check("nested data moved with the directory", new File(staged, "nested/material.cache").exists());

			int deleted = StartupCacheWiper.deleteRecursively(staged);
			check("background cleanup removes staged tree", !staged.exists());
			check("cleanup reports removed entries", deleted == 3);

			File orphan = new File(staged.getParentFile(), "imported.stale-old-process");
			check("orphan fixture created", orphan.mkdirs());
			check("interrupted cleanup is discoverable on next boot",
					StartupCacheWiper.findStagedSiblings(imported).contains(orphan));

			check("missing cache is a successful no-op",
					StartupCacheWiper.stageForDeletion(imported, "again") == null);

			File collisionActive = new File(root, "collision/imported");
			File collisionStaged = new File(root, "collision/imported.stale-same");
			check("collision fixture active created", collisionActive.mkdirs());
			check("collision fixture staged created", collisionStaged.mkdirs());
			boolean collisionRejected = false;
			try {
				StartupCacheWiper.stageForDeletion(collisionActive, "same");
			} catch (java.io.IOException expected) {
				collisionRejected = true;
			}
			check("staging collision is rejected", collisionRejected);
			check("failed staging preserves active cache", collisionActive.exists());
			System.out.println("All startup cache-wiper tests passed.");
		} finally {
			StartupCacheWiper.deleteRecursively(root);
		}
	}

	private static void check(String name, boolean condition) {
		if (!condition) throw new AssertionError("FAIL " + name);
		System.out.println("PASS " + name);
	}
}
