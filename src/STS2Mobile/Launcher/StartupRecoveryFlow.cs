using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Modding;

namespace STS2Mobile.Launcher;

internal static class StartupRecoveryFlow
{
    private static bool _resolvedForProcess;
    private static bool _rendererNoticeShown;

    public static async Task<ModRecoveryPlan> ResolveRecoveryAsync(
        Node owner,
        bool allowChoice = true
    )
    {
        if (_resolvedForProcess)
            return ModRecoverySession.Current;

        if (TryConsumeDebugModPartition(out int partitionIndex, out int partitionCount))
        {
            var debugMods = ScanEnabledMods();
            bool hasRequiredCompanion = TryConsumeDebugModCompanion(out string companionId);
            var debugPlan = ModRecoveryPolicy.BuildPartition(
                partitionIndex,
                partitionCount,
                debugMods,
                hasRequiredCompanion ? new[] { companionId } : Array.Empty<string>()
            );
            ModRecoverySession.Configure(debugPlan);
            _resolvedForProcess = true;
            PatchHelper.Log(
                $"[FrameProbe] session-only mod partition armed "
                    + $"partition={partitionIndex}/{partitionCount} "
                    + $"requiredCompanion={hasRequiredCompanion} "
                    + $"selectedMods={debugPlan.SelectedModCount}/{debugPlan.TotalModCount}"
            );
            return debugPlan;
        }

        if (TryConsumeDebugSafeMode())
        {
            var debugMods = ScanEnabledMods();
            var debugPlan = ModRecoveryPolicy.Build(RecoveryAction.SafeMode, "", debugMods);
            ModRecoverySession.Configure(debugPlan);
            _resolvedForProcess = true;
            PatchHelper.Log("[FrameProbe] session-only no-mod comparison armed");
            return debugPlan;
        }

        if (allowChoice)
            await ShowCompatibilityRendererNoticeAsync(owner);

        Control probe = allowChoice ? CreateProbe(owner) : null;
        var request = await WaitForRequestAsync(owner);
        if (probe != null && GodotObject.IsInstanceValid(probe))
            probe.QueueFree();

        if (!request.Pending)
        {
            _resolvedForProcess = true;
            ModRecoverySession.Configure(ModRecoveryPlan.Normal);
            return ModRecoverySession.Current;
        }

        StartupRecoveryBridge.RecordStage("recovery-ui");
        StartupPerformanceTracker.AdvanceTo(StartupStageId.RecoveryChoice);
        var mods = ScanEnabledMods();
        var candidateAvailable = mods.Any(mod =>
            string.Equals(mod.Id, request.ModCandidate, StringComparison.Ordinal)
        );
        var choice = RecoveryAction.SafeMode;
        if (allowChoice && GodotObject.IsInstanceValid(owner) && owner.IsInsideTree())
        {
            var dialog = new StartupRecoveryDialog(
                request,
                candidateAvailable,
                mods.Count,
                LauncherUI.ResolveScale(owner)
            );
            owner.AddChild(dialog);
            choice = await dialog.Result;
        }
        else
        {
            PatchHelper.Log(
                "[Recovery] launcher UI unavailable; using session-only Safe Mode fallback"
            );
        }

        var plan = ModRecoveryPolicy.Build(choice, request.ModCandidate, mods);
        ModRecoverySession.Configure(plan);
        StartupRecoveryBridge.ClearRecoveryRequest();
        StartupRecoveryBridge.RecordStage("launcher-ready");
        StartupPerformanceTracker.AdvanceTo(StartupStageId.LauncherReady);
        _resolvedForProcess = true;
        return plan;
    }

