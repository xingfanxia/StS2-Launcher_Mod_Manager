# Goal: 让 launcher 可从崩溃/黑屏安全恢复，并提供完整英文界面

## Intent

在现有稳定性修复基础上继续降低闪退、永久黑屏和 crash loop 风险。
核心不变量是：launcher 无法保证任意第三方代码不破坏同进程内存，但一次异常退出
不得演变为持续黑屏、用户数据损坏或无法解释的重复崩溃；恢复机制必须保留用户的
mod、存档、凭据和正常 Vulkan 默认行为。选择 EN 后，launcher 自己生成的所有可见
文字必须有真正的英文版本，不能因新增流程或动态状态再次漏出韩语。

## Grounded context

- Verified: 第一轮 launcher-controlled 稳定性治理已经完成；根因、自动化、APK 和
  真机证据见 `docs/STABILITY_FAILURE_MATRIX.md` 与
  `docs/STABILITY_PROOF.md`（2026-08-16）。
- Verified: shader warmup v7 在当前 ARM64 真机完成，但耗时约 156 秒、峰值 RSS
  约 1.94 GiB；低内存设备仍可能在首次尝试中触发 LMK。
- Verified: Android 11+ 已能在下次启动读取 `ApplicationExitInfo`，并能区分计划重启；
  launcher 已有启动阶段日志、心跳和 mod 诊断基础。
- Verified: public v0.107.1 与 public-beta v0.111.0 的正常分支切换、PCK 缓存
  重建和 assembly 同步已通过真机验证。
- Verified: 第三方 mod、Harmony/MonoMod 或 Godot interop 可能让同一进程进入
  native 不可信状态；发生内存破坏后不能依靠 managed `try/catch` 原地恢复。
- Verified: 当前 EN overlay 由 `Loc`、`EnglishLocalization` 和
  `LocalizedTextRegistry` 组成；未命中的韩语翻译会原样返回，registry 主要覆盖
  `Label`、`Button` 和 `LineEdit`。用户已在 EN 模式观察到韩语残留，现有机制缺少
  “所有 launcher-authored 可见文字均已翻译”的静态和运行时 gate。
- Assumption: 当前 Godot Android 构建可能支持一个可用的 compatibility renderer；
  必须先用实际 APK 验证，若不成立则不得提供虚假的 renderer fallback。

## Done state

- Shader warmup 能响应可注入的 Android 内存压力和进程内存预算；压力升高时安全
  停止剩余预热并转为运行时按需编译，不等待 LMK，也不会在下次启动重复 crash loop。
- 每次启动都有最小、无敏感信息的 durable attempt journal。它能区分计划重启、
  正常结束、用户离开、crash、ANR、LMK 和未知异常退出，并记录最后完成的启动阶段。
- 连续两次在相同规范化阶段、相同相关配置下异常退出时，下次启动进入明确的
  recovery flow；单次偶发退出不会自动改写用户配置。
- Safe Mode 只作用于当前启动：不移动、不重命名、不删除 mod，不清空存档或凭据。
  它至少允许跳过可选 warmup、临时不加载第三方 mod，并只在证据指向派生缓存时
  请求重建；退出 Safe Mode 后原配置仍完整。
- Mod 隔离记录“最后开始加载”和“最后成功完成”的 mod/阶段。异常退出后只把它
  表述为候选触发者，不把时间相关性冒充确定归因；用户可临时排除候选 mod 或使用
  有限的二分启用流程恢复到可工作集合。
- EN 模式覆盖 launcher 的启动、登录、下载、更新、存档管理、mod 管理、Workshop、
  所有 picker/dialog/tooltip/status/error、Android native overlay 和新增 recovery UI。
  其中 launcher 自己生成的可见文字不得含 Hangul；mod 名、Workshop 标题/描述、
  用户名、存档名、文件名和外部错误正文等用户/第三方内容保持原文。
- KR↔EN toggle 对已打开控件和之后产生的动态文字都立即生效并持久化。每个
  launcher-authored 韩语字符串必须有明确英文 pair、集中映射或经过说明的非 UI
  分类；EN 模式不得静默接受未翻译的 launcher 文本。
- 游戏下载、验证、激活、PCK/assembly 同步的每个事务边界都有故障注入证明。
  任意受控中断后只能继续使用上一套完整版本或恢复到下一套完整版本，不能启动
  PCK、manifest 和 DLL 混合的半更新状态。
