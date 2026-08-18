# Goal: 消除已确认的 Quick Restart 持续掉帧，并兑现下一批有量化收益的整体性能优化

## Intent

在上一轮启动速度、帧尖峰和启动可观测性已经通过 Proof 的基础上，继续处理用户真实可见、
可重复且 launcher 能安全控制的性能问题。首要结果是让 Quick Restart v2.0.0 在 Android
上不再把正常战斗从接近 60 Hz 的帧节奏拖到约 32 ms p99；随后对首次地图打开和剩余
mod-loading 关键路径做有判别力的调查，只落地达到 meaningful 收益门槛且不破坏 mod、
存档、Vulkan、稳定性或 upstream 可更新性的改动。

核心不变量：先证明 owner，再改变一个变量；不能把第三方 mod 全关、降低画质、隐藏工作、
伪造进度或把昂贵工作挪到另一个用户可见卡顿点来制造“优化”。

## Grounded context

- Verified (2026-08-17): 同一最终源码、同一 Vulkan 真机、同一战斗 fixture 的 120 秒
  会话级分区对照中，BaseLib-only 为 `p99 18.807 ms`、`>2x 4`；唯一新增
  Quick Restart v2.0.0 后为 `p99 31.514 ms`、`>2x 61`。此前的独立二分也得到
  `p99 32.107 ms` 对 `18.810 ms`，因此 Quick Restart 是可重复的 sustained-jank
  owner，不是 BaseLib、renderer 或 thermal 偶然波动。原始 numeric evidence 保留在
  仓库外私有缓存。
- Verified: 本机只读反编译显示 Quick Restart 的隐藏 hold-progress 控件在 idle 时仍每帧
  执行 `_Process`；该路径每帧调用 `CanRestart()`，进而调用
  `SaveManager.HasRunSave -> RunSaveManager.HasRunSave -> ISaveStore.FileExists`，并在
  未按键时重复写 progress/modulate 状态。反编译产物、mod DLL 和原始日志不得进入仓库。
- Inference to test: idle `_Process` 中的同步文件存在性检查和重复 UI invalidation 共同造成
  sustained jank；让该控件在 idle 时不处理、只在真实 hold/release 边界工作，应在不改变
  Quick Restart 功能的前提下恢复 BaseLib-only 帧节奏。
- Verified: 当前最终 APK 的 returning-user total p50/p95 为
  `28.797/29.702 s`，其中 pre-logo/final 系列的 anonymous mod-load p50 约
  `11.527 s`；no-mod smoke 曾建立约 `20.695 s` 的总启动下限。见
  `docs/PERFORMANCE_BASELINE.md` 和 `docs/PERFORMANCE_PROOF.md`。
- Verified: 首次地图打开仍有约 `80 ms` 的用户可见尖峰；真实 map tree 已在打开前建立，
  但 upstream `Open` 还拥有 pause、screen-stack、signal、audio 和一次性动画状态，故隐藏
  open/close 不是可接受的预热捷径。
- Assumption: 当前 ARM64 设备、已批准的 sacrificial combat fixture 和相同 60 Hz/Vulkan
  条件仍可用于主要 A/B；其他 ROM、GPU、游戏或 mod 版本只能在独立证据后外推。

## Done state

- 对 Quick Restart v2.0.0 使用窄、版本/identity 受控、fail-open 的 Android 兼容层；
  不复制或修改 mod 文件，不全局改变 `SaveManager.HasRunSave`，不添加 release 轮询。
- idle 时 Quick Restart 不再每帧访问存档文件或反复写隐藏控件；按下、hold、release、
  进度指示、到时只触发一次 room restart，以及 pause-menu restart button 的原行为均保留。
- 三个交错的 120 秒 Quick Restart+BaseLib 同 APK A/B pair 均有效：修复后的 median p99
  至少改善 25%，`>2x` frame density 至少改善 75%，并达到 `p99 <= 20.7 ms`、
  `>2x <= 8/120 s`；不得用 Safe Mode 或删除/禁用 Quick Restart 达标。
- exact Quick Restart identity 不匹配、目标缺失、mod 更新或 patch 失败时保留原行为并给出
  bounded actionable diagnosis；不得误 patch 同名类型、导致启动失败或静默部分行为。
- 对首次地图打开和 mod-loading 各完成一个 owner 闭环：baseline、单一 falsifiable
  hypothesis、判别实验和结论。只有达到下列 meaningful 门槛的 launcher-owned 候选才实现：
  - runtime：重复场景的 p99 或 `>2x` density 改善至少 25%，且不制造新的 `>100 ms`；
  - startup：完整自动启动 p50 改善至少 10%，或目标 stage p50 改善至少 20% 且完整
    total p50 有正收益；total p95 不得恶化超过 5%。