    public static async Task ShowRecoverySuccessAsync(Node gameNode)
    {
        var plan = ModRecoverySession.Current;
        if (!plan.FiltersMods || !GodotObject.IsInstanceValid(gameNode))
            return;
        if (DebugFrameTimeProbe.ShouldAutoContinueRecoverySession)
        {
            PatchHelper.Log("[Recovery] debug capture continuing session automatically");
            return;
        }

        var sessionKo = plan.Action switch
        {
            RecoveryAction.SafeMode => "서드파티 모드를 로드하지 않은 안전 모드",
            RecoveryAction.ExcludeCandidate => $"'{plan.Candidate}' 모드를 제외한 복구 모드",
            _ => $"{plan.SelectedModCount}/{plan.TotalModCount}개 모드만 로드한 테스트 모드",
        };
        var sessionEn = plan.Action switch
        {
            RecoveryAction.SafeMode => "Safe Mode without third-party mods",
            RecoveryAction.ExcludeCandidate =>
                $"recovery mode with candidate '{plan.Candidate}' excluded",
            _ => $"test mode with {plan.SelectedModCount}/{plan.TotalModCount} mods selected",
        };
        var sessionZh = plan.Action switch
        {
            RecoveryAction.SafeMode => "不加载第三方 mod 的安全模式",
            RecoveryAction.ExcludeCandidate => $"排除候选 mod“{plan.Candidate}”的恢复模式",
            _ => $"仅加载 {plan.SelectedModCount}/{plan.TotalModCount} 个 mod 的测试模式",
        };
        var completion = new DialogCompletion<bool>(false);
        var dialog = new StyledDialog(
            Loc.Tr(
                $"{sessionKo}로 메뉴에 도달했습니다.\n\n"
                    + "실제 mod 폴더와 설정은 변경되지 않았습니다. 지금 일반 모드로 재시작하거나 이 세션을 계속할 수 있습니다.",
                $"The game reached the menu in {sessionEn}.\n\n"
                    + "Your real mod folders and settings were not changed. Restart normally now, or continue this session.",
                $"游戏已通过{sessionZh}进入主菜单。\n\n"
                    + "实际 mod 文件夹和设置均未更改。你可以立即以普通模式重启，或继续本次运行。"
            ),
            LauncherUI.ResolveScale(gameNode),
            okLabel: Loc.Tr("일반 모드로 재시작", "Restart normally"),
            cancelLabel: Loc.Tr("이 세션 계속", "Continue this session")
        )
        {
            ZIndex = 300,
        };
        dialog.Confirmed += () => completion.Complete(true);
        dialog.Cancelled += () => completion.Complete(false);
        gameNode.AddChild(dialog);

        if (!await completion.Task)
            return;
        var app = LauncherModel.GetGodotApp();
        if (app == null)
        {
            PatchHelper.Log("[Recovery] normal restart bridge unavailable");
            return;
        }
        app.Call("restartApp");
        PatchHelper.Log("[Recovery] restartApp returned unexpectedly after recovery success");
    }

    private static async Task ShowCompatibilityRendererNoticeAsync(Node owner)
    {
        if (
            _rendererNoticeShown
            || !StartupRecoveryBridge.IsCompatibilityRendererSession()
            || !GodotObject.IsInstanceValid(owner)
            || !owner.IsInsideTree()
        )
            return;

        _rendererNoticeShown = true;
        var completion = new DialogCompletion<bool>(false);
        var dialog = new StyledDialog(
            Loc.Tr(
                "이번 실행에서는 시작 충돌 복구를 위해 OpenGL 호환 렌더러를 사용합니다.\n\n"
                    + "이 변경은 이번 실행에만 적용되며 다음 실행은 자동으로 기본 Vulkan을 사용합니다. 지금 Vulkan으로 다시 시작하거나 호환 모드로 계속할 수 있습니다.",
                "This run is using the OpenGL compatibility renderer for startup recovery.\n\n"
                    + "The change applies only to this run; the next launch automatically returns to Vulkan. Restart with Vulkan now, or continue in compatibility mode."
            ),
            LauncherUI.ResolveScale(owner),
            okLabel: Loc.Tr("Vulkan으로 다시 시작", "Restart with Vulkan"),
            cancelLabel: Loc.Tr("호환 모드로 계속", "Continue in compatibility mode")
        )
        {
            ZIndex = 301,
        };
        dialog.Confirmed += () => completion.Complete(true);
        dialog.Cancelled += () => completion.Complete(false);
        owner.AddChild(dialog);

        StartupPerformanceTracker.AdvanceTo(StartupStageId.RecoveryChoice);
        bool restartWithVulkan = await completion.Task;
        StartupPerformanceTracker.AdvanceTo(StartupStageId.LauncherReady);
        if (!restartWithVulkan)
            return;
        var app = LauncherModel.GetGodotApp();
        if (app == null)
        {
            PatchHelper.Log("[Renderer] Vulkan restart bridge unavailable");
            return;
        }
        app.Call("restartApp");
        PatchHelper.Log("[Renderer] restartApp returned unexpectedly after Vulkan restore");
    }

