using System;
using System.Collections.Generic;

namespace STS2Mobile;

// Last-line defense for diagnostics. Account names, SteamIDs and credentials
// registered by the encrypted credential store are redacted before either the
// console or the launcher's visible log receives a message.
public static class SensitiveLogRedactor
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> AccountValues = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TokenValues = new(StringComparer.Ordinal);
    private static readonly HashSet<string> OpaqueValues = new(StringComparer.Ordinal);

    public static void RegisterAccount(
        string accountName,
        ulong steamId,
        string refreshToken,
        string guardData = null
    )
    {
        lock (Gate)
        {
            if (!string.IsNullOrWhiteSpace(accountName))
                AccountValues.Add(accountName);
            if (steamId != 0)
                AccountValues.Add(
                    steamId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                );
            if (!string.IsNullOrWhiteSpace(refreshToken))
                TokenValues.Add(refreshToken);
            if (!string.IsNullOrWhiteSpace(guardData))
                TokenValues.Add(guardData);
        }
    }

    public static void RegisterOpaqueValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        lock (Gate)
            OpaqueValues.Add(value);
    }

    public static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        lock (Gate)
        {
            foreach (var token in TokenValues)
                message = message.Replace(token, "<steam-token>", StringComparison.Ordinal);
            foreach (var value in OpaqueValues)
                message = message.Replace(value, "<private-slot>", StringComparison.Ordinal);
            foreach (var account in AccountValues)
                message = message.Replace(
                    account,
                    "<steam-account>",
                    StringComparison.OrdinalIgnoreCase
                );
        }
        return message;
    }
}
