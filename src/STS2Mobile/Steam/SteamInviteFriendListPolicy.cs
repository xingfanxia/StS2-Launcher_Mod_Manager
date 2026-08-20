using System;
using System.Collections.Generic;
using System.Linq;

namespace STS2Mobile.Steam;

// Pure friend-picker policy. Keeping search, visibility, and rank semantics
// outside the Godot dialog makes the privacy-sensitive recipient list easy to
// test without persisting or logging any friend data.
internal static class SteamInviteFriendListPolicy
{
    // Search must see the complete bounded relationship set. Rendering remains
    // much smaller so an account with thousands of friends cannot create
    // thousands of Godot Controls in one frame.
    internal const int MaxSearchableFriends = 5000;
    internal const int MaxRenderedFriends = 200;

    internal static bool HasNickname(string nickname) => !string.IsNullOrWhiteSpace(nickname);

    internal static string PrimaryName(string personaName, string nickname) =>
        HasNickname(nickname) ? nickname : personaName ?? string.Empty;

    internal static bool Matches(string personaName, string nickname, string query)
    {
        query = query?.Trim() ?? string.Empty;
        return query.Length == 0
            || (personaName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (nickname?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    internal static bool IsVisible(bool isOnline, bool showOffline, string query = null) =>
        isOnline || showOffline || !string.IsNullOrWhiteSpace(query);

    internal static IReadOnlyList<T> RenderWindow<T>(IEnumerable<T> orderedMatches) =>
        (orderedMatches ?? Enumerable.Empty<T>()).Take(MaxRenderedFriends).ToList();

    internal static int Rank(
        bool hasNickname,
        bool isPlayingGame,
        bool playedRecently,
        bool isOnline
    ) =>
        (hasNickname ? 8 : 0)
        | (isPlayingGame ? 4 : 0)
        | (playedRecently ? 2 : 0)
        | (isOnline ? 1 : 0);

    internal static string IdentityKey(string personaName, string nickname) =>
        $"{PrimaryName(personaName, nickname)}\u001f{personaName ?? string.Empty}";
}
