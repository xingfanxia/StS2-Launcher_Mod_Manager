package com.game.sts2launcher.modmanager;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

/** Defense-in-depth validation at the C# to Android Sharesheet boundary. */
final class LanInviteShareContract {
	static final String V1_PREFIX = "sts2lan:v1:";
	static final int MAX_CODE_LENGTH = 128;
	static final int MAX_PAYLOAD_LENGTH = 1_024;
	static final int MAX_CHOICES = 8;

	private LanInviteShareContract() {}

	static List<String> parseCodes(String payload) {
		Set<String> unique = new LinkedHashSet<>();
		if (payload == null || payload.length() > MAX_PAYLOAD_LENGTH) {
			return new ArrayList<>();
		}
		for (String raw : payload.split("\\n")) {
			String code = raw.trim();
			if (isValidCode(code)) unique.add(code);
			if (unique.size() >= MAX_CHOICES) break;
		}
		return new ArrayList<>(unique);
	}

	static boolean isValidCode(String code) {
		if (code == null || code.length() > MAX_CODE_LENGTH || !code.startsWith(V1_PREFIX)) {
			return false;
		}
		String endpoint = code.substring(V1_PREFIX.length());
		int colon = endpoint.indexOf(':');
		if (colon <= 0 || colon != endpoint.lastIndexOf(':')) return false;

		String[] octets = endpoint.substring(0, colon).split("\\.", -1);
		if (octets.length != 4) return false;
		int[] address = new int[4];
		for (int i = 0; i < octets.length; i++) {
			String octet = octets[i];
			if (!octet.matches("[0-9]{1,3}")
					|| (octet.length() > 1 && octet.charAt(0) == '0')) return false;
			try {
				address[i] = Integer.parseInt(octet);
				if (address[i] > 255) return false;
			} catch (NumberFormatException ex) {
				return false;
			}
		}
		if (address[0] == 0 || address[0] == 127 || address[0] >= 224
				|| (address[0] == 169 && address[1] == 254)) return false;

		String portText = endpoint.substring(colon + 1);
		if (!portText.matches("[0-9]{1,5}")) return false;
		try {
			int port = Integer.parseInt(portText);
			return port > 0 && port <= 65_535;
		} catch (NumberFormatException ex) {
			return false;
		}
	}
}
