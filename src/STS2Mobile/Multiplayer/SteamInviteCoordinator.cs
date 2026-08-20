using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using STS2Mobile.Launcher;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Patches;
using STS2Mobile.Steam;

namespace STS2Mobile.Multiplayer;

// Owns the narrow boundary between the game's existing ENet screens and the
// SteamKit signaling bridge. The Harmony patch only reports surface lifecycle
// and supplies the existing JoinViaIp delegate; credentials, lobby state,
// callbacks, UI confirmation, and teardown stay out of the upstream-sensitive
// patcher.
internal static class SteamInviteCoordinator
{
    private enum SurfaceMode
    {
        None,
        Join,
        Host,
    }

    // HOME shorter than this keeps the invitation session warm. Longer
    // backgrounding invalidates every callback and connection; this is a real
    // lifecycle policy timeout, not a test synchronization delay.
    internal const int BackgroundGraceSeconds = 30;

    private static readonly object Gate = new();

    private static SurfaceMode _mode;
    private static Node _surfaceOwner;
    private static object _joinScreen;
    private static Action<object, string, int> _joinViaIp;
    private static IReadOnlyList<LanJoinEndpoint> _hostEndpoints = Array.Empty<LanJoinEndpoint>();
    private static SteamLobbyInviteBridge _bridge;
    private static SteamInviteUiPump _uiPump;
    private static Control _activeModal;
    private static SteamInviteListenerStatus _listenerStatus;
    private static BusyOverlay _busyOverlay;
    private static CancellationTokenSource _backgroundGrace;
    private static bool _backgrounded;
    private static long _generation;

    internal static void OnJoinScreenOpened(Node screen, Action<object, string, int> joinViaIp)
    {
        if (screen == null || joinViaIp == null || !GodotObject.IsInstanceValid(screen))
            return;

        ResetSessionOnMain();
        EnsureUiPump(screen);
        long generation;
        lock (Gate)
        {
            _mode = SurfaceMode.Join;
            _surfaceOwner = screen;
            _joinScreen = screen;
            _joinViaIp = joinViaIp;
            generation = ++_generation;
        }
        StartJoinListener(generation);
    }

    internal static void OnJoinScreenClosed()
    {
        lock (Gate)
        {
            if (_mode != SurfaceMode.Join)
                return;
        }
        ResetSessionOnMain();
    }

    internal static void OnHostStarted() => ResetSessionOnMain();

    internal static void ShowInviteMethod(
        Node owner,
        IReadOnlyList<LanJoinEndpoint> endpoints,
        Action showLanShare
    )
    {
        if (
            owner == null
            || !GodotObject.IsInstanceValid(owner)
            || endpoints == null
            || endpoints.Count == 0
        )
            return;

        lock (Gate)
        {
            if (_activeModal != null && GodotObject.IsInstanceValid(_activeModal))
                return;
        }

        bool replace;
        lock (Gate)
            replace = _mode != SurfaceMode.Host;
        if (replace)
            ResetSessionOnMain();

        EnsureUiPump(owner);
        long generation;
        lock (Gate)
        {
            if (_mode != SurfaceMode.Host)
            {
                _mode = SurfaceMode.Host;
                ++_generation;
            }
            _surfaceOwner = owner;
            _hostEndpoints = endpoints.ToList();
            generation = _generation;
        }

        var dialog = new SteamInviteMethodDialog(LauncherUI.ResolveScale(owner));
        dialog.SteamFriendsSelected += () =>
        {
            if (IsCurrent(generation, SurfaceMode.Host))
                BeginHostPreparation(generation);
        };
        dialog.LanShareSelected += () =>
        {
            if (IsCurrent(generation, SurfaceMode.Host))
                showLanShare?.Invoke();
        };
        SetActiveModal(owner, dialog);
    }

