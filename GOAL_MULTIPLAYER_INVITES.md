# Goal: 先交付低冲突联机邀请，再判定并尽可能支持 Steam 好友邀请

## Intent

让 launcher 用户不再手抄主机 IP，并在不破坏现有 LAN、离线、Steam 登录和游戏联机协议
的前提下逐步获得可分享的邀请体验。首要不变量是诚实区分“直连邀请信令”和“Steam 原生
传输/relay”：房间码、Steam lobby 或漂亮按钮都不能冒充尚未实现的 NAT 穿透。

## Grounded context

- Verified: `src/STS2Mobile/Patches/LanMultiplayerPatcher.cs` 已用 ENet 在 UDP 33771
  host，支持 UDP 33770 LAN discovery 和手动 `IP[:port]` join；低冲突方案可复用现有
  `JoinViaIp`/`JoinGameAsync`，无需另写游戏网络栈。
- Verified: pinned game DLL 中，`NMultiplayerHostSubmenu.StartHostAsync` 仅在
  `SteamInitializer.Initialized` 时使用 `StartSteamHost`，否则使用 ENet；
  `NInvitePlayersButton` 只对支持 native invite dialog 的 platform 显示。来源为
  2026-08-19 对 build 输入中 `sts2.dll` 的本地 metadata/decompile 检查。
- Verified: 当前 APK 含 ARM64 `libsteam_api.so`，但 Valve 的 Android ARM64 Steamworks
  支持文档面向 Steam Frame/Lepton；当前普通 Android 真机没有 Steam/Lepton package。
  不能从库文件存在推断普通手机可初始化 Steamworks。
- Verified: 当前 SteamKit 3.4.0 提供 friend list、`CreateLobby`、lobby metadata、
  `InviteToLobby`、`JoinLobby` 和 `ChatInviteCallback`；`SteamConnection` 已有 callback
  pump 与 idle-timeout suspend/resume。SteamKit lobby 不等于游戏的
  SteamNetworkingSockets/SDR transport。来源见 `PLAN_MULTIPLAYER_INVITES.md`。
- Assumption: 第一阶段的最低可用范围是同 LAN、可达 VPN 或用户已配置端口映射的直连；
  普通公网/CGNAT 不在没有 relay 的情况下自动可达。

## Done state

- Phase 1 首先独立完成：ENet host 能从现有 multiplayer lobby 醒目地 Copy/Share 一个
  versioned invite，join 端可粘贴该 invite 或既有 `IP[:port]`，经过严格解析后走同一
  `ENetClientConnectionInitializer`；现有 LAN discovery 和手动 join 无回归。
- Share 使用 Android system Sharesheet，由用户选择目标；launcher 不静默发消息。若加入
  deep link，外部 link 永不 auto-join，必须显示目标并由用户确认。
- 新邀请 UI、状态、错误和确认在韩语、英语、简体中文下完整可用；旋转、HOME/resume、
  host disconnect 和 join screen teardown 后无陈旧 callback、重复按钮或后台 socket。
- Phase 2 只在 Phase 1 proof 通过后尝试：用已有 SteamKit connection 建立 friends-only
  bridge lobby、显示好友、显式发送邀请、接收并验证 invite metadata，再复用 ENet join。
- SteamKit bridge 明确标为 launcher-to-launcher direct invite，除非实际证明原版 PC
  Steam client 能通过同一 invite 加入并交换游戏数据，否则不得宣称 PC/Steam relay 兼容。
- Phase 3 对 native Steamworks 做 fail-closed feasibility probe。若普通 Android 无受支持
  Steam client 而初始化失败，保留 Phase 1/2，不伪造 `SteamInitializer.Initialized`；若在
  Steam Frame/Lepton 或其他受支持环境成功，优先复用游戏原生 Steam host/invite/join。
- Steam 原生方向的“尝试”只有在完成一个可判别实验后才算结束：要么两端真实邀请并加入
  成功，要么用 init/callback/transport 证据锁定最小外部阻塞。未经测试的猜测不算结论。

## Proof

- Run/check: 为 invite code/metadata parser 添加确定性正负测试。
  Pass when: 版本、IP/port、长度、重复/过期 nonce、错误 App ID、未知 transport、越界值
  全部 fail closed，第三方显示名与 Steam ID 不进入持久化日志。
- Independent check: 两个 launcher instance 在同一 LAN 完成 host → share/copy → paste →
  confirm → join → disconnect → rejoin。
  Pass when: 游戏实际进入同一 lobby/run，旧 LAN discovery 和 plain `IP[:port]` 同时可用。
- Independent check: Android Sharesheet、可选 deep link、旋转、HOME/resume、进程重建。
  Pass when: chooser 不自动发送，外部 link 不自动连接，teardown 后不再处理旧 invite。
- Run/check: pinned Docker APK build、localization/stability/Java lifecycle、member-reference 和
  patch-target audit。
  Pass when: 全部退出码为 0，两条支持的游戏 branch required target 为 0 missing。
- Conditional check: 使用两个明确指定的 Steam 测试账号执行 SteamKit lobby invite。
  Pass when: A 发送、B 收到一次提示，decline 无副作用，accept 在可达网络加入 A 的 ENet
  host；凭据、raw Steam ID、好友列表不进入 proof/log。
