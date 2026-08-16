# Plan: Launcher crash/black-screen hardening and complete English coverage

本 Plan 执行 `GOAL_STABILITY_HARDENING.md`。每个 phase 必须形成独立证据和可回滚
提交；前一 gate 未通过时，不把后续压力测试当成替代证明。

## Phase 0 — Baseline and fault vocabulary

Status: completed

1. 从 `docs/STABILITY_PROOF.md` 的已验证 APK/commit 建立 baseline。
2. 列出已有启动阶段、planned restart、previous-exit、warmup marker、mod loader、
   depot/PCK/assembly 激活点、renderer 启动参数和 KR/EN text surface 的
   owner/call site。
3. 定义规范化终态：success、planned restart、user exit、crash、ANR、LMK、unknown。
4. 确认所有测试/日志字段不包含账户、凭据、存档正文、设备序列号或完整私有路径。

Gate:

- baseline APK 可重复构建或现有 hash 可验证；现有 focused tests 全绿。
- 每个后续 fault model 都映射到唯一 owner 和可观测 stage，不重复造状态源。

## Phase 1 — Durable attempt journal and recovery decision

Status: completed

1. 先写纯状态机测试，覆盖正常启动、计划重启、单次异常、相同阶段连续异常、不同
   阶段异常、配置变化、过期 attempt、并发/撕裂写入和 Android 7–10 降级。
2. 复用现有 previous-exit collector，新增最小原子 journal；只保存 schema、attempt ID、
   时间、规范化阶段、完成标记、相关配置 fingerprint 和无敏感信息的候选 mod ID。
3. 只有两次相同阶段且相关 fingerprint 相同的非计划退出才产生 recovery request。
4. 计划重启、正常 HOME/background 和用户主动退出不能累计 crash loop。
5. 把 stage 写入放在风险操作之前，把完成写入放在可验证成功之后。

Gate:

- torn write、重复回调和进程中止都不会生成矛盾状态。
- 一次偶发退出不触发 Safe Mode；两次同阶段异常稳定触发；成功启动清除计数。

Commit boundary: recovery journal + deterministic decision tests。

## Phase 2 — Adaptive shader warmup

Status: implementation and automated gate completed; cumulative signed-device gate is deferred to
Phase 7 so the user's installed game/mod/login state is upgraded only once.

1. 为 warmup 增加可注入的 memory-pressure provider；优先使用 Android
   `onTrimMemory`/系统可用内存和进程 RSS 的最小可靠组合，不在测试中依赖真实 LMK。
2. 明确 warmup 结果：completed、deferred-memory-pressure、failed-but-bypassed、
   interrupted；所有结果都必须完成 await，且只有需要 clean restart 时才重启。
3. 在 batch 之间检查压力。达到软阈值时释放当前资源、保存 deferred 原因并退出；
   剩余 shader 由正常运行时按需编译。
4. 保持当前最多 8 个 material 的 streaming 上限和 deterministic disposal。
5. 验证首次 deferred 后不会在每次启动重新进入相同高压 warmup。

Gate:

- 注入每一种 pressure level 都得到 bounded、唯一终态，无资源持有增长和永久 await。
- 真机正常内存路径仍完成；受控低预算路径在 LMK 前退出并进入游戏/恢复流。

Commit boundary: adaptive warmup + pressure bridge + focused tests。

## Phase 3 — Crash-loop Safe Mode and mod isolation

Status: in progress

1. 在加载任何第三方 mod 前消费 recovery request，显示原因、上次失败阶段和可逆操作。
2. Safe Mode 使用 session-only override：跳过可选 warmup、临时不加载第三方 mod；
   只有明确 cache-stage 证据时才提供派生缓存重建。禁止移动/重命名真实 mod 目录。
3. 在每个 mod 执行前记录 candidate，在初始化完成后记录 success；用 stable mod ID，
   不记录账户或用户路径。
4. UI 始终称其为“候选触发者”。提供本次排除候选和有限二分启用；用户明确确认前
   不持久改变启用集合。
5. Safe Mode 成功到达菜单后，显示恢复普通启动的入口；普通启动成功清除 crash loop。
6. 用测试 mod/fault injector 分别模拟 managed exception、hang、native-like abrupt exit
   和延迟崩溃，验证不会错误声称确定归因。

Gate:

- 连续两次模拟 mod-stage 退出后，第三次先进入可操作 recovery flow。
- 原 mod 数量、目录内容和启用配置 byte-for-byte 不变。
- Safe Mode 可到达菜单；恢复普通模式后受支持 mod 仍正常工作。

Commit boundary: one-shot Safe Mode + mod candidate journal/tests。

## Phase 4 — Transactional game update and interruption recovery

Status: pending

1. 建模完整版本元组：branch、Steam manifest/build、PCK identity、game assembly set、
   atlas/cache stamp；确定唯一 active marker 的提交点。
2. 在 download、verify、activation、process restart、assembly sync 和 cache staging
   边界加入 debug-only deterministic fault hooks。
3. 先以自动测试逐边界中止，再选择代表性边界在真机 force-stop；每次重启检查只会
   选择完整旧版本或完整新版本。
4. 所有 staging 清理在后台且可恢复；不得因清理失败删除 active 数据。
5. 在进入游戏前运行版本一致性/兼容审计；发现混合状态时回到可操作恢复 UI，不能
   继续黑屏启动。

