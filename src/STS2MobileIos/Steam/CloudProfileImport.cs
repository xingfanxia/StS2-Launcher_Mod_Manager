using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace STS2MobileIos.Steam;

public sealed record CloudProfileCopy(string Source, IReadOnlyList<CloudFile> Files, Dictionary<string, byte[]> Data)
{
    public bool IsModded => Source.StartsWith("modded/", StringComparison.Ordinal);
}
public sealed record CloudImportReceipt(int Slot, string Snapshot);

public static class CloudProfileImport
{
    public static string SourceProfile(string name)
    {
        var match = Regex.Match(name ?? "", @"\A((?:modded/)?profile[1-3])/saves/(progress|prefs|current_run)\.save\z");
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string ModeMismatch(bool sourceModded, bool runningModded) => sourceModded == runningModded ? null
        : sourceModded ? "Enable the profile's mods and restart before importing a modded Cloud profile."
        : "Disable mods and restart before importing a vanilla Cloud profile.";

    public static string ProfileDirectory(string accountDirectory, int slot, bool modded) =>
        Path.Combine(modded ? Path.Combine(accountDirectory, "modded") : accountDirectory, "profile" + slot);

    public static async Task<CloudProfileCopy> Prepare(SteamCloudClient client, string source, bool includeRun,
        CancellationToken cancellation)
    {
        if (SourceProfile(source + "/saves/progress.save") != source)
            throw new InvalidOperationException("Choose progress.save, prefs.save, or current_run.save from a profile.");
        var inventory = await client.ListFiles(cancellation);
        var files = SelectFiles(inventory, source, includeRun);
        var data = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in files)
            data.Add(Path.GetFileName(file.Name), await client.Download(file, cancellation));
        var copy = new CloudProfileCopy(source, files, data);
        await Recheck(client, copy, includeRun, cancellation);
        return copy;
    }

    private static List<CloudFile> SelectFiles(IEnumerable<CloudFile> inventory, string source, bool includeRun)
    {
        var selected = inventory.Where(file => file.Name == source + "/saves/progress.save"
            || file.Name == source + "/saves/prefs.save"
            || (includeRun && file.Name == source + "/saves/current_run.save")).OrderBy(file => file.Name).ToList();
        if (!selected.Any(file => file.Name.EndsWith("/progress.save", StringComparison.Ordinal)))
            throw new InvalidOperationException("This cloud profile has no progress.save to import.");
        if (includeRun && !selected.Any(file => file.Name.EndsWith("/current_run.save", StringComparison.Ordinal)))
            throw new InvalidOperationException("This cloud profile has no current run. Turn off Include current run to import progress.");
        return selected;
    }

    public static async Task Recheck(SteamCloudClient client, CloudProfileCopy copy, bool includeRun, CancellationToken cancellation)
    {
        var latest = SelectFiles(await client.ListFiles(cancellation), copy.Source, includeRun);
        if (!copy.Files.SequenceEqual(latest))
            throw new InvalidOperationException("Cloud profile changed. Close the desktop game and preview the import again.");
        // Some Steam inventories omit hashes; compare actual content before committing.
        foreach (var file in latest.Where(file => string.IsNullOrEmpty(file.Sha1)))
            if (!(await client.Download(file, cancellation)).SequenceEqual(copy.Data[Path.GetFileName(file.Name)]))
                throw new InvalidOperationException("Cloud profile changed. Preview the import again.");
    }

    public static int FindUnusedSlot(string accountDirectory, int activeSlot, bool modded = false)
    {
        for (int slot = 1; slot <= 3; slot++)
        {
            string path = ProfileDirectory(accountDirectory, slot, modded);
            if (slot != activeSlot && IsEmptySlot(path)) return slot;
        }
        throw new InvalidOperationException("All three iPad profile slots are active or contain saved files. Existing profiles will not be replaced.");
    }

