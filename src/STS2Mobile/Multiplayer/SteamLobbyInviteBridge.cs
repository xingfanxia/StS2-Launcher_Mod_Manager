using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using STS2Mobile.Steam;

namespace STS2Mobile.Multiplayer;

internal enum SteamInviteBridgeResult
{
    Success,
    Unavailable,
    ConnectionFailed,
    LobbyFailed,
    NotFriend,
    RateLimited,
    Stale,
    JoinFailed,
}

internal sealed class SteamHostInvitePreparation
{
    public SteamInviteBridgeResult Result { get; }
    public IReadOnlyList<SteamInviteFriend> Friends { get; }

    public SteamHostInvitePreparation(
        SteamInviteBridgeResult result,
        IReadOnlyList<SteamInviteFriend> friends = null
    )
    {
        Result = result;
        Friends = friends ?? Array.Empty<SteamInviteFriend>();
    }
}

internal sealed class SteamIncomingDirectInvite
{
    internal long BridgeGeneration { get; }
    internal ulong LobbySteamId { get; }
    internal ulong PatronSteamId { get; }
    internal SteamLobbyDirectInvite DirectInvite { get; }

    public string InviterDisplayName { get; }
    public IReadOnlyList<LanJoinEndpoint> EndpointCandidates => DirectInvite.EndpointCandidates;

    internal SteamIncomingDirectInvite(
        long bridgeGeneration,
        ulong lobbySteamId,
        ulong patronSteamId,
        string inviterDisplayName,
        SteamLobbyDirectInvite directInvite
    )
    {
        BridgeGeneration = bridgeGeneration;
        LobbySteamId = lobbySteamId;
        PatronSteamId = patronSteamId;
        InviterDisplayName = inviterDisplayName;
        DirectInvite = directInvite;
    }

    public override string ToString() => nameof(SteamIncomingDirectInvite);
}

// Owns one short-lived SteamKit session for a visible multiplayer host/join
// surface. It never persists friend IDs, lobby IDs, endpoints, or metadata and
// never logs them. All callback work is generation/account checked again after
// each await so teardown, logout, and account/process changes fail closed.
internal sealed class SteamLobbyInviteBridge : IDisposable
{
    private const uint AppId = 2868840;
    private const int MaxLobbyMembers = 4;
    private static long _nextGeneration;

    private readonly object _gate = new();
    private readonly string _launcherBuild;
    private readonly string _gameBuild;
    private readonly SteamConnection _connection;
    private readonly SteamLobbyInviteReplayGuard _replayGuard = new();
    private readonly SteamLobbyInviteSendRateLimiter _sendLimiter = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _incomingGate = new(1, 1);
    private readonly long _generation;

    private SteamMatchmaking _matchmaking;
    private SteamID _hostLobby;
    private SteamID _joinedLobby;
    private SteamIncomingDirectInvite _pendingInvite;
    private bool _surfaceActive;
    private bool _idleLeaseHeld;
    private int _disposed;

    public event Action<SteamIncomingDirectInvite> IncomingInvite;

    public SteamLobbyInviteBridge(
        string accountName,
        string refreshToken,
        string launcherBuild,
        string gameBuild
    )
    {
        if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Steam credentials are unavailable.");
        _launcherBuild = launcherBuild;
        _gameBuild = gameBuild;
        _generation = Interlocked.Increment(ref _nextGeneration);
        _connection = new SteamConnection(accountName, refreshToken);
        _connection.ChatLobbyInviteReceived += OnChatLobbyInviteReceived;
    }

    public async Task<SteamInviteBridgeResult> StartListeningAsync()
    {
        try
        {
            await EnsureReadyAsync().ConfigureAwait(false);
            lock (_gate)
                _surfaceActive = true;
            return SteamInviteBridgeResult.Success;
        }
        catch
        {
            return SteamInviteBridgeResult.ConnectionFailed;
        }
    }