- Conditional check: 记录 ordinary Android 的 `SteamAPI.InitEx`/overlay/matchmaking/socket
  sanitized 结果；有 Steam Frame/Lepton 时做同 build A/B。
  Pass when: 只在 init 成功后调用 native interface，失败路径保持现有 ENet 且不 crash。

## Scope and authority

- May read: 整个 fork、pinned game assembly/dependency metadata、现有设备上本应用的脱敏日志，
  以及 Valve/SteamKit primary documentation/source。
- May change: 邀请 code/metadata/lifecycle、`LanMultiplayerPatcher` 的窄 hook、
  `SteamConnection` handler/event、Android Sharesheet/deep-link bridge、三语文案、相关 audit/
  tests、`PLAN_MULTIPLAYER_INVITES.md` 和 proof 文档。
- May exercise: 本地/container build、两个受控 launcher instance 的 LAN 连接、不开启发送的
  Sharesheet、恶意 input fixtures、force-stop/relaunch、旋转和 HOME/resume。
- Must preserve: 现有 LAN discovery/manual IP、离线启动、Steam refresh token/Guard data、
  cloud/Workshop、存档、mod、默认 renderer、第三方名称，以及 updater/package/signing identity。
- Requires new authorization: 向真实好友或未指定账号发送 Steam invite/message、使用第二个
  账号凭据、部署公网 relay/backend、修改 Steamworks partner 配置、上传日志/APK、commit/
  push/PR/merge/release、卸载/clear data 或改写真实存档/mod。

## Non-goals and invalid shortcuts

- 不在 Phase 1 自建公网 matchmaking/relay，不承诺 CGNAT 下仅凭房间码即可连接。
- 不把 base64/短码称为安全或匿名；无 backend 时它只是 endpoint 的可复制编码。
- 不强制把 `SteamInitializer.Initialized` 改为 true，不在 init 失败后调用 Steamworks interface，
  不模拟 Steam client IPC 或伪造 game presence。
- 不因 SteamKit 能创建 lobby 就宣称原版 PC 可加入；必须另外证明 Steam transport 数据通路。
- 不把 refresh token、Steam ID、好友列表、IP inventory 或 invite payload 写入公开 proof。

## Priorities and tradeoffs

1. 不泄露凭据、不自动连接/发送、不误导网络可达性
2. Phase 1 低冲突邀请可稳定使用且不回归现有 LAN
3. launcher-to-launcher Steam invite 的真实双账号 proof
4. 原版 PC/Steam relay 互操作
5. upstream 低冲突和可审计性

当原生 Steam 体验与普通 Android 的受支持边界冲突时，保留稳定 ENet/SteamKit bridge 并
清楚标注限制；当深链接便利性与安全确认冲突时，必须保留确认。

## Unknowns and decision rules

- 第一工作单元只实现 Phase 1 的 pure contract、Share/Copy 和 paste，不同时接 SteamKit。
- 若多个本地网络地址都可能有效，展示候选让 host 选择，不擅自发布公网地址。
- 若没有第二设备/第二 Steam 测试账号，完成 Phase 1 和 SteamKit deterministic tests 后将
  Phase 2 标为 blocked，报告最小缺口；不得用单账号自发自收冒充 E2E。
- 若普通 Android native init 失败，先判断是否缺少 Steam Frame/Lepton client；同一假设
  连续两次无新证据时改变观测点，不 patch around failure。
- 若 SteamKit invite 能送达但原版 PC 接受后 transport 失败，结论是 signaling 可行、PC
  interoperability 未完成；不得扩大为 SteamNetworkingSockets 重写，除非用户另行授权。
- 公网 relay、partner-side 配置或外部服务一旦成为必要条件，暂停该分支并给出架构/运维/
  隐私成本与最小授权，不默认部署。

## Control loop and resumption

- Work unit: 一个“pure contract → 一处 UI/bridge → lifecycle cleanup → deterministic test →
  两端行为 proof”的闭环。
- State: 使用原生 goal/plan；最终证据写入新的 `docs/MULTIPLAYER_INVITE_PROOF.md`，设备日志
  和截图保存在仓库外且脱敏。
- Retry/budget: 同一邀请或 callback 路径失败两次且无新增证据时，回到 endpoint/lobby/
  transport 边界重新画数据流；不得叠加 speculative Harmony patches。
- Stop when: Phase 1 全部 proof 通过，且 Steam 方向完成成功 E2E 或得到可复现的最小外部
  阻塞结论；任何凭据/真实好友/外部服务授权缺失时停止对应分支而不阻塞安全文档交付。

## Delivery

- Produce: Phase 1 可用实现、conditional SteamKit bridge/probe、三语 UI、tests/audits、
  `docs/MULTIPLAYER_INVITE_PROOF.md` 和更新后的本 plan。
- Report: 每一层实际可达范围、两端互操作矩阵、lifecycle/security proof、未解决的 NAT/
  Steam client/relay 限制及 upstream conflict surface。
- Complete only when: Phase 1 是真实双端可用状态，Steam 方向的实验有可复现结果，所有
  applicable proof 通过；只写按钮、只创建 lobby 或只观察到 callback 不能标记完成。
- Otherwise report: partial/blocked，附最小复现、已排除假设和继续所需的设备/账号/权限，
  不把“Steam API 存在”写成“Steam 原生邀请已支持”。
