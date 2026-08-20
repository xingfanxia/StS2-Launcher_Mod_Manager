using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using SteamKit2;

namespace STS2Mobile.Steam;

// Verifies game ownership via Steam PICS and caches the result as an encrypted
// marker file. The marker persists indefinitely — ownership is checked once at
// initial login and never re-verified.
public class OwnershipVerifier
{
    private const uint AppId = 2868840;

    private readonly string _markerPath;
    private readonly string _legacyMarkerPath;
    private readonly string _accountName;

    public OwnershipVerifier(string dataDir, string accountName)
    {
        _legacyMarkerPath = Path.Combine(dataDir, "ownership_verified.enc");
        _markerPath = AccountDataIsolation.IsValidSlot(AccountDataIsolation.ActiveSlot)
            ? Path.Combine(
                AccountDataIsolation.GetAccountRoot(dataDir, AccountDataIsolation.ActiveSlot),
                "ownership_verified.enc"
            )
            : _legacyMarkerPath;
        _accountName = accountName;
    }

    public bool HasMarker()
    {
        try
        {
            var godotApp = GetGodotApp();
            if (MarkerMatches(_markerPath, godotApp))
                return true;

            // Upgrade path: copy a matching global v1 marker into the first
            // account slot, preserving the original file and offline play.
            if (_markerPath != _legacyMarkerPath && MarkerMatches(_legacyMarkerPath, godotApp))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
                var temporary = _markerPath + ".tmp";
                File.Copy(_legacyMarkerPath, temporary, overwrite: true);
                File.Move(temporary, _markerPath, overwrite: true);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    // Queries Steam PICS for app ownership. On success, saves a permanent marker
    // and sets the connection's AppAccessToken for depot downloads. Returns true
    // if the account owns the game.
    public async Task<bool> VerifyAsync(SteamConnection connection, bool persistMarker = true)
    {
        var result = await connection.Apps.PICSGetAccessTokens(AppId, null);
        bool owns = result.AppTokens.ContainsKey(AppId);

        if (owns)
        {
            result.AppTokens.TryGetValue(AppId, out var token);
            connection.AppAccessToken = token;
            if (persistMarker)
                SaveMarker();
        }

        return owns;
    }

    public void SaveMarker()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new Marker
                {
                    Account = _accountName,
                    VerifiedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                }
            );
            var godotApp = GetGodotApp();
            var encrypted = (string)godotApp?.Call("encryptString", json);
            if (encrypted != null)
                File.WriteAllText(_markerPath, encrypted);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Ownership] Failed to save marker: {ex.Message}");
        }
    }

    private bool MarkerMatches(string path, GodotObject godotApp)
    {
        if (!File.Exists(path) || godotApp == null)
            return false;
        var json = (string)godotApp.Call("decryptString", File.ReadAllText(path));
        if (json == null)
            return false;
        var marker = JsonSerializer.Deserialize<Marker>(json);
        return marker?.Account == _accountName;
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

    private class Marker
    {
        public string Account { get; set; }
        public long VerifiedAt { get; set; }
    }
}
