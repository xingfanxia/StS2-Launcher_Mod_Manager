using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;

namespace STS2Mobile.Launcher;

// Narrow bridge to Android's app-private startup journal. Values crossing this
// boundary are app-authored stage tokens, an opaque configuration hash, and a
// bounded mod id; account, credential, save, device, and path data never cross.
internal static class StartupRecoveryBridge
{
    public static void InitializeAttemptContext()
    {
        try
        {
            var app = LauncherModel.GetGodotApp();
            if (app == null)
                return;
            app.Call("setStartupFingerprint", ComputeConfigurationFingerprint());
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[StartupRecovery] fingerprint bridge failed: {ex.Message}");
        }
    }

    public static void RecordStage(string stage) => Call("recordStartupStage", stage);

    public static void RecordModCandidate(string modId) => Call("recordModCandidate", modId);

    public static void RecordModSuccessful(string modId) => Call("recordModSuccessful", modId);

    public static void MarkHealthy(string terminalStage) =>
        Call("markStartupHealthy", terminalStage);

    private static void Call(string method, string value)
    {
        try
        {
            LauncherModel.GetGodotApp()?.Call(method, value ?? "");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[StartupRecovery] {method} bridge failed: {ex.Message}");
        }
    }

    private static string ComputeConfigurationFingerprint()
    {
        var material = new List<string> { "v1", LauncherModel.LoadSelectedBranch() };
        try
        {
            var pck = new FileInfo(Path.Combine(OS.GetDataDir(), "game", "SlayTheSpire2.pck"));
            material.Add(
                pck.Exists ? $"pck:{pck.Length}:{pck.LastWriteTimeUtc.Ticks}" : "pck:missing"
            );
        }
        catch
        {
            material.Add("pck:unavailable");
        }

        try
        {
            foreach (
                var modId in Directory
                    .EnumerateDirectories(AppPaths.ExternalModsDir)
                    .Select(Path.GetFileName)
                    .Where(value => !string.IsNullOrEmpty(value) && !value.StartsWith("."))
                    .OrderBy(value => value, StringComparer.Ordinal)
            )
            {
                // Raw ids exist only in this in-process hash input. The Android
                // journal receives the digest, never the directory name or path.
                material.Add($"mod:{modId}");
            }

            if (File.Exists(AppPaths.ExternalModConfigFile))
            {
                var configHash = SHA256.HashData(File.ReadAllBytes(AppPaths.ExternalModConfigFile));
                material.Add($"mod-config:{Convert.ToHexString(configHash)}");
            }
        }
        catch
        {
            material.Add("mods:unavailable");
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", material)));
        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }
}
