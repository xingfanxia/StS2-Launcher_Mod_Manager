using System.Text.RegularExpressions;

namespace STS2MobileIos.Steam;

public enum CloudFileKind { Saves, History, Other }

public sealed record CloudBrowserEntry(CloudFile File, bool IsModded, CloudFileKind Kind, int? Profile, string Title)
{
    public string ImportProfile => Kind == CloudFileKind.Saves
        ? CloudProfileImport.SourceProfile(File.Name) : null;
}

public static class CloudBrowser
{
    public static CloudBrowserEntry Describe(CloudFile file)
    {
        string path = file.Name;
        bool modded = path.Split('/').Any(part => part.Equals("modded", StringComparison.OrdinalIgnoreCase));
        if (!CloudSavePolicy.SafePath(path))
            return new(file, modded, CloudFileKind.Other, null, "Unrecognized path");
        var profileMatch = Regex.Match(path, @"\A(?:modded/)?profile([1-3])/saves/");
        int? profile = profileMatch.Success ? int.Parse(profileMatch.Groups[1].Value) : null;
        string name = Path.GetFileName(path);
        string profileLabel = profile.HasValue ? $"Profile {profile}  |  " : "";
        bool profileSave = Regex.IsMatch(path, @"\A(?:modded/)?profile[1-3]/saves/(progress|prefs|current_run)\.save\z");
        if (profileSave)
        {
            string purpose = name switch {
                "progress.save" => "Progress & unlocks",
                "current_run.save" => "Current run",
                _ => "Preferences",
            };
            return new(file, modded, CloudFileKind.Saves, profile, profileLabel + purpose);
        }
        if (name.EndsWith(".run", StringComparison.OrdinalIgnoreCase))
            return new(file, modded, CloudFileKind.History, profile, profileLabel + name);
        return new(file, modded, CloudFileKind.Other, profile, path);
    }

    public static List<CloudBrowserEntry> Filter(IEnumerable<CloudBrowserEntry> entries, bool modded, CloudFileKind kind)
    {
        var filtered = entries.Where(entry => entry.IsModded == modded && entry.Kind == kind);
        if (kind == CloudFileKind.History)
            return filtered.OrderByDescending(entry => entry.File.Timestamp).ThenBy(entry => entry.File.Name, StringComparer.Ordinal).ToList();
        return filtered.OrderBy(entry => entry.Profile ?? int.MaxValue)
            .ThenBy(entry => Path.GetFileName(entry.File.Name) switch { "progress.save" => 0, "current_run.save" => 1, "prefs.save" => 2, _ => 3 })
            .ThenBy(entry => entry.File.Name, StringComparer.Ordinal).ToList();
    }
}
