using System;
using System.Collections.Generic;

namespace STS2Mobile.Launcher;

internal enum StartupStageId
{
    AndroidProcess = 1,
    InstallRecovery = 2,
    CacheSync = 3,
    AssemblySync = 4,
    GodotBootstrap = 5,
    LauncherCreation = 6,
    LauncherReady = 7,
    RecoveryChoice = 8,
    UserWait = 9,
    CloudSync = 10,
    ShaderWarmup = 11,
    GameSettings = 12,
    GameStartup = 13,
    ModDiscovery = 14,
    ModLoad = 15,
    GameReady = 16,
}

internal enum StartupStageOwner
{
    Android,
    Installer,
    GodotNative,
    Launcher,
    Cloud,
    ShaderWarmup,
    Game,
    ModLoader,
}

internal enum StartupProgressKind
{
    Indeterminate,
    ItemsWhenKnown,
    BytesWhenKnown,
}

internal enum StartupWatchdogPolicy
{
    NoneForUserWait,
    DiagnoseAndContinue,
    DegradeAndContinue,
    RecoveryRequired,
}

[Flags]
internal enum StartupTerminalSet
{
    None = 0,
    Completed = 1 << 0,
    Skipped = 1 << 1,
    Degraded = 1 << 2,
    Failed = 1 << 3,
    Recovery = 1 << 4,
}

internal enum StartupStageTerminal
{
    Completed = 1,
    Skipped = 2,
    Degraded = 3,
    Failed = 4,
    Recovery = 5,
}

internal sealed record StartupStageDefinition(
    StartupStageId Id,
    StartupStageOwner Owner,
    string TitleKo,
    string TitleEn,
    string TitleZh,
    string WatchdogKo,
    string WatchdogEn,
    string WatchdogZh,
    StartupProgressKind ProgressKind,
    long WatchdogAfterUsec,
    StartupWatchdogPolicy WatchdogPolicy,
    StartupTerminalSet AllowedTerminals,
    bool CanStartTimeline,
    params StartupStageId[] AllowedNext
)
{
    internal bool Allows(StartupStageTerminal terminal) =>
        (AllowedTerminals & ToSet(terminal)) != 0;

    private static StartupTerminalSet ToSet(StartupStageTerminal terminal) =>
        terminal switch
        {
            StartupStageTerminal.Completed => StartupTerminalSet.Completed,
            StartupStageTerminal.Skipped => StartupTerminalSet.Skipped,
            StartupStageTerminal.Degraded => StartupTerminalSet.Degraded,
            StartupStageTerminal.Failed => StartupTerminalSet.Failed,
            StartupStageTerminal.Recovery => StartupTerminalSet.Recovery,
            _ => StartupTerminalSet.None,
        };
}

// One source of truth for stable stage ids, UI copy, owners, progress semantics,
// watchdogs, terminal outcomes, and legal control-flow edges. Recovery journaling
// intentionally remains separate: its durable stage names answer crash-loop
// safety, while this catalog answers performance and visible progress.
internal static class StartupStageCatalog
{
    private const long Second = 1_000_000;
    private const StartupTerminalSet StandardTerminals =
        StartupTerminalSet.Completed
        | StartupTerminalSet.Skipped
        | StartupTerminalSet.Degraded
        | StartupTerminalSet.Failed
        | StartupTerminalSet.Recovery;