- Vulkan 保持默认。只有真正的“首个可用帧之前重复渲染失败”才能建议一次性兼容
  模式；后台化后的 surface teardown/`QueuePresentKHR` 不得触发此建议。若当前构建
  没有经验证的可用 renderer fallback，则保留诊断和手动恢复，不伪装成已修复。
- 在真实 ARM64 设备上完成重复冷启动、PLAY、前后台、旋转、断网恢复、受控进程
  终止、Safe Mode 和 warmup 压力矩阵；没有 launcher-controlled Crash/ANR/LMK、
  永久 wait 或无法退出的黑屏。
- 改动保持窄接口、可回滚、可单独 cherry-pick；不重写 launcher 架构，不破坏
  upstream easy-update、EN toggle、package identity、升级安装或现有存档布局。

## Proof

- Run/check: 状态机、warmup 压力、attempt journal、Safe Mode、mod 候选归因、
  更新事务和 renderer 决策均有 deterministic focused tests。
  Pass when: 每个测试先能复现旧风险，并对正常、异常、重复、过期和竞态路径给出
  唯一终态；测试进程退出码为 0。
- Run/check: 对下载、验证、激活、assembly 同步和缓存切换的每个已命名边界执行
  fault injection，包括进程终止后重新进入。
  Pass when: 重启后版本元组 `{branch, manifest, PCK, game assemblies}` 全部属于旧版
  或全部属于新版；兼容审计退出码为 0，且任何 staging 数据都可继续或安全回收。
- Run/check: 按 `docker/README.md` 使用 pinned Docker toolchain 构建实际 APK。
  Pass when: 所有现有及新增测试、MemberRef/interface audit、patch-target audit、
  Java/Gradle/FMOD/签名检查通过，构建退出码为 0，并记录 APK SHA-256。
- Independent check: 在 ARM64 真机至少完成 30 次冷启动到可交互菜单、30 次
  HOME/resume、20 次旋转/配置恢复，并覆盖 warmup 内存压力、离线/重连、连续两次
  模拟异常退出后的 Safe Mode、普通 mod、BaseLib 和已知不兼容 mod。
  Pass when: `ApplicationExitInfo` 与系统日志中没有未解释的 Crash/ANR/LMK；每次
  启动要么到达菜单，要么进入有原因、有操作的 recovery UI。
- Independent check: compatibility renderer capability 使用真实 APK 验证。
  Pass when: 仅在它能稳定到达 launcher/菜单时才展示该选项；没有问题 GPU 时只能
  证明机制可用，不能声称某个 GPU 驱动问题已修复。
- Run/check: static localization audit 枚举 C#、Java 和 Android resource 中所有韩语
  string literal，并区分 launcher UI、日志/注释和外部内容。
  Pass when: 所有 launcher-authored 可见文字都有英文对应；豁免项逐条说明原因，
  新增未分类或未翻译字符串会让测试失败。
- Independent check: EN 模式遍历每个 launcher screen/dialog/native overlay 的可见
  text、tooltip、placeholder、按钮和动态 status，并执行 KR→EN→KR→EN round trip。
  Pass when: launcher-authored 可见文字没有 Hangul、切换后当前/未来控件均更新、
  英文没有不可操作的截断/遮挡，同时韩语外部内容保持原样。
- Run/check: `git diff --check`、修改文件 formatter 和最终 worktree/secret audit。
  Pass when: 检查退出码为 0，仓库中没有凭据、账户名、设备序列号、私有日志、
  游戏文件、FMOD 或签名材料。

## Scope and authority

- May read: 整个本地仓库、第一轮稳定性证据、公开 upstream issues/commits、本地
  Android/Gradle/Godot/.NET 官方工具输出，以及已连接测试设备的只读系统状态。
- May change: 与 startup recovery、warmup、mod 加载、更新事务、renderer 选择、
  launcher KR/EN localization、Android lifecycle、诊断、测试和证明直接相关的
  本地 fork 文件。
- May exercise: 构建/签名本地 APK、升级安装、启动/force-stop 本应用、可逆网络与
  旋转测试、debug-only fault injection，以及在不修改真实存档内容的前提下运行测试。
- Must preserve: 用户现有 mod 目录和启用配置、存档、Steam 登录、语言设置、EN
  toggle、package/signing identity、默认 Vulkan、云数据和升级兼容性。