    public async Task<string> GetAuthenticatedPersonaNameAsync()
    {
        try
        {
            await EnsureReadyAsync().ConfigureAwait(false);
            ThrowIfInactive();
            return await Task.Run(
                    () => _connection.GetAuthenticatedPersonaName(_lifetime.Token),
                    _lifetime.Token
                )
                .ConfigureAwait(false);
        }
        catch
        {
            // Identity text is optional UI enrichment. A blank value is safer
            // than exposing a login name, numeric ID, or stale account persona.
            return string.Empty;
        }
    }

    public async Task<SteamHostInvitePreparation> PrepareHostInviteAsync(
        IReadOnlyList<LanJoinEndpoint> endpoints
    )
    {
        try
        {
            await EnsureReadyAsync().ConfigureAwait(false);
            lock (_gate)
                _surfaceActive = true;

            await LeaveHostLobbyAsync().ConfigureAwait(false);
            var metadata = SteamLobbyInviteMetadata.Create(
                _launcherBuild,
                _gameBuild,
                endpoints,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            );

            ThrowIfInactive();
            var createJob = _matchmaking.CreateLobby(
                AppId,
                ELobbyType.FriendsOnly,
                MaxLobbyMembers,
                lobbyFlags: 0,
                metadata
            );
            if (createJob == null)
                return new SteamHostInvitePreparation(SteamInviteBridgeResult.LobbyFailed);
            var callback = await createJob
                .ToTask()
                .WaitAsync(TimeSpan.FromSeconds(15), _lifetime.Token)
                .ConfigureAwait(false);

            if (
                callback == null
                || callback.Result != EResult.OK
                || callback.AppID != AppId
                || callback.LobbySteamID == null
                || callback.LobbySteamID.ConvertToUInt64() == 0
            )
                return new SteamHostInvitePreparation(SteamInviteBridgeResult.LobbyFailed);

            bool current;
            lock (_gate)
            {
                current = IsCurrentLocked();
                if (current)
                    _hostLobby = callback.LobbySteamID;
            }
            if (!current)
            {
                TryLeaveLobby(callback.LobbySteamID);
                return new SteamHostInvitePreparation(SteamInviteBridgeResult.Stale);
            }

            ThrowIfInactive();
            IReadOnlyList<SteamInviteFriend> friends = await _connection
                .GetInviteFriendsAsync(_lifetime.Token)
                .ConfigureAwait(false);
            return new SteamHostInvitePreparation(SteamInviteBridgeResult.Success, friends);
        }
        catch (OperationCanceledException)
        {
            return new SteamHostInvitePreparation(SteamInviteBridgeResult.Stale);
        }
        catch
        {
            return new SteamHostInvitePreparation(SteamInviteBridgeResult.ConnectionFailed);
        }
    }

