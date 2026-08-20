package com.game.sts2launcher.modmanager;

import java.util.List;

public final class LanInviteShareContractTest {
	public static void main(String[] args) {
		String first = "sts2lan:v1:192.168.1.8:33771";
		String second = "sts2lan:v1:100.64.1.4:41000";
		List<String> parsed = LanInviteShareContract.parseCodes(
				first + "\n" + first + "\ninvalid\n" + second);
		check("valid choices retain order and deduplicate",
				parsed.equals(List.of(first, second)));

		StringBuilder many = new StringBuilder();
		for (int i = 1; i <= 12; i++) {
			many.append("sts2lan:v1:10.0.0.").append(i).append(":33771\n");
		}
		check("choice count is bounded",
				LanInviteShareContract.parseCodes(many.toString()).size()
						== LanInviteShareContract.MAX_CHOICES);
		check("oversized payload is rejected",
				LanInviteShareContract.parseCodes("x".repeat(
						LanInviteShareContract.MAX_PAYLOAD_LENGTH + 1)).isEmpty());

		for (String invalid : new String[] {
				"STS2LAN:v1:192.168.1.8:33771",
				"sts2lan:v2:192.168.1.8:33771",
				"sts2lan:v1:example.com:33771",
				"sts2lan:v1:192.168.1:33771",
				"sts2lan:v1:192.168.001.8:33771",
				"sts2lan:v1:127.0.0.1:33771",
				"sts2lan:v1:169.254.1.1:33771",
				"sts2lan:v1:224.0.0.1:33771",
				"sts2lan:v1:192.168.1.8:0",
				"sts2lan:v1:192.168.1.8:65536",
				"sts2lan:v1:192.168.1.8:+33771",
				"sts2lan:v1:192.168.1.8:33771:extra"
		}) {
			check("invalid code rejected: " + invalid,
					!LanInviteShareContract.isValidCode(invalid));
		}
		System.out.println("All LAN-invite share contract tests passed.");
	}

	private static void check(String name, boolean condition) {
		if (!condition) throw new AssertionError("FAIL " + name);
		System.out.println("PASS " + name);
	}
}