    internal static void OnHostDisconnected()
    {
        // NetHostGameService.Disconnect is shared by hosts and clients. A client
        // can return to Join (which starts a fresh listener) before this postfix
        // runs; an unconditional reset would then tear down that new listener.
        // Join-screen lifecycle owns client cleanup, so this callback may reset
        // only an invitation session that is actually in host mode.
        lock (Gate)
        {
            if (_mode != SurfaceMode.Host)
                return;
        }
        ResetSessionOnMain();
    }

    internal static void OnAppBackgrounded()
    {
        CancellationTokenSource grace;
        lock (Gate)
        {
            if (_mode == SurfaceMode.None)
                return;
            _backgrounded = true;
            CancelBackgroundGraceLocked();
            grace = new CancellationTokenSource();
            _backgroundGrace = grace;
        }
        _ = ExpireBackgroundSessionAsync(grace);
    }

    internal static void OnAppForegrounded()
    {
        Node owner;
        bool restartJoin;
        long generation = 0;
        lock (Gate)
        {
            _backgrounded = false;
            CancelBackgroundGraceLocked();
            owner = _surfaceOwner;
            restartJoin = _mode == SurfaceMode.Join && _bridge == null;
            if (restartJoin)
                generation = ++_generation;
        }

        if (!restartJoin)
            return;
        if (owner == null || !GodotObject.IsInstanceValid(owner))
        {
            ResetSessionOnMain();
            return;
        }
        EnsureUiPump(owner);
        StartJoinListener(generation);
    }

    private static void StartJoinListener(long generation)
    {
        SteamLobbyInviteBridge bridge;
        string error = null;
        lock (Gate)
        {
            if (!IsCurrentLocked(generation, SurfaceMode.Join, null))
                return;
            if (!TryCreateBridge(out bridge, out error))
            {
                bridge = null;
            }
            else
            {
                _bridge = bridge;
            }
        }

        if (bridge == null)
        {
            ShowMessage(error);
            return;
        }

        bridge.IncomingInvite += invite => HandleIncomingInvite(generation, bridge, invite);
        _ = StartListeningAsync(generation, bridge);
    }

    private static async Task StartListeningAsync(long generation, SteamLobbyInviteBridge bridge)
    {
        var result = await bridge.StartListeningAsync().ConfigureAwait(false);
        if (result == SteamInviteBridgeResult.Success)
        {
            var personaName = await bridge.GetAuthenticatedPersonaNameAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(personaName))
            {
                PostCurrent(
                    generation,
                    bridge,
                    () => ShowListenerStatus(generation, bridge, personaName)
                );
            }
            return;
        }

