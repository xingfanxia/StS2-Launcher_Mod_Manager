using System;
using Godot;

namespace STS2Mobile.Launcher.Components;

internal static class LauncherLanguagePreference
{
    private const string PreferencesPath = "user://launcher_language.cfg";
    private const string PreferencesSection = "localization";
    private const string LanguageKey = "language";

    private static bool? _isKorean;

    public static bool IsKorean => _isKorean ??= LoadIsKorean();

    public static bool SetEnglish(bool enabled)
    {
        var nextIsKorean = !enabled;
        if (_isKorean == nextIsKorean)
            return false;

        _isKorean = nextIsKorean;
        try
        {
            var config = new ConfigFile();
            config.SetValue(PreferencesSection, LanguageKey, enabled ? "en" : "ko");
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

    private static bool LoadIsKorean()
    {
        try
        {
            var config = new ConfigFile();
            if (config.Load(PreferencesPath) == Error.Ok)
            {
                var saved = (string)config.GetValue(PreferencesSection, LanguageKey, "");
                if (saved == "ko")
                    return true;
                if (saved == "en")
                    return false;
            }

            var language = OS.GetLocaleLanguage();
            return !string.IsNullOrEmpty(language)
                && language.StartsWith("ko", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
