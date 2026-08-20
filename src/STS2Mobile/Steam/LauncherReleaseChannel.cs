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

    public static string GetExpectedApkAssetName(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var segmentHasDigit = false;
        foreach (var character in version)
        {
            if (character >= '0' && character <= '9')
            {
                segmentHasDigit = true;
                continue;
            }

            if (character != '.' || !segmentHasDigit)
                return null;
            segmentHasDigit = false;
        }

        return segmentHasDigit ? $"StS2Launcher-v{version}.apk" : null;
    }

    // A release may contain checksums, debug builds, or unrelated APKs. Install
    // only the exact signed artifact produced by this repository's release job.
    public static bool IsExpectedApkAsset(string assetName, string version)
    {
        var expectedName = GetExpectedApkAssetName(version);
        if (string.IsNullOrWhiteSpace(assetName) || expectedName == null)
            return false;
        return string.Equals(assetName, expectedName, StringComparison.OrdinalIgnoreCase);
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

    public static bool IsExpectedDownloadUrl(string downloadUrl, string version)
    {
        var expectedName = GetExpectedApkAssetName(version);
        return expectedName != null
            && IsExpectedDownloadUrl(downloadUrl)
            && Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
            && string.Equals(
                Uri.UnescapeDataString(uri.Segments[^1]),
                expectedName,
                StringComparison.OrdinalIgnoreCase
            );
    }
}
