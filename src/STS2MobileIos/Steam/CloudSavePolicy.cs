using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace STS2MobileIos.Steam;

public static class CloudSavePolicy
{
    public static string Classification(string path)
    {
        if (!SafePath(path)) return "Unknown path";
        if (path.StartsWith("modded/", StringComparison.Ordinal))
            return CloudProfileImport.SourceProfile(path) != null ? "Modded candidate" : "Modded - archive only";
        return Regex.IsMatch(path, @"^(?:[0-9]+/)?profile[1-3]/saves/(?:progress|prefs|current_run)\.save$")
            ? "Vanilla candidate" : "Other file - archive only";
    }

    public static bool SafePath(string path) => !string.IsNullOrWhiteSpace(path) && path.Length < 512
        && !path.Contains('\\') && !path.Contains(':') && !path.Any(char.IsControl)
        && path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

    public static byte[] Decode(byte[] data)
    {
        if (data.Length < 4 || data[0] != 'P' || data[1] != 'K' || data[2] != 3 || data[3] != 4)
            return data;
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        if (zip.Entries.Count != 1 || zip.Entries[0].Length > SteamCloudClient.MaximumSaveBytes)
            throw new InvalidDataException("Unsupported cloud save archive.");
        using var input = zip.Entries[0].Open();
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            if (output.Length + count > SteamCloudClient.MaximumSaveBytes)
                throw new InvalidDataException("Cloud save is too large after decompression.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public static string Describe(byte[] data)
    {
        try
        {
            using var json = JsonDocument.Parse(data);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                return "Unknown save format; archived only.";
            var info = new List<string>();
            foreach (var property in json.RootElement.EnumerateObject())
            {
                if (property.Name is "version" or "schema_version" or "game_version")
                    info.Add(property.Name + ": " + property.Value.ToString());
                if (property.Name.Contains("mod", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind != JsonValueKind.Null)
                    info.Add("Contains mod metadata; compatibility requires review.");
            }
            return info.Count == 0 ? "JSON save; compatibility has not been established." : string.Join("\n", info);
        }
        catch (JsonException) { return "Unknown save format; archived only."; }
    }

    public static string Archive(string directory, CloudFile file, byte[] data)
    {
        if (!SafePath(file.Name))
            throw new InvalidDataException("Cloud path is unsafe; no files were written.");
        string snapshot = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(snapshot);
        string path = Path.Combine(snapshot, Path.GetFileName(file.Name));
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            output.Write(data);
        return path;
    }
}
