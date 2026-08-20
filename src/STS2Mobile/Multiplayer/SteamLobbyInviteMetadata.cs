using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace STS2Mobile.Multiplayer;

public enum SteamLobbyInviteSenderTrust
{
    Unknown,
    Friend,
    NonFriend,
    Blocked,
}

public enum SteamLobbyInviteMetadataError
{
    None,
    UntrustedSender,
    MissingField,
    UnknownField,
    TooLong,
    UnsupportedSchema,
    WrongAppId,
    UnknownTransport,
    InvalidExpectedBuild,
    InvalidBuild,
    IncompatibleLauncherBuild,
    IncompatibleGameBuild,
    InvalidExpiry,
    Expired,
    ExpiryTooFarInFuture,
    InvalidNonce,
    InvalidEndpoint,
    TooManyEndpoints,
    DuplicateEndpoint,
    ReplayStateUnavailable,
    Replay,
    ReplayCapacityExceeded,
}

public enum SteamLobbyInviteReplayResult
{
    Accepted,
    Replay,
    CapacityExceeded,
}

public enum SteamLobbyInviteSendRateResult
{
    Accepted,
    InvalidTarget,
    TargetCooldown,
    GlobalLimit,
}

// A parsed launcher-to-launcher invitation. It deliberately carries no sender
// Steam ID, account name, credential, friend-list entry, save path, or mod list.
// ToString is intentionally non-diagnostic because endpoints are private
// connection data and must not reach logs by accidental interpolation.
public sealed class SteamLobbyDirectInvite
{
    public string LauncherBuild { get; }
    public string GameBuild { get; }
    public long ExpiresAtUnixSeconds { get; }
    public IReadOnlyList<LanJoinEndpoint> EndpointCandidates { get; }

    internal SteamLobbyDirectInvite(
        string launcherBuild,
        string gameBuild,
        long expiresAtUnixSeconds,
        IReadOnlyList<LanJoinEndpoint> endpointCandidates
    )
    {
        LauncherBuild = launcherBuild;
        GameBuild = gameBuild;
        ExpiresAtUnixSeconds = expiresAtUnixSeconds;
        EndpointCandidates = endpointCandidates;
    }

    public override string ToString() => nameof(SteamLobbyDirectInvite);
}

// Bounded, in-memory replay protection for invite prompts. Live entries are
// never evicted merely to admit attacker-controlled unique nonces: capacity
// pressure fails closed until an entry expires or the owning session ends.
public sealed class SteamLobbyInviteReplayGuard
{
    public const int DefaultCapacity = 128;
    public const int MaxCapacity = 1024;

    private readonly object _sync = new();
    private readonly int _capacity;
    private readonly Dictionary<string, long> _consumed = new(StringComparer.Ordinal);