    private static readonly StartupStageDefinition[] Definitions =
    {
        Stage(
            StartupStageId.AndroidProcess,
            StartupStageOwner.Android,
            "앱 프로세스 시작 중",
            "Starting app process",
            "正在启动应用进程",
            "Android 시작 상태를 확인하는 중입니다",
            "Still checking Android startup",
            "正在检查 Android 启动状态",
            StartupProgressKind.Indeterminate,
            15,
            StartupWatchdogPolicy.RecoveryRequired,
            StandardTerminals & ~StartupTerminalSet.Skipped,
            true,
            StartupStageId.InstallRecovery,
            StartupStageId.CacheSync,
            StartupStageId.AssemblySync,
            StartupStageId.GodotBootstrap
        ),
        Stage(
            StartupStageId.InstallRecovery,
            StartupStageOwner.Installer,
            "중단된 업데이트 복구 중",
            "Recovering interrupted update",
            "正在恢复中断的更新",
            "설치 파일의 일관성을 확인하는 중입니다",
            "Still verifying install consistency",
            "正在验证安装文件完整性",
            StartupProgressKind.ItemsWhenKnown,
            60,
            StartupWatchdogPolicy.RecoveryRequired,
            StandardTerminals,
            false,
            StartupStageId.CacheSync,
            StartupStageId.AssemblySync,
            StartupStageId.GodotBootstrap
        ),
        Stage(
            StartupStageId.CacheSync,
            StartupStageOwner.Installer,
            "모바일 캐시 준비 중",
            "Preparing mobile cache",
            "正在准备移动端缓存",
            "캐시 작업이 계속 진행 중입니다",
            "Cache work is still in progress",
            "缓存处理仍在进行",
            StartupProgressKind.ItemsWhenKnown,
            60,
            StartupWatchdogPolicy.DegradeAndContinue,
            StandardTerminals,
            false,
            StartupStageId.CacheSync,
            StartupStageId.AssemblySync,
            StartupStageId.GodotBootstrap
        ),
        Stage(
            StartupStageId.AssemblySync,
            StartupStageOwner.Installer,
            "게임 어셈블리 준비 중",
            "Preparing game assemblies",
            "正在准备游戏程序集",
            "게임 코드 준비가 계속 진행 중입니다",
            "Game code preparation is still in progress",
            "游戏代码准备仍在进行",
            StartupProgressKind.ItemsWhenKnown,
            45,
            StartupWatchdogPolicy.RecoveryRequired,
            StandardTerminals,
            false,
            StartupStageId.AssemblySync,
            StartupStageId.GodotBootstrap
        ),
        Stage(
            StartupStageId.GodotBootstrap,
            StartupStageOwner.GodotNative,
            "게임 엔진 시작 중",
            "Starting game engine",
            "正在启动游戏引擎",
            "그래픽 엔진 응답을 기다리는 중입니다",
            "Still waiting for the graphics engine",
            "仍在等待图形引擎响应",
            StartupProgressKind.Indeterminate,
            30,
            StartupWatchdogPolicy.RecoveryRequired,
            StandardTerminals & ~StartupTerminalSet.Skipped,
            false,
            StartupStageId.LauncherCreation
        ),
        Stage(
            StartupStageId.LauncherCreation,
            StartupStageOwner.Launcher,
            "런처 준비 중",
            "Preparing launcher",
            "正在准备启动器",
            "런처 화면을 계속 준비하고 있습니다",
            "Launcher setup is still in progress",
            "启动器界面仍在准备",
            StartupProgressKind.Indeterminate,
            15,
            StartupWatchdogPolicy.RecoveryRequired,
            StandardTerminals & ~StartupTerminalSet.Skipped,
            true,
            StartupStageId.LauncherReady,
            StartupStageId.CloudSync,
            StartupStageId.ShaderWarmup,
            StartupStageId.GameSettings
        ),
        Stage(
            StartupStageId.LauncherReady,
            StartupStageOwner.Launcher,
            "런처 준비 완료",
            "Launcher ready",
            "启动器已就绪",
            "런처 입력을 확인하는 중입니다",
            "Still checking launcher input",
            "正在检查启动器输入",
            StartupProgressKind.Indeterminate,
            5,
            StartupWatchdogPolicy.DiagnoseAndContinue,
            StandardTerminals & ~StartupTerminalSet.Skipped,
            false,
            StartupStageId.RecoveryChoice,
            StartupStageId.UserWait
        ),
        Stage(
            StartupStageId.RecoveryChoice,
            StartupStageOwner.Launcher,
            "시작 복구 선택 대기 중",
            "Waiting for startup recovery choice",
            "正在等待启动恢复选择",
            "복구 선택을 기다리고 있습니다",
            "Waiting for a recovery choice",
            "等待选择恢复方式",
            StartupProgressKind.Indeterminate,
            0,
            StartupWatchdogPolicy.NoneForUserWait,
            StandardTerminals,
            false,
            StartupStageId.LauncherReady,
            StartupStageId.UserWait
        ),
        Stage(
            StartupStageId.UserWait,
            StartupStageOwner.Launcher,
            "PLAY 입력 대기 중",
            "Waiting for PLAY",
            "等待点击 PLAY",
            "PLAY 입력을 기다리고 있습니다",
            "Waiting for PLAY",
            "等待点击 PLAY",
            StartupProgressKind.Indeterminate,
            0,
            StartupWatchdogPolicy.NoneForUserWait,
            StartupTerminalSet.Completed | StartupTerminalSet.Recovery,
            false,
            StartupStageId.CloudSync,
            StartupStageId.ShaderWarmup,
            StartupStageId.GameSettings
        ),
        Stage(
            StartupStageId.CloudSync,
            StartupStageOwner.Cloud,
            "클라우드 세이브 동기화 중",
            "Syncing cloud saves",
            "正在同步云存档",
            "네트워크 또는 클라우드 응답을 기다리는 중입니다",
            "Still waiting for network or cloud response",
            "仍在等待网络或云端响应",
            StartupProgressKind.ItemsWhenKnown,
            30,
            StartupWatchdogPolicy.DegradeAndContinue,
            StandardTerminals,
            false,
            StartupStageId.CloudSync,
            StartupStageId.ShaderWarmup,
            StartupStageId.GameSettings
        ),
        Stage(
            StartupStageId.ShaderWarmup,
            StartupStageOwner.ShaderWarmup,
            "셰이더 준비 중",
            "Preparing shaders",
            "正在准备着色器",
            "셰이더 준비가 계속 진행 중입니다",
            "Shader preparation is still in progress",
            "着色器准备仍在进行",
            StartupProgressKind.ItemsWhenKnown,
            30,
            StartupWatchdogPolicy.DegradeAndContinue,
            StandardTerminals,
            false,
            StartupStageId.ShaderWarmup,
            StartupStageId.GameSettings
        ),
        Stage(
            StartupStageId.GameSettings,
            StartupStageOwner.Game,
            "게임 설정 불러오는 중",
            "Loading game settings",
            "正在加载游戏设置",
            "게임 설정을 계속 불러오고 있습니다",
            "Game settings are still loading",
            "游戏设置仍在加载",
            StartupProgressKind.Indeterminate,
            15,
            StartupWatchdogPolicy.RecoveryRequired,
            StandardTerminals & ~StartupTerminalSet.Skipped,
            false,
            StartupStageId.GameStartup
        ),
        Stage(
            StartupStageId.GameStartup,
            StartupStageOwner.Game,
            "게임 시작 중",
            "Starting game",
            "正在启动游戏",
            "게임 초기화가 계속 진행 중입니다",
            "Game initialization is still in progress",
            "游戏初始化仍在进行",
            StartupProgressKind.Indeterminate,
            45,
            StartupWatchdogPolicy.RecoveryRequired,
            StandardTerminals & ~StartupTerminalSet.Skipped,
            false,
            StartupStageId.ModDiscovery,
            StartupStageId.GameReady
        ),
        Stage(
            StartupStageId.ModDiscovery,
            StartupStageOwner.ModLoader,
            "모드 확인 중",
            "Checking mods",
            "正在检查 mod",
            "모드 목록 확인이 계속 진행 중입니다",
            "Mod discovery is still in progress",
            "mod 列表检查仍在进行",
            StartupProgressKind.ItemsWhenKnown,
            15,
            StartupWatchdogPolicy.DegradeAndContinue,
            StandardTerminals,
            false,
            StartupStageId.ModDiscovery,
            StartupStageId.ModLoad,
            StartupStageId.GameStartup,
            StartupStageId.GameReady
        ),
        Stage(
            StartupStageId.ModLoad,
            StartupStageOwner.ModLoader,
            "모드 불러오는 중",
            "Loading mods",
            "正在加载 mod",
            "모드 초기화가 계속 진행 중입니다",
            "Mod initialization is still in progress",
            "mod 初始化仍在进行",
            StartupProgressKind.ItemsWhenKnown,
            30,
            StartupWatchdogPolicy.RecoveryRequired,
            StandardTerminals,
            false,
            StartupStageId.ModLoad,
            StartupStageId.ModDiscovery,
            StartupStageId.GameStartup,
            StartupStageId.GameReady
        ),
        Stage(
            StartupStageId.GameReady,
            StartupStageOwner.Game,
            "게임 준비 완료",
            "Game ready",
            "游戏已就绪",
            "게임 입력 준비를 확인하는 중입니다",
            "Still checking game input readiness",
            "正在确认游戏输入已就绪",
            StartupProgressKind.Indeterminate,
            5,
            StartupWatchdogPolicy.DiagnoseAndContinue,
            StartupTerminalSet.Completed,
            false
        ),
    };