    public async Task<SteamInviteBridgeResult> SendInviteAsync(ulong friendSteamId)
    {
        try
        {
            await EnsureReadyAsync().ConfigureAwait(false);
            SteamID lobby;
            lock (_gate)
            {
                if (!IsCurrentLocked() || _hostLobby == null)
                    return SteamInviteBridgeResult.Stale;
                lobby = _hostLobby;
            }

            if (
                _connection.ClassifyInviteSender(friendSteamId)
                != SteamLobbyInviteSenderTrust.Friend
            )
                return SteamInviteBridgeResult.NotFriend;
            if (
                _sendLimiter.TryAcquire(friendSteamId, DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                != SteamLobbyInviteSendRateResult.Accepted
            )
                return SteamInviteBridgeResult.RateLimited;

            _matchmaking.InviteToLobby(AppId, lobby, new SteamID(friendSteamId));
            return SteamInviteBridgeResult.Success;
        }
        catch (OperationCanceledException)
        {
            return SteamInviteBridgeResult.Stale;
        }
        catch
        {
            return SteamInviteBridgeResult.ConnectionFailed;
        }
    }

    public void Decline(SteamIncomingDirectInvite invite)
    {
        if (invite == null)
            return;
        lock (_gate)
        {
            if (ReferenceEquals(_pendingInvite, invite))
                _pendingInvite = null;
        }
    }

    public async Task<SteamInviteBridgeResult> AcceptAsync(SteamIncomingDirectInvite invite)
    {
        if (invite == null)
            return SteamInviteBridgeResult.Stale;
        SteamID attemptedLobby = null;
        try
        {
            await EnsureReadyAsync().ConfigureAwait(false);
            lock (_gate)
            {
                if (
                    !SteamLobbyInviteSessionGuard.CanAcceptInvite(
                        invite.DirectInvite.ExpiresAtUnixSeconds,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        ReferenceEquals(_pendingInvite, invite),
                        IsCurrentLocked() && invite.BridgeGeneration == _generation
                    )
                )
                    return SteamInviteBridgeResult.Stale;
            }

            attemptedLobby = new SteamID(invite.LobbySteamId);
            var job = _matchmaking.JoinLobby(AppId, attemptedLobby);
            if (job == null)
                return SteamInviteBridgeResult.JoinFailed;
            var callback = await job.ToTask()
                .WaitAsync(TimeSpan.FromSeconds(15), _lifetime.Token)
                .ConfigureAwait(false);

            ulong owner = callback.Lobby?.OwnerSteamID?.ConvertToUInt64() ?? 0;
            if (
                callback.AppID != AppId
                || callback.ChatRoomEnterResponse != EChatRoomEnterResponse.Success
                || callback.Lobby == null
                || owner != invite.PatronSteamId
            )
            {
                TryLeaveLobby(callback.Lobby?.SteamID ?? attemptedLobby);
                return SteamInviteBridgeResult.JoinFailed;
            }

            bool stale;
            lock (_gate)
            {
                stale = !SteamLobbyInviteSessionGuard.CanAcceptInvite(
                    invite.DirectInvite.ExpiresAtUnixSeconds,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ReferenceEquals(_pendingInvite, invite),
                    IsCurrentLocked() && invite.BridgeGeneration == _generation
                );
                if (!stale)
                {
                    _pendingInvite = null;
                    _joinedLobby = callback.Lobby.SteamID;
                }
            }
            if (stale)
            {
                TryLeaveLobby(callback.Lobby.SteamID);
                return SteamInviteBridgeResult.Stale;
            }
            return SteamInviteBridgeResult.Success;
        }
        catch (OperationCanceledException)
        {
            TryLeaveLobby(attemptedLobby);
            return SteamInviteBridgeResult.Stale;
        }
        catch
        {
            TryLeaveLobby(attemptedLobby);
            return SteamInviteBridgeResult.JoinFailed;
        }
    }

    public async Task CancelHostPreparationAsync()
    {
        try
        {
            await LeaveHostLobbyAsync().ConfigureAwait(false);
        }
        catch { }
    }

    private async Task EnsureReadyAsync()
    {
        ThrowIfDisposed();
        await _connectGate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        try
        {
            if (_matchmaking != null)
                return;
            _matchmaking = await Task.Run(() => _connection.Matchmaking, _lifetime.Token)
                .ConfigureAwait(false);
            _connection.SetInvitePresenceOnline();
            _connection.SuspendIdleTimeout();
            _idleLeaseHeld = true;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private void OnChatLobbyInviteReceived(SteamChatLobbyInviteSignal signal)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        _ = ProcessIncomingInviteAsync(signal, _generation);
    }

    private async Task ProcessIncomingInviteAsync(
        SteamChatLobbyInviteSignal signal,
        long callbackGeneration
    )
    {
        try
        {
            await _incomingGate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            try
            {
                await EnsureReadyAsync().ConfigureAwait(false);
                bool active;
                lock (_gate)
                    active = IsCurrentLocked() && _pendingInvite == null;
                if (
                    signal.AppId != AppId
                    || !signal.IsLobby
                    || signal.LobbySteamId == 0
                    || signal.PatronSteamId == 0
                    || !SteamLobbyInviteSessionGuard.CanHandleCallback(
                        callbackGeneration,
                        _generation,
                        signal.InvitedSteamId,
                        _connection.AuthenticatedSteamId,
                        active,
                        _connection.State == ConnectionState.Connected
                    )
                )
                    return;

                var trust = _connection.ClassifyInviteSender(signal.PatronSteamId);
                if (trust != SteamLobbyInviteSenderTrust.Friend)
                    return;

                var job = _matchmaking.GetLobbyData(AppId, new SteamID(signal.LobbySteamId));
                var callback = await job.ToTask()
                    .WaitAsync(TimeSpan.FromSeconds(15), _lifetime.Token)
                    .ConfigureAwait(false);
                ulong owner = callback.Lobby?.OwnerSteamID?.ConvertToUInt64() ?? 0;
                if (
                    callback.AppID != AppId
                    || callback.Lobby == null
                    || callback.Lobby.SteamID?.ConvertToUInt64() != signal.LobbySteamId
                    || owner != signal.PatronSteamId
                )
                    return;

                if (
                    !SteamLobbyInviteMetadata.TryValidateAndConsume(
                        callback.Lobby.Metadata,
                        _launcherBuild,
                        _gameBuild,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        trust,
                        _replayGuard,
                        out var directInvite,
                        out _
                    )
                )
                    return;

                var pending = new SteamIncomingDirectInvite(
                    _generation,
                    signal.LobbySteamId,
                    signal.PatronSteamId,
                    _connection.GetInviteFriendDisplayName(signal.PatronSteamId),
                    directInvite
                );
                lock (_gate)
                {
                    if (!IsCurrentLocked() || _pendingInvite != null)
                        return;
                    _pendingInvite = pending;
                }
                var handler = IncomingInvite;
                if (handler == null)
                {
                    Decline(pending);
                    return;
                }
                try
                {
                    handler(pending);
                }
                catch
                {
                    Decline(pending);
                }
            }
            finally
            {
                _incomingGate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task LeaveHostLobbyAsync()
    {
        SteamID lobby;
        lock (_gate)
        {
            lobby = _hostLobby;
            _hostLobby = null;
        }
        if (lobby == null || _matchmaking == null)
            return;
        var job = _matchmaking.LeaveLobby(AppId, lobby);
        await job.ToTask()
            .WaitAsync(TimeSpan.FromSeconds(10), _lifetime.Token)
            .ConfigureAwait(false);
    }

    private void TryLeaveLobby(SteamID lobby)
    {
        if (lobby == null || _matchmaking == null)
            return;
        try
        {
            _matchmaking.LeaveLobby(AppId, lobby);
        }
        catch { }
    }

    private bool IsCurrentLocked() =>
        _surfaceActive && Volatile.Read(ref _disposed) == 0 && !_lifetime.IsCancellationRequested;

    private void ThrowIfInactive()
    {
        lock (_gate)
        {
            if (!IsCurrentLocked())
                throw new OperationCanceledException();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(SteamLobbyInviteBridge));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _connection.ChatLobbyInviteReceived -= OnChatLobbyInviteReceived;
        _lifetime.Cancel();
        SteamID host;
        SteamID joined;
        lock (_gate)
        {
            _surfaceActive = false;
            _pendingInvite = null;
            host = _hostLobby;
            joined = _joinedLobby;
            _hostLobby = null;
            _joinedLobby = null;
        }
        TryLeaveLobby(host);
        if (joined?.ConvertToUInt64() != host?.ConvertToUInt64())
            TryLeaveLobby(joined);
        if (_idleLeaseHeld)
        {
            _idleLeaseHeld = false;
            _connection.ResumeIdleTimeout();
        }
        try
        {
            _connection.Dispose();
        }
        catch { }
        _lifetime.Dispose();
    }
}
