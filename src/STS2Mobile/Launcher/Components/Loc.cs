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

    public static void Watch(Label label) => LocalizedTextRegistry.Watch(label);

    public static void Watch(BaseButton button) => LocalizedTextRegistry.Watch(button);

    public static void Watch(LineEdit lineEdit) => LocalizedTextRegistry.Watch(lineEdit);

    // Called four times per second by LanguageToggle. This keeps text assigned by
    // upstream flows localized too, without editing every controller/dialog.
    public static void RefreshWatched() => LocalizedTextRegistry.Refresh(IsEnglish);

    public static void SetEnglish(bool enabled)
    {
        if (LauncherLanguagePreference.SetEnglish(enabled))
            RefreshWatched();
    }
}
