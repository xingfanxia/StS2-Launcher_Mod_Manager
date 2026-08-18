# Plan: Startup speed, frame pacing, and truthful progress

本 Plan 执行 `GOAL_PERFORMANCE_OBSERVABILITY.md`。顺序是先证明指标可靠，再建立 stage
contract，最后只优化有 owner 证据的 critical path/spike。稳定性 baseline commit
`8176861` 始终作为回归边界。

## Phase 0 — Reproducible baseline and trustworthy metrics

Status: complete — canonical frame metric and focused real-game baseline are
implemented. A dedicated physical-device harness now arms only debug probes,
refuses low-battery/release builds before mutation, and emits numeric-only
frame/RSS/thermal TSV. Its game A/B uses one debug APK with identical
instrumentation and a process-local switch that disables only the gameplay
performance fixes. The no-mod idle-combat and deterministic deck-cycle paths
now each have 3×120-second paired evidence. Three thermal-valid supported-mod
pairs and two independent 30-pair startup series are retained. The interleaved
runner itself is implemented and host-proven: it validates package/signers,
refuses locked devices, alternates arm order, restores candidate on failure,
and directly emits nearest-rank boundary/stage percentiles plus acceptance gates.

1. 固定 reference device、public branch、Vulkan、分辨率/刷新率、语言和三组 mod
   配置；记录 thermal/battery/scheduler 条件，不记录设备序列号、账户或 mod 名。
2. 把启动时间拆为 process→launcher-ready、user-wait、PLAY→game-ready，并为现有
   Android/native、launcher、cloud、warmup、game/mod startup 边界建立初始 owner map。
3. 用 debug-only 100 ms frame stall 验证 Godot monotonic frame telemetry、Perfetto
   FrameTimeline、SurfaceFlinger 和 `gfxinfo` 的真实覆盖；选一个 canonical source，
   其余只做辅助证据。
4. 对 baseline APK 运行 30 次 warm-cache cold start；对 launcher 交互、主菜单和
   代表性 supported-mod 场景各运行至少 3×120 秒，输出 sanitized percentile/histogram。
5. 建立 `docs/PERFORMANCE_BASELINE.md`，只提交汇总，不提交 raw trace/log/screenshot。

Gate:

- 受控 100 ms spike 在正确窗口被 canonical metric 检出，对照不误报。
- 报告 frame-time p50/p95/p99/max、长帧阈值/连续簇和 thermal validity，不以平均
  FPS 代替。
- baseline 30/30 到达 `game-ready`，运行条件足以支持 paired A/B。

Commit boundary: metric validation + sanitized baseline harness/proof。

## Phase 1 — Startup stage contract and low-overhead observability

Status: complete for the implementation/overhead gate — the independent closed
catalog, bounded monotonic native and managed timelines, numeric-only
persistence boundary, sparse progress, and
deterministic path/privacy tests are implemented. A reference-device normal
PLAY path closes at `game-ready` with visible settings/game/mod transitions;
3x120-second instrumentation persistence on/off CPU/RSS/frame A/B stays inside
the 3% gate, and exit retention has deterministic coverage.

1. 建立独立的 performance stage catalog，不改变 crash-recovery journal 的语义。
   每个 stage 定义 owner、start、`completed/skipped/degraded/failed/recovery` 终态、
   timeout 或 watchdog、known work units 和允许的下一阶段。
2. 覆盖 Android process/bootstrap、install recovery、cache/assembly sync、Godot/native
   boot、managed launcher、cloud、warmup、game settings、mod discovery/load、game
   startup 和 `game-ready`；`launcher-ready→PLAY` 单列为 `user-wait`。
3. 用 monotonic clock 记录 bounded span；跨 Java/C#/Godot 的 correlation id 只在内存
   和 app-private summary 中存在，不含设备/账户/路径/mod 名。
4. frame sampler 只保留 bounded ring buffer、spike event 和低频 histogram；禁止
   release 每帧 logcat/文件写入。详细 trace 必须显式开启并有自动停止时间。
5. deterministic tests 覆盖正常、skip、degraded、timeout、retry、teardown、重复回调
   和 stage 不合法倒退；进程退出也能保留最后一个 bounded performance stage。

Gate:

- 每条启动路径产生唯一、闭合 stage timeline，user-wait 不污染优化指标。
- instrumentation on/off A/B 的 CPU/RSS p50 回归不超过 3%，不产生新的长帧簇。
- telemetry schema 的 privacy fixture 拒绝账户、路径、控制字符和未分类动态字段。

Commit boundary: stage catalog + sparse telemetry + focused tests。

## Phase 2 — Truthful KR/EN startup progress UI

