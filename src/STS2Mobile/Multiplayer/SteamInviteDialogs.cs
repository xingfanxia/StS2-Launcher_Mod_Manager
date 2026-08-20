using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Godot;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Steam;

namespace STS2Mobile.Multiplayer;

internal sealed class SteamInviteUiPump : Node
{
    private readonly ConcurrentQueue<Action> _actions = new();
    private volatile bool _active = true;
    private bool _hooked;

    public SteamInviteUiPump()
    {
        Name = "SteamInviteUiPump";
        ProcessMode = ProcessModeEnum.Always;
        // Embedded managed Node virtuals are not reliably dispatched by this
        // game build (the same device finding drove TouchScroll). SceneTree's
        // ProcessFrame signal is the proven main-thread tick source.
        TreeEntered += Hook;
        TreeExiting += Unhook;
    }

    public bool Post(Action action)
    {
        if (!_active || action == null)
            return false;
        _actions.Enqueue(action);
        return true;
    }

    private void Hook()
    {
        if (_hooked)
            return;
        var tree = GetTree();
        if (tree == null)
            return;
        tree.ProcessFrame += Drain;
        _hooked = true;
    }

    private void Drain()
    {
        for (int count = 0; count < 32 && _actions.TryDequeue(out var action); count++)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[SteamInvite] UI callback degraded: {ex.GetType().Name}");
            }
        }
    }

    private void Unhook()
    {
        _active = false;
        if (_hooked)
        {
            try
            {
                GetTree().ProcessFrame -= Drain;
            }
            catch
            {
                // SceneTree may already be tearing down.
            }
            _hooked = false;
        }
        while (_actions.TryDequeue(out _)) { }
    }
}

internal abstract class SteamInviteOverlay : ColorRect
{
    protected bool Resolved;

    protected SteamInviteOverlay()
    {
        ModalGate.Register(this);
        SetAnchorsPreset(LayoutPreset.FullRect);
        Color = new Color(0, 0, 0, 0.72f);
        ZIndex = 500;
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop;
    }

    protected static PanelContainer CreatePanel(float scale, int width = 520)
    {
        var panel = new PanelContainer();
        var style = Ui.Filled(scale, Ui.SurfaceHigh, Ui.RadiusL);
        style.SetContentMarginAll(Ui.S(scale, 24));
        panel.AddThemeStyleboxOverride("panel", style);
        panel.CustomMinimumSize = new Vector2(Ui.S(scale, width), 0);
        return panel;
    }

    protected static StyledLabel CreateTitle(string text, float scale)
    {
        var title = new StyledLabel(text, scale, fontSize: Ui.FontTitle);
        title.AddThemeColorOverride("font_color", Ui.TextPrimary);
        return title;
    }
}

// Non-modal, input-transparent identity chip shown only while the visible join
// surface owns a live Steam invite listener. It exposes the sanitized persona
// name needed to select the correct controlled recipient, never a login name or
// numeric Steam ID, and is removed with the coordinator session.
internal sealed class SteamInviteListenerStatus : MarginContainer
{
    public SteamInviteListenerStatus(string personaName, float scale)
    {
        SetAnchorsPreset(LayoutPreset.TopWide);
        OffsetLeft = Ui.S(scale, 16);
        OffsetTop = Ui.S(scale, 12);
        OffsetRight = -Ui.S(scale, 16);
        ZIndex = 490;
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(center);

        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var style = Ui.Filled(scale, Ui.SurfaceHigh, Ui.RadiusL);
        style.BorderColor = Ui.Accent;
        style.SetBorderWidthAll(Math.Max(1, Ui.S(scale, 1)));
        style.SetContentMarginAll(Ui.S(scale, 12));
        panel.AddThemeStyleboxOverride("panel", style);
        center.AddChild(panel);

        var label = new StyledLabel(
            Loc.Select(
                $"Steam 초대 수신 계정: {personaName}. 이 화면을 열어 두세요.",
                $"Steam invites active as {personaName}. Keep this page open.",
                $"Steam 好友邀请已启用：{personaName}。请保持此页面打开。"
            ),
            scale,
            fontSize: Ui.FontCaption,
            provenance: TextProvenance.LauncherTemplateWithExternalContent
        );
        label.AddThemeColorOverride("font_color", Ui.TextPrimary);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(Ui.S(scale, 320), 0);
        panel.AddChild(label);
    }
}

