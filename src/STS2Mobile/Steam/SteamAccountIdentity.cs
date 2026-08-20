using System;
using System.Text.Json;

namespace STS2Mobile.Steam;

// Extracts the stable SteamID from a Steam refresh token without ever exposing
// the token to logs. The token is still authenticated by SteamConnection before
// it is accepted; this parser only supplies the account-scoped local path early
// enough for the game's save system to initialize against the correct account.
public static class SteamAccountIdentity
{
    public static bool TryGetSteamId(string refreshToken, out ulong steamId)
    {
        steamId = 0;
        if (string.IsNullOrWhiteSpace(refreshToken))
            return false;

        var parts = refreshToken.Split('.');
        if (parts.Length != 3 || parts[1].Length == 0 || parts[1].Length > 16_384)
            return false;

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            var bytes = Convert.FromBase64String(payload);
            using var document = JsonDocument.Parse(bytes);
            if (!document.RootElement.TryGetProperty("sub", out var subject))
                return false;

            if (subject.ValueKind == JsonValueKind.String)
                return ulong.TryParse(subject.GetString(), out steamId) && steamId != 0;
            if (subject.ValueKind == JsonValueKind.Number)
                return subject.TryGetUInt64(out steamId) && steamId != 0;
            return false;
        }
        catch
        {
            steamId = 0;
            return false;
        }
    }
}
