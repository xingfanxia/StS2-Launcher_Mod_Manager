using System;

namespace STS2Mobile.Launcher.Components;

// Stable persisted launcher-language contract. Keep the wire values separate
// from enum names so existing ko/en preferences remain upgrade-compatible.
internal enum LauncherLanguage
{
    Korean,
    English,
    SimplifiedChinese,
}

internal static class LauncherLanguageCodes
{
    internal const string Korean = "ko";
    internal const string English = "en";
    internal const string SimplifiedChinese = "zh-Hans";

    internal static string ToPreferenceValue(LauncherLanguage language) =>
        language switch
        {
            LauncherLanguage.Korean => Korean,
            LauncherLanguage.SimplifiedChinese => SimplifiedChinese,
            _ => English,
        };

    internal static bool TryParsePreference(string value, out LauncherLanguage language)
    {
        var normalized = Normalize(value);
        if (normalized == Korean)
        {
            language = LauncherLanguage.Korean;
            return true;
        }
        if (normalized == English)
        {
            language = LauncherLanguage.English;
            return true;
        }
        if (IsSimplifiedChinese(normalized))
        {
            language = LauncherLanguage.SimplifiedChinese;
            return true;
        }

        language = default;
        return false;
    }

    internal static LauncherLanguage FromSystemLocale(string locale)
    {
        var normalized = Normalize(locale);
        if (normalized == Korean || normalized.StartsWith("ko-", StringComparison.Ordinal))
            return LauncherLanguage.Korean;
        if (IsTraditionalChinese(normalized))
            return LauncherLanguage.English;
        if (IsSimplifiedChinese(normalized))
            return LauncherLanguage.SimplifiedChinese;
        return LauncherLanguage.English;
    }

    private static bool IsSimplifiedChinese(string normalized) =>
        normalized == "zh"
        || normalized == "zh-hans"
        || normalized.StartsWith("zh-hans-", StringComparison.Ordinal)
        || normalized == "zh-cn"
        || normalized.StartsWith("zh-cn-", StringComparison.Ordinal)
        || normalized == "zh-sg"
        || normalized.StartsWith("zh-sg-", StringComparison.Ordinal);

    private static bool IsTraditionalChinese(string normalized) =>
        normalized == "zh-hant"
        || normalized.StartsWith("zh-hant-", StringComparison.Ordinal)
        || normalized == "zh-tw"
        || normalized.StartsWith("zh-tw-", StringComparison.Ordinal)
        || normalized == "zh-hk"
        || normalized.StartsWith("zh-hk-", StringComparison.Ordinal)
        || normalized == "zh-mo"
        || normalized.StartsWith("zh-mo-", StringComparison.Ordinal);

    private static string Normalize(string value) =>
        (value ?? "").Trim().Replace('_', '-').ToLowerInvariant();
}