internal sealed class SteamInviteMethodDialog : SteamInviteOverlay
{
    public event Action SteamFriendsSelected;
    public event Action LanShareSelected;
    public event Action Cancelled;

    public SteamInviteMethodDialog(float scale)
    {
        TreeExiting += CancelIfNeeded;
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = CreatePanel(scale);
        center.AddChild(panel);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", Ui.S(scale, 12));
        panel.AddChild(content);

        content.AddChild(
            CreateTitle(Loc.Select("플레이어 초대", "INVITE PLAYER", "邀请玩家"), scale)
        );
        var hint = new StyledLabel(
            Loc.Select(
                "Steam 친구 초대와 LAN/VPN 직접 공유는 같은 ENet 연결을 사용합니다.",
                "Steam friend invites and LAN/VPN sharing both use the existing direct ENet connection.",
                "Steam 好友邀请与 LAN/VPN 分享均使用现有的 ENet 直连。"
            ),
            scale,
            fontSize: Ui.FontCaption
        );
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.AddThemeColorOverride("font_color", Ui.TextSecondary);
        content.AddChild(hint);

        var steam = new StyledButton(
            Loc.Select("STEAM 친구 초대", "INVITE STEAM FRIEND", "邀请 STEAM 好友"),
            scale,
            variant: ButtonVariant.Primary
        );
        steam.Pressed += () => Resolve(SteamFriendsSelected);
        content.AddChild(steam);

        var lan = new StyledButton(
            Loc.Select("LAN/VPN 코드 공유", "SHARE LAN/VPN CODE", "分享 LAN/VPN 代码"),
            scale
        );
        lan.Pressed += () => Resolve(LanShareSelected);
        content.AddChild(lan);

        var cancel = new StyledButton(
            Loc.Select("취소", "CANCEL", "取消"),
            scale,
            variant: ButtonVariant.Ghost
        );
        cancel.Pressed += () => Resolve(Cancelled);
        content.AddChild(cancel);
    }

    private void Resolve(Action action)
    {
        if (Resolved)
            return;
        Resolved = true;
        QueueFree();
        action?.Invoke();
    }

    private void CancelIfNeeded()
    {
        if (Resolved)
            return;
        Resolved = true;
        Cancelled?.Invoke();
    }
}

internal sealed class SteamInviteFriendPickerDialog : SteamInviteOverlay
{
    private readonly IReadOnlyList<SteamInviteFriend> _friends;
    private readonly float _scale;
    private readonly StyledLineEdit _search;
    private readonly CheckButton _showOffline;
    private readonly VBoxContainer _list;

    public event Action<SteamInviteFriend> FriendSelected;
    public event Action Cancelled;