    public SteamLobbyInviteReplayGuard(int capacity = DefaultCapacity)
    {
        if (capacity is < 1 or > MaxCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    internal SteamLobbyInviteReplayResult TryConsume(
        string nonce,
        long expiresAtUnixSeconds,
        long nowUnixSeconds
    )
    {
        lock (_sync)
        {
            var expired = new List<string>();
            foreach (var pair in _consumed)
            {
                if (pair.Value <= nowUnixSeconds)
                    expired.Add(pair.Key);
            }
            foreach (var key in expired)
                _consumed.Remove(key);

            if (_consumed.ContainsKey(nonce))
                return SteamLobbyInviteReplayResult.Replay;
            if (_consumed.Count >= _capacity)
                return SteamLobbyInviteReplayResult.CapacityExceeded;

            _consumed.Add(nonce, expiresAtUnixSeconds);
            return SteamLobbyInviteReplayResult.Accepted;
        }
    }
}

// A bridge lifetime owns one limiter. It bounds both accidental double taps and
// a compromised UI loop without persisting friend identities or contacting any
// user until an explicit selection has passed this gate.
public sealed class SteamLobbyInviteSendRateLimiter
{
    public const int MaxInvitesPerWindow = 3;
    public const long WindowSeconds = 60;
    public const long TargetCooldownSeconds = 10;

    private readonly object _sync = new();
    private readonly Queue<long> _global = new();
    private readonly Dictionary<ulong, long> _lastByTarget = new();

    public SteamLobbyInviteSendRateResult TryAcquire(ulong targetSteamId, long nowUnixSeconds)
    {
        if (targetSteamId == 0 || nowUnixSeconds < 0)
            return SteamLobbyInviteSendRateResult.InvalidTarget;

        lock (_sync)
        {
            while (_global.Count > 0 && nowUnixSeconds - _global.Peek() >= WindowSeconds)
                _global.Dequeue();

            var expiredTargets = new List<ulong>();
            foreach (var pair in _lastByTarget)
            {
                if (nowUnixSeconds - pair.Value >= WindowSeconds)
                    expiredTargets.Add(pair.Key);
            }
            foreach (var target in expiredTargets)
                _lastByTarget.Remove(target);

            if (
                _lastByTarget.TryGetValue(targetSteamId, out long lastSent)
                && nowUnixSeconds - lastSent < TargetCooldownSeconds
            )
                return SteamLobbyInviteSendRateResult.TargetCooldown;
            if (_global.Count >= MaxInvitesPerWindow)
                return SteamLobbyInviteSendRateResult.GlobalLimit;

            _global.Enqueue(nowUnixSeconds);
            _lastByTarget[targetSteamId] = nowUnixSeconds;
            return SteamLobbyInviteSendRateResult.Accepted;
        }
    }
}

public static class SteamLobbyInviteSessionGuard
{
    public static bool CanHandleCallback(
        long callbackGeneration,
        long currentGeneration,
        ulong invitedSteamId,
        ulong authenticatedSteamId,
        bool surfaceActive,
        bool connectionActive
    ) =>
        callbackGeneration == currentGeneration
        && invitedSteamId != 0
        && invitedSteamId == authenticatedSteamId
        && surfaceActive
        && connectionActive;

    public static bool CanAcceptInvite(
        long expiresAtUnixSeconds,
        long nowUnixSeconds,
        bool pendingInviteMatches,
        bool currentSurface
    ) =>
        nowUnixSeconds >= 0
        && expiresAtUnixSeconds > nowUnixSeconds
        && pendingInviteMatches
        && currentSurface;
}

// Closed Steam lobby metadata contract for launcher-to-launcher ENet signaling.
// Steam lobby metadata and the inviter relationship are untrusted input. Full
// validation and nonce consumption occur before a caller may show a prompt or
// route an endpoint into the existing LAN join path.
public static class SteamLobbyInviteMetadata
{
    public const string SchemaKey = "sts2mm_schema";
    public const string AppIdKey = "sts2mm_app_id";
    public const string TransportKey = "sts2mm_transport";
    public const string LauncherBuildKey = "sts2mm_launcher_build";
    public const string GameBuildKey = "sts2mm_game_build";
    public const string EndpointsKey = "sts2mm_endpoints";
    public const string ExpiresKey = "sts2mm_expires";
    public const string NonceKey = "sts2mm_nonce";

    public const string SchemaV1 = "sts2mm-direct-v1";
    public const string EnetDirectTransport = "enet-direct";
    public const string GameAppId = "2868840";
    public const int MaxBuildLength = 48;
    public const int NonceByteLength = 16;
    public const int NonceLength = 22;
    public const int MaxMetadataCharacters = 2048;
    public const long MaxFutureLifetimeSeconds = 600;

    private static readonly string[] RequiredKeys =
    {
        SchemaKey,
        AppIdKey,
        TransportKey,
        LauncherBuildKey,
        GameBuildKey,
        EndpointsKey,
        ExpiresKey,
        NonceKey,
    };

    private static readonly HashSet<string> AllowedKeys = new(RequiredKeys, StringComparer.Ordinal);

    public static string CreateNonce()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(NonceByteLength);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static IReadOnlyDictionary<string, string> Create(
        string launcherBuild,
        string gameBuild,
        IReadOnlyList<LanJoinEndpoint> endpoints,
        long nowUnixSeconds,
        long lifetimeSeconds = 300
    )
    {
        if (!IsBuildToken(launcherBuild) || !IsBuildToken(gameBuild))
            throw new ArgumentException("Invite build identity is invalid.");
        if (
            nowUnixSeconds < 0
            || lifetimeSeconds is < 1 or > MaxFutureLifetimeSeconds
            || nowUnixSeconds > long.MaxValue - lifetimeSeconds
        )
            throw new ArgumentOutOfRangeException(nameof(lifetimeSeconds));
        if (endpoints == null || endpoints.Count is < 1 or > LanInviteCode.MaxShareChoices)
            throw new ArgumentException(
                "Invite endpoints are missing or excessive.",
                nameof(endpoints)
            );

        var encoded = new List<string>(endpoints.Count);
        var unique = new HashSet<LanJoinEndpoint>();
        foreach (var endpoint in endpoints)
        {
            if (!unique.Add(endpoint))
                throw new ArgumentException(
                    "Invite endpoints contain a duplicate.",
                    nameof(endpoints)
                );
            encoded.Add(LanInviteCode.Format(endpoint));
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SchemaKey] = SchemaV1,
            [AppIdKey] = GameAppId,
            [TransportKey] = EnetDirectTransport,
            [LauncherBuildKey] = launcherBuild,
            [GameBuildKey] = gameBuild,
            [EndpointsKey] = string.Join(',', encoded),
            [ExpiresKey] = (nowUnixSeconds + lifetimeSeconds).ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            [NonceKey] = CreateNonce(),
        };
        return new ReadOnlyDictionary<string, string>(metadata);
    }

    public static bool TryValidateAndConsume(
        IReadOnlyDictionary<string, string> metadata,
        string expectedLauncherBuild,
        string expectedGameBuild,
        long nowUnixSeconds,
        SteamLobbyInviteSenderTrust senderTrust,
        SteamLobbyInviteReplayGuard replayGuard,
        out SteamLobbyDirectInvite invite,
        out SteamLobbyInviteMetadataError error
    )
    {
        invite = null;
        if (senderTrust != SteamLobbyInviteSenderTrust.Friend)
        {
            error = SteamLobbyInviteMetadataError.UntrustedSender;
            return false;
        }
        if (!IsBuildToken(expectedLauncherBuild) || !IsBuildToken(expectedGameBuild))
        {
            error = SteamLobbyInviteMetadataError.InvalidExpectedBuild;
            return false;
        }
        if (metadata == null)
        {
            error = SteamLobbyInviteMetadataError.MissingField;
            return false;
        }

        long totalCharacters = 0;
        foreach (var pair in metadata)
        {
            if (pair.Key == null || pair.Value == null)
            {
                error = SteamLobbyInviteMetadataError.MissingField;
                return false;
            }
            if (!AllowedKeys.Contains(pair.Key))
            {
                error = SteamLobbyInviteMetadataError.UnknownField;
                return false;
            }
            totalCharacters += pair.Key.Length + pair.Value.Length;
            if (totalCharacters > MaxMetadataCharacters)
            {
                error = SteamLobbyInviteMetadataError.TooLong;
                return false;
            }
        }
        if (metadata.Count != RequiredKeys.Length)
        {
            error = SteamLobbyInviteMetadataError.MissingField;
            return false;
        }
        foreach (var key in RequiredKeys)
        {
            if (!metadata.TryGetValue(key, out var value) || value == null)
            {
                error = SteamLobbyInviteMetadataError.MissingField;
                return false;
            }
        }

        if (metadata[SchemaKey] != SchemaV1)
        {
            error = SteamLobbyInviteMetadataError.UnsupportedSchema;
            return false;
        }
        if (metadata[AppIdKey] != GameAppId)
        {
            error = SteamLobbyInviteMetadataError.WrongAppId;
            return false;
        }
        if (metadata[TransportKey] != EnetDirectTransport)
        {
            error = SteamLobbyInviteMetadataError.UnknownTransport;
            return false;
        }

        string launcherBuild = metadata[LauncherBuildKey];
        string gameBuild = metadata[GameBuildKey];
        if (!IsBuildToken(launcherBuild) || !IsBuildToken(gameBuild))
        {
            error = SteamLobbyInviteMetadataError.InvalidBuild;
            return false;
        }
        if (launcherBuild != expectedLauncherBuild)
        {
            error = SteamLobbyInviteMetadataError.IncompatibleLauncherBuild;
            return false;
        }
        if (gameBuild != expectedGameBuild)
        {
            error = SteamLobbyInviteMetadataError.IncompatibleGameBuild;
            return false;
        }

        if (
            nowUnixSeconds < 0
            || !TryParsePositiveInt64(metadata[ExpiresKey], out long expiresAtUnixSeconds)
        )
        {
            error = SteamLobbyInviteMetadataError.InvalidExpiry;
            return false;
        }
        if (expiresAtUnixSeconds <= nowUnixSeconds)
        {
            error = SteamLobbyInviteMetadataError.Expired;
            return false;
        }
        if (expiresAtUnixSeconds - nowUnixSeconds > MaxFutureLifetimeSeconds)
        {
            error = SteamLobbyInviteMetadataError.ExpiryTooFarInFuture;
            return false;
        }

        string nonce = metadata[NonceKey];
        if (!IsNonce(nonce))
        {
            error = SteamLobbyInviteMetadataError.InvalidNonce;
            return false;
        }

        string[] encodedEndpoints = metadata[EndpointsKey].Split(',');
        if (encodedEndpoints.Length is < 1 or > LanInviteCode.MaxShareChoices)
        {
            error =
                encodedEndpoints.Length > LanInviteCode.MaxShareChoices
                    ? SteamLobbyInviteMetadataError.TooManyEndpoints
                    : SteamLobbyInviteMetadataError.InvalidEndpoint;
            return false;
        }
        var endpoints = new List<LanJoinEndpoint>(encodedEndpoints.Length);
        var uniqueEndpoints = new HashSet<LanJoinEndpoint>();
        foreach (string encodedEndpoint in encodedEndpoints)
        {
            if (
                !LanInviteCode.TryParseJoinInput(
                    encodedEndpoint,
                    out var endpoint,
                    out var parseError
                )
                || parseError != LanInviteParseError.None
                || encodedEndpoint != LanInviteCode.Format(endpoint)
            )
            {
                error = SteamLobbyInviteMetadataError.InvalidEndpoint;
                return false;
            }
            if (!uniqueEndpoints.Add(endpoint))
            {
                error = SteamLobbyInviteMetadataError.DuplicateEndpoint;
                return false;
            }
            endpoints.Add(endpoint);
        }

        if (replayGuard == null)
        {
            error = SteamLobbyInviteMetadataError.ReplayStateUnavailable;
            return false;
        }
        switch (replayGuard.TryConsume(nonce, expiresAtUnixSeconds, nowUnixSeconds))
        {
            case SteamLobbyInviteReplayResult.Replay:
                error = SteamLobbyInviteMetadataError.Replay;
                return false;
            case SteamLobbyInviteReplayResult.CapacityExceeded:
                error = SteamLobbyInviteMetadataError.ReplayCapacityExceeded;
                return false;
        }

        invite = new SteamLobbyDirectInvite(
            launcherBuild,
            gameBuild,
            expiresAtUnixSeconds,
            new ReadOnlyCollection<LanJoinEndpoint>(endpoints)
        );
        error = SteamLobbyInviteMetadataError.None;
        return true;
    }

    internal static bool IsBuildToken(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxBuildLength)
            return false;
        foreach (char c in value)
        {
            if (
                !(c is >= 'a' and <= 'z')
                && !(c is >= 'A' and <= 'Z')
                && !(c is >= '0' and <= '9')
                && c is not '.' and not '_' and not '-' and not '+'
            )
                return false;
        }
        return true;
    }

    private static bool IsNonce(string value)
    {
        if (value == null || value.Length != NonceLength)
            return false;
        foreach (char c in value)
        {
            if (
                !(c is >= 'a' and <= 'z')
                && !(c is >= 'A' and <= 'Z')
                && !(c is >= '0' and <= '9')
                && c is not '-' and not '_'
            )
                return false;
        }
        return true;
    }

    private static bool TryParsePositiveInt64(string value, out long parsed)
    {
        parsed = 0;
        if (string.IsNullOrEmpty(value) || value.Length > 19 || value[0] == '0')
            return false;
        foreach (char c in value)
        {
            if (c is < '0' or > '9')
                return false;
            int digit = c - '0';
            if (parsed > (long.MaxValue - digit) / 10)
                return false;
            parsed = parsed * 10 + digit;
        }
        return parsed > 0;
    }
}
