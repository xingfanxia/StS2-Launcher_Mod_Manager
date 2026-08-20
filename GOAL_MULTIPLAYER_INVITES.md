# Goal: 安全支持多 Steam 账号切换，随后交付低冲突与 Steam 联机邀请

## Intent

先让 launcher 用户能自由切换多个 Steam 账号且绝不串用本地存档、云会话或 token，再让
用户不必手抄主机 IP，并在不破坏现有 LAN、离线、Steam 登录和游戏联机协议的前提下逐步
获得可分享的邀请体验。首要不变量是账号边界与数据保全；其后必须诚实区分“直连邀请信令”
和“Steam 原生传输/relay”：房间码、Steam lobby 或漂亮按钮都不能冒充尚未实现的 NAT 穿透。

## Grounded context

- Verified: `src/STS2Mobile/Patches/LanMultiplayerPatcher.cs` 已用 ENet 在 UDP 33771
  host，支持 UDP 33770 LAN discovery 和手动 `IP[:port]` join；低冲突方案可复用现有
  `JoinViaIp`/`JoinGameAsync`，无需另写游戏网络栈。
- Verified: pinned game DLL 中，`NMultiplayerHostSubmenu.StartHostAsync` 仅在
  `SteamInitializer.Initialized` 时使用 `StartSteamHost`，否则使用 ENet；
  `NInvitePlayersButton` 只对支持 native invite dialog 的 platform 显示。来源为
  2026-08-19 对 build 输入中 `sts2.dll` 的本地 metadata/decompile 检查。
- Verified: 当前 APK 的 ARM64 `libsteam_api.so` 是仓库 `src/stubs/steam_stub.c` 的假实现，
  反汇编显示 Init/InitFlat 硬编码成功、IsSteamRunning 硬编码 false、user/pipe 为固定 dummy
  handle；它不能作为 native Steamworks probe。Valve 的 Android ARM64 Steamworks 支持文档面向
  Steam Frame/Lepton，当前普通 Android 真机也没有该受支持 client/runtime 环境。
- Verified: 当前 SteamKit 3.4.0 提供 friend list、`CreateLobby`、lobby metadata、
  `InviteToLobby`、`JoinLobby` 和 `ChatInviteCallback`；`SteamConnection` 已有 callback
  pump 与 idle-timeout suspend/resume。SteamKit lobby 不等于游戏的
  SteamNetworkingSockets/SDR transport。来源见 `PLAN_MULTIPLAYER_INVITES.md`。
- Assumption: 第一阶段的最低可用范围是同 LAN、可达 VPN 或用户已配置端口映射的直连；
  普通公网/CGNAT 不在没有 relay 的情况下自动可达。
- Verified: launcher 原先只有一个 `steam_credentials.enc`、一组全局 credential/cloud static
  和单例 `SteamKit2CloudSaveStore`；游戏在 mobile null platform 下把本地数据落在逻辑
  `user://default/1`。仅清 token 会让不同账号共享本地目录，并允许旧 cloud/token 异步任务
  穿越切换边界。来源为 2026-08-19 的 `SteamCredentialStore.cs`、`LauncherModel.cs`、
  `LauncherPatches.cs`、`SteamKit2CloudSaveStore.cs` 与 pinned `sts2.dll` decompile。
- Verified: 游戏安装在 app-internal `game/`，Workshop/manual mod 在 external
  `StS2LauncherMM/Mods`，两者可跨账号共享；Steam Cloud RPC 本身按登录账号隔离，因此本地
  账号目录必须分开，但 cloud logical filename 必须保持不变以保留既有云端数据。
- Verified: 本 fork 的 `.github/workflows/build-apk.yml` 仅在 merge/push 到 `main` 后运行
  pinned signed APK build 并上传 artifact；没有自动 GitHub Release job。最近版本为 `v0.4.7`，
  既有流程是 ready PR → merge commit → main build artifact → tag/GitHub Release。来源为
  2026-08-19 的 workflow、GitHub run/release 和 git history live 检查。

## Done state

- Phase 0 必须先完成：launcher 提供明显的 Steam 账号选择入口，可添加账号并在已保存账号间
  无需重新输入密码地反复切换；所有账号 token/Guard data 只保存在 Android Keystore 加密的
  多账号 vault，切换成功后强制 clean restart。
