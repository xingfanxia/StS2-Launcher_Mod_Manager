package com.game.sts2launcher.modmanager;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

public final class GameInstallRecoveryTest {
	public static void main(String[] args) throws Exception {
		File root = Files.createTempDirectory("sts2-install-recovery-").toFile();
		try {
			writeInstall(new File(root, "game.rollback"), "old", true, "public");
			writeInstall(new File(root, "game.staging"), "new", true, "public-beta");
			GameInstallRecovery.recover(root);
			check("prepared staging is promoted when active rename was interrupted",
					readVersion(new File(root, "game")).equals("new"));
			check("rollback remains until game-ready", new File(root, "game.rollback").isDirectory());

			GameInstallRecovery.completeValidation(root);
			check("healthy validation removes rollback", !new File(root, "game.rollback").exists());
			check("validated branch is published",
					new String(Files.readAllBytes(new File(root, "selected_branch").toPath()),
							StandardCharsets.UTF_8).equals("public-beta"));

			reset(root);
			writeInstall(new File(root, "game.rollback"), "old", false, "public");
			writeInstall(new File(root, "game"), "new", true, "public-beta");
			GameInstallRecovery.beginValidation(root);
			GameInstallRecovery.recover(root);
			check("failed first startup restores the last validated install",
					readVersion(new File(root, "game")).equals("old"));

			reset(root);
			writeInstall(new File(root, "game.rollback"), "old", false, "public");
			writeInstall(new File(root, "game"), "mixed", true, "public-beta");
			Files.write(new File(root, "game/SlayTheSpire2.pck").toPath(),
					"GDPCdifferent".getBytes(StandardCharsets.UTF_8));
			GameInstallRecovery.recover(root);
			check("invalid activated tuple restores rollback",
					readVersion(new File(root, "game")).equals("old"));
			check("invalid active is retained only as cleanup data", hasFailedDirectory(root));

			reset(root);
			writeInstall(new File(root, "game"), "legacy", false, "public");
			check("legacy complete install remains launchable",
					GameInstallRecovery.isActiveLaunchable(root));

			Files.write(new File(root, "game/SlayTheSpire2.pck").toPath(),
					"bad".getBytes(StandardCharsets.UTF_8));
			check("incomplete active install falls back to bootstrap",
					!GameInstallRecovery.isActiveLaunchable(root));
			System.out.println("All game-install recovery tests passed.");
		} finally {
			delete(root);
		}
	}

	private static void writeInstall(File directory, String version, boolean marker, String branch)
			throws Exception {
		File assemblies = new File(directory, "data_test");
		check("fixture directory created", assemblies.mkdirs());
		byte[] pck = ("GDPC" + version).getBytes(StandardCharsets.UTF_8);
		byte[] dll = version.getBytes(StandardCharsets.UTF_8);
		Files.write(new File(directory, "SlayTheSpire2.pck").toPath(), pck);
		Files.write(new File(assemblies, "sts2.dll").toPath(), dll);
		Files.write(new File(directory, "version.txt").toPath(), dll);
		if (marker) {
			String json = "{\"Schema\":1,\"TransactionId\":\"0123456789abcdef0123456789abcdef\","
					+ "\"Branch\":\"" + branch + "\","
					+ "\"PckLength\":" + pck.length + ",\"AssemblyLength\":" + dll.length + "}";
			Files.write(new File(directory, ".launcher_install_complete").toPath(),
					json.getBytes(StandardCharsets.UTF_8));
		}
	}

	private static String readVersion(File directory) throws Exception {
		return new String(Files.readAllBytes(new File(directory, "version.txt").toPath()),
				StandardCharsets.UTF_8);
	}

	private static boolean hasFailedDirectory(File root) {
		File[] children = root.listFiles();
		if (children == null) return false;
		for (File child : children) {
			if (child.getName().startsWith("game.failed.")) return true;
		}
		return false;
	}

	private static void reset(File root) {
		File[] children = root.listFiles();
		if (children == null) return;
		for (File child : children) delete(child);
	}

	private static void delete(File file) {
		if (!file.exists()) return;
		if (file.isDirectory()) {
			File[] children = file.listFiles();
			if (children != null) for (File child : children) delete(child);
		}
		if (!file.delete()) throw new AssertionError("failed to delete fixture " + file);
	}

	private static void check(String name, boolean condition) {
		if (!condition) throw new AssertionError("FAIL " + name);
		System.out.println("PASS " + name);
	}
}