- 若地图或 mod-loading 被证实属于 game/mod 内部且没有行为保持的 launcher 边界，明确记录
  owner、已排除方案和最小上游修复建议，不以 speculative patch 代替完成；Quick Restart
  的已确认修复仍必须完成。
- no-mod、BaseLib-only、一个普通 supported-mod、Quick Restart、正常启动、Safe Mode、
  HOME/resume、rotation、KR/EN、Vulkan、crash-loop recovery、存档和事务更新 guardrails
  均保持通过；release APK 拒绝全部 debug-only A/B/fault intents。
- 实现主要放在新文件和窄注册点，和 `upstream/main` 的 merge-tree 无冲突；仓库不包含
  mod DLL/PCK、反编译源码、设备标识、账户、存档内容、raw trace/log/screenshot 或签名材料。

## Proof

- Run/check: 用现有 `tools/device-performance/run-mod-jank-workflow.sh` 扩展同 APK
  Quick Restart baseline/optimized arm，在同一 fixture、renderer、分辨率、mod closure 和
  thermal 0-2 下交错运行 3 个 120 秒 pair。
  Pass when: 两边 3/3 `gameplay-interactive`、同一真实 Quick Restart+BaseLib closure、
  无 mod-load error/ANR/LMK/crash，且达到 Done state 的 p99 和 `>2x` 门槛。
- Run/check: debug-only method counters/timing 分别测 idle `HoldProgressIndicator._Process`、
  `CanRestart`、save-existence probe 和 UI mutation；每个实验只改变一个变量。
  Pass when: owner 与帧恶化/恢复在时间和调用次数上闭环；release 不注册逐帧诊断 wrapper。
- Independent check: 在 sacrificial fixture 上验证 Quick Restart idle、短按/释放、完整 hold、
  indicator、单次 restart 和 pause button；再验证一次取消/异常路径。
  Pass when: 功能、存档读取结果、输入处理和 UI reset 与原版一致，没有重复触发或残留节点。
- Run/check: 对 exact identity、MVID/version/target mismatch、late assembly load、重复 load、
  missing dependency 和 Harmony failure 写 host/source-contract tests。
  Pass when: exact target 只 patch 一次；所有 mismatch 均 fail-open 且 bounded log，无启动中断。
- Run/check: 对 first-map-open 做至少 3 个 baseline 和 3 个候选 120 秒 fixed-interaction run；
  对 mod-loading 做匿名 per-initializer/PatchAll span 和完整 startup A/B。
  Pass when: 只有达到 meaningful 门槛的实现被保留；失败候选从 runtime/source 中移除并写入
  proof，不把 covered/offscreen 时间冒充用户收益。
- Run/check: `tools/test-workflow.sh focused`，pinned Docker signed APK pipeline，最终签名/APK
  identity check，以及适用的 device stability/localization/compatibility suites。
  Pass when: 全部退出码为 0，最终 no-suffix APK 在设备上恢复并 force-stop，无新增
  Crash/ANR/LMK/black surface 或语言/renderer/config 漂移。
- Run/check: `git diff --check`、private-artifact/credential scan、`git fetch upstream` 后的
  merge-base/merge-tree review。
  Pass when: 无私有产物或凭据、无不必要的大文件/生成噪音、无 upstream 冲突。

## Scope and authority

- May read: 本地 fork、现有 performance/stability proof、matched game assembly、连接设备的
  bounded performance 状态，以及已安装 Quick Restart/BaseLib 的 manifest/assembly，后两者
  仅可在仓库外私有缓存中只读分析。
- May change: 与 exact mod compatibility、debug-only attribution、frame/startup harness、
  first-map/mod-loading 的已证明 launcher-owned 优化、测试和新 proof 直接相关的本地 fork 文件。
- May exercise: pinned Docker 本地签名构建、同签名 upgrade install、force-stop/launch、
  session-only mod partitions、debug A/B、已批准 sacrificial save 的 bounded load/restart，
  以及可逆的 thermal/renderer 对照；不得 clear data 或改永久 mod enablement。
- Must preserve: 用户现有 worktree、真实 mod 文件和配置、Steam 登录、存档/云数据、EN/KR、
  Vulkan 默认、package/signing identity、更新事务、Safe Mode 和所有稳定性 hardening。