- 每个账号使用随机 opaque local data slot；游戏逻辑/cloud path 保持不变，仅 local Godot I/O
  重定向到当前 slot。首次升级把旧 `default/1` 复制到第一个账号且保留原目录；第二账号不得
  继承该副本。游戏安装、存档、Workshop/mod、local backup、cloud data 和任何已保存账号均
  不得因切换、失败、取消或迁移而删除。
- 切换前必须阻止新 cloud/save/Workshop 操作，排空并 dispose 旧账号 cloud singleton 与连接；
  旧 token renewal 或 callback 完成后不得回写当前账号。目录/vault 任一步失败时保持或恢复旧
  账号，并明确提示，不得进入“UI 显示 B、后台仍是 A”的混合状态。
- 账号名、SteamID、refresh token、Guard data 和 PIN 不得进入 console、launcher log、proof
  或公开 artifact；账号名只能在账号选择/当前账号等必要 UI 中显示。随机 opaque slot 与账号
  无可逆关系，可作为 app-private 路径的一部分，但实际值不得写入公开 proof。
- Phase 1 首先独立完成：ENet host 能从现有 multiplayer lobby 醒目地 Copy/Share 一个
  versioned invite，join 端可粘贴该 invite 或既有 `IP[:port]`，经过严格解析后走同一
  `ENetClientConnectionInitializer`；现有 LAN discovery 和手动 join 无回归。
- Share 使用 Android system Sharesheet，由用户选择目标；launcher 不静默发消息。若加入
  deep link，外部 link 永不 auto-join，必须显示目标并由用户确认。
- 新邀请 UI、状态、错误和确认在韩语、英语、简体中文下完整可用；旋转、HOME/resume、
  host disconnect 和 join screen teardown 后无陈旧 callback、重复按钮或后台 socket。
- Phase 2 只在 Phase 1 proof 通过后尝试：用已有 SteamKit connection 建立 friends-only
  bridge lobby、显示好友、显式发送邀请、接收并验证 invite metadata，再复用 ENet join。
- 好友选择页默认隐藏离线好友，可显式显示；搜索同时匹配 Steam persona name 与用户备注昵称。
  排序严格依次优先有备注、正在玩《杀戮尖塔 2》、最近玩过该游戏、其他在线好友；备注作为
  主名称、Steam 名作为副名称。Steam 协议只有 `played_recently` 证据，因此不得把它标成
  “最近一起玩过”。好友信息仅在内存中使用，不写入日志、proof 或持久化文件。
- 接收端 join 页面只在 invite listener 存活时显示经过清洗的当前 Steam persona，便于发送端
  安全识别指定账号；不得用登录名、raw Steam ID 或猜测列表位置代替。该状态不拦截输入、不写
  日志/持久化，并随切换账号、页面 teardown、logout 或后台超时移除。
- SteamKit bridge 明确标为 launcher-to-launcher direct invite，除非实际证明原版 PC
  Steam client 能通过同一 invite 加入并交换游戏数据，否则不得宣称 PC/Steam relay 兼容。
- Phase 3 对 native Steamworks 做 fail-closed feasibility probe。若普通 Android 无受支持
  Steam client 而初始化失败，保留 Phase 1/2，不伪造 `SteamInitializer.Initialized`；若在
  Steam Frame/Lepton 或其他受支持环境成功，优先复用游戏原生 Steam host/invite/join。
- Steam 原生方向的“尝试”只有在完成一个可判别实验后才算结束：要么两端真实邀请并加入
  成功，要么用 init/callback/transport 证据锁定最小外部阻塞。未经测试的猜测不算结论。

## Proof

- Run/check: 多账号 vault、token identity、opaque slot、一次性 legacy copy、非覆盖复制、失败
  rollback、stale renewal generation guard 和日志脱敏的 deterministic tests。
  Pass when: malformed identity fail closed；第二 slot 为空；legacy/目标原件均保留；vault
  decrypt/persist 失败不覆盖旧 vault；旧 session 不能更新新 active account。
- Independent check: 在一台保留真实 app data 的设备上覆盖安装，记录脱敏的游戏/存档/mod/
  Workshop 文件计数与 hash；添加第二测试账号，完成 A→B→A 与取消/失败路径。
  Pass when: 每次启动只看到当前账号自己的 synthetic/local fixture，原文件计数/hash 不变，
  shared game/mod 仍存在，cloud logical filename 不变，log scan 无账号标识或 token。不得通过
  `pm clear`、卸载或删除目录制造通过结果。
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
  host；默认离线过滤、备注/Steam 名搜索、备注→正在玩→最近玩过→其他在线排序在真实列表
  生效；接收端自身 persona 在必要 UI 中可见且发送端唯一匹配该 persona；凭据、raw Steam ID、
  好友列表不进入 proof/log。
