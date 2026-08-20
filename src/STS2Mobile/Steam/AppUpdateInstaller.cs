using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace STS2Mobile.Steam;

public readonly struct ApkDownloadProgress
{
    public long DownloadedBytes { get; }
    public long TotalBytes { get; }
    public float Percentage { get; }

    public ApkDownloadProgress(long downloaded, long total)
    {
        DownloadedBytes = downloaded;
        TotalBytes = total;
        Percentage = total > 0 ? downloaded * 100f / total : 0f;
    }
}

public static class AppUpdateInstaller
{
    public static async Task<string> DownloadApkAsync(
        string url,
        string expectedVersion,
        IProgress<ApkDownloadProgress> progress,
        CancellationToken ct
    )
    {
        var cacheDir =
            GetCacheDir() ?? throw new InvalidOperationException("Cache dir unavailable");
        var apkFileName =
            LauncherReleaseChannel.GetExpectedApkAssetName(expectedVersion)
            ?? throw new InvalidOperationException("Invalid launcher update version");
        if (!LauncherReleaseChannel.IsExpectedDownloadUrl(url, expectedVersion))
            throw new InvalidOperationException("Unexpected launcher update URL");

        // The FileProvider URI includes the filename. Reusing one constant name
        // across releases lets some OEM installers cache the previous APK's
        // parsed version and offer to reinstall it. Keep the release asset name
        // so every launcher version receives a distinct content URI.
        DeleteStaleInstallerApks(cacheDir, apkFileName);
        var dest = Path.Combine(cacheDir, apkFileName);
        if (File.Exists(dest))
        {
            try
            {
                File.Delete(dest);
            }
            catch { }
        }

        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.Add("User-Agent", "StS2-Launcher");

        using var response = await http.GetAsync(
                url,
                System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
                ct
            )
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        long downloaded = 0;
        var lastReportedPct = -1f;

        using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var dst = new FileStream(
            dest,
            FileMode.Create,
            System.IO.FileAccess.Write,
            FileShare.None,
            8192,
            useAsync: true
        );

        var buf = new byte[8192];
        int read;
        while (
            (read = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0
        )
        {
            await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;

            if (totalBytes > 0)
            {
                var pct = downloaded * 100f / totalBytes;
                if (pct - lastReportedPct >= 0.5f || downloaded == totalBytes)
                {
                    lastReportedPct = pct;
                    progress?.Report(new ApkDownloadProgress(downloaded, totalBytes));
                }
            }
            else
            {
                progress?.Report(new ApkDownloadProgress(downloaded, -1));
            }
        }

        return dest;
    }

    private static void DeleteStaleInstallerApks(string cacheDir, string keepName)
    {
        try
        {
            foreach (var candidate in Directory.EnumerateFiles(cacheDir, "*.apk"))
            {
                var name = Path.GetFileName(candidate);
                var isOwnedInstaller =
                    string.Equals(name, "launcher_update.apk", StringComparison.OrdinalIgnoreCase)
                    || (
                        name.StartsWith("StS2Launcher-v", StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                    );
                if (
                    isOwnedInstaller
                    && !string.Equals(name, keepName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    try
                    {
                        File.Delete(candidate);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public static void LaunchInstall(string apkPath)
    {
        var app = GetGodotApp();
        app?.Call("installApk", apkPath);
    }

    public static bool CanRequestInstallPackages()
    {
        var app = GetGodotApp();
        if (app == null)
            return false;
        return (bool)app.Call("canRequestInstallPackages");
    }

    public static void RequestInstallPackagesPermission()
    {
        var app = GetGodotApp();
        app?.Call("requestInstallPackagesPermission");
    }

    private static string GetCacheDir()
    {
        try
        {
            var app = GetGodotApp();
            return app == null ? null : (string)app.Call("getCacheDirPath");
        }
        catch
        {
            return null;
        }
    }

    private static GodotObject GetGodotApp()
    {
        try
        {
            var jcw = Engine.GetSingleton("JavaClassWrapper");
            var wrapper = (GodotObject)
                jcw.Call("wrap", "com.game.sts2launcher.modmanager.GodotApp");
            return (GodotObject)wrapper.Call("getInstance");
        }
        catch
        {
            return null;
        }
    }
}