    public SteamInviteFriendPickerDialog(IReadOnlyList<SteamInviteFriend> friends, float scale)
    {
        _friends = friends ?? Array.Empty<SteamInviteFriend>();
        _scale = scale;
        TreeExiting += CancelIfNeeded;
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = CreatePanel(scale, 620);
        center.AddChild(panel);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", Ui.S(scale, 10));
        panel.AddChild(content);
        content.AddChild(
            CreateTitle(
                Loc.Select("STEAM 친구 선택", "SELECT STEAM FRIEND", "选择 STEAM 好友"),
                scale
            )
        );

        _search = new StyledLineEdit(
            Loc.Select(
                "Steam 이름 또는 별명 검색…",
                "Search Steam name or nickname…",
                "搜索 Steam 名称或备注昵称…"
            ),
            scale
        )
        {
            MaxLength = 64,
        };
        _search.TextChanged += _ => RebuildList();
        content.AddChild(_search);

        _showOffline = new CheckButton
        {
            Text = Loc.Select("오프라인 친구 표시", "Show offline friends", "显示离线好友"),
            ButtonPressed = false,
            CustomMinimumSize = new Vector2(0, Ui.S(scale, Ui.TouchHeight)),
        };
        _showOffline.AddThemeFontSizeOverride("font_size", Ui.S(scale, Ui.FontBody));
        _showOffline.AddThemeColorOverride("font_color", Ui.TextSecondary);
        Loc.Watch(_showOffline);
        _showOffline.Toggled += _ => RebuildList();
        content.AddChild(_showOffline);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, Ui.S(scale, 360)),
        };
        TouchScroll.Attach(scroll);
        content.AddChild(scroll);
        _list = new VBoxContainer();
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _list.AddThemeConstantOverride("separation", Ui.S(scale, 6));
        scroll.AddChild(_list);
        RebuildList();

        var cancel = new StyledButton(
            Loc.Select("취소", "CANCEL", "取消"),
            scale,
            variant: ButtonVariant.Ghost
        );
        cancel.Pressed += () => Resolve(null);
        content.AddChild(cancel);
    }

    private void RebuildList()
    {
        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        var matching = _friends
            .Where(friend =>
                SteamInviteFriendListPolicy.Matches(
                    friend.PersonaName,
                    friend.Nickname,
                    _search.Text
                )
                && SteamInviteFriendListPolicy.IsVisible(
                    friend.IsOnline,
                    _showOffline.ButtonPressed,
                    _search.Text
                )
            )
            .OrderByDescending(friend =>
                SteamInviteFriendListPolicy.Rank(
                    friend.HasNickname,
                    friend.IsPlayingGame,
                    friend.PlayedRecently,
                    friend.IsOnline
                )
            )
            .ThenBy(friend => friend.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (matching.Count == 0)
        {
            var empty = new StyledLabel(
                _showOffline.ButtonPressed || !string.IsNullOrWhiteSpace(_search.Text)
                    ? Loc.Select(
                        "검색어와 일치하는 Steam 친구가 없습니다.",
                        "No Steam friends match this search.",
                        "没有匹配此搜索的 Steam 好友。"
                    )
                    : Loc.Select(
                        "검색 조건에 맞는 온라인 Steam 친구가 없습니다.",
                        "No online Steam friends match these filters.",
                        "没有符合筛选条件的在线 Steam 好友。"
                    ),
                _scale
            );
            empty.AddThemeColorOverride("font_color", Ui.TextSecondary);
            _list.AddChild(empty);
            return;
        }

        var visible = SteamInviteFriendListPolicy.RenderWindow(matching);
        foreach (var friend in visible)
        {
            var status =
                friend.IsPlayingGame
                    ? Loc.Select(
                        "《슬레이 더 스파이어 2》 플레이 중",
                        "PLAYING SLAY THE SPIRE 2",
                        "正在玩《杀戮尖塔 2》"
                    )
                : friend.PlayedRecently
                    ? Loc.Select(
                        "최근 《슬레이 더 스파이어 2》를 플레이함",
                        "RECENTLY PLAYED SLAY THE SPIRE 2",
                        "最近玩过《杀戮尖塔 2》"
                    )
                : friend.IsOnline ? Loc.Select("온라인", "ONLINE", "在线")
                : Loc.Select("오프라인", "OFFLINE", "离线");
            var identity = friend.HasNickname
                ? $"{friend.Nickname}\nSteam: {friend.PersonaName}"
                : friend.PersonaName;
            var row = new StyledButton(
                $"{identity}\n{status}",
                _scale,
                height: friend.HasNickname ? 82 : 68,
                provenance: TextProvenance.LauncherTemplateWithExternalContent
            );
            row.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            var captured = friend;
            row.Pressed += () => Resolve(captured);
            _list.AddChild(row);
        }

        if (matching.Count > visible.Count)
        {
            var truncated = new StyledLabel(
                Loc.Select(
                    $"첫 {visible.Count}개의 결과만 표시합니다. Steam 이름이나 별명으로 검색 범위를 좁혀 주세요.",
                    $"Showing the first {visible.Count} matches. Search by Steam name or nickname to narrow the list.",
                    $"仅显示前 {visible.Count} 个匹配项，请按 Steam 名称或备注昵称缩小搜索范围。"
                ),
                _scale,
                fontSize: Ui.FontCaption,
                provenance: TextProvenance.LauncherTemplateWithExternalContent
            );
            truncated.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            truncated.AddThemeColorOverride("font_color", Ui.TextSecondary);
            _list.AddChild(truncated);
        }
    }

    private void Resolve(SteamInviteFriend friend)
    {
        if (Resolved)
            return;
        Resolved = true;
        QueueFree();
        if (friend == null)
            Cancelled?.Invoke();
        else
            FriendSelected?.Invoke(friend);
    }

    private void CancelIfNeeded()
    {
        if (Resolved)
            return;
        Resolved = true;
        Cancelled?.Invoke();
    }
}

