# Goal: 缩短 launcher 到可交互游戏的启动时间，并消除可归因的卡顿/掉帧

## Intent

在已经通过稳定性 Proof 的基础上，找出启动慢、运行卡顿和掉帧的真实关键路径，
修复 launcher 能控制的原因，并让用户在整个启动过程中始终看到真实、可诊断的
当前阶段。核心不变量是：性能优化不能用关闭 Vulkan、mod、云同步或安全检查来换取，
也不能用动画或虚假百分比掩盖没有进展的工作。

## Grounded context

- Verified: 稳定性 baseline 为 commit `8176861`，其完整证据在
  `docs/STABILITY_HARDENING_PROOF.md`；最终 APK SHA-256 为
  `ce1d535a99291256916d5cdf76311d148a43d04157ac83be3aba6e1115bb0ecc`。
- Verified: 同一 ARM64 真机的 30 次 Vulkan warm-cache 冷启动均到达
  `game-ready`，最短 38,324 ms、平均 41,169 ms、最长 44,055 ms。
- Verified: shader warmup v7 处理 1,592 个 material 约需 156 秒，峰值 RSS
  约 1.94 GiB；内存压力路径会安全 deferred，但按需 shader 编译仍可能产生帧尖峰。
- Verified: `LauncherPatches.RunLauncherThenGame` 已有 launcher、cloud、warmup、
  `GameStartup` 和 `game-ready` 边界；`CloudSyncOverlay` 与
  `ShaderWarmupScreen` 只覆盖部分阶段，Android/native bootstrap 与 mod/game
  初始化尚无统一的用户可见进度契约。
- Verified: `tools/device-stability/capture.sh` 能采集 Android `gfxinfo`，但该指标
  是否完整覆盖 Godot/Vulkan Surface 尚未证明；性能 Goal 的第一项必须用受控帧阻塞
  验证观测源，不能直接把平均 FPS 当真值。
- Assumption: 当前连接的 ARM64 真机可作为主要 A/B reference device；不同 ROM、GPU
  和第三方 mod 的结果只能在相同配置下比较，不能外推为所有设备结论。

## Done state

- 从 Android process entry 到 `launcher-ready`，以及从 PLAY 到 `game-ready`，都有
  monotonic stage/span 记录。用户等待 PLAY 的时间单独标为 `user-wait`，不计入启动
  性能回归；每个阶段有唯一 owner、开始条件、正常/跳过/降级/失败终态、timeout 或
  watchdog 策略。
- 启动时始终有 KR/EN 双语的可见阶段 UI。总体使用阶段列表/当前阶段；只有存在真实
  `done/total` 或 byte count 时才显示 determinate progress，未知工作量显示
  indeterminate 状态。禁止按时间匀速增长、伪造 ETA 或把跨阶段权重包装成准确百分比。
- 性能 trace 能把长启动 span 和 frame-time spike 至少区分为 launcher main-thread、
  Godot/game main-thread、render/GPU、shader compile、GC/allocation、file I/O、network、
  mod 初始化及 Android scheduling/thermal；无法证明 owner 时标为 unknown，不猜测。
- 稀疏的 startup telemetry 和 bounded frame-spike ring buffer 不记录账户、token、
  存档正文、mod 名、文件路径、设备序列号或原始日志；普通运行不逐帧写盘，也不因
  instrumentation 自身制造可测卡顿。
- 在同一设备、branch、mod 集合、renderer、分辨率/刷新率和可比 thermal band 下，
  30-run returning-user A/B 的自动启动总 p50（process→launcher-ready 加
  PLAY→game-ready）相对 baseline 至少降低 10%，p95 不得恶化超过 5%，且 30/30
  到达 `game-ready`。
- 对首次派生缓存失效/warmup 路径，用户可在 30 秒内进入安全的按需编译路径，或
  PLAY→game-ready p50 相对 156 秒 warmup baseline 至少降低 30%；其后 5 分钟的
  shader-related long-frame density 不得比 baseline 恶化超过 10%。
