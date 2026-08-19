using System;
using Godot;

namespace STS2Mobile.Launcher.Components;

// Prominent three-language selector. The legacy filename is retained to keep
// upstream merges narrow; the public type describes the new dropdown behavior.
public sealed class LanguageSelector : HBoxContainer
{
    private readonly OptionButton _option;
    private readonly Action<string> _showStatus;
    private LocalizationAuditSnapshot? _auditCandidate;
    private LocalizationAuditSnapshot? _lastReportedAudit;
    private int _auditStableTicks;
    private bool _updatingSelection;

    public LanguageSelector(float scale, Action<string> showStatus)
    {
        _showStatus = showStatus;
        AddThemeConstantOverride("separation", Ui.S(scale, 6));
        CustomMinimumSize = new Vector2(Ui.S(scale, 180), Ui.S(scale, Ui.TouchHeight));

        var glyph = new StyledLabel(
            "LANG",
            scale,
            fontSize: 11,
            provenance: TextProvenance.ExternalContent
        );
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.AddThemeColorOverride("font_color", Ui.Accent);
        AddChild(glyph);

        _option = new OptionButton();
        _option.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _option.CustomMinimumSize = new Vector2(
            Ui.S(scale, 145),
            Ui.S(scale, Ui.TouchHeight)
        );
        _option.AddThemeFontSizeOverride("font_size", Ui.S(scale, 14));
        _option.AddThemeColorOverride("font_color", Ui.TextPrimary);
        _option.AddThemeColorOverride("font_hover_color", Ui.TextPrimary);
        _option.AddThemeColorOverride("font_pressed_color", Ui.TextPrimary);
        _option.AddThemeColorOverride("font_focus_color", Ui.TextPrimary);

        var normal = Ui.Filled(scale, Ui.Card);
        normal.BorderColor = Ui.Accent;
        normal.SetBorderWidthAll(Math.Max(1, Ui.S(scale, 1)));
        normal.ContentMarginLeft = Ui.S(scale, 12);
        normal.ContentMarginRight = Ui.S(scale, 10);
        _option.AddThemeStyleboxOverride("normal", normal);
        _option.AddThemeStyleboxOverride("hover", Ui.Filled(scale, Ui.CardHover));
        _option.AddThemeStyleboxOverride("pressed", Ui.Filled(scale, Ui.CardDown));
        _option.AddThemeStyleboxOverride("focus", Ui.Outline(scale, Ui.Accent));

        var popup = _option.GetPopup();
        popup.AddThemeFontSizeOverride("font_size", Ui.S(scale, 15));
        popup.AddThemeConstantOverride("v_separation", Ui.S(scale, 14));
        popup.AddThemeColorOverride("font_color", Ui.TextPrimary);

        _option.AddItem("한국어", (int)LauncherLanguage.Korean);
        _option.AddItem("English", (int)LauncherLanguage.English);
        _option.AddItem("简体中文", (int)LauncherLanguage.SimplifiedChinese);
        RefreshAppearance();
        _option.ItemSelected += OnItemSelected;
        AddChild(_option);

        var refreshTimer = new Timer { WaitTime = 0.25, Autostart = true };
        refreshTimer.Timeout += RefreshAndAudit;
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
            $"[LocalizationAudit] language={LauncherLanguageCodes.ToPreferenceValue(Loc.CurrentLanguage)} "
            + $"visible={audit.VisibleText} "
            + $"untranslated={audit.UntranslatedLauncherText} "
            + $"preserved_external_hangul={audit.PreservedExternalText}";
        if (warning)
            GD.PushWarning(message);
        else
            GD.Print(message);
    }

    private void OnItemSelected(long index)
    {
        if (_updatingSelection)
            return;
        var language = (LauncherLanguage)_option.GetItemId((int)index);
        if (language == Loc.CurrentLanguage)
            return;

        Loc.SetLanguage(language);
        _auditCandidate = null;
        _lastReportedAudit = null;
        _auditStableTicks = 0;
        RefreshAppearance();
        _showStatus?.Invoke(
            Loc.Select(
                "한국어로 전환했습니다.",
                "English enabled.",
                "已切换到简体中文。"
            )
        );
    }

    private void RefreshAppearance()
    {
        _updatingSelection = true;
        for (var index = 0; index < _option.ItemCount; index++)
        {
            if (_option.GetItemId(index) == (int)Loc.CurrentLanguage)
            {
                _option.Selected = index;
                break;
            }
        }
        _option.TooltipText = Loc.Select("언어 선택", "Select language", "选择语言");
        _updatingSelection = false;
    }
}
