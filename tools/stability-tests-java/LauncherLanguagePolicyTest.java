package com.game.sts2launcher.modmanager;

public final class LauncherLanguagePolicyTest {
	public static void main(String[] args) {
		check("legacy ko remains readable",
				LauncherLanguagePolicy.KOREAN.equals(
						LauncherLanguagePolicy.normalizePreference("ko")));
		check("legacy en remains readable",
				LauncherLanguagePolicy.ENGLISH.equals(
						LauncherLanguagePolicy.normalizePreference("en")));
		for (String value : new String[] {"zh", "zh-Hans", "zh_CN", "zh-SG"}) {
			check("simplified preference " + value,
					LauncherLanguagePolicy.SIMPLIFIED_CHINESE.equals(
							LauncherLanguagePolicy.normalizePreference(value)));
		}
		check("unknown preference rejected",
				"".equals(LauncherLanguagePolicy.normalizePreference("broken")));

		for (String locale : new String[] {"zh", "zh-Hans", "zh_CN", "zh-SG"}) {
			check("simplified locale " + locale,
					LauncherLanguagePolicy.SIMPLIFIED_CHINESE.equals(
							LauncherLanguagePolicy.fromLocale(locale)));
		}
		for (String locale : new String[] {"zh-Hant", "zh_TW", "zh-HK", "zh_MO"}) {
			check("traditional locale stays non-Simplified " + locale,
					LauncherLanguagePolicy.ENGLISH.equals(
							LauncherLanguagePolicy.fromLocale(locale)));
		}
		check("Korean locale", LauncherLanguagePolicy.KOREAN.equals(
				LauncherLanguagePolicy.fromLocale("ko-KR")));
		check("other locale defaults English", LauncherLanguagePolicy.ENGLISH.equals(
				LauncherLanguagePolicy.fromLocale("fr-FR")));
		System.out.println("All launcher-language policy tests passed.");
	}

	private static void check(String name, boolean condition) {
		if (!condition) throw new AssertionError("FAIL " + name);
		System.out.println("PASS " + name);
	}
}
