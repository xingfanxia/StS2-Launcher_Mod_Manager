namespace STS2Mobile.Launcher.Components;

// Visible text has different trust/localization boundaries. Launcher copy must
// resolve completely in EN and zh-Hans modes, while user/mod/Workshop content
// must remain byte-for-byte as authored in any script.
public enum TextProvenance
{
    LauncherAuthored,
    LauncherTemplateWithExternalContent,
    LauncherDiagnosticWithExternalContent,
    ExternalContent,
}

internal static class LocalizedTextPolicy
{
    public static string Render(
        string value,
        LauncherLanguage language,
        TextProvenance provenance
    )
    {
        if (provenance == TextProvenance.ExternalContent)
            return value;

        if (provenance == TextProvenance.LauncherDiagnosticWithExternalContent)
        {
            return language == LauncherLanguage.SimplifiedChinese
                ? SimplifiedChineseLocalization.ForDisplay(
                    SimplifiedChineseLocalization.TranslateDiagnostic(value)
                )
                : value;
        }

        if (provenance == TextProvenance.LauncherTemplateWithExternalContent)
        {
            var canonical = value;
            if (EnglishLocalization.TryRestoreRegistered(value, out var restoredEnglish))
                canonical = restoredEnglish;
            else if (
                SimplifiedChineseLocalization.TryRestoreRegistered(value, out var restoredChinese)
            )
                canonical = restoredChinese;

            if (language == LauncherLanguage.Korean)
                return canonical;
            if (
                language == LauncherLanguage.English
                && EnglishLocalization.TryTranslateRegistered(canonical, out var translatedEnglish)
            )
                return translatedEnglish;
            if (
                language == LauncherLanguage.SimplifiedChinese
                && SimplifiedChineseLocalization.TryTranslateRegistered(
                    canonical,
                    out var translatedChinese
                )
            )
                return translatedChinese;
            return value;
        }

        var korean = SimplifiedChineseLocalization.RestoreCanonical(value);
        korean = EnglishLocalization.RestoreKorean(korean);
        if (language == LauncherLanguage.Korean)
            return korean;
        var english = EnglishLocalization.Translate(korean);
        return language == LauncherLanguage.SimplifiedChinese
            ? SimplifiedChineseLocalization.Translate(korean, english)
            : english;
    }

    public static bool IsUntranslatedLauncherText(
        string value,
        LauncherLanguage language,
        TextProvenance provenance
    ) =>
        (
            provenance
                is TextProvenance.LauncherAuthored
                    or TextProvenance.LauncherDiagnosticWithExternalContent
        )
        && language != LauncherLanguage.Korean
        && (
            EnglishLocalization.ContainsKorean(value)
            || language == LauncherLanguage.SimplifiedChinese
                && SimplifiedChineseLocalization.LooksUntranslated(value)
        );

    public static bool IsPreservedExternalText(string value, TextProvenance provenance) =>
        (
            provenance
                is TextProvenance.LauncherTemplateWithExternalContent
                    or TextProvenance.ExternalContent
        )
            && EnglishLocalization.ContainsKorean(value);
}
