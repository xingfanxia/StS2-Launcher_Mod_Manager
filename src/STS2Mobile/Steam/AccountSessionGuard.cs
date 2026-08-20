namespace STS2Mobile.Steam;

// Pure commit predicate for async work that captured a Steam account session.
// Any account switch advances the generation and tears down the connection;
// both identities must still match before a renewed token may be persisted.
public static class AccountSessionGuard
{
    public static bool CanCommitRenewal(
        int capturedGeneration,
        int currentGeneration,
        ulong capturedSteamId,
        ulong currentSteamId,
        bool sameConnection
    ) =>
        capturedGeneration == currentGeneration
        && capturedSteamId != 0
        && capturedSteamId == currentSteamId
        && sameConnection;
}
