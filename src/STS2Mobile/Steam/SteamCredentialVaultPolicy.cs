using System.Collections.Generic;
using System.Linq;

namespace STS2Mobile.Steam;

public readonly record struct SteamCredentialDescriptor(
    ulong SteamId,
    bool HasAccountName,
    bool HasRefreshToken,
    string DataSlot
)
{
    public override string ToString() => nameof(SteamCredentialDescriptor);
}

public enum SteamCredentialVaultError
{
    None,
    UnsupportedVersion,
    MissingAccounts,
    InvalidEntry,
    DuplicateAccount,
    InvalidDataSlot,
    InvalidActiveAccount,
}

// Pure fail-closed validation for the encrypted multi-account vault. Invalid
// metadata must never be normalized into a new slot or silently discard an
// account, because either behavior can make intact saves appear to vanish.
public static class SteamCredentialVaultPolicy
{
    public static SteamCredentialVaultError Validate(
        int version,
        int currentVersion,
        ulong activeSteamId,
        IReadOnlyList<SteamCredentialDescriptor> accounts
    )
    {
        if (version != currentVersion)
            return SteamCredentialVaultError.UnsupportedVersion;
        if (accounts == null)
            return SteamCredentialVaultError.MissingAccounts;
        if (
            accounts.Any(account =>
                account.SteamId == 0 || !account.HasAccountName || !account.HasRefreshToken
            )
        )
            return SteamCredentialVaultError.InvalidEntry;
        if (accounts.GroupBy(account => account.SteamId).Any(group => group.Count() != 1))
            return SteamCredentialVaultError.DuplicateAccount;
        if (accounts.Any(account => !AccountDataIsolation.IsValidSlot(account.DataSlot)))
            return SteamCredentialVaultError.InvalidDataSlot;
        if (
            accounts.Count == 0
                ? activeSteamId != 0
                : accounts.All(account => account.SteamId != activeSteamId)
        )
            return SteamCredentialVaultError.InvalidActiveAccount;
        return SteamCredentialVaultError.None;
    }
}