Status: complete on the reference device — the native splash-to-overlay handoff and PLAY-to-game
overlay use KR/EN stage copy, completed-stage markers, elapsed time, truthful
work units, and watchdog copy. The reference-device EN fast path proved that
the Android UI-thread surface keeps its stage plus separate stage/total clocks
moving while Godot's engine thread is occupied, and remains until a rendered
Godot game frame replaces it. Real EN/KR normal, watchdog, renderer-recovery,
and managed-recovery paths plus rotated EN layout pass. A release-inert, one-shot,
20-second maximum debug hold now makes the 15-second game-settings watchdog
visually reproducible without running the 156-second high-memory shader path.

1. Android native loading surface 显示 native stage，并在 Godot UI 可用后无闪黑地交接
   到统一 startup overlay；launcher 正常可交互页面不被无意义遮挡。
2. UI 主体使用完成阶段列表、当前阶段和 elapsed time。只有 download bytes、file
   count、cloud backup items、warmup scanned/total 等真实 work units 使用 determinate
   bar；未知 work 显示 indeterminate，不显示虚假 overall percentage/ETA。
3. 每个 stage 的 watchdog 到期后切换为“仍在进行/可诊断”的明确状态；真正 timeout
   必须进入 bounded degrade/retry/recovery terminal，不能让 bar 永远转。
4. 与 Safe Mode、renderer compatibility、offline fallback 和 update recovery 组合；
   错误状态提供可逆操作，不能吞异常或自动清用户数据。
5. 为所有 title/status/detail/timeout/action 提供 KR/EN pair；执行动态 KR→EN→KR→EN
   和长英文/旋转 layout audit。

Gate:

- 每一个 displayed percentage 都能由 trace 中的真实 `done/total` 重算。
- 所有 stage 都有 owner、watchdog/timeout 和 terminal；无 30 秒以上 silent surface。
- EN runtime audit 的 launcher-authored Hangul 为 0，无截断/遮挡；外部内容不改写。

Commit boundary: native/Godot progress handoff + localized stage UI tests。

## Phase 3 — Startup critical-path optimization

Status: complete — a first 30-run
candidate exposed previous-exit I/O serialized after Godot bootstrap. The query
now overlaps bootstrap behind an exact-once
Activity-ready gate. A later interleaved series falsified the apparent 11%
gain and localized a second root cause: the launcher progress surface completely
covered a roughly nine-second logo path. A scoped, fail-open prefix now selects
the game's existing no-logo path only during that covered startup and never
mutates the user's setting. The Goal baseline→exact-final 30-pair series passes:
total p50 `38.061 → 28.797 s` (-24.34%), p95 `38.695 → 29.702 s`
(-23.24%), and 60/60 `game-ready`. Under injected memory
pressure warmup deferred in 3 ms, performed a planned restart, and the 300-second
on-demand capture changed long-frame density by +5.05% with no >100 ms frame.

1. 从 paired baseline 的 critical path 逐一排序 Android file work、managed bootstrap、
   launcher construction、cloud cache、warmup、settings/game startup 和 mod load；一次
   只验证一个 root-cause hypothesis。
2. 优先移出 main thread 的非依赖 I/O/CPU work、消除重复 scan/load/hash、复用有明确
   lifetime 的 cache/connection，并只并行真正独立且仍满足事务/云一致性的工作。
3. 对首次 warmup 比较 bounded budget、用户可立即继续的按需路径和 shader spike
   成本；不能为缩短启动而把不可接受的编译尖峰推到前五分钟。
4. 每项 change 执行 focused A/B、内存/CPU/thermal guardrail 和当前 stability tests；
   无改善或产生 p95/p99 回归时回退该改动。
5. 只在指标稳定后执行最终 30-run returning-user 和派生缓存失效 matrix。

Gate:

- 自动启动总 p50 相对 baseline 至少改善 10%，p95 回归不超过 5%，30/30
  `game-ready`。
- first-run 在 30 秒内可进入按需路径，或 time-to-interactive p50 至少改善 30%，
  后续 shader long-frame density 回归不超过 10%。
- 没有跳过云/更新一致性、安全检查、默认 Vulkan 或用户选择来达标。

Commit boundary: one commit per measured startup root cause。

## Phase 4 — Runtime jank and frame-pacing fixes

