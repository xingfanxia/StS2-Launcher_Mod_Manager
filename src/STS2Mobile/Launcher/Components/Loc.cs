using Godot;

namespace STS2Mobile.Launcher.Components;

// Locale-aware string picker. The first launch follows the device language; a
// manual KR/EN choice from the launch screen is persisted in user:// and wins on
// subsequent launches. Button labels and proper nouns (SUBSCRIBE, WORKSHOP, mod
// ids…) stay English by design — sentences and notifications are localized.
public static class Loc
{
    public static bool IsKo => LauncherLanguagePreference.IsKorean;

    public static bool IsEnglish => !IsKo;

    public static string Tr(string ko, string en)
    {
        EnglishLocalization.Register(ko, en);
        return IsKo ? ko : en;
    }

    public static string Authored(string text)
    {
        var korean = EnglishLocalization.RestoreKorean(text);
        var english = EnglishLocalization.Translate(korean);
        if (english != korean)
            EnglishLocalization.Register(korean, english);
        return IsEnglish ? english : korean;
    }

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

    // Called four times per second by LanguageToggle. This keeps text assigned by
    // upstream flows localized too, without editing every controller/dialog.
    public static LocalizationAuditSnapshot RefreshWatched() =>
        LocalizedTextRegistry.Refresh(IsEnglish);

    public static void SetEnglish(bool enabled)
    {
        if (LauncherLanguagePreference.SetEnglish(enabled))
            RefreshWatched();
    }
}
