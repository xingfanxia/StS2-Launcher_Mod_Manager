using System;
using Godot;

namespace STS2Mobile.Launcher.Components;

// Self-contained KR/EN switch. Keeping the state, styling, and persistence out
// of LauncherView leaves only one mount point in that frequently-updated file.
public sealed class LanguageToggle : StyledButton
{
    private readonly float _scale;
    private readonly Action<string> _showStatus;
    private LocalizationAuditSnapshot? _auditCandidate;
    private LocalizationAuditSnapshot? _lastReportedAudit;
    private int _auditStableTicks;

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
                RefreshAndAudit();
        };
        AddChild(refreshTimer);
    }

    private void RefreshAndAudit()
    {
        var audit = Loc.RefreshWatched();
        if (_auditCandidate != audit)
        {
            _auditCandidate = audit;
            _auditStableTicks = 1;
            if (audit.UntranslatedLauncherText > 0)
                ReportAudit(audit, warning: true);
            return;
        }

        _auditStableTicks++;
        if (_auditStableTicks == 4 && _lastReportedAudit != audit)
            ReportAudit(audit, warning: audit.UntranslatedLauncherText > 0);
    }

    private void ReportAudit(LocalizationAuditSnapshot audit, bool warning)
    {
        _lastReportedAudit = audit;
        var message =
            $"[LocalizationAudit] visible={audit.VisibleText} "
            + $"authored_hangul={audit.UntranslatedLauncherText} "
            + $"preserved_external_hangul={audit.PreservedExternalText}";
        if (warning)
            GD.PushWarning(message);
        else
            GD.Print(message);
    }

    private void OnToggled(bool englishEnabled)
    {
        Loc.SetEnglish(englishEnabled);
        RefreshAppearance();
        _showStatus?.Invoke(Loc.Tr("한국어로 전환했습니다.", "English enabled."));
    }

    private void RefreshAppearance()
    {
        var englishEnabled = Loc.IsEnglish;
        Text = englishEnabled ? "EN · ON" : "EN · OFF";
        TooltipText = englishEnabled
            ? "English is enabled. Tap to use Korean."
            : "영어로 전환하려면 누르세요.";
        ApplyVariant(_scale, englishEnabled ? ButtonVariant.Accent : ButtonVariant.Secondary);
    }
}
