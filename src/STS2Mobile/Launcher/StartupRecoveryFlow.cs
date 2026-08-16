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

    public static async Task<ModRecoveryPlan> ResolveRecoveryAsync(
        Node owner,
        bool allowChoice = true
    )
    {
        if (_resolvedForProcess)
            return ModRecoverySession.Current;

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
        _resolvedForProcess = true;
        return plan;
    }

    public static async Task ShowRecoverySuccessAsync(Node gameNode)
    {
        var plan = ModRecoverySession.Current;
        if (!plan.FiltersMods || !GodotObject.IsInstanceValid(gameNode))
            return;

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
        var completion = new DialogCompletion<bool>(false);
        var dialog = new StyledDialog(
            Loc.Tr(
                $"{sessionKo}로 메뉴에 도달했습니다.\n\n"
                    + "실제 mod 폴더와 설정은 변경되지 않았습니다. 지금 일반 모드로 재시작하거나 이 세션을 계속할 수 있습니다.",
                $"The game reached the menu in {sessionEn}.\n\n"
                    + "Your real mod folders and settings were not changed. Restart normally now, or continue this session."
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