- Conditional check: 记录 ordinary Android 的 `SteamAPI.InitEx`/overlay/matchmaking/socket
  sanitized 结果；有 Steam Frame/Lepton 时做同 build A/B。
  Pass when: 只在 init 成功后调用 native interface，失败路径保持现有 ENet 且不 crash。
- Publication gate: 上述全部 applicable proof、integration tests 与真实 E2E 必须先在未发布分支
  通过；任何一项失败都不得 commit/push 用于发布、merge、tag 或 release。全部通过后，无需再次
  请求用户确认，自动 bump 到 live 检查所得的下一个未占用 semver/versionCode，intentional commit、
  push 当前分支、创建并 ready PR、等待并修复 CI/review 的 material failure，全部 required checks
  通过后 merge。随后等待 main `Build APK` 成功，下载该 merge commit 的 signed artifact，核验
  签名、hash、覆盖安装、启动及 applicable release E2E，再用该 artifact 创建并 push tag 与
  non-draft GitHub Release；release asset 名、`.sha256` 和 updater channel 必须一致。

## Scope and authority

- May read: 整个 fork、pinned game assembly/dependency metadata、现有设备上本应用的脱敏日志，
  以及 Valve/SteamKit primary documentation/source。
- May change: encrypted credential vault、account local-path redirection、launcher account UI、
  connection/cloud lifecycle、邀请 code/metadata/lifecycle、`LanMultiplayerPatcher` 的窄 hook、
  `SteamConnection` handler/event、Android Sharesheet/deep-link bridge、三语文案、相关 audit/
  tests、`PLAN_MULTIPLAYER_INVITES.md` 和 proof 文档。
- May exercise: 本地/container build、已授权且已解锁设备上的覆盖安装、账号选择/切换、两个
  受控 launcher instance 的 LAN 连接、不开启发送的 Sharesheet、恶意 input fixtures、
  force-stop/relaunch、旋转和 HOME/resume。不得把解锁 PIN 写入文件、命令输出或 proof。
- Must preserve: 现有 LAN discovery/manual IP、离线启动、Steam refresh token/Guard data、
  cloud/Workshop、存档、mod、默认 renderer、第三方名称，以及 updater/package/signing identity。
- Authorized after all applicable integration/E2E proof passes: 无需额外确认，为本 goal 的完整 diff
  做版本 bump、intentional commit、push 当前分支、创建 ready PR、等待/修复 CI、merge 到 `main`，
  并用对应 main CI signed artifact 创建并 push tag 与 GitHub Release。该授权不允许 force-push、
  绕过 required checks、发布未经 main artifact 真机验证的本地 APK，或夹带无关修改。
- Requires new authorization: 向真实好友或未指定账号发送 Steam invite/message、使用尚未明确
  授权的第二账号凭据、部署公网 relay/backend、修改 Steamworks partner 配置、上传脱敏 proof
  之外的日志、卸载/clear data、改写真实存档/mod、force-push 或处理与本 goal 无关的发布内容。

## Non-goals and invalid shortcuts

- 不在 Phase 1 自建公网 matchmaking/relay，不承诺 CGNAT 下仅凭房间码即可连接。
- 不把 base64/短码称为安全或匿名；无 backend 时它只是 endpoint 的可复制编码。
- 不强制把 `SteamInitializer.Initialized` 改为 true，不在 init 失败后调用 Steamworks interface，
  不模拟 Steam client IPC 或伪造 game presence。
- 不因 SteamKit 能创建 lobby 就宣称原版 PC 可加入；必须另外证明 Steam transport 数据通路。
- 不把 refresh token、Steam ID、好友列表、IP inventory 或 invite payload 写入公开 proof。
- 不以“注销并忘记旧账号”冒充自由切换，不用共享 `default/1` 冒充账号隔离，不改变 cloud
  filename 来制造本地分目录，不用 delete/move 原数据简化迁移，也不把账号名/SteamID 当目录名。

## Priorities and tradeoffs

