# Goal: 为 launcher 提供完整简体中文，并用醒目的语言下拉选择器替代 EN 开关

## Intent

让韩语、英语和简体中文用户都能在启动页第一眼找到语言入口，并在 launcher、启动恢复、
云存档、Workshop、mod 管理和 Android 原生提示中获得一致、完整、可持久化的语言体验。
核心不变量是：新增中文不能破坏现有 KR/EN、本地数据或第三方原文，也不能为了省改动让
中文模式悄悄回退成韩文或英文；实现应继续保持与 upstream 的低冲突。

## Grounded context

- Verified: 当前语言 UI 是 `LanguageToggle`，位于 `LauncherView` 右侧 Console 标题栏，
  收起状态只显示 `EN · ON/OFF`，最小宽度 82、控件高度 28；它不在启动页的主要操作
  视觉层级。来源：`src/STS2Mobile/Launcher/Components/LanguageToggle.cs` 和
  `src/STS2Mobile/Launcher/LauncherView.cs`。
- Verified: 当前语言状态是二元模型。`LauncherLanguagePreference` 只持久化 `ko/en`，
  `Loc` 暴露 `IsKo/IsEnglish/SetEnglish`，Android Java 侧也只读取布尔 English 状态并
  使用 `nativeText(korean, english)`。来源：对应 C# 文件与
  `android/src/com/game/sts2launcher/modmanager/GodotApp.java`。
- Verified: 当前源树约有 140 个 `Loc.Tr(ko,en)` 调用和 10 个 Android
  `nativeText(ko,en)` 调用；legacy 韩文文案主要由集中式 `EnglishLocalization` 覆盖。
- Verified: `LocalizedTextPolicy` 和 `TextProvenance` 已区分 launcher 文案、含外部值的
  模板及第三方原文；mod 名、Workshop 内容、用户名、路径和外部错误文本不得被翻译。
- Verified: launcher root 已启用 Android system font fallback，可显示 CJK；现有完整
  localization audit 已集成到 pinned Docker APK build。来源：`LauncherView.cs`、
  `docs/LOCALIZATION_INVENTORY.md` 和 `docker/build-apk.sh`。
- Assumption: 下拉选项使用 `한국어`、`English`、`简体中文`，内部规范值使用 `ko`、
  `en`、`zh-Hans`；读取时兼容 `zh`、`zh-CN`、`zh-SG` 等既有/系统表示。

## Done state

- 启动页使用单选语言 dropdown/OptionButton 取代 `EN · ON/OFF` toggle，关闭状态始终
  显示当前语言名称。控件带不依赖当前语言也容易识别的语言/地球图标或标签，位于
  `StS2 Launcher` 主标题附近、登录区之前，在启动页出现时无需滚动即可看到。
- 选择器至少有 44dp 等效触控高度，宽度能完整显示 `简体中文`；popup、文字、焦点、
  hover/pressed 状态与现有主题一致。在手机、平板/折叠屏代表性 viewport 和旋转/恢复后
  不重叠、不截断、不跑出安全区域，Debug/Console 不再决定语言入口是否可见。
- 语言状态改为明确的三值类型/契约，而不是继续堆叠布尔值。选择 `한국어`、`English`、
  `简体中文` 后，当前 launcher 所有已挂载和随后动态创建的文案立即切换，无需重启；
  状态提示本身也使用新选择的语言。
- `launcher_language.cfg` 规范写入 `ko`、`en` 或 `zh-Hans`。升级时现有 `ko/en` 设置
  原样保留，不重置登录、云同步、mod 或 renderer 配置；未知/损坏值安全回退。
- 首次启动无已保存设置时，韩语系统选择韩语，明确的简体中文 locale（如
  `zh-Hans`、`zh_CN`、`zh_SG`）选择简体中文，其余选择英语。`zh-Hant`、`zh_TW`、
  `zh_HK`、`zh_MO` 不得被静默当成简体中文；用户仍可手动选择简体中文。
