namespace STS2Mobile.Launcher.Components;

// Visible text has different trust/localization boundaries. Launcher copy must
// resolve completely in EN mode, while user/mod/Workshop content must remain
// byte-for-byte as authored even when it contains Hangul.
public enum TextProvenance
{
    LauncherAuthored,
    LauncherTemplateWithExternalContent,
    ExternalContent,
}

internal static class LocalizedTextPolicy
{
    public static string Render(string value, bool useEnglish, TextProvenance provenance)
    {
        if (provenance == TextProvenance.ExternalContent)
            return value;

        if (provenance == TextProvenance.LauncherTemplateWithExternalContent)
        {
            if (useEnglish && EnglishLocalization.TryTranslateRegistered(value, out var translated))
                return translated;
            if (!useEnglish && EnglishLocalization.TryRestoreRegistered(value, out var restored))
                return restored;
            return value;
        }

        return useEnglish
            ? EnglishLocalization.Translate(value)
            : EnglishLocalization.RestoreKorean(value);
    }

    public static bool IsUntranslatedLauncherText(string value, TextProvenance provenance) =>
        provenance == TextProvenance.LauncherAuthored && EnglishLocalization.ContainsKorean(value);

    public static bool IsPreservedExternalText(string value, TextProvenance provenance) =>
        provenance != TextProvenance.LauncherAuthored && EnglishLocalization.ContainsKorean(value);
}
