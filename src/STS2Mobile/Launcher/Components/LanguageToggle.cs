using System;
using Godot;

namespace STS2Mobile.Launcher.Components;

// Self-contained KR/EN switch. Keeping the state, styling, and persistence out
// of LauncherView leaves only one mount point in that frequently-updated file.
public sealed class LanguageToggle : StyledButton
{
    private readonly float _scale;
    private readonly Action<string> _showStatus;

    public LanguageToggle(float scale, Action<string> showStatus)
        : base("", scale, fontSize: 11, height: 28)
    {
        _scale = scale;
        _showStatus = showStatus;
        ToggleMode = true;
        ButtonPressed = Loc.IsEnglish;
        CustomMinimumSize = new Vector2(Ui.S(scale, 82), CustomMinimumSize.Y);
        RefreshAppearance();
        Toggled += OnToggled;

        var refreshTimer = new Timer { WaitTime = 0.25, Autostart = true };
        refreshTimer.Timeout += () =>
        {
            if (Loc.IsEnglish)
                Loc.RefreshWatched();
        };
        AddChild(refreshTimer);
    }

    private void OnToggled(bool englishEnabled)
    {
        Loc.SetEnglish(englishEnabled);
        RefreshAppearance();
        _showStatus?.Invoke(
            Loc.Tr(
                "한국어로 전환했습니다.",
                "English enabled."
            )
        );
    }

    private void RefreshAppearance()
    {
        var englishEnabled = Loc.IsEnglish;
        Text = englishEnabled ? "EN · ON" : "EN · OFF";
        TooltipText = englishEnabled
            ? "English is enabled. Tap to use Korean."
            : "영어로 전환하려면 누르세요.";
        ApplyVariant(
            _scale,
            englishEnabled ? ButtonVariant.Accent : ButtonVariant.Secondary
        );
    }
}
