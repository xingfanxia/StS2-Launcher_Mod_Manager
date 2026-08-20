using System;
using System.IO;

namespace STS2Mobile.Steam;

// Redirects only local Godot file I/O into an opaque per-account slot. Logical
// game/cloud paths stay unchanged, so existing Steam Cloud data remains visible
// inside each Steam account's server-side namespace. Game binaries and external
// Mods/Workshop remain shared. Legacy default/1 data is copied once, never moved
// or deleted.
public static class AccountDataIsolation
{
    private const string LegacyAdoptionMarker = "legacy_save_adopted_v1";
    private const string LegacyBackupAdopter = "legacy_backup_adopter_v1";
    private const string LegacyBackupAdoptionMarker = "legacy_backup_adopted_v1";
    private const string LogicalAccountRoot = "user://default/1";

    public static string ActiveSlot { get; private set; }

    public static bool TryActivate(string dataDir, string slot, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(dataDir) || !IsValidSlot(slot))
        {
            error = "The account data slot is invalid.";
            return false;
        }

        try
        {
            SensitiveLogRedactor.RegisterOpaqueValue(slot);
            var accountRoot = GetAccountRoot(dataDir, slot);
            var dataRoot = Path.Combine(accountRoot, "data", "default", "1");
            Directory.CreateDirectory(dataRoot);

            var marker = Path.Combine(dataDir, "account_profiles", LegacyAdoptionMarker);
            if (!File.Exists(marker))
            {
                var legacy = Path.Combine(dataDir, "default", "1");
                if (Directory.Exists(legacy))
                    CopyTreeWithoutOverwrite(legacy, dataRoot);

                CopyFileWithoutOverwrite(
                    Path.Combine(dataDir, "cloud_sync_enabled"),
                    Path.Combine(accountRoot, "cloud_sync_enabled")
                );
                CopyFileWithoutOverwrite(
                    Path.Combine(dataDir, "pending_upload_batch"),
                    Path.Combine(accountRoot, "pending_upload_batch")
                );

                Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
                WriteAtomic(Path.Combine(dataDir, "account_profiles", LegacyBackupAdopter), slot);
                WriteAtomic(marker, "complete");
            }

            ActiveSlot = slot;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void ClearActive() => ActiveSlot = null;

    public static string GetAccountRoot(string dataDir, string slot)
    {
        if (!IsValidSlot(slot))
            throw new ArgumentException("Invalid account data slot", nameof(slot));
        return Path.Combine(dataDir, "account_profiles", slot);
    }

    public static string GetAccountPreferencePath(string dataDir, string fileName)
    {
        if (!IsValidSlot(ActiveSlot))
            return Path.Combine(dataDir, fileName);
        return Path.Combine(GetAccountRoot(dataDir, ActiveSlot), fileName);
    }

    public static string GetExternalBackupDirectory(string externalSaveRoot, string kind)
    {
        if (kind is not ("manual" or "auto"))
            throw new ArgumentException("Unsupported backup kind", nameof(kind));
        if (!IsValidSlot(ActiveSlot))
            return Path.Combine(externalSaveRoot, kind);
        return Path.Combine(externalSaveRoot, "accounts", ActiveSlot, kind);
    }

    // Best-effort and retryable because shared-storage permission may be granted
    // after StartSession. Only the first adopted account may copy legacy backup
    // sets; the source is preserved and later accounts always start with an empty
    // backup namespace.
    public static bool TryAdoptExternalBackups(string dataDir, string externalSaveRoot)
    {
        if (!IsValidSlot(ActiveSlot))
            return false;
        try
        {
            var adopterPath = Path.Combine(dataDir, "account_profiles", LegacyBackupAdopter);
            if (!File.Exists(adopterPath))
                return false;
            var adopter = File.ReadAllText(adopterPath).Trim();
            if (!string.Equals(adopter, ActiveSlot, StringComparison.Ordinal))
                return false;

            var marker = Path.Combine(externalSaveRoot, "accounts", LegacyBackupAdoptionMarker);
            if (File.Exists(marker))
                return true;

            CopyTreeWithoutOverwrite(
                Path.Combine(externalSaveRoot, "manual"),
                GetExternalBackupDirectory(externalSaveRoot, "manual")
            );
            CopyTreeWithoutOverwrite(
                Path.Combine(externalSaveRoot, "auto"),
                GetExternalBackupDirectory(externalSaveRoot, "auto")
            );
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            WriteAtomic(marker, "complete");
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Harmony postfix for GodotFileIo.GetFullPath. It runs after the game has
    // composed its normal logical path, affecting local disk only; SteamKit2's
    // cloud store continues to receive the original logical path.
    public static string RewriteLocalGodotPath(string original)
    {
        if (!IsValidSlot(ActiveSlot) || string.IsNullOrEmpty(original))
            return original;
        var scopedRoot = $"user://account_profiles/{ActiveSlot}/data/default/1";

        // GodotFileIo.WriteFile resolves a path, then passes that resolved path
        // back through GetFullPath from RenameFile/FileExists. Because its
        // SaveDir remains the logical user://default/1 root, the second pass can
        // produce user://default/1/user://account_profiles/<slot>/... . Recover
        // that already-scoped path instead of nesting the account root again.
        // This is required for the first settings/save write in a new account.
        var doubledScopedRoot = LogicalAccountRoot + "/" + scopedRoot;
        if (
            original.Equals(doubledScopedRoot, StringComparison.Ordinal)
            || original.StartsWith(doubledScopedRoot + "/", StringComparison.Ordinal)
        )
            return original.Substring(LogicalAccountRoot.Length + 1);

        if (
            original.Equals(scopedRoot, StringComparison.Ordinal)
            || original.StartsWith(scopedRoot + "/", StringComparison.Ordinal)
        )
            return original;
        if (
            !original.Equals(LogicalAccountRoot, StringComparison.Ordinal)
            && !original.StartsWith(LogicalAccountRoot + "/", StringComparison.Ordinal)
        )
            return original;

        var suffix = original.Substring(LogicalAccountRoot.Length);
        return scopedRoot + suffix;
    }

    public static bool IsValidSlot(string slot)
    {
        if (slot == null || slot.Length != 32)
            return false;
        foreach (var c in slot)
        {
            if (!char.IsAsciiHexDigit(c) || char.IsUpper(c))
                return false;
        }
        return true;
    }

    private static void CopyTreeWithoutOverwrite(string source, string destination)
    {
        if (!Directory.Exists(source))
            return;
        Directory.CreateDirectory(destination);
        foreach (
            var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories)
        )
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target))
                File.Copy(file, target, overwrite: false);
        }
    }

    private static void CopyFileWithoutOverwrite(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
    }

    private static void WriteAtomic(string path, string contents)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, contents);
        File.Move(temporary, path, overwrite: true);
    }
}