- 简体中文覆盖全部 launcher-owned 可见文案，包括登录、下载/更新、PLAY、状态/错误、
  云存档、备份/恢复、Workshop、SUBSCRIBED/DOWNLOADS、mod 兼容提示、Safe Mode、启动
  progress/recovery、Android native dialog/toast/overlay 及按钮、tooltip、placeholder。
- 中文文案是自然、明确的简体中文，保留 Steam、Workshop、mod、Vulkan、OpenGL、
  DLL 等必要产品/技术名；不得用整段英文/韩文回退、隐藏控件、空字符串、拼音或笼统
  占位符冒充已翻译。
- 动态模板能翻译 launcher-owned 部分，同时逐字保留用户名、mod/Workshop 标题与作者
  文本、存档名、路径、版本号和外部错误正文。语言往返 `ko → en → zh-Hans → ko` 不得
  累积翻译、损坏占位符或改写第三方内容。
- 集中式 catalog/adapter、审计和窄 UI mount point 继续作为 source of truth；避免为
  同一语言状态在大量高变动 controller 中增加分散分支，降低未来 upstream merge 冲突。

## Proof

- Run/check: 扩展 `tools/localization-audit` 及其 negative fixtures，清点 C#、Android
  Java/XML 的 KR/EN/zh-Hans 路径。
  Pass when: 每条 launcher-owned 可见文案都有非空简体中文结果；故意新增缺少中文的
  `Loc.Tr`、catalog entry、动态模板或 `nativeText` 会使审计失败；第三方原文 fixture
  在三种语言下均保持 byte-for-byte 不变。
- Run/check: 为语言值解析、旧 `ko/en` 迁移、`zh-Hans/zh_CN/zh_SG` 默认、
  `zh-Hant/zh_TW/zh_HK/zh_MO` 非简体默认、损坏配置回退和往返切换添加确定性测试。
  Pass when: 三值状态、持久化和 fallback 全部符合 Done state，且旧配置无需迁移写回
  就能正确读取。
- Independent check: 在真机从每种语言打开 dropdown，依次切换三种语言，遍历登录、
  更新、云存档、Workshop、mod 管理和 PLAY 前后的动态状态，再 force-stop/relaunch 和
  覆盖安装一次。
  Pass when: 当前值醒目可见、切换即时、重启/升级后仍保持选择，中文模式没有任何
  launcher-authored 韩文或未批准的整句英文残留。
- Independent check: 通过现有 debug/recovery 注入或等价安全 harness 展示 Android
  native renderer recovery、atlas rebuild、mod guard/Safe Mode 等启动前后提示。
  Pass when: 每个 native surface 都按同一 persisted language 显示简体中文，按钮可操作、
  无截断；不需要制造真实 crash、删缓存或破坏用户数据。
- Independent check: 对代表性手机和宽屏/折叠屏 viewport 截图并检查主标题区域、popup
  边界、44dp 等效触控尺寸、CJK glyph、长文换行和旋转/HOME-resume 后布局。
  Pass when: dropdown 无需滚动即可发现，关闭时完整显示当前语言，所有目标 viewport
  无遮挡、溢出、tofu 方框或不可点击项。
- Run/check: pinned Docker APK build、localization/stability/lifecycle/compatibility gates、
  `git diff --check` 和 upstream conflict review。
  Pass when: 全部退出码为 0，APK 正确签名安装，无新增 crash/ANR/黑屏，且证明文档记录
  最终 APK SHA-256、测试设备/viewport 与任何剩余的外部内容例外。

## Scope and authority

- May read: 整个本地 fork、现有 localization/stability proof、构建脚本及连接 Android
  设备上与本应用有关的可见 UI、package 状态和脱敏日志。
- May change: localization state/catalog/policy/audit、语言选择器与窄布局 mount point、
  launcher-owned C#/Java/XML 文案、相关测试与 `docs/LOCALIZATION_INVENTORY.md`/proof。
- May exercise: pinned 本地构建、覆盖安装、启动/force-stop 本应用、语言切换、旋转、
  HOME/resume 及现有非破坏性 debug/recovery UI 注入。
- Must preserve: package/signing identity、现有 KR/EN 文案与选择、登录与 Autofill、存档/
  云数据、mod 文件与启用状态、默认 Vulkan、更新和 crash-loop recovery 行为，以及全部
  user/mod/Workshop/external 原文。
