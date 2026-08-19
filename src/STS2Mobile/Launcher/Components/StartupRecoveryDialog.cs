using System;
using System.Threading.Tasks;
using Godot;

namespace STS2Mobile.Launcher.Components;

internal sealed class StartupRecoveryDialog : ColorRect
{
    private readonly DialogCompletion<RecoveryAction> _completion = new(RecoveryAction.SafeMode);

    public StartupRecoveryDialog(
        StartupRecoveryRequest request,
        bool candidateAvailable,
        int modCount,
        float scale
    )
    {
        ModalGate.Register(this);
        TreeExiting += _completion.CompleteFallback;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Color = new Color(0, 0, 0, 0.78f);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 300;

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        var panelStyle = Ui.Filled(scale, Ui.SurfaceHigh);
        panelStyle.SetContentMarginAll(Ui.S(scale, 24));
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        center.AddChild(panel);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", Ui.S(scale, 12));
        panel.AddChild(content);

        var title = new StyledLabel(Loc.Tr("시작 복구", "Startup recovery"), scale, fontSize: 20);
        content.AddChild(title);

        var bodyScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(Ui.S(scale, 520), Ui.S(scale, 150)),
        };
        TouchScroll.Attach(bodyScroll);
        content.AddChild(bodyScroll);

        var body = new StyledLabel(BuildMessage(request), scale, fontSize: 13);
        body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.CustomMinimumSize = new Vector2(Ui.S(scale, 520), 0);
        body.HorizontalAlignment = HorizontalAlignment.Left;
        bodyScroll.AddChild(body);

        AddChoice(
            content,
            Loc.Tr("안전 모드로 계속 (권장)", "Continue in Safe Mode (recommended)"),
            RecoveryAction.SafeMode,
            scale,
            ButtonVariant.Primary
        );
        if (candidateAvailable)
        {
            AddChoice(
                content,
                Loc.Tr(
                    $"이번 실행에서 '{request.ModCandidate}' 제외",
                    $"Exclude '{request.ModCandidate}' this run"
                ),
                RecoveryAction.ExcludeCandidate,
                scale,
                ButtonVariant.Accent
            );
        }
        if (modCount >= 2)
        {
            AddChoice(
                content,
                Loc.Tr("이번 실행에서 모드 절반 테스트", "Test half of the mods this run"),
                RecoveryAction.BisectFirstHalf,
                scale,
                ButtonVariant.Secondary
            );
        }
        AddChoice(
            content,
            Loc.Tr("평소대로 시작", "Start normally"),
            RecoveryAction.ContinueNormally,
            scale,
            ButtonVariant.Ghost
        );
    }

    public Task<RecoveryAction> Result => _completion.Task;

    private static string BuildMessage(StartupRecoveryRequest request)
    {
        var candidateKo = string.IsNullOrEmpty(request.ModCandidate)
            ? ""
            : $"\n마지막으로 로드를 시작한 모드: {request.ModCandidate}\n"
                + "이 모드는 후보일 뿐이며 원인으로 확정되지 않았습니다.\n";
        var candidateEn = string.IsNullOrEmpty(request.ModCandidate)
            ? ""
            : $"\nLast mod whose load started: {request.ModCandidate}\n"
                + "This is only a candidate, not a confirmed cause.\n";
        var candidateZh = string.IsNullOrEmpty(request.ModCandidate)
            ? ""
            : $"\n最后开始加载的 mod：{request.ModCandidate}\n"
                + "该 mod 仅为候选，并未确认是问题原因。\n";
        return Loc.Tr(
            $"같은 시작 단계에서 {request.FailureCount}회 연속 비정상 종료가 감지되었습니다.\n"
                + $"단계: {request.Stage} · 종료 사유: {request.Reason}\n"
                + candidateKo
                + "안전 모드는 이번 실행에만 적용됩니다. 모드, 저장 파일, 로그인 정보와 설정은 변경하지 않습니다.",
            $"The app exited abnormally {request.FailureCount} times at the same startup stage.\n"
                + $"Stage: {request.Stage} · Exit reason: {request.Reason}\n"
                + candidateEn
                + "Safe Mode applies only to this run. It does not change mods, saves, login data, or settings.",
            $"应用在同一启动阶段连续异常退出 {request.FailureCount} 次。\n"
                + $"阶段：{request.Stage} · 退出原因：{request.Reason}\n"
                + candidateZh
                + "安全模式仅对本次运行生效，不会修改 mod、存档、登录数据或设置。"
        );
    }

    private void AddChoice(
        VBoxContainer parent,
        string label,
        RecoveryAction action,
        float scale,
        ButtonVariant variant
    )
    {
        var button = new StyledButton(label, scale, fontSize: 13, height: 44, variant: variant);
        button.Pressed += () => Resolve(action);
        parent.AddChild(button);
    }

    private void Resolve(RecoveryAction action)
    {
        _completion.Complete(action);
        QueueFree();
    }
}