- 在可重复的 launcher 交互和代表性游戏场景中，以 frame time 而非平均 FPS 验收。
  对已确认的 launcher-controlled 主导卡顿，p99 或超过 2×display-frame-budget 的
  frame density 至少改善 25%；无 mod、BaseLib 和一个普通受支持 mod 场景均不得
  出现超过 10% 的 p95/p99 回归。
- Vulkan、EN toggle、Safe Mode、事务更新、存档/云数据、mod 配置、package/signing
  identity 和现有 crash-loop recovery 不变；性能代码保持窄接口和 upstream 低冲突。

## Proof

- Run/check: 构造一个 debug-only、固定 100 ms 的单帧阻塞，并同时采集候选 Godot、
  Perfetto/FrameTimeline、SurfaceFlinger 或 Android 指标。
  Pass when: 选定的 canonical metric 在正确时间窗口检出该 spike，未注入对照不检出，
  且文档说明其覆盖与盲区。
- Run/check: 用 baseline commit/APK 与 final APK 在相同设备上交错执行 30 次
  warm-cache cold start；分别报告 process→launcher-ready、user-wait、
  PLAY→game-ready 和每个 stage 的 p50/p95/p99/max。
  Pass when: 达到 Done state 的 10% p50 改善、p95 guardrail 和 30/30 terminal。
- Run/check: 使用只重建派生缓存的 debug hook 重复 first-run/warmup 场景；不得卸载、
  clear app data 或触碰真实存档/mod/login。
  Pass when: 达到 30 秒可进入按需路径或 30% time-to-interactive 改善，并满足内存和
  后续 shader spike guardrail。
- Independent check: 每个代表性 frame 场景至少 3 个 120 秒 run，报告 frame-time
  p50/p95/p99/max、>1×/>2×/>3× refresh budget、>50/>100/>250 ms、连续慢帧和
  thermal/scheduler 状态；平均 FPS 只能作为辅助指标。
  Pass when: 受控卡顿可复现、owner 证据闭环、launcher-owned 修复达到 25% 改善，
  并且各 mod 配置不越过 10% regression guardrail。
- Run/check: instrumentation on/off A/B。
  Pass when: sparse telemetry 不增加可检测的长帧簇，CPU/RSS p50 不恶化超过 3%，
  release 构建不接受 debug-only stall/fault intents。
- Independent check: 遍历 native bootstrap、launcher、cloud、warmup、game startup、
  mod load、timeout/degraded/recovery 的 KR↔EN UI。
  Pass when: 每个阶段有真实终态，EN 无 launcher-authored Hangul、无不可操作截断，
  且任何 determinate value 都能追溯到真实工作量。
- Run/check: pinned Docker APK build、现有 stability/localization/compatibility audits、
  30 cold starts、HOME/resume、rotation、offline 和 Safe Mode focused regression。
  Pass when: 全部退出码为 0，无新增 Crash/ANR/LMK/黑屏，记录最终 APK SHA-256。
- Run/check: `git diff --check`、secret/private-artifact audit 和 upstream conflict review。
  Pass when: 仓库不含凭据、设备标识、原始 trace/log/screenshot、游戏文件或签名材料，
  且高变动 upstream 文件的修改有必要性说明。

## Scope and authority

- May read: 整个本地 fork、现有 stability proof、构建产物和本机/连接设备的性能系统
  状态，以及解决已确认工具/驱动语义所需的官方资料。
- May change: 与 startup stage/progress、低开销 telemetry、profiling harness、
  launcher/Godot/render/shader/mod 启动性能、测试和 proof 直接相关的本地 fork 文件。
- May exercise: pinned Docker 本地构建/签名、升级安装、启动/force-stop 本应用、
  debug-only stall/cache hook、可逆旋转/网络/renderer 对照和 bounded performance trace。
- Must preserve: 现有 mod/启用配置、存档、Steam 登录、云数据、语言设置、默认 Vulkan、
  package/signing identity、事务更新及所有 stability hardening 行为。