    public static bool IsEmptySlot(string path)
    {
        if (!Path.Exists(path)) return true;
        try
        {
            var pending = new Stack<string>();
            pending.Push(path);
            int count = 0;
            while (pending.TryPop(out string directory))
            {
                var attributes = File.GetAttributes(directory);
                if (++count > 10_000 || (attributes & FileAttributes.ReparsePoint) != 0
                    || (attributes & FileAttributes.Directory) == 0) return false;
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory)) pending.Push(entry);
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static CloudImportReceipt Commit(string accountDirectory, string snapshotDirectory, int slot,
        CloudProfileCopy copy, string steamId)
    {
        if (slot is < 1 or > 3 || SourceProfile(copy.Source + "/saves/progress.save") != copy.Source
            || !copy.Data.ContainsKey("progress.save") || copy.Data.Count > 3
            || copy.Data.Any(pair => pair.Key is not ("progress.save" or "prefs.save" or "current_run.save")
                || pair.Value.Length > SteamCloudClient.MaximumSaveBytes))
            throw new InvalidOperationException("Invalid profile import.");
        string destination = ProfileDirectory(accountDirectory, slot, copy.IsModded);
        if (!IsEmptySlot(destination))
            throw new InvalidOperationException("The selected iPad slot is no longer unused. Preview again.");
        Directory.CreateDirectory(snapshotDirectory);
        string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
        string pending = Path.Combine(snapshotDirectory, ".pending-" + id);
        string snapshot = Path.Combine(snapshotDirectory, id);
        string staging = Path.Combine(accountDirectory, ".cloud-import-" + id);
        try
        {
            Directory.CreateDirectory(pending);
            var manifest = new JsonObject { ["source_profile"] = copy.Source, ["steam_id"] = steamId,
                ["destination_slot"] = slot, ["destination_profile"] = (copy.IsModded ? "modded/" : "") + "profile" + slot,
                ["created_utc"] = DateTimeOffset.UtcNow.ToString("O") };
            var local = new JsonArray();
            long total = 0;
            int count = 0;
            BackupDirectory(accountDirectory, Path.Combine(pending, "local"), "", local, ref total, ref count);
            manifest["local_files"] = local;
            var remote = new JsonArray();
            foreach (var file in copy.Files)
                remote.Add(new JsonObject { ["name"] = file.Name, ["timestamp"] = file.Timestamp,
                    ["sha256"] = Convert.ToHexString(SHA256.HashData(copy.Data[Path.GetFileName(file.Name)])) });
            manifest["cloud_files"] = remote;
            foreach (var pair in copy.Data)
                WriteNew(Path.Combine(pending, "incoming", pair.Key), pair.Value);
            WriteNew(Path.Combine(pending, "manifest.json"), System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString()));
            Directory.Move(pending, snapshot);
            foreach (var pair in copy.Data)
                WriteNew(Path.Combine(staging, "saves", pair.Key), pair.Value);
            // The game can pre-create empty profile/saves/history directories.
            // Non-recursive deletion fails if a file appears before publication.
            if (Directory.Exists(destination)) RemoveEmptyDirectories(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
            return new(slot, snapshot);
        }
        finally
        {
            if (Directory.Exists(pending)) Directory.Delete(pending, true);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static void RemoveEmptyDirectories(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The destination slot changed. Preview the import again.");
        foreach (string child in Directory.EnumerateDirectories(directory)) RemoveEmptyDirectories(child);
        Directory.Delete(directory, recursive: false);
    }

    private static void BackupDirectory(string source, string destination, string relative, JsonArray manifest,
        ref long total, ref int count)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("A linked save directory cannot be backed up.");
        Directory.CreateDirectory(destination);
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A linked save file cannot be backed up.");
            if (++count > 10_000) throw new InvalidOperationException("The local backup has too many files.");
            string name = Path.GetFileName(entry), path = relative + name;
            if ((attributes & FileAttributes.Directory) != 0)
                BackupDirectory(entry, Path.Combine(destination, name), path + "/", manifest, ref total, ref count);
            else
            {
                using var input = File.OpenRead(entry);
                total += input.Length;
                if (input.Length > SteamCloudClient.MaximumSaveBytes || total > 256L * 1024 * 1024)
                    throw new InvalidOperationException("The local backup exceeds the import size limit.");
                using var output = new FileStream(Path.Combine(destination, name), FileMode.CreateNew);
                input.CopyTo(output);
                output.Flush(true);
                input.Position = 0;
                manifest.Add(new JsonObject { ["path"] = path, ["sha256"] = Convert.ToHexString(SHA256.HashData(input)) });
            }
        }
    }

    private static void WriteNew(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        output.Write(data);
        output.Flush(true);
    }
}