Status: complete on the reference device — focused evidence identified main-thread scene/card-grid
and Canvas-pipeline spike owners; frame pacing, real-hand covering, one
run-tree-owned same-player deck-screen reuse, and upstream pause-cache priming
are implemented. A hidden cached deck is invalidated before the upstream pile
event can rebuild its full card grid; card upgrades use their separate upstream
notification and all subscriptions detach on invalidation or tree teardown.
The reversible obtain/remove/upgrade/downgrade device proof, next-open node
reconstruction, frame-time and cleanup gates pass on the reference device.
The first-map path is also traced but intentionally unchanged because synthetic
pre-open mutates combat, input, signal, audio, and one-time animation state.
Same-device 3×120 s no-mod idle, deck-cycle, and supported-mod A/B now pass.
The paired A/B is defined as alternating `game-baseline-120` and `game-120`
inside the same signed debug APK, so build and probe overhead are held constant.
A one-command settled-main-menu prescreen is now device-proven for full, Safe
Mode, and anonymous numeric mod partitions. It standardizes launch, capture,
sanitization, load-error classification, and TSV aggregation; it is only a fast
triage gate and does not replace the real-combat matrix. The same runner now
accepts combat-only `baseline,optimized` scenarios and preserves their order for
unattended same-APK repetitions. It also composes the same performance switch
with session-only Safe Mode as `baseline-safe,optimized-safe`, rejects Android
thermal status above 2 before device mutation, invalidates hotter results, and
force-stops every capture-owned process on success or failure. Its bounded
`deck-cycle` waits for the real interaction marker, then performs exactly five
open/close cycles so the deck-cache owner is measured rather than diluted by
an idle-combat window. Across three paired Safe Mode runs this reduced the
median >2× frame count from 12 to 9 (-25%), >50 ms frames from 6 to 4, and
>100 ms frames from 3 to 0 without p95/p99 or RSS regression.

1. 对每个 spike cluster 将 trace 与 Godot main thread、render/GPU queue、shader compile、
   GC/allocation、file/network I/O、mod initializer 和 Android scheduler/thermal 对齐。
2. 先修 launcher-owned 的同步阻塞、每帧 allocation/polling、无界 UI rebuild、重复
   resource load 或错误线程 marshal；保持输入/lifecycle 和数据一致性 contract。
3. 用 no-mod A/B 分离 game/renderer 基线，用 BaseLib/普通 mod A/B 记录匿名 mod-load
   span；候选慢 mod 只提示证据，不自动禁用或永久改配置。
4. Vulkan 保持默认。OpenGL 只作为诊断对照/既有一次性兼容模式，不因单次较快就
   变更产品默认；GPU/driver claim 需要等效受影响设备证据。
5. 每个修复重跑同一 3×120 秒场景和 instrumentation-overhead 对照。

Gate:

- 已确认 launcher-controlled 主导卡顿的 p99 或 >2×frame-budget density 至少改善
  25%，且没有新的 >100 ms 连续长帧簇。
- no-mod、BaseLib、普通 supported mod 的 p95/p99 回归均不超过 10%。
- external game/mod/driver residual 有最小复现、owner 证据和 Safe Mode/诊断路径，
  不写成 launcher 已修复。

Commit boundary: one commit per measured jank root cause。

## Phase 5 — Final signed APK, device matrix, and proof

Status: complete on the reference device — the final source passes the pinned
signed APK pipeline, prior-signer upgrade installation, compatibility and
localization audits, reversible hidden-deck mutation proof, both 30-pair startup
matrices, supported-mod pairs, KR/EN degraded/recovery traversal, warmup,
offline/reconnect, HOME/resume, and rotation. The final signed `0.4.2` APK is
installed, uses the unchanged signer/package/Vulkan defaults, and has SHA-256
`3bd7c050ff15f3e2df7bfa9155695640e7d8bcc4a5fb8d6dd32389147767aa4e`.

1. 对 final diff 运行 pinned Docker APK pipeline、MemberRef/interface、patch-target、
   localization、Java/Gradle/FMOD/signing 和全部 performance/stability tests。
2. 安装 final production-signed APK，执行 30 cold starts、HOME/resume、rotation、
   offline/reconnect、warmup/on-demand、Safe Mode 和三组 mod performance matrix。
3. 真机遍历所有 startup stages、timeout/degraded/recovery 与 KR/EN round trip；核对
   progress value 和 trace work units 一致。
4. 更新 `docs/PERFORMANCE_PROOF.md`，记录 baseline/final percentile、stage critical
   path、APK SHA-256、instrumentation overhead、外部 residual 和 upstream conflict
   review；raw evidence 留在仓库外。
5. 运行 `git diff --check`、formatter、secret/private-artifact scan，形成 PR-ready
   本地 diff；未经单独授权不 commit、push、开 PR、merge、release 或上传。

Final gate:

- `GOAL_PERFORMANCE_OBSERVABILITY.md` 的全部量化 Done state 和 Proof 通过。
- 进度 UI 真实、可终止、KR/EN 完整；无 Crash/ANR/LMK/黑屏或数据/配置回归。
- APK、raw trace/log/screenshot、设备/账户/mod 私有内容和签名材料均未进入 git。
- 未达到 startup、frame-time 或 stability guardrail 时不得标记 Goal complete。