- Requires new authorization: push、开/合 PR、release、上传 APK/trace/log、卸载、
  clear app data、删除/移动真实 mod 或存档、制造真实双边云冲突、改变签名/默认 renderer，
  或在其他账户/设备上测试。

## Non-goals and invalid shortcuts

- 不承诺修复游戏本体、ROM/GPU driver 或任意第三方 mod 内部的性能 bug；但必须提供
  足够证据把它们与 launcher-controlled 路径区分开。
- 不通过全局禁用 mod、云同步、warmup、安全校验或 Vulkan 来改善数字。
- 不通过降低分辨率/画质、修改刷新率或只挑热缓存样本来制造不可比的提升。
- 不用平均 FPS 掩盖 frame-time 尖峰，不把 loading 动画流畅等同于后台工作有进展。
- 不先写“优化”再找证据；每项改动必须对应一个可重复 span/spike 和单一根因假设。

## Priorities and tradeoffs

1. 存档、凭据、mod、更新一致性和 crash-loop 安全
2. time-to-interactive 与无永久等待
3. p95/p99 frame time 和长帧簇
4. 真实、可操作的启动进度与诊断
5. 平均吞吐、warmup shader 覆盖率和电量
6. upstream 低冲突

当预热覆盖率与 time-to-interactive/内存/后续卡顿冲突时，用测得的总体验选择 bounded
warmup 或按需路径；当低延迟与数据一致性冲突时，保留一致性并优化其实现或清晰展示
真实阶段，不跳过安全边界。

## Unknowns and decision rules

- 先验证哪一种 Android/Godot 指标真正看到 Vulkan presents；`gfxinfo` 无法检出受控
  stall 时不得用它做 canonical frame gate。
- 先把 CPU/main-thread、GPU/render、shader、GC、I/O、network、mod 和 scheduler
  假设分别证伪；两次实验无新增信息后必须改变观测点或假设。
- thermal throttling、后台任务或动态刷新率超出可比 band 时丢弃该 A/B pair 并记录
  原因，不把它算成功或失败样本。
- third-party mod 只记录匿名/聚合 span；最后开始加载仍只是候选，不能自动禁用或
  声称确定归因。
- 若性能目标被已证明的游戏/driver 外部下限阻塞，当前 Goal 保持 partial/blocked，
  提交最小复现和下一项判别实验；解释原因不能替代 Done state 的量化改善。
- 无关问题记录到 performance proof 的 residual，不扩展本 Goal。

## Control loop and resumption

- Work unit: 一个“metric 验证或 baseline → 单一 root-cause hypothesis → 最小改动 →
  focused A/B → stability guardrail”的闭环，或一个“stage owner → UI/terminal →
  timeout/degraded test”的闭环。
- State: 使用原生 goal/plan 状态；汇总结果写入 `docs/PERFORMANCE_PROOF.md`，原始
  Perfetto trace、截图和设备日志只保存在仓库外私有缓存。
- Retry/budget: 同一假设最多两次无新增信息的实验；第三次前改变策略。大规模 30-run
  matrix 只在指标和 diff 稳定后执行。
- Stop when: 全部 Done state 与 Proof 通过；或缺少必要设备/工具/权限而无法继续，
  并已给出最小阻塞条件。

## Delivery

- Produce: 最小冲突实现、stage catalog、truthful progress UI、sanitized performance
  harness、`docs/PERFORMANCE_BASELINE.md`、`docs/PERFORMANCE_PROOF.md`、本地签名 APK
  与 SHA-256、按根因拆分的 PR-ready 本地 commits。
- Report: 启动关键路径、卡顿 owner、前后 p50/p95/p99、进度语义、稳定性 guardrail、
  instrumentation overhead、外部 residual 和 upstream 同步风险。
- Complete only when: Done state 的量化改善、可观测进度和全部适用 Proof 同时通过。
- Otherwise report: partial 或 blocked，附已有 A/B、被排除假设和继续所需的最小条件。
