using System;
using Godot;

namespace STS2Mobile.Launcher.Components;

internal static class LauncherLanguagePreference
{
    private const string PreferencesPath = "user://launcher_language.cfg";
    private const string PreferencesSection = "localization";
    private const string LanguageKey = "language";

    private static LauncherLanguage? _language;

    public static LauncherLanguage Current => _language ??= Load();

    public static bool Set(LauncherLanguage language)
    {
        if (_language == language)
            return false;

        _language = language;
        try
        {
            var config = new ConfigFile();
            config.SetValue(
                PreferencesSection,
                LanguageKey,
                LauncherLanguageCodes.ToPreferenceValue(language)
            );
            var error = config.Save(PreferencesPath);
            if (error != Error.Ok)
                GD.PushWarning($"Could not save launcher language preference: {error}");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Could not save launcher language preference: {ex.Message}");
        }

        return true;
    }

    private static LauncherLanguage Load()
    {
        try
        {
            var config = new ConfigFile();
            if (config.Load(PreferencesPath) == Error.Ok)
            {
                var saved = (string)config.GetValue(PreferencesSection, LanguageKey, "");
                if (LauncherLanguageCodes.TryParsePreference(saved, out var language))
                    return language;
            }

            var locale = OS.GetLocale();
            if (string.IsNullOrWhiteSpace(locale))
                locale = OS.GetLocaleLanguage();
            return LauncherLanguageCodes.FromSystemLocale(locale);
        }
        catch
        {
            return LauncherLanguage.English;
        }
    }
}