- Requires new authorization: commit/push、开合 PR、release、上传 APK/截图/log、卸载、
  clear app data、删除或改写真实存档/mod、改变签名/package id 或测试真实破坏性故障。

## Non-goals and invalid shortcuts

- 不翻译游戏本体、Steam 网页、第三方 mod/Workshop 内容，也不承诺繁体中文支持。
- 不把整个 UI 交给在线机器翻译或运行时网络翻译；launcher 必须离线可用且译文可审查。
- 不通过把所有未知文案默认显示英语、隐藏韩文控件或只翻译首页来宣称“支持中文”。
- 不用国旗代表语言；语言入口应对地区和多语言用户保持明确。
- 不为减少初始 diff 继续扩展 `IsEnglish` 布尔分支，因为它会让第三种语言在 native、
  dynamic overlay 和未来文案中持续产生歧义。

## Priorities and tradeoffs

1. 现有用户数据、配置和第三方内容不被改写
2. 简体中文覆盖完整且语义正确
3. 语言入口醒目、可操作、布局稳定
4. KR/EN 行为与稳定性无回归
5. 集中式实现和 upstream 低冲突

当最小 diff 与完整三语契约冲突时，允许重构语言状态核心，但把高变动页面的接入收敛到
catalog、policy 和单一选择器 mount point；当短译文与准确性冲突时，保留准确语义并调整
布局/换行，不删减安全、数据覆盖或恢复警告。

## Unknowns and decision rules

- 第一工作单元先生成当前 launcher-owned 文案 inventory，并区分 exact、pattern、动态
  template 和 native surface；无法确定 provenance 的文本先追踪 owner，不自动翻译。
- 中文术语有多个合理译法时，以 launcher 内一致、Android/Steam 常见用语和操作后果
  清晰为准，并在 proof 中记录少量需要产品确认的术语；不因单个词阻塞其余覆盖。
- 若 Godot `OptionButton` popup 在目标 Android build 上存在尺寸/焦点问题，可实现同语义
  的自包含 popup selector，但必须保留单选、当前值可见、可访问触控和低冲突 mount point。
- 若没有第二台物理设备，可用受控 viewport/rotation 覆盖宽屏布局，并把缺少的真机矩阵
  标为 residual；至少一台 Android 真机的完整交互与 persistence proof 不可省略。
- 无关功能或翻译游戏/mod 的需求记录为 follow-up，不扩展本 Goal。

## Control loop and resumption

- Work unit: 一个“语言状态/持久化契约 → catalog/动态 provenance → 一个 UI/native
  surface → static audit → 真机切换”的闭环。
- State: 使用原生 goal/plan 状态；最终证据写入 `docs/SIMPLIFIED_CHINESE_PROOF.md`，
  截图和设备日志仅保存在仓库外临时目录，不提交设备序列号或私密内容。
- Retry/budget: 同一 UI 或 translation routing 失败两次且没有新增证据时，改变观测点或
  实现策略；全量真机遍历只在三值状态与静态 audit 稳定后执行。
- Stop when: 全部 Done state 与 Proof 通过；或缺少必要依赖、设备/权限、关键译文决策
  而无法继续，并已报告最小阻塞条件。

## Delivery

- Produce: 三值语言模型、醒目的语言 dropdown、完整简体中文 catalog/native copy、
  扩展后的 localization audit/fixtures、更新后的 inventory、
  `docs/SIMPLIFIED_CHINESE_PROOF.md`、签名 APK 与 SHA-256，以及低冲突说明。
- Report: 中文覆盖范围、dropdown 位置和 viewport 证据、旧设置迁移、native/dynamic
  验证、第三方内容保护、测试结果和剩余例外。
- Complete only when: 三语切换、完整简体中文、持久化/升级、native surface、布局和全部
  applicable regression Proof 同时通过。
- Otherwise report: partial 或 blocked，附缺失文案/surface、可复现证据和继续所需的
  最小条件；“大部分已翻译”不能标记完成。
