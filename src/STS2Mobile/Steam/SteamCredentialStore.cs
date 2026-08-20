using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace STS2Mobile.Steam;

public sealed record SteamAccountSummary(ulong SteamId, string AccountName, bool IsActive)
{
    public override string ToString() => nameof(SteamAccountSummary);
}

// Persists multiple Steam accounts in one Android-Keystore-encrypted vault.
// Account switching changes only the active vault entry; it never deletes
// credentials, saves, game files, Workshop content, mods, or cloud data.
public class SteamCredentialStore
{
    private const int CurrentVersion = 2;

    private readonly string _credentialsPath;
    private CredentialVault _vault = new() { Version = CurrentVersion };
    private SteamCredentials _legacyActive;
    private bool _loadFailed;

    private SteamCredentials Active =>
        _vault.Accounts.FirstOrDefault(a => a.SteamId == _vault.ActiveSteamId) ?? _legacyActive;

    public string AccountName => Active?.AccountName;
    public string RefreshToken => Active?.RefreshToken;
    public string GuardData => Active?.GuardData;
    public ulong SteamId => Active?.SteamId ?? 0;
    public string DataSlot => Active?.DataSlot;
    public bool LoadFailed => _loadFailed;
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Active?.RefreshToken)
        && !string.IsNullOrWhiteSpace(Active?.AccountName);

    public IReadOnlyList<SteamAccountSummary> Accounts =>
        _vault
            .Accounts.Where(a => a.SteamId != 0 && !string.IsNullOrWhiteSpace(a.AccountName))
            .Select(a => new SteamAccountSummary(
                a.SteamId,
                a.AccountName,
                a.SteamId == _vault.ActiveSteamId
            ))
            .ToArray();

    public string GetGuardDataForAccount(string accountName) =>
        _vault
            .Accounts.FirstOrDefault(a =>
                string.Equals(a.AccountName, accountName, StringComparison.OrdinalIgnoreCase)
            )
            ?.GuardData;

    public string GetDataSlot(ulong steamId) =>
        _vault.Accounts.FirstOrDefault(a => a.SteamId == steamId)?.DataSlot;

    public static string CreateDataSlot() => Guid.NewGuid().ToString("N");

    public SteamCredentialStore(string dataDir)
    {
        _credentialsPath = Path.Combine(dataDir, "steam_credentials.enc");
    }

    public void Load()
    {
        _vault = new CredentialVault { Version = CurrentVersion };
        _legacyActive = null;
        _loadFailed = false;
        try
        {
            if (!File.Exists(_credentialsPath))
                return;

            var encrypted = File.ReadAllText(_credentialsPath);
            var godotApp = GetGodotApp();
            if (godotApp == null)
            {
                PatchHelper.Log("[Credentials] Credential service unavailable");
                _loadFailed = true;
                return;
            }

            var json = (string)godotApp.Call("decryptString", encrypted);
            if (string.IsNullOrWhiteSpace(json))
            {
                // Preserve the unreadable file. Deleting it would silently remove
                // every stored account after a transient Keystore/bridge failure.
                PatchHelper.Log("[Credentials] Vault decryption failed; encrypted file preserved");
                _loadFailed = true;
                return;
            }

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("Accounts", out _))
            {
                _vault =
                    JsonSerializer.Deserialize<CredentialVault>(json)
                    ?? new CredentialVault { Version = CurrentVersion };
                var vaultError = SteamCredentialVaultPolicy.Validate(
                    _vault.Version,
                    CurrentVersion,
                    _vault.ActiveSteamId,
                    _vault
                        .Accounts?.Select(account => new SteamCredentialDescriptor(
                            account?.SteamId ?? 0,
                            !string.IsNullOrWhiteSpace(account?.AccountName),
                            !string.IsNullOrWhiteSpace(account?.RefreshToken),
                            account?.DataSlot
                        ))
                        .ToArray()
                );
                if (vaultError != SteamCredentialVaultError.None)
                    throw new InvalidDataException($"Credential vault rejected: {vaultError}");
            }
            else
            {
                // v1 migration: retain the legacy entry in memory and convert it
                // to a v2 account entry as soon as its SteamID is known.
                _legacyActive = JsonSerializer.Deserialize<SteamCredentials>(json);
                if (_legacyActive != null)
                {
                    SteamAccountIdentity.TryGetSteamId(_legacyActive.RefreshToken, out var id);
                    _legacyActive.SteamId = id;
                    if (id != 0)
                    {
                        _legacyActive.DataSlot = CreateDataSlot();
                        _vault.Accounts.Add(_legacyActive);
                        _vault.ActiveSteamId = id;
                        _legacyActive = null;
                        if (!Persist())
                            throw new InvalidOperationException(
                                "Could not persist the migrated account vault"
                            );
                    }
                }
            }

            RegisterSecrets();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Credentials] Vault load failed: {ex.GetType().Name}");
            _vault = new CredentialVault { Version = CurrentVersion };
            _legacyActive = null;
            _loadFailed = true;
        }
    }

    public bool Save(
        string accountName,
        string refreshToken,
        string guardData,
        ulong steamId,
        string dataSlot
    )
    {
        if (_loadFailed)
            return false;
        if (
            steamId == 0
            || string.IsNullOrWhiteSpace(accountName)
            || string.IsNullOrWhiteSpace(refreshToken)
            || !AccountDataIsolation.IsValidSlot(dataSlot)
        )
            return false;

        var previousActive = _vault.ActiveSteamId;
        var previousLegacy = _legacyActive;
        var existing = _vault.Accounts.FirstOrDefault(a => a.SteamId == steamId);
        var wasAdded = existing == null;
        var prior =
            existing == null
                ? null
                : new SteamCredentials
                {
                    SteamId = existing.SteamId,
                    AccountName = existing.AccountName,
                    RefreshToken = existing.RefreshToken,
                    GuardData = existing.GuardData,
                    DataSlot = existing.DataSlot,
                };
        if (existing == null)
        {
            existing = new SteamCredentials { SteamId = steamId, DataSlot = dataSlot };
            _vault.Accounts.Add(existing);
        }
        existing.AccountName = accountName;
        existing.RefreshToken = refreshToken;
        existing.GuardData = guardData;
        if (!AccountDataIsolation.IsValidSlot(existing.DataSlot))
            existing.DataSlot = dataSlot;
        _vault.ActiveSteamId = steamId;
        _legacyActive = null;
        SensitiveLogRedactor.RegisterAccount(accountName, steamId, refreshToken, guardData);
        if (Persist())
            return true;

        _vault.ActiveSteamId = previousActive;
        _legacyActive = previousLegacy;
        if (wasAdded)
            _vault.Accounts.Remove(existing);
        else
        {
            existing.AccountName = prior.AccountName;
            existing.RefreshToken = prior.RefreshToken;
            existing.GuardData = prior.GuardData;
            existing.DataSlot = prior.DataSlot;
        }
        return false;
    }

    public bool TryActivate(ulong steamId)
    {
        if (_loadFailed)
            return false;
        if (steamId == 0 || _vault.Accounts.All(a => a.SteamId != steamId))
            return false;
        var previousActive = _vault.ActiveSteamId;
        _vault.ActiveSteamId = steamId;
        _legacyActive = null;
        if (Persist())
            return true;
        _vault.ActiveSteamId = previousActive;
        return false;
    }

    public bool TryUpdateRefreshToken(ulong steamId, string expectedToken, string newToken)
    {
        if (_loadFailed)
            return false;
        var account = _vault.Accounts.FirstOrDefault(a => a.SteamId == steamId);
        if (
            account == null
            || !string.Equals(account.RefreshToken, expectedToken, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(newToken)
        )
            return false;
        var previousToken = account.RefreshToken;
        account.RefreshToken = newToken;
        SensitiveLogRedactor.RegisterAccount(account.AccountName, steamId, newToken);
        if (Persist())
            return true;
        account.RefreshToken = previousToken;
        return false;
    }

    private bool Persist()
    {
        try
        {
            var dir = Path.GetDirectoryName(_credentialsPath)!;
            Directory.CreateDirectory(dir);
            var godotApp = GetGodotApp();
            if (godotApp == null)
            {
                PatchHelper.Log("[Credentials] Credential service unavailable");
                return false;
            }

            _vault.Version = CurrentVersion;
            var json = JsonSerializer.Serialize(_vault);
            var encrypted = (string)godotApp.Call("encryptString", json);
            if (string.IsNullOrWhiteSpace(encrypted))
            {
                PatchHelper.Log("[Credentials] Vault encryption failed");
                return false;
            }

            if (
                !NonDestructiveFileTransaction.TryWriteAtomic(
                    _credentialsPath,
                    encrypted,
                    out var failureType
                )
            )
            {
                PatchHelper.Log($"[Credentials] Vault publish failed: {failureType}");
                return false;
            }
            PatchHelper.Log("[Credentials] Encrypted account vault saved");
            return true;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Credentials] Vault save failed: {ex.GetType().Name}");
            return false;
        }
    }

    private void RegisterSecrets()
    {
        foreach (var account in _vault.Accounts)
            SensitiveLogRedactor.RegisterAccount(
                account.AccountName,
                account.SteamId,
                account.RefreshToken,
                account.GuardData
            );
        if (_legacyActive != null)
            SensitiveLogRedactor.RegisterAccount(
                _legacyActive.AccountName,
                _legacyActive.SteamId,
                _legacyActive.RefreshToken,
                _legacyActive.GuardData
            );
    }

    private static bool IsUsable(SteamCredentials account) =>
        account != null
        && account.SteamId != 0
        && !string.IsNullOrWhiteSpace(account.AccountName)
        && !string.IsNullOrWhiteSpace(account.RefreshToken);

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

    private sealed class CredentialVault
    {
        public int Version { get; set; }
        public ulong ActiveSteamId { get; set; }
        public List<SteamCredentials> Accounts { get; set; } = new();
    }

    private sealed class SteamCredentials
    {
        public ulong SteamId { get; set; }
        public string AccountName { get; set; }
        public string RefreshToken { get; set; }
        public string GuardData { get; set; }
        public string DataSlot { get; set; }
    }
}
