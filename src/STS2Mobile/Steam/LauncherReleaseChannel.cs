using System;

namespace STS2Mobile.Steam;

// Single source of truth for this fork's launcher self-update channel. Both the
// startup auto-check and the manual button use AppUpdateChecker, while fallback
// browser links use LatestReleasePageUrl.
internal static class LauncherReleaseChannel
{
    public const string Repository = "xingfanxia/StS2-Launcher_Mod_Manager";
    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string LatestReleasePageUrl =
        "https://github.com/" + Repository + "/releases/latest";

    // A release may contain checksums, debug builds, or unrelated APKs. Install
    // only the exact signed artifact produced by this repository's release job.
    public static bool IsExpectedApkAsset(string assetName, string version)
    {
        if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(version))
            return false;
        return string.Equals(
            assetName,
            $"StS2Launcher-v{version}.apk",
            StringComparison.OrdinalIgnoreCase
        );
    }

    public static bool IsExpectedDownloadUrl(string downloadUrl)
    {
        if (
            !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        )
            return false;

        return uri.AbsolutePath.StartsWith(
            "/" + Repository + "/releases/download/",
            StringComparison.Ordinal
        );
    }
}
