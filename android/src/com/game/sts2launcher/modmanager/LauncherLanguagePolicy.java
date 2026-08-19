package com.game.sts2launcher.modmanager;

import java.util.Locale;

// Android-native mirror of the managed launcher-language wire contract. This
// class has no Android dependencies so upgrade/locale behavior is unit-tested.
final class LauncherLanguagePolicy {
	static final String KOREAN = "ko";
	static final String ENGLISH = "en";
	static final String SIMPLIFIED_CHINESE = "zh-Hans";

	private LauncherLanguagePolicy() {}

	static String normalizePreference(String value) {
		String normalized = normalize(value);
		if (KOREAN.equals(normalized)) return KOREAN;
		if (ENGLISH.equals(normalized)) return ENGLISH;
		if (isSimplifiedChinese(normalized)) return SIMPLIFIED_CHINESE;
		return "";
	}

	static String fromLocale(String locale) {
		String normalized = normalize(locale);
		if (KOREAN.equals(normalized) || normalized.startsWith("ko-")) return KOREAN;
		if (isTraditionalChinese(normalized)) return ENGLISH;
		if (isSimplifiedChinese(normalized)) return SIMPLIFIED_CHINESE;
		return ENGLISH;
	}

	private static boolean isSimplifiedChinese(String value) {
		return "zh".equals(value)
				|| "zh-hans".equals(value)
				|| value.startsWith("zh-hans-")
				|| "zh-cn".equals(value)
				|| value.startsWith("zh-cn-")
				|| "zh-sg".equals(value)
				|| value.startsWith("zh-sg-");
	}

	private static boolean isTraditionalChinese(String value) {
		return "zh-hant".equals(value)
				|| value.startsWith("zh-hant-")
				|| "zh-tw".equals(value)
				|| value.startsWith("zh-tw-")
				|| "zh-hk".equals(value)
				|| value.startsWith("zh-hk-")
				|| "zh-mo".equals(value)
				|| value.startsWith("zh-mo-");
	}

	private static String normalize(String value) {
		return value == null
				? ""
				: value.trim().replace('_', '-').toLowerCase(Locale.ROOT);
	}
}
