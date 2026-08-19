using Godot;

namespace STS2Mobile.Launcher.Components;

// Locale-aware string picker. The first launch follows the device language; a
// manual KR/EN/zh-Hans choice from the launch screen is persisted in user://
// and wins on subsequent launches. Product/technical names and external mod or
// Workshop content are preserved while launcher-owned actions and sentences
// are localized.
public static class Loc
{
    internal static LauncherLanguage CurrentLanguage => LauncherLanguagePreference.Current;

    public static bool IsKo => CurrentLanguage == LauncherLanguage.Korean;

    public static bool IsEnglish => CurrentLanguage == LauncherLanguage.English;

    public static bool IsSimplifiedChinese =>
        CurrentLanguage == LauncherLanguage.SimplifiedChinese;

    public static string Tr(string ko, string en)
    {
        var zh = SimplifiedChineseLocalization.Translate(ko, en);
        return Select(ko, en, zh);
    }

    public static string Tr(string ko, string en, string zh) => Select(ko, en, zh);

    public static string Select(string ko, string en, string zh)
    {
        zh = SimplifiedChineseLocalization.ForDisplay(zh);
        EnglishLocalization.Register(ko, en);
        SimplifiedChineseLocalization.Register(ko, en, zh);
        return CurrentLanguage switch
        {
            LauncherLanguage.Korean => ko,
            LauncherLanguage.SimplifiedChinese => zh,
            _ => en,
        };
    }

    public static string Authored(string text)
    {
        var korean = SimplifiedChineseLocalization.RestoreCanonical(text);
        korean = EnglishLocalization.RestoreKorean(korean);
        var english = EnglishLocalization.Translate(korean);
        var chinese = SimplifiedChineseLocalization.Translate(korean, english);
        if (english != korean)
            EnglishLocalization.Register(korean, english);
        if (chinese != korean)
            SimplifiedChineseLocalization.Register(korean, english, chinese);
        return CurrentLanguage switch
        {
            LauncherLanguage.Korean => korean,
            LauncherLanguage.SimplifiedChinese => chinese,
            _ => english,
        };
    }

    internal static string Render(string text, TextProvenance provenance) =>
        LocalizedTextPolicy.Render(text, CurrentLanguage, provenance);

    public static void Watch(
        Label label,
        TextProvenance provenance = TextProvenance.LauncherAuthored
    ) => LocalizedTextRegistry.Watch(label, provenance);

    public static void Watch(
        Button button,
        TextProvenance provenance = TextProvenance.LauncherAuthored
    ) => LocalizedTextRegistry.Watch(button, provenance);

    public static void Watch(
        LineEdit lineEdit,
        TextProvenance provenance = TextProvenance.LauncherAuthored
    ) => LocalizedTextRegistry.Watch(lineEdit, provenance);

    public static void Watch(
        OptionButton optionButton,
        int itemIndex,
        TextProvenance provenance = TextProvenance.LauncherAuthored
    ) => LocalizedTextRegistry.Watch(optionButton, itemIndex, provenance);

    // Called four times per second by LanguageSelector. This keeps text assigned by
    // upstream flows localized too, without editing every controller/dialog.
    public static LocalizationAuditSnapshot RefreshWatched() =>
        LocalizedTextRegistry.Refresh(CurrentLanguage);

    internal static void SetLanguage(LauncherLanguage language)
    {
        if (LauncherLanguagePreference.Set(language))
            RefreshWatched();
    }
}
