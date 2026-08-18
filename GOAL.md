# Goal: 系统定位并修复 StS2 Launcher 的闪退、黑屏与无响应故障

## Intent

对本仓库做一次证据驱动的稳定性治理。

目标不是用超时、吞异常或禁用功能掩盖黑屏，而是：

1. 区分不同根因但表象相同的故障。
2. 修复所有有证据支持、且由 launcher 可控制的相关问题。
3. 对设备驱动、第三方 mod 等 launcher 无法根治的问题提供检测、隔离、恢复或明确诊断。
4. 保持与 `iunius612/StS2-Launcher_Mod_Manager` upstream 的 minimal future conflict，便于持续同步。

## Grounded context

- Verified: 游戏升级会改变内部接口、方法签名和 async IL 结构；launcher 直接引用并 Harmony patch 这些内部实现，因此存在结构性的版本错配风险。
- Verified: 游戏 v0.111 给 `IModManagerFileIo` 增加成员后，旧实现发生 `TypeLoadException`，表现为 Play 后黑屏；见 [issue #86](https://github.com/iunius612/StS2-Launcher_Mod_Manager/issues/86)。
- Verified: BaseLib、MonoMod/Harmony 动态 IL 和 Android Mono 曾导致 Godot `StringName` 内存破坏。当前 issue #87 日志再次出现大量 `BUG: Unreferenced static string` 后进程退出；见 [issue #87](https://github.com/iunius612/StS2-Launcher_Mod_Manager/issues/87)。
- Verified: 某些 Android ROM 返回 Java Unicode-extension locale，导致 `CultureInfo` 初始化失败及永久黑屏；现有 `PlatformPatches.SanitizeLocale` 是已验证修复。
- Verified: 部分 Adreno/Vulkan 设备存在渲染或 surface 生命周期问题；但应用切后台后的 `QueuePresentKHR` 错误也可能只是结果，不能仅凭日志尾部认定为根因。
- Verified: 启动流程包含主线程上的文件复制/缓存清理、云同步等待、全资源 shader warmup 和应用重启；这些路径存在 ANR、长时间黑屏或永久等待风险。
- Verified: 当前日志由应用自身进程捕获，native crash 时往往无法写入最后的 fatal/tombstone，因此“日志没有异常”不能证明进程正常结束。
- Evidence corpus: a repository-external private investigation cache.
- Relevant code:
  - `android/src/com/game/sts2launcher/modmanager/GodotApp.java`
  - `src/STS2Mobile/ModEntry.cs`
  - `src/STS2Mobile/Patches/LauncherPatches.cs`
  - `src/STS2Mobile/Patches/BaseLibCompatPatches.cs`
  - `src/STS2Mobile/Patches/ExternalModsFileIo.cs`
  - `src/STS2Mobile/Patches/PlatformPatches.cs`
  - `src/STS2Mobile/Patches/ModExceptionAttributionPatches.cs`
  - `src/STS2Mobile/Launcher/ShaderWarmupScreen.cs`
  - `src/STS2Mobile/Launcher/CloudSyncOverlay.cs`
  - `tools/memberref-audit/`

## Done state

- 建立一份按启动阶段和根因分类的 failure matrix，至少覆盖：
  - launcher 出现前立即黑屏/ANR
  - launcher 页面渲染异常
  - Play 后、游戏启动前黑屏
  - shader warmup 卡死或重启失败
  - 游戏升级后的 assembly/PCK/API/IL 错配
  - 云同步、离线和网络超时
  - BaseLib、Harmony、MonoMod及第三方 mod 引起的 managed/native crash
  - Vulkan surface、切后台/恢复和设备驱动问题
  - stale cache、locale、TMPDIR、FMOD、Steamworks native dependency
- 每项必须标为：confirmed、reproduced、ruled out、external/unsupported 或 instrumented-awaiting-evidence。
- 所有 confirmed/reproduced 且 launcher 可控制的问题均已得到最小范围修复，并有对应回归测试。
- 不可根治的问题不得继续表现为无法解释的永久黑屏：应尽可能提供阶段信息、恢复路径、安全 fallback、隔离建议或下次启动时的明确诊断。
- 启动异步流程不存在永远无法完成的 Task、丢失 completion、依赖 `Runtime.exit()` 才能结束 await，或无上限的 UI-thread blocking。
- 对支持的旧/新游戏 DLL 运行静态兼容审计；已知接口或 MemberRef 错配能在构建/启动前被发现，而不是等到用户看到黑屏。
- 本地 pinned Docker 环境能够成功生成 APK。
- 真机完成关键路径验证；如果没有可用真机，目标只能交付为 partial，不得宣称稳定性修复全部完成。
- 改动保持集中、可回滚、低冲突，不进行与稳定性无关的 UI 或架构重写。

## Proof

### Baseline evidence

- 保存每个根因的最小复现步骤、关键日志和失败阶段。
- 对日志中的第一处因果异常和最终进程状态分别分析。
- 明确区分真正的初始 Vulkan failure 与 Activity 后台化后正常出现的 surface teardown。

### Automated checks

- 为新增或修改的状态机、locale/cache/version 检查、超时及错误分类添加针对性测试。
- 运行所有仓库现有测试、`git diff --check` 和相关构建。
- 使用 `tools/memberref-audit` 对所有可获得的受支持 `sts2.dll` 版本审计；退出码必须为 0，或有经过验证的动态兼容策略。
- 对反射/Harmony patch 点增加独立审计，因为 MemberRef audit 不覆盖字符串反射和目标 IL 形状。

### APK build

- 按 `docker/README.md` 使用 pinned Docker toolchain 和私有 `/deps` 输入完成 APK 构建。
- 构建必须退出码 0，产出 APK 和 SHA-256。
- 不提交或上传游戏、FMOD、签名材料等私有依赖。

### Device matrix

至少验证：

- fresh install 与已有缓存升级安装
- 无 mod 启动
- BaseLib 启动
- 一个受支持的普通 mod
- 已知不兼容 mod 的安全失败行为
- 在线、离线、慢网和云同步冲突
- 首次 shader warmup、warm cache 再启动
- 前后台切换、旋转/配置变化和重新进入
- 游戏文件更新、分支切换及 stale assembly/cache
- 至少一台 ARM64 Android 真机；GPU/Vulkan 修复若涉及设备差异，需要问题设备或等效指纹验证

### Regression

- locale、TMPDIR、FMOD、external mods、cloud save 和现有 launcher 功能继续工作。
- 不能通过全局禁用 BaseLib、所有 mod、云同步、Vulkan 或异常日志来让测试“通过”。

## Scope and authority

- May read:
  - 整个本地 repository、git history、现有缓存日志
  - upstream 和原始 launcher 的公开 issues、PR、commits、releases
  - Godot、Android、.NET、Harmony/MonoMod 的官方源码和文档
- May change:
  - 本地 fork 中与诊断、兼容、启动生命周期、错误恢复、测试和构建检查直接相关的文件
  - 可创建独立、集中的兼容/诊断组件，避免散布 conditional hacks
  - 可产生本地 APK、日志、测试报告和 PR-ready commits
- Must preserve:
  - 用户已有改动和 EN language toggle
  - package identity、存档、Steam 凭据和升级安装兼容性
  - upstream easy-update 能力
  - 私有构建依赖与签名材料不得进入 git
- Requires new authorization:
  - push、开 PR、merge、release、上传 APK
  - 删除用户数据、清空真机 app data 或卸载 app
  - 修改签名身份或公开任何私有二进制

## Non-goals and invalid shortcuts

- 不承诺消灭第三方 mod、ROM、GPU 驱动或游戏本体自身的所有 bug。
- 不把“加一个固定延迟”“吞掉所有异常”“无限重试”“自动删缓存/存档”视为修复。
- 不根据单条 `QueuePresentKHR`、最后一条日志或 issue 标题直接归因。
- 不以禁用所有 mod、BaseLib、云同步或强制单一 renderer 作为默认解决方案。
- 不为追求代码整洁进行大规模无关重构。
- 不将无法复现的问题标记为 fixed；应增加低开销、可关闭且不泄露敏感数据的诊断证据。

## Priorities and tradeoffs

1. 存档、凭据和用户数据安全
2. 可验证的根因修复
3. 避免 native crash、ANR 和永久黑屏
4. 游戏版本及设备兼容性
5. 与 upstream 的低冲突
6. 性能和启动体验

发生冲突时，优先保护用户数据和正确诊断；不要用高风险自动恢复换取表面上的启动成功。

## Unknowns and decision rules

- 每个改动前先提出可证伪假设，并寻找能区分竞争根因的证据。
- 若缺少真机或特定 GPU，先完成其余可验证工作，生成测试 APK和精确采集步骤；保持 partial 状态。
- 若问题只能在第三方 mod 中解决，证明边界并在 launcher 侧实现合理隔离/诊断，不复制或重写整个 mod。
- 若需要修改自定义 Godot engine，先证明 Java/C# 层无法安全处理，再将 engine 改动独立成 commit。
- 若修复要求破坏旧游戏版本兼容，优先使用 runtime capability detection；无法兼容时明确支持矩阵并停止静默启动。
- 发现无关问题时记录，不扩展目标。
- 连续两次实验没有增加信息量时，必须改变假设或观测方式，不能继续重复同一尝试。

## Control loop and resumption

- Work unit: 一个明确的 failure class，包括复现、根因、最小修复、自动测试和设备验证。
- State: 优先使用原生 goal/plan 状态；如跨 session 信息开始漂移，可创建未提交的调查 ledger，记录 hypothesis、evidence、decision、verification 和 residual risk。
- 每完成一个 failure class 后重新运行启动、构建和兼容回归。
- 修复之间保持独立 commits，便于 bisect、revert 和 upstream cherry-pick。
- Stop when:
  - 所有 confirmed launcher-controlled 类别通过 proof；或
  - 缺少必须的真机、私有依赖或授权，无法继续验证；或
  - 剩余问题均已证明属于外部组件并具备足够的诊断与缓解措施。

## Delivery

Produce:

- 根因与症状 failure matrix
- 已实现的最小冲突修复
- 针对性自动测试
- 本地构建的 APK 与 SHA-256
- 真机验证记录
- PR-ready、按根因拆分的 commits
- 简短的 upstream 同步风险说明

Report:

- confirmed root causes
- ruled-out hypotheses
- changed files and commit boundaries
- exact verification commands and results
- unsupported/external cases
- remaining risks and next discriminating test

Complete only when done state and proof pass。若缺少真机验证、构建依赖或关键复现证据，必须报告 partial/blocked，不能用“代码看起来正确”代替完成。