- Requires new authorization: commit/push、开/合 PR、release/上传 APK 或 evidence、卸载、
  clear app data、编辑/替换/删除真实 mod 或存档、改变默认 renderer/画质/刷新率/分辨率、
  或在其他账户/设备上操作。

## Non-goals and invalid shortcuts

- 不承诺通用修复任意第三方 mod；Quick Restart 兼容层只服务已证明的 exact identity，且必须
  在 mod 更新时 fail-open。
- 不直接 patch `FileAccess.FileExists`、`SaveManager.HasRunSave` 或其他全局游戏 API 来掩盖
  一个 mod 的错误调用频率。
- 不通过禁用 Quick Restart/BaseLib、Safe Mode、OpenGL、降低画质/分辨率、改变刷新率或
  选择更冷/更快样本改善数字。
- 不并行化有顺序/全局副作用的 mod initializer，不缓存未知第三方状态，不复制反编译实现。
- 不用隐藏 map open/close 触发 pause、signal、audio、screen-stack 或一次性动画来“预热”；
  若只能改变行为，则保留约 80 ms residual。
- 不把 covered loading 更流畅、工作挪到启动遮罩下或平均 FPS 变化当作 frame-time 改善。

## Priorities and tradeoffs

1. 存档、mod、凭据、更新一致性和 crash-loop 安全
2. Quick Restart 功能正确性与 sustained frame pacing
3. 用户可见 p95/p99、长帧簇和 input responsiveness
4. 完整 time-to-interactive
5. upstream 低冲突和可删除的 exact compatibility boundary
6. 平均吞吐、CPU/RSS 和电量

当通用 game patch 与 exact-mod compatibility 冲突时，选择 exact、可检测、可 fail-open 的
兼容层；当小幅收益需要改变游戏/mod 行为时，保留行为并拒绝该优化。

## Unknowns and decision rules

- 先区分 Quick Restart idle path 中的同步 file probe、重复 UI property mutation、Harmony
  wrapper 和 scheduler 影响；一次只抑制一个 owner，不能直接把合并 patch 当因果证明。
- exact identity 优先使用程序集元数据/MVID 和结构契约；若只能依赖易漂移的 private member，
  必须有 target audit、mismatch test 和清晰 fail-open 日志。
- mod-loading 只在 debug/private evidence 中允许临时关联真实 mod；仓库中的持久 telemetry 和
  proof 保持匿名聚合。不得把“最后加载”自动当作性能 owner。
- thermal 超出 0-2、fixture/refresh/renderer/mod closure 不一致或 capture 非 terminal 时，
  整个 pair invalid，不 cherry-pick 单臂。
- 同一 hypothesis 两次没有新增判别信息后，改变观测点或假设；三次失败不得继续叠 patch。
- 遇到无关问题只记录 residual，不扩展本 Goal。

## Control loop and resumption

- Work unit: `baseline -> 单一 owner hypothesis -> debug-only 判别 -> 最小 coherent fix ->
  focused A/B -> behavior/stability guardrail`；每次只保留一个可归因变量。
- State: 使用原生 goal/plan；聚合结果写入新文件 `docs/PERFORMANCE_FOLLOWUP_PROOF.md`，
  numeric/raw device evidence、mod inventory、反编译和截图只留在仓库外私有缓存。
- Retry/budget: Quick Restart 先完成 1 个判别 pair，再做 3-pair final proof；地图和
  mod-loading 各最多两个无新增信息的实验，只有 diff 稳定后才运行长矩阵。
- On repeated failure: 回到 owner map，删除失败候选，记录被证伪的机制；不得用更宽 patch
  或放松性能/稳定性门槛凑完成。
- Stop when: Quick Restart Done state 和全部 Proof 通过，两个次级 residual 均有证据结论，
  所有保留优化达到 meaningful 门槛；或出现需要新权限/设备/上游 mod 源码的明确阻塞。

## Delivery

- Produce: 最小冲突实现、exact compatibility tests、扩展后的标准化 A/B、
  `docs/PERFORMANCE_FOLLOWUP_PROOF.md`、最终本地签名 no-suffix APK 和 SHA-256。
- Report: Quick Restart 根因、功能保持方式、前后 p50/p95/p99/long-frame density、地图和
  mod-loading 结论、被删除的无效候选、稳定性 guardrail、最终设备/语言/renderer 状态。
- Complete only when: Quick Restart 的量化修复、行为 Proof、次级 residual 结论、全套
  stability/privacy/upstream guardrail 同时通过；仅“定位到了”或“看起来更顺”不得完成。
- Otherwise report: partial 或 blocked，附最小复现、已排除假设和继续所需的最小条件。
