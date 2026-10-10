using System.Text.RegularExpressions;

namespace STS2MobileIos.Steam;

public static class ProgressImportWarnings
{
    // ProgressState keeps these unknown history entries in separate collections
    // and includes them again in ToSerializable. Nested removals are not equivalent.
    public static bool IsRetainedHistory(bool fatal, string path, string message)
    {
        if (fatal || path == null || message == null) return false;
        var stats = Regex.Match(path, @"\A(CharStats|CardStats|EncounterStats|EnemyStats|AncientStats)\.\[[0-9]+\]\z");
        string prefix = stats.Success ? stats.Groups[1].Value switch {
            "CharStats" => "Unknown character ID: CHARACTER.",
            "CardStats" => "Unknown card ID: CARD.",
            "EncounterStats" => "Unknown encounter ID: ENCOUNTER.",
            "EnemyStats" => "Unknown enemy ID: MONSTER.",
            "AncientStats" => "Unknown ancient event ID: EVENT.",
            _ => null,
        } : path switch {
            "DiscoveredCards" => "Unknown CardModel ID: CARD.",
            "DiscoveredRelics" => "Unknown RelicModel ID: RELIC.",
            "DiscoveredPotions" => "Unknown PotionModel ID: POTION.",
            "DiscoveredEvents" => "Unknown EventModel ID: EVENT.",
            "DiscoveredActs" => "Unknown ActModel ID: ACT.",
            _ => null,
        };
        return prefix != null && message.StartsWith(prefix, StringComparison.Ordinal)
            && Regex.IsMatch(message[prefix.Length..], @"\A[A-Z0-9_]+\z");
    }

    public static string Summary(int count) => count == 0 ? "" :
        $"\nCompatibility note: {count} historical references are unavailable in this game build. "
        + "They remain in the save, but their content will not appear on this iPad. Current-run content is checked separately.";
}