1. 不泄露凭据或账号数据，切换不串号、不删除、不覆盖
2. Phase 0 多账号切换真实往返 proof
3. 不自动连接/发送、不误导网络可达性
4. Phase 1 低冲突邀请可稳定使用且不回归现有 LAN
5. launcher-to-launcher Steam invite 的真实双账号 proof
6. 原版 PC/Steam relay 互操作
7. upstream 低冲突和可审计性

当原生 Steam 体验与普通 Android 的受支持边界冲突时，保留稳定 ENet/SteamKit bridge 并
清楚标注限制；当深链接便利性与安全确认冲突时，必须保留确认。

## Unknowns and decision rules

- Phase 1 已按“pure contract、Share/Copy 和 paste，不同时接 SteamKit”的边界实现并保留
  现有证据；账号切换是恢复本 goal 后的新增前置工作单元，在其 proof 通过前不得继续会改变 Steam
  session 的 Phase 2。Phase 1 已完成的 deterministic/UI proof 保留，不重复制造替代证据。
- 若 vault 解密、旧目录 copy、cloud drain 或 active-slot publish 失败，fail closed 到旧账号并
  保留所有文件；同一路径连续两次失败时改变观测点，不追加删除/重建式 workaround。
- 若多个本地网络地址都可能有效，展示候选让 host 选择，不擅自发布公网地址。
- 若没有第二设备/第二 Steam 测试账号，完成 Phase 1 和 SteamKit deterministic tests 后将
  Phase 2 标为 blocked，报告最小缺口；不得用单账号自发自收冒充 E2E。
- 若普通 Android native init 失败，先判断是否缺少 Steam Frame/Lepton client；同一假设
  连续两次无新证据时改变观测点，不 patch around failure。
- 若 SteamKit invite 能送达但原版 PC 接受后 transport 失败，结论是 signaling 可行、PC
  interoperability 未完成；不得扩大为 SteamNetworkingSockets 重写，除非用户另行授权。
- 公网 relay、partner-side 配置或外部服务一旦成为必要条件，暂停该分支并给出架构/运维/
  隐私成本与最小授权，不默认部署。
- 若本地 E2E、PR diff、hosted CI、CI artifact 签名/安装/启动任一失败，保持 release 未创建，修复
  后从对应 gate 重跑；不得用旧 artifact、手工本地包或 release 后补测替代。

## Control loop and resumption

- Work unit: Phase 0 使用“vault/path contract → lifecycle barrier → UI → deterministic test →
  A→B→A 真机 proof”；邀请阶段仍使用“pure contract → 一处 UI/bridge → lifecycle cleanup →
  deterministic test → 两端行为 proof”的闭环。
- State: 使用原生 goal/plan；最终证据写入新的 `docs/MULTIPLAYER_INVITE_PROOF.md`，设备日志
  和截图保存在仓库外且脱敏。
- Retry/budget: 同一邀请或 callback 路径失败两次且无新增证据时，回到 endpoint/lobby/
  transport 边界重新画数据流；不得叠加 speculative Harmony patches。
- Stop when: Phase 0 和 Phase 1 全部 proof 通过，Steam 方向完成成功 E2E 或得到可复现的最小
  外部阻塞结论，ready PR 已 merge，main CI artifact 已签名/安装/启动验证且正式 Release 可下载；
  任何新账号凭据/真实好友/外部服务授权缺失时停止对应分支而不伪造 proof 或提前发布。

## Delivery

- Produce: Phase 0 多账号切换与独立本地数据目录、Phase 1 可用实现、conditional SteamKit
  bridge/probe、三语 UI、tests/audits、
  `docs/MULTIPLAYER_INVITE_PROOF.md`、更新后的本 plan、merged PR 和正式 GitHub Release。
- Report: 每一层实际可达范围、两端互操作矩阵、lifecycle/security proof、未解决的 NAT/
  Steam client/relay 限制及 upstream conflict surface。
- Complete only when: 多账号在真实设备完成 A→B→A 且数据/日志 proof 通过，Phase 1 是真实
  双端可用状态，Steam 方向的实验有可复现结果，所有
  applicable proof 通过，且发布 gate 的 PR/main CI/signed artifact/release 全部完成；只写按钮、
  只创建 lobby、只观察到 callback 或只有本地 APK 不能标记完成。
- Otherwise report: partial/blocked，附最小复现、已排除假设和继续所需的设备/账号/权限，
  不把“Steam API 存在”写成“Steam 原生邀请已支持”。
