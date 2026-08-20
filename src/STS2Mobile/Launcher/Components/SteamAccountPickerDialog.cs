using System;
using System.Collections.Generic;
using Godot;
using STS2Mobile.Steam;

namespace STS2Mobile.Launcher.Components;

// Shows account names only in the UI. SteamIDs and tokens are never rendered or
// logged. Selecting a stored entry is password-free because its token remains in
// the Android-Keystore-encrypted vault.
public sealed class SteamAccountPickerDialog : ColorRect
{
    public event Action<ulong> AccountSelected;
    public event Action AddAccountRequested;
    public event Action Cancelled;

    private bool _resolved;

    public SteamAccountPickerDialog(IReadOnlyList<SteamAccountSummary> accounts, float scale)
    {
        ModalGate.Register(this);
        TreeExiting += Cancel;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Color = new Color(0, 0, 0, 0.68f);
        ZIndex = 220;

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = Ui.SurfaceHigh };
        style.SetCornerRadiusAll((int)(8 * scale));
        style.SetContentMarginAll((int)(24 * scale));
        panel.AddThemeStyleboxOverride("panel", style);
        center.AddChild(panel);

        var content = new VBoxContainer();
        content.CustomMinimumSize = new Vector2((int)(420 * scale), 0);
        content.AddThemeConstantOverride("separation", (int)(12 * scale));
        panel.AddChild(content);

        var title = new StyledLabel("Steam 계정 전환", scale, fontSize: 18);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(title);

        var hint = new StyledLabel(
            "계정마다 세이브와 런처 설정을 따로 보관합니다. 게임 파일과 모드는 공유됩니다.",
            scale,
            fontSize: 12
        );
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
        content.AddChild(hint);

        foreach (var account in accounts)
        {
            var label = account.IsActive
                ? $"{account.AccountName}  ·  {Loc.Tr("현재", "CURRENT", "当前")}"
                : account.AccountName;
            var button = new StyledButton(
                label,
                scale,
                fontSize: 14,
                height: 48,
                provenance: TextProvenance.ExternalContent
            );
            button.Disabled = account.IsActive;
            var capturedId = account.SteamId;
            button.Pressed += () => Select(capturedId);
            content.AddChild(button);
        }

        var add = new StyledButton("다른 계정 추가", scale, fontSize: 14, height: 48);
        add.Pressed += Add;
        content.AddChild(add);

        var cancel = new StyledButton("취소", scale, fontSize: 13, height: 42);
        cancel.Pressed += () =>
        {
            Cancel();
            QueueFree();
        };
        content.AddChild(cancel);
    }

    private void Select(ulong steamId)
    {
        if (_resolved)
            return;
        _resolved = true;
        AccountSelected?.Invoke(steamId);
        QueueFree();
    }

    private void Add()
    {
        if (_resolved)
            return;
        _resolved = true;
        AddAccountRequested?.Invoke();
        QueueFree();
    }

    private void Cancel()
    {
        if (_resolved)
            return;
        _resolved = true;
        Cancelled?.Invoke();
    }
}