    private static async Task<StartupRecoveryRequest> WaitForRequestAsync(Node owner)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (StartupRecoveryBridge.TryGetRecoveryRequest(out var request))
                return request;
            if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree())
                break;
            var tree = owner.GetTree();
            if (tree == null)
                break;
            await owner.ToSignal(tree.CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
        }
        PatchHelper.Log("[Recovery] previous-exit report timed out; no automatic override applied");
        return StartupRecoveryRequest.None;
    }

    private static List<RecoveryModDescriptor> ScanEnabledMods()
    {
        try
        {
            return ModScanner
                .Scan()
                .Where(mod => !mod.Disabled && mod.Manifest != null)
                .Select(mod => new RecoveryModDescriptor(
                    mod.Id,
                    mod.TopLevelDir,
                    mod.Manifest.Dependencies.Where(dependency =>
                            !string.IsNullOrWhiteSpace(dependency.Id)
                        )
                        .Select(dependency => dependency.Id)
                        .ToArray()
                ))
                .ToList();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Recovery] read-only mod inventory failed: {ex.Message}");
            return new List<RecoveryModDescriptor>();
        }
    }

    private static bool TryConsumeDebugSafeMode()
    {
        try
        {
            var app = LauncherModel.GetGodotApp();
            return app != null && (bool)app.Call("consumeDebugModSafeMode");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[FrameProbe] safe-mode bridge failed: {ex.GetType().Name}");
            return false;
        }
    }

    private static bool TryConsumeDebugModPartition(out int index, out int count)
    {
        index = -1;
        count = 0;
        try
        {
            var app = LauncherModel.GetGodotApp();
            string value = app == null ? "" : (string)app.Call("consumeDebugModPartition");
            var parts = value.Split('/');
            return parts.Length == 2
                && int.TryParse(parts[0], out index)
                && int.TryParse(parts[1], out count)
                && count >= 2
                && count <= 32
                && index >= 0
                && index < count;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[FrameProbe] mod-partition bridge failed: {ex.GetType().Name}");
            index = -1;
            count = 0;
            return false;
        }
    }

    private static bool TryConsumeDebugModCompanion(out string modId)
    {
        modId = "";
        try
        {
            var app = LauncherModel.GetGodotApp();
            modId = app == null ? "" : (string)app.Call("consumeDebugModCompanionId");
            return !string.IsNullOrWhiteSpace(modId);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[FrameProbe] mod-companion bridge failed: {ex.GetType().Name}");
            modId = "";
            return false;
        }
    }

    private static Control CreateProbe(Node owner)
    {
        if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree())
            return null;
        var probe = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.25f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = 299,
        };
        probe.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        center.AddChild(
            new StyledLabel(
                Loc.Tr("이전 시작 상태 확인 중...", "Checking the previous startup..."),
                LauncherUI.ResolveScale(owner),
                fontSize: 14
            )
        );
        probe.AddChild(center);
        owner.AddChild(probe);
        return probe;
    }
}