    private static readonly Dictionary<StartupStageId, StartupStageDefinition> ById = BuildIndex();

    internal static IReadOnlyList<StartupStageDefinition> All => Definitions;

    internal static StartupStageDefinition Get(StartupStageId id) =>
        ById.TryGetValue(id, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(id));

    internal static bool IsAllowedNext(StartupStageId current, StartupStageId next) =>
        Array.IndexOf(Get(current).AllowedNext, next) >= 0;

    private static StartupStageDefinition Stage(
        StartupStageId id,
        StartupStageOwner owner,
        string titleKo,
        string titleEn,
        string titleZh,
        string watchdogKo,
        string watchdogEn,
        string watchdogZh,
        StartupProgressKind progressKind,
        int watchdogSeconds,
        StartupWatchdogPolicy watchdogPolicy,
        StartupTerminalSet terminals,
        bool canStartTimeline,
        params StartupStageId[] allowedNext
    ) =>
        new(
            id,
            owner,
            titleKo,
            titleEn,
            titleZh,
            watchdogKo,
            watchdogEn,
            watchdogZh,
            progressKind,
            watchdogSeconds * Second,
            watchdogPolicy,
            terminals,
            canStartTimeline,
            allowedNext
        );

    private static Dictionary<StartupStageId, StartupStageDefinition> BuildIndex()
    {
        var index = new Dictionary<StartupStageId, StartupStageDefinition>(Definitions.Length);
        foreach (var definition in Definitions)
        {
            if (!index.TryAdd(definition.Id, definition))
                throw new InvalidOperationException($"Duplicate startup stage {definition.Id}");
            if (definition.WatchdogAfterUsec == 0)
            {
                if (definition.WatchdogPolicy != StartupWatchdogPolicy.NoneForUserWait)
                    throw new InvalidOperationException(
                        $"Stage {definition.Id} lacks a watchdog policy"
                    );
            }
            else if (definition.WatchdogPolicy == StartupWatchdogPolicy.NoneForUserWait)
            {
                throw new InvalidOperationException(
                    $"Stage {definition.Id} has a watchdog duration but no policy"
                );
            }
        }

        foreach (var definition in Definitions)
        foreach (var next in definition.AllowedNext)
            if (!index.ContainsKey(next))
                throw new InvalidOperationException(
                    $"Stage {definition.Id} references unknown next stage {next}"
                );

        return index;
    }
}