        PostCurrent(
            generation,
            bridge,
            () =>
            {
                DetachAndDisposeBridgeOnMain(generation, bridge);
                ShowMessage(
                    Loc.Select(
                        "Steam 친구 초대를 받을 수 없습니다. LAN 직접 연결은 계속 사용할 수 있습니다.",
                        "Steam friend invites are unavailable. LAN direct connect remains available.",
                        "Steam 好友邀请当前不可用，仍可使用 LAN 直连。"
                    )
                );
            }
        );
    }

    private static void BeginHostPreparation(long generation)
    {
        SteamLobbyInviteBridge bridge;
        IReadOnlyList<LanJoinEndpoint> endpoints;
        Node owner;
        string error = null;
        lock (Gate)
        {
            if (!IsCurrentLocked(generation, SurfaceMode.Host, null))
                return;
            owner = _surfaceOwner;
            endpoints = _hostEndpoints;
            bridge = _bridge;
            if (bridge == null && TryCreateBridge(out bridge, out error))
                _bridge = bridge;
            else if (bridge != null)
                error = null;
        }

        if (bridge == null)
        {
            ShowMessage(error);
            return;
        }

        ShowBusy(
            owner,
            Loc.Select(
                "Steam 친구 목록 준비 중…",
                "Preparing Steam friends…",
                "正在准备 Steam 好友列表…"
            )
        );
        _ = PrepareHostInviteAsync(generation, bridge, endpoints);
    }

    private static async Task PrepareHostInviteAsync(
        long generation,
        SteamLobbyInviteBridge bridge,
        IReadOnlyList<LanJoinEndpoint> endpoints
    )
    {
        var preparation = await bridge.PrepareHostInviteAsync(endpoints).ConfigureAwait(false);
        PostCurrent(
            generation,
            bridge,
            () =>
            {
                DismissBusy();
                if (preparation.Result == SteamInviteBridgeResult.Success)
                {
                    ShowFriendPicker(generation, bridge, preparation.Friends);
                    return;
                }
                ShowMessage(DescribePreparationFailure(preparation.Result));
                DetachAndDisposeBridgeOnMain(generation, bridge);
            }
        );
    }

    private static void ShowFriendPicker(
        long generation,
        SteamLobbyInviteBridge bridge,
        IReadOnlyList<SteamInviteFriend> friends
    )
    {
        Node owner;
        lock (Gate)
        {
            if (!IsCurrentLocked(generation, SurfaceMode.Host, bridge))
                return;
            owner = _surfaceOwner;
        }
        if (owner == null || !GodotObject.IsInstanceValid(owner))
            return;

        var dialog = new SteamInviteFriendPickerDialog(
            friends ?? Array.Empty<SteamInviteFriend>(),
            LauncherUI.ResolveScale(owner)
        );
        dialog.FriendSelected += friend =>
        {
            if (IsCurrent(generation, SurfaceMode.Host, bridge))
                ShowSendConfirmation(generation, bridge, friend);
        };
        dialog.Cancelled += () =>
        {
            if (IsCurrent(generation, SurfaceMode.Host, bridge))
                _ = bridge.CancelHostPreparationAsync();
        };
        SetActiveModal(owner, dialog);
    }

    private static void ShowSendConfirmation(
        long generation,
        SteamLobbyInviteBridge bridge,
        SteamInviteFriend friend
    )
    {
        if (friend == null)
            return;
        Node owner;
        lock (Gate)
        {
            if (!IsCurrentLocked(generation, SurfaceMode.Host, bridge))
                return;
            owner = _surfaceOwner;
        }

        string displayName = string.IsNullOrWhiteSpace(friend.DisplayName)
            ? Loc.Select("Steam 친구", "Steam friend", "Steam 好友")
            : friend.DisplayName;
        var dialog = new StyledDialog(
            Loc.Select(
                $"{displayName} 님에게 런처 직접 초대를 보내시겠습니까? Steam Relay가 아니며 LAN/VPN ENet 주소가 전달됩니다.",
                $"Send a launcher direct invite to {displayName}? This is not Steam Relay; it shares the LAN/VPN ENet endpoint.",
                $"向 {displayName} 发送 launcher 直连邀请吗？这不是 Steam Relay，会分享 LAN/VPN ENet 地址。"
            ),
            LauncherUI.ResolveScale(owner),
            Loc.Select("보내기", "SEND", "发送"),
            Loc.Select("취소", "CANCEL", "取消")
        );
        dialog.Confirmed += () =>
        {
            if (IsCurrent(generation, SurfaceMode.Host, bridge))
                BeginSendInvite(generation, bridge, friend.SteamId);
        };
        dialog.Cancelled += () =>
        {
            if (IsCurrent(generation, SurfaceMode.Host, bridge))
                _ = bridge.CancelHostPreparationAsync();
        };
        SetActiveModal(owner, dialog);
    }

    private static void BeginSendInvite(
        long generation,
        SteamLobbyInviteBridge bridge,
        ulong friendSteamId
    )
    {
        Node owner;
        lock (Gate)
        {
            if (!IsCurrentLocked(generation, SurfaceMode.Host, bridge))
                return;
            owner = _surfaceOwner;
        }
        ShowBusy(
            owner,
            Loc.Select("초대 요청 전송 중…", "Submitting invite request…", "正在提交邀请请求…")
        );
        _ = SendInviteAsync(generation, bridge, friendSteamId);
    }

    private static async Task SendInviteAsync(
        long generation,
        SteamLobbyInviteBridge bridge,
        ulong friendSteamId
    )
    {
        var result = await bridge.SendInviteAsync(friendSteamId).ConfigureAwait(false);
        if (result != SteamInviteBridgeResult.Success)
            await bridge.CancelHostPreparationAsync().ConfigureAwait(false);
        PostCurrent(
            generation,
            bridge,
            () =>
            {
                DismissBusy();
                ShowMessage(DescribeSendResult(result));
            }
        );
    }

    private static void HandleIncomingInvite(
        long generation,
        SteamLobbyInviteBridge bridge,
        SteamIncomingDirectInvite invite
    )
    {
        bool posted = PostCurrent(
            generation,
            bridge,
            () => ShowIncomingInvite(generation, bridge, invite),
            () => bridge.Decline(invite)
        );
        if (!posted)
            bridge.Decline(invite);
    }

    private static void ShowIncomingInvite(
        long generation,
        SteamLobbyInviteBridge bridge,
        SteamIncomingDirectInvite invite
    )
    {
        Node owner;
        lock (Gate)
        {
            if (!IsCurrentLocked(generation, SurfaceMode.Join, bridge))
            {
                bridge.Decline(invite);
                return;
            }
            owner = _surfaceOwner;
        }
        if (owner == null || !GodotObject.IsInstanceValid(owner))
        {
            bridge.Decline(invite);
            return;
        }

        var dialog = new SteamIncomingInviteDialog(invite, LauncherUI.ResolveScale(owner));
        dialog.Declined += () => bridge.Decline(invite);
        dialog.Accepted += endpoint =>
        {
            if (IsCurrent(generation, SurfaceMode.Join, bridge))
                BeginAcceptInvite(generation, bridge, invite, endpoint);
            else
                bridge.Decline(invite);
        };
        SetActiveModal(owner, dialog);
    }

    private static void BeginAcceptInvite(
        long generation,
        SteamLobbyInviteBridge bridge,
        SteamIncomingDirectInvite invite,
        LanJoinEndpoint endpoint
    )
    {
        Node owner;
        lock (Gate)
            owner = _surfaceOwner;
        ShowBusy(owner, Loc.Select("초대 확인 중…", "Verifying invite…", "正在验证邀请…"));
        _ = AcceptInviteAsync(generation, bridge, invite, endpoint);
    }

    private static async Task AcceptInviteAsync(
        long generation,
        SteamLobbyInviteBridge bridge,
        SteamIncomingDirectInvite invite,
        LanJoinEndpoint endpoint
    )
    {
        var result = await bridge.AcceptAsync(invite).ConfigureAwait(false);
        PostCurrent(
            generation,
            bridge,
            () =>
            {
                DismissBusy();
                if (result != SteamInviteBridgeResult.Success)
                {
                    ShowMessage(DescribeAcceptFailure(result));
                    return;
                }

                object screen;
                Action<object, string, int> join;
                lock (Gate)
                {
                    if (!IsCurrentLocked(generation, SurfaceMode.Join, bridge))
                        return;
                    screen = _joinScreen;
                    join = _joinViaIp;
                }
                if (screen is not Node node || !GodotObject.IsInstanceValid(node) || join == null)
                    return;
                join(screen, endpoint.Address.ToString(), endpoint.Port);
            }
        );
    }

    private static bool TryCreateBridge(out SteamLobbyInviteBridge bridge, out string error)
    {
        bridge = null;
        error = null;
        if (
            string.IsNullOrWhiteSpace(LauncherPatches.SavedAccountName)
            || string.IsNullOrWhiteSpace(LauncherPatches.SavedRefreshToken)
        )
        {
            error = Loc.Select(
                "Steam 친구 초대를 사용하려면 런처에서 Steam에 로그인하세요.",
                "Sign in to Steam in the launcher to use Steam friend invites.",
                "请先在 launcher 中登录 Steam，再使用 Steam 好友邀请。"
            );
            return false;
        }
        if (!TryReadBuildIdentity(out string launcherBuild, out string gameBuild))
        {
            error = Loc.Select(
                "현재 런처/게임 빌드를 안전하게 확인할 수 없어 Steam 초대를 시작하지 않았습니다.",
                "The current launcher/game build could not be verified, so no Steam invite session was started.",
                "无法安全验证当前 launcher/游戏版本，因此未启动 Steam 邀请会话。"
            );
            return false;
        }

        try
        {
            bridge = new SteamLobbyInviteBridge(
                LauncherPatches.SavedAccountName,
                LauncherPatches.SavedRefreshToken,
                launcherBuild,
                gameBuild
            );
            return true;
        }
        catch
        {
            error = Loc.Select(
                "Steam 초대 세션을 만들 수 없습니다.",
                "The Steam invite session could not be created.",
                "无法创建 Steam 邀请会话。"
            );
            return false;
        }
    }

    private static bool TryReadBuildIdentity(out string launcherBuild, out string gameBuild)
    {
        launcherBuild = null;
        gameBuild = null;
        try
        {
            var app = LauncherModel.GetGodotApp();
            launcherBuild = app == null ? null : (string)app.Call("getVersionName");
            var game = GameInstallTransaction.ReadActiveTuple(OS.GetDataDir());
            if (
                game == null
                || !GameInstallTransaction.ActiveTupleMatchesFiles(OS.GetDataDir())
                || string.IsNullOrWhiteSpace(game.Branch)
                || string.IsNullOrWhiteSpace(game.BuildId)
            )
                return false;
            gameBuild = $"{game.Branch}-{game.BuildId}";
            return SteamLobbyInviteMetadata.IsBuildToken(launcherBuild)
                && SteamLobbyInviteMetadata.IsBuildToken(gameBuild);
        }
        catch
        {
            launcherBuild = null;
            gameBuild = null;
            return false;
        }
    }

    private static async Task ExpireBackgroundSessionAsync(CancellationTokenSource grace)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(BackgroundGraceSeconds), grace.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        SteamLobbyInviteBridge bridge;
        SteamInviteUiPump pump;
        lock (Gate)
        {
            if (!ReferenceEquals(_backgroundGrace, grace) || !_backgrounded)
                return;
            _backgroundGrace = null;
            bridge = _bridge;
            _bridge = null;
            ++_generation;
            pump = _uiPump;
        }
        pump?.Post(DismissUiOnMain);
        DisposeBridgeOffMain(bridge);
        grace.Dispose();
    }

    private static void DetachAndDisposeBridgeOnMain(
        long generation,
        SteamLobbyInviteBridge expected
    )
    {
        SteamLobbyInviteBridge bridge = null;
        lock (Gate)
        {
            if (_generation == generation && ReferenceEquals(_bridge, expected))
            {
                bridge = _bridge;
                _bridge = null;
            }
        }
        DisposeBridgeOffMain(bridge);
    }

    private static void ResetSessionOnMain()
    {
        SteamLobbyInviteBridge bridge;
        CancellationTokenSource grace;
        lock (Gate)
        {
            ++_generation;
            _mode = SurfaceMode.None;
            _surfaceOwner = null;
            _joinScreen = null;
            _joinViaIp = null;
            _hostEndpoints = Array.Empty<LanJoinEndpoint>();
            bridge = _bridge;
            _bridge = null;
            _backgrounded = false;
            grace = _backgroundGrace;
            _backgroundGrace = null;
        }
        if (grace != null)
        {
            grace.Cancel();
            grace.Dispose();
        }
        DismissUiOnMain();
        DisposeBridgeOffMain(bridge);
    }

    private static void DisposeBridgeOffMain(SteamLobbyInviteBridge bridge)
    {
        if (bridge != null)
            _ = Task.Run(bridge.Dispose);
    }

    private static void EnsureUiPump(Node owner)
    {
        lock (Gate)
        {
            if (_uiPump != null && GodotObject.IsInstanceValid(_uiPump))
                return;
        }
        var pump = new SteamInviteUiPump();
        var root = owner.GetTree()?.Root;
        if (root != null)
            root.AddChild(pump);
        else
            owner.AddChild(pump);
        lock (Gate)
            _uiPump = pump;
    }

    private static void SetActiveModal(Node owner, Control modal)
    {
        if (owner == null || modal == null || !GodotObject.IsInstanceValid(owner))
            return;
        lock (Gate)
            _activeModal = modal;
        modal.TreeExiting += () =>
        {
            lock (Gate)
            {
                if (ReferenceEquals(_activeModal, modal))
                    _activeModal = null;
            }
        };
        // Host actions originate from NInvitePlayersButton. Attaching a
        // full-screen overlay to that Control clips it to the button rectangle;
        // the SceneTree root gives both host and join surfaces the same viewport
        // coordinate space across rotation.
        LauncherOverlay.Show(GetOverlayContext(owner), modal);
    }

    private static void ShowListenerStatus(
        long generation,
        SteamLobbyInviteBridge bridge,
        string personaName
    )
    {
        Node owner;
        SteamInviteListenerStatus previous;
        lock (Gate)
        {
            if (!IsCurrentLocked(generation, SurfaceMode.Join, bridge))
                return;
            owner = _surfaceOwner;
            previous = _listenerStatus;
            _listenerStatus = null;
        }
        if (previous != null && GodotObject.IsInstanceValid(previous))
            previous.QueueFree();
        if (owner == null || !GodotObject.IsInstanceValid(owner))
            return;

        var status = new SteamInviteListenerStatus(personaName, LauncherUI.ResolveScale(owner));
        lock (Gate)
        {
            if (!IsCurrentLocked(generation, SurfaceMode.Join, bridge))
            {
                status.QueueFree();
                return;
            }
            _listenerStatus = status;
        }
        status.TreeExiting += () =>
        {
            lock (Gate)
            {
                if (ReferenceEquals(_listenerStatus, status))
                    _listenerStatus = null;
            }
        };
        LauncherOverlay.Show(GetOverlayContext(owner), status);
    }

    private static void ShowBusy(Node owner, string message)
    {
        DismissBusy();
        if (owner == null || !GodotObject.IsInstanceValid(owner))
            return;
        var busy = BusyOverlay.Show(
            GetOverlayContext(owner),
            message,
            LauncherUI.ResolveScale(owner)
        );
        lock (Gate)
            _busyOverlay = busy;
        busy.TreeExiting += () =>
        {
            lock (Gate)
            {
                if (ReferenceEquals(_busyOverlay, busy))
                    _busyOverlay = null;
            }
        };
    }

    private static void DismissBusy()
    {
        BusyOverlay busy;
        lock (Gate)
        {
            busy = _busyOverlay;
            _busyOverlay = null;
        }
        if (busy != null && GodotObject.IsInstanceValid(busy))
            busy.Dismiss();
    }

    private static void DismissUiOnMain()
    {
        Control modal;
        SteamInviteListenerStatus listenerStatus;
        lock (Gate)
        {
            modal = _activeModal;
            _activeModal = null;
            listenerStatus = _listenerStatus;
            _listenerStatus = null;
        }
        if (modal != null && GodotObject.IsInstanceValid(modal))
            modal.QueueFree();
        if (listenerStatus != null && GodotObject.IsInstanceValid(listenerStatus))
            listenerStatus.QueueFree();
        DismissBusy();
    }

    private static Node GetOverlayContext(Node owner) => owner?.GetTree()?.Root ?? owner;

    private static bool PostCurrent(
        long generation,
        SteamLobbyInviteBridge bridge,
        Action action,
        Action dropped = null
    )
    {
        SteamInviteUiPump pump;
        lock (Gate)
        {
            if (_generation != generation || !ReferenceEquals(_bridge, bridge))
                return false;
            pump = _uiPump;
        }
        if (pump == null)
            return false;
        return pump.Post(() =>
        {
            bool current;
            lock (Gate)
                current = _generation == generation && ReferenceEquals(_bridge, bridge);
            if (current)
                action?.Invoke();
            else
                dropped?.Invoke();
        });
    }

    private static bool IsCurrent(
        long generation,
        SurfaceMode mode,
        SteamLobbyInviteBridge bridge = null
    )
    {
        lock (Gate)
            return IsCurrentLocked(generation, mode, bridge);
    }

    private static bool IsCurrentLocked(
        long generation,
        SurfaceMode mode,
        SteamLobbyInviteBridge bridge
    ) =>
        _generation == generation
        && _mode == mode
        && !_backgrounded
        && (bridge == null || ReferenceEquals(_bridge, bridge));

    private static void CancelBackgroundGraceLocked()
    {
        var grace = _backgroundGrace;
        _backgroundGrace = null;
        if (grace == null)
            return;
        grace.Cancel();
        grace.Dispose();
    }

    private static void ShowMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        try
        {
            LauncherModel.GetGodotApp()?.Call("showLanInviteMessage", message);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[SteamInvite] Status UI degraded: {ex.GetType().Name}");
        }
    }

    private static string DescribePreparationFailure(SteamInviteBridgeResult result) =>
        result == SteamInviteBridgeResult.LobbyFailed
            ? Loc.Select(
                "Steam 직접 초대 로비를 만들 수 없습니다.",
                "The Steam direct-invite lobby could not be created.",
                "无法创建 Steam 直连邀请房间。"
            )
            : Loc.Select(
                "Steam 연결 또는 친구 목록을 준비하지 못했습니다.",
                "The Steam connection or friend list could not be prepared.",
                "无法准备 Steam 连接或好友列表。"
            );

    private static string DescribeSendResult(SteamInviteBridgeResult result) =>
        result switch
        {
            SteamInviteBridgeResult.Success => Loc.Select(
                "Steam에 초대 요청을 제출했습니다. 전송 완료를 보장하지는 않습니다.",
                "The invite request was submitted to Steam; delivery is not guaranteed.",
                "邀请请求已提交给 Steam，但不保证送达。"
            ),
            SteamInviteBridgeResult.NotFriend => Loc.Select(
                "친구 관계가 변경되어 초대를 보내지 않았습니다.",
                "The friend relationship changed, so no invite was sent.",
                "好友关系已变化，因此未发送邀请。"
            ),
            SteamInviteBridgeResult.RateLimited => Loc.Select(
                "초대 요청이 너무 잦습니다. 잠시 후 다시 시도하세요.",
                "Invite requests are too frequent. Try again shortly.",
                "邀请请求过于频繁，请稍后再试。"
            ),
            _ => Loc.Select(
                "초대 요청을 보내지 못했습니다.",
                "The invite request could not be submitted.",
                "无法提交邀请请求。"
            ),
        };

    private static string DescribeAcceptFailure(SteamInviteBridgeResult result) =>
        result == SteamInviteBridgeResult.Stale
            ? Loc.Select(
                "초대가 만료되었거나 화면이 변경되어 참가하지 않았습니다.",
                "The invite expired or the screen changed, so no connection was made.",
                "邀请已过期或页面已变化，因此没有连接。"
            )
            : Loc.Select(
                "Steam 초대를 확인하지 못해 ENet 연결을 시작하지 않았습니다.",
                "The Steam invite could not be verified, so the ENet connection was not started.",
                "无法验证 Steam 邀请，因此未启动 ENet 连接。"
            );
}
