using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher;

// Issue #53 — bridges the visual gap between the launcher's QueueFree (PLAY pressed)
// and the game boot. The pre-PLAY cloud handshake, conflict backup, and sync-apply
// steps can take 1~2 minutes (a discarded cloud tree is downloaded serially), during
// which nothing was on screen — users mistook the black screen for a crash and killed
// the app mid-sync. This lightweight programmatic overlay shows which step is running.
//
// ZIndex sits below CloudConflictDialog (200) so the conflict prompt still renders on
// top when a decision is needed; the overlay shows through behind it.
public class CloudSyncOverlay : Control
{
    private float _scale;
    private Label _statusLabel;
    private Label _detailLabel;
    private StyledProgressBar _progressBar;
    private bool _inputGateHeld;

    public void Initialize()
    {
        ZIndex = 150;

        // LauncherUI is queued for deletion before cloud work starts. Keep raw
        // Android Back/hotkeys from reaching the not-yet-started game, and keep
        // SceneTree from auto-quitting while this transition owns the screen.
        _inputGateHeld = true;
        StartupInputGate.Enter(this);
        TreeExiting += ReleaseInputGate;

        try
        {
            SetAnchorsPreset(LayoutPreset.FullRect);
            var vpSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
            Size = vpSize;
            _scale = LauncherUI.ResolveScale(this);

            var bg = new ScreenBackground();
            AddChild(bg);

            var panel = new StyledPanel(_scale, widthRatio: 0.6f, heightRatio: 0.3f);
            panel.UpdateSizeFromViewport(vpSize);
            AddChild(panel);

            _statusLabel = new StyledLabel(
                Loc.Tr("클라우드 상태 확인 중...", "Checking cloud status..."),
                _scale,
                fontSize: 20
            );
            panel.Content.AddChild(_statusLabel);

            _progressBar = new StyledProgressBar(_scale);
            _progressBar.MinValue = 0;
            _progressBar.MaxValue = 100;
            _progressBar.Value = 0;
            _progressBar.Visible = false;
            panel.Content.AddChild(_progressBar);

            _detailLabel = new StyledLabel(
                Loc.Tr("잠시만 기다려 주세요", "Please wait"),
                _scale,
                fontSize: 13
            );
            _detailLabel.Modulate = new Color(0.7f, 0.7f, 0.7f);
            panel.Content.AddChild(_detailLabel);

            PatchHelper.Log("[Cloud] Sync overlay displayed");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Cloud] Sync overlay BuildUI failed: {ex.Message}");
        }
    }

    // Sets the headline step text and clears the progress bar / detail line. Called
    // at each phase transition (handshake → backup → apply). Callers may be on a
    // background thread (post-await continuations resume off-main via ConfigureAwait
    // deeper in the backup path), so the mutation is deferred onto the main thread.
    public void SetStatus(string status, string detail = "잠시만 기다려 주세요")
    {
        Callable
            .From(() =>
            {
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
                    return;
                if (IsAlive(_statusLabel))
                    _statusLabel.Text = Loc.Authored(status);
                if (IsAlive(_detailLabel))
                    _detailLabel.Text = Loc.Authored(detail);
                if (IsAlive(_progressBar))
                    _progressBar.Visible = false;
            })
            .CallDeferred();
    }

    // Drives the "클라우드 백업 중 n/전체" phase. Reported once per cloud file as the
    // discarded tree downloads serially, so the bar and count actually move and the
    // screen never looks frozen. Progress callbacks arrive on a background thread —
    // defer the UI mutation onto the main thread.
    public void SetBackupProgress(int done, int total)
    {
        if (total > 0)
            StartupPerformanceTracker.ReportProgress(StartupStageId.CloudSync, done, total);
        Callable
            .From(() =>
            {
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
                    return;
                if (IsAlive(_statusLabel))
                    _statusLabel.Text = Loc.Tr("클라우드 백업 중", "Backing up to cloud");
                if (IsAlive(_progressBar))
                {
                    _progressBar.Visible = true;
                    _progressBar.Value = total > 0 ? (double)done / total * 100 : 0;
                }
                if (IsAlive(_detailLabel))
                    _detailLabel.Text = total > 0 ? $"{done} / {total}" : $"{done}";
            })
            .CallDeferred();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMGoBackRequest && _inputGateHeld)
            StartupInputGate.HandleBack();
    }

    private void ReleaseInputGate()
    {
        if (!_inputGateHeld)
            return;

        _inputGateHeld = false;
        StartupInputGate.Exit(this);
        _statusLabel = null;
        _detailLabel = null;
        _progressBar = null;
    }

    private static bool IsAlive(GodotObject value) =>
        value != null && GodotObject.IsInstanceValid(value);
}