Gate:

- 每个 fault point 的版本元组一致；没有 mixed DLL/PCK/manifest。
- public↔public-beta 正常切换和至少三个真机中断点通过，用户存档/mod/login 不变。

Commit boundary: update transaction guards + fault-injection tests。

## Phase 5 — Renderer compatibility recovery

Status: pending

1. 首先验证当前 APK/Godot build 是否真的包含可启动的 compatibility renderer。
2. renderer failure 判定只使用“首个可用帧之前的重复失败 + exit/stage 证据”；
   `onPause`/surface teardown 后的 `QueuePresentKHR` 必须被排除。
3. 若 capability 通过，提供一次性 renderer override 和清楚的 restart/restore UI；
   Vulkan 始终是默认，成功后可一键恢复。
4. 若 capability 不通过，不合入死选项；保留诊断、受影响设备采集步骤和明确 residual。

Gate:

- reference device 上默认 Vulkan 与一次性兼容模式都能达到 launcher/菜单，或已有
  可重复证据证明 fallback 不可用并从产品 UI 移除。
- 没有 affected/equivalent GPU 时不声称驱动问题 fixed。

Commit boundary: renderer decision/override only if capability gate passes。

## Phase 6 — Complete KR/EN localization coverage

Status: pending

1. 建立 launcher-authored 可见文字 inventory，覆盖 C#/Godot 和 Android Java/resource
   的 screen、dialog、native overlay、toast/alert、status、tooltip、placeholder、按钮、
   动态格式字符串和 Phase 1–5 新增的 recovery 文案。
2. 把韩语来源分成三类并纳入可审查 manifest：
   - launcher UI，必须提供准确英文；
   - 日志、注释和测试 fixture，不属于可见 UI；
   - mod 名、Workshop 标题/描述、用户名、存档名、文件名和外部错误正文，必须保持原文。
3. 为 static localization audit 先添加会因现有残留失败的 fixture/测试；任何新增未分类
   韩语 literal 或缺少英文 pair 的 launcher UI 文本都必须让测试失败。
4. 低冲突策略：旧的高变动 upstream 文案优先在集中 overlay 中补齐；新建或已独立
   修改的 UI 使用显式 `Loc.Tr(ko, en)`。不得为了翻译而批量重写无关 controller。
5. 扩展 runtime audit，检查 EN 模式下所有可见 text、tooltip 和 placeholder；未知的
   launcher-authored 韩语不能静默通过。外部字段必须携带 provenance，避免误翻译。
6. 脚本化打开主 launcher、Save Manager、Mod Manager、Workshop、branch picker、
   cloud conflict、update/download、错误/超时、Safe Mode 和 renderer recovery 状态，
   执行 KR→EN→KR→EN，并覆盖控件打开后产生的新动态文案。
7. 真机检查较长英文的换行、按钮尺寸、滚动和 fold/rotation 布局；危险操作的英文
   必须保留删除、覆盖、上传、下载、恢复和仅本次生效等语义。

Gate:

- static audit 中 launcher-authored 韩语的未分类/未翻译计数为 0；所有豁免有来源说明。
- runtime EN surface audit 的 Hangul 残留为 0，外部韩语内容不被改写。
- toggle round trip 对当前控件和之后产生的动态控件都通过，重启后 EN 偏好保持。
- 真机所有关键页面可读、可滚动、按钮可操作，没有因英文变长造成遮挡或截断。

Commit boundary: centralized English coverage + static/runtime localization tests。

## Phase 7 — Soak, regression and final proof

Status: pending

1. 扩展 `tools/device-stability/`，记录净化后的 attempt、stage、PID continuity、
   `ApplicationExitInfo`、menu/recovery terminal state 和 elapsed time。
2. 在 ARM64 真机执行：
   - 30 次 cold start → PLAY → 可交互菜单；
   - 30 次 HOME/resume；
   - 20 次旋转/配置恢复；
   - online、offline、重连、warmup 正常/压力退出；
   - 两次连续异常后的 Safe Mode；
   - no-mod、BaseLib、普通 mod、已知不兼容 mod；
   - public↔public-beta 与代表性更新中断；
   - EN 模式全 surface audit 与 KR↔EN round trip。
3. 每一失败先保留第一因果异常和最终 exit reason；修复后只重跑相关 focused row，
   最终 diff 稳定后再跑一次完整矩阵与 pinned Docker build。
4. 更新 failure matrix，新增 `docs/STABILITY_HARDENING_PROOF.md`，记录 APK hash、
   build/audit/test 结果、外部边界和 upstream conflict review。
5. 按 Phase/root cause 形成 PR-ready commits；不 push、开 PR、merge、release 或上传。

Final gate:

- 所有适用 automated、Docker APK、compatibility audit 和真机 Proof 通过。
- EN 模式 launcher-authored 可见韩语残留为 0，static/runtime localization gate 通过。
- 任何剩余问题都明确标为 external/unsupported 或 blocked，并有诊断/恢复路径。
- `git diff --check`、formatter、secret/private-artifact audit 通过，worktree 范围明确。
- 未完成真机矩阵、更新 fault injection、Safe Mode 恢复或 EN 全 surface Proof 时，
  不得标记 Goal complete。