internal sealed class SteamIncomingInviteDialog : SteamInviteOverlay
{
    public event Action<LanJoinEndpoint> Accepted;
    public event Action Declined;

    public SteamIncomingInviteDialog(SteamIncomingDirectInvite invite, float scale)
    {
        TreeExiting += DeclineIfNeeded;
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = CreatePanel(scale, 620);
        center.AddChild(panel);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", Ui.S(scale, 10));
        panel.AddChild(content);
        content.AddChild(
            CreateTitle(
                Loc.Select("런처 직접 초대", "LAUNCHER DIRECT INVITE", "LAUNCHER 直连邀请"),
                scale
            )
        );

        var inviter = string.IsNullOrWhiteSpace(invite.InviterDisplayName)
            ? Loc.Select("Steam 친구", "Steam friend", "Steam 好友")
            : invite.InviterDisplayName;
        var message = new StyledLabel(
            Loc.Select(
                $"{inviter} 님이 직접 ENet 게임에 초대했습니다. Steam Relay가 아니며 표시된 LAN/VPN 주소로만 연결합니다.",
                $"{inviter} invited you to a direct ENet game. This is not Steam Relay; it connects only to the LAN/VPN address you choose below.",
                $"{inviter} 邀请你加入 ENet 直连游戏。这不是 Steam Relay；只会连接到你在下方选择的 LAN/VPN 地址。"
            ),
            scale,
            fontSize: Ui.FontBody,
            provenance: TextProvenance.LauncherTemplateWithExternalContent
        );
        message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        message.AddThemeColorOverride("font_color", Ui.TextSecondary);
        content.AddChild(message);

        var endpointScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(
                0,
                Ui.S(
                    scale,
                    Math.Min(320, invite.EndpointCandidates.Count * (Ui.TouchHeight + Ui.GapS))
                )
            ),
        };
        TouchScroll.Attach(endpointScroll);
        content.AddChild(endpointScroll);
        var endpointList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        endpointList.AddThemeConstantOverride("separation", Ui.S(scale, Ui.GapS));
        endpointScroll.AddChild(endpointList);
        foreach (var endpoint in invite.EndpointCandidates)
        {
            var captured = endpoint;
            var join = new StyledButton(
                Loc.Select("참가", "JOIN", "加入") + "  " + endpoint,
                scale,
                variant: ButtonVariant.Primary,
                provenance: TextProvenance.ExternalContent
            );
            join.Pressed += () => Resolve(captured);
            endpointList.AddChild(join);
        }

        var decline = new StyledButton(
            Loc.Select("거절", "DECLINE", "拒绝"),
            scale,
            variant: ButtonVariant.Ghost
        );
        decline.Pressed += () => Resolve(null);
        content.AddChild(decline);
    }

    private void Resolve(LanJoinEndpoint? endpoint)
    {
        if (Resolved)
            return;
        Resolved = true;
        QueueFree();
        if (endpoint.HasValue)
            Accepted?.Invoke(endpoint.Value);
        else
            Declined?.Invoke();
    }

    private void DeclineIfNeeded()
    {
        if (Resolved)
            return;
        Resolved = true;
        Declined?.Invoke();
    }
}