- Requires new authorization: push、开/合 PR、release、上传 APK/日志、卸载应用、
  清除 app data、删除或移动真实 mod/存档、制造双边真实云存档冲突、修改签名身份，
  或在其他设备/账户上执行测试。

## Non-goals and invalid shortcuts

- 不承诺修复任意第三方 mod、ROM、GPU 驱动、Godot 或游戏本体的内部 bug。
- 不通过全局禁用 mod、BaseLib、warmup、云同步或 Vulkan 来制造“稳定”。
- 不在一次异常退出后永久改写用户 mod 配置，也不依据“最后加载”自动断言某 mod
  有罪。
- 不翻译或改写 mod/Workshop 作者提供的名称、描述、用户生成内容和文件名；它们含
  韩语不属于 launcher 英文本地化残留。
- 不用删除韩语、显示空字符串、机器音译或笼统的英文占位符来让 Hangul audit 通过；
  英文必须保留原操作含义，尤其是删除、覆盖、云同步和恢复等高风险提示。
- 不用固定延迟、无限重试、吞掉所有异常、自动清空缓存/存档或强制杀进程掩盖
  永久 wait。
- 不把测试数量本身当完成；每个压力测试必须对应明确故障模型和可判定结果。

## Priorities and tradeoffs

1. 存档、凭据、mod 和用户配置安全
2. 防止重复 Crash/ANR/LMK 和永久黑屏
3. 明确诊断、一次性恢复和可逆隔离
4. 正确的游戏版本、PCK 和 assembly 一致性
5. 完整、准确且可操作的 EN launcher 文案
6. upstream 低冲突与可回滚性
7. 首次启动耗时和 warmup 覆盖率

当 shader 覆盖率与内存安全冲突时，提前停止可选 warmup并按需编译；当自动恢复
与误伤用户配置冲突时，只做一次性 session override，并让用户明确选择持久变更。

## Unknowns and decision rules

- Renderer fallback 是否真实可用必须先实验；若失败，删除/不合入该选项，保留证据
  和下一项可区分测试，不把它标成 fixed。
- `ApplicationExitInfo` 不可用的 Android 7–10 只允许使用 bounded logcat/journal
  证据，并明确能力差异。
- 真实第三方 native corruption 若没有最小复现集合，只实现候选归因、安全启动和
  隔离工具，状态保持 external/unsupported。
- 若 fault injection 暴露现有更新流程无法小范围事务化，先给出最小迁移/回滚设计
  和数据安全证明；不要直接重写下载器。
- 遇到包含韩语的动态文本时，先按来源分类；launcher 模板必须翻译，外部/用户字段
  必须原样嵌入英文模板。来源无法可靠区分时，先补 provenance，而不是全局替换。
- 同一实验连续两次没有增加信息量时，改变假设、注入点或观测方式。
- 出现无关问题时记录到 proof 的 residual section，不扩展本 Goal。

## Control loop and resumption

- Work unit: 一个故障类的“旧风险复现 → 最小修复 → focused test → APK/设备证据”，
  或一个 UI surface 的“韩语 inventory → 英文 pair → static/runtime audit → 真机检查”。
- State: 使用原生 goal/plan 状态；`PLAN_STABILITY_HARDENING.md` 是阶段和 proof gate，
  不是自动生成日志。长日志与私有设备证据继续保存在仓库外缓存目录。
- Retry/budget: 每个假设最多两次无新增信息的同类实验；第三次前必须改变策略。
- Stop when: 全部非条件 Done state 与 Proof 通过，renderer 条件能力得到诚实结论，
  且剩余项都已证明为 external/unsupported 并有恢复路径；或缺少必要设备、私有依赖
  或授权而无法继续。

## Delivery

- Produce: 最小冲突实现、focused tests、完整 localization inventory/audit、更新后的
  failure matrix、
  `docs/STABILITY_HARDENING_PROOF.md`、本地签名 APK 与 SHA-256、净化后的真机矩阵、
  按故障类拆分的 PR-ready commits。
- Report: 已确认根因、已修复边界、mod recovery 结果、EN 覆盖结果、测试与真机结果、
  renderer 能力结论、外部/不支持情况、残余风险、提交边界和 upstream 同步风险。
- Complete only when: Done state 和全部适用 Proof 通过，且没有把第三方/GPU 未验证
  风险写成已修复。
- Otherwise report: partial 或 blocked，附已有证据和继续所需的最小设备、依赖、权限
  或用户决定。
