using System;
using System.Linq;
using System.Threading;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher;

// Low-overhead PLAY-to-game-ready surface. It polls the current numeric state
// four times per second and rebuilds the completed-stage list only when a sparse
// timeline event arrives. Unknown work never receives a synthetic percentage.
internal sealed class StartupProgressOverlay : Control
{
    private const double RefreshIntervalSeconds = 0.25;
    private const int RecentCompletedStageCount = 3;

    private Label _completedLabel;
    private Label _statusLabel;
    private Label _detailLabel;
    private StyledProgressBar _progressBar;
    private double _refreshAccumulator;
    private int _timelineDirty = 1;
    private int _nativeHandoffEnabled;
    private GodotObject _nativeApp;

    internal void Initialize()
    {
        ZIndex = 90;
        SetAnchorsPreset(LayoutPreset.FullRect);
        var viewportSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
        Size = viewportSize;
        float scale = LauncherUI.ResolveScale(this);

        var background = new ScreenBackground();
        AddChild(background);

        var panel = new StyledPanel(scale, widthRatio: 0.62f, heightRatio: 0.36f);
        panel.UpdateSizeFromViewport(viewportSize);
        AddChild(panel);

        _completedLabel = new StyledLabel("", scale, fontSize: 12);
        _completedLabel.Modulate = new Color(0.65f, 0.72f, 0.78f);
        panel.Content.AddChild(_completedLabel);

        _statusLabel = new StyledLabel(
            Loc.Tr("게임 시작 단계 확인 중…", "Checking game startup stage…"),
            scale,
            fontSize: 20
        );
        panel.Content.AddChild(_statusLabel);

        _progressBar = new StyledProgressBar(scale)
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 0,
            ShowPercentage = true,
            Visible = false,
        };
        panel.Content.AddChild(_progressBar);

        _detailLabel = new StyledLabel(
            Loc.Tr("잠시만 기다려 주세요", "Please wait"),
            scale,
            fontSize: 13
        );
        _detailLabel.Modulate = new Color(0.75f, 0.75f, 0.75f);
        panel.Content.AddChild(_detailLabel);

        StartupPerformanceTracker.Changed += OnTimelineChanged;
        TreeExiting += OnTreeExiting;
        RefreshView(forceTimeline: true);
    }

    public override void _Process(double delta)
    {
        _refreshAccumulator += delta;
        if (_refreshAccumulator < RefreshIntervalSeconds)
            return;
        _refreshAccumulator = 0;
        RefreshView(forceTimeline: Interlocked.Exchange(ref _timelineDirty, 0) != 0);
    }

    // GameStartup performs long Godot/.NET initialization spans on the engine
    // thread. _Process cannot run during those spans, so publish the same
    // truthful snapshot to Android's independent UI thread once that handoff
    // begins. Stage transitions remain sparse; no per-frame JNI calls are made.
    internal void BeginMainThreadHandoff()
    {
        if (Interlocked.Exchange(ref _nativeHandoffEnabled, 1) != 0)
            return;

        _nativeApp = LauncherModel.GetGodotApp();
        PublishNativeSnapshot();
    }

    internal void EndMainThreadHandoff()
    {
        if (Interlocked.Exchange(ref _nativeHandoffEnabled, 0) == 0)
            return;

        try
        {
            _nativeApp?.Call("hideLoadingOverlay");
        }
        catch (Exception ex)
        {
            PatchHelper.Log(
                $"[StartupPerformance] native progress hide failed: {ex.GetType().Name}"
            );
        }
        _nativeApp = null;
    }

    private void OnTimelineChanged()
    {
        Interlocked.Exchange(ref _timelineDirty, 1);
        if (Volatile.Read(ref _nativeHandoffEnabled) != 0)
            PublishNativeSnapshot();
    }

    private void OnTreeExiting()
    {
        StartupPerformanceTracker.Changed -= OnTimelineChanged;
        EndMainThreadHandoff();
        _completedLabel = null;
        _statusLabel = null;
        _detailLabel = null;
        _progressBar = null;
    }

    private void PublishNativeSnapshot()
    {
        try
        {
            var app = _nativeApp;
            if (app == null)
                return;

            StartupPerformanceSnapshot snapshot = StartupPerformanceTracker.GetSnapshot();
            if (snapshot.ActiveStage is not { } activeStage)
                return;

            StartupStageDefinition definition = StartupStageCatalog.Get(activeStage);
            string title = Loc.Select(
                definition.TitleKo,
                definition.TitleEn,
                definition.TitleZh
            );
            string watchdog = Loc.Select(
                definition.WatchdogKo,
                definition.WatchdogEn,
                definition.WatchdogZh
            );
            app.Call(
                "showManagedStartupProgress",
                (int)activeStage,
                title,
                BuildCompletedStageText(),
                Loc.Tr("진행 중 · ", "In progress · ", "进行中 · "),
                Loc.Tr("단계 ", "Stage ", "阶段 "),
                Loc.Tr(" · 전체 ", " · Total ", " · 总计 "),
                Loc.Tr("초", "s", "秒"),
                snapshot.ActiveElapsedUsec,
                snapshot.TotalElapsedUsec,
                snapshot.Progress.Done,
                snapshot.Progress.Total,
                definition.WatchdogAfterUsec,
                watchdog
            );
        }
        catch (Exception ex)
        {
            PatchHelper.Log(
                $"[StartupPerformance] native progress bridge failed: {ex.GetType().Name}"
            );
        }
    }

    private void RefreshView(bool forceTimeline)
    {
        if (!IsAlive(_statusLabel) || !IsAlive(_detailLabel) || !IsAlive(_progressBar))
            return;

        StartupPerformanceSnapshot snapshot = StartupPerformanceTracker.GetSnapshot();
        if (snapshot.ActiveStage is not { } activeStage)
            return;

        StartupStageDefinition definition = StartupStageCatalog.Get(activeStage);
        _statusLabel.Text = Loc.Select(
            definition.TitleKo,
            definition.TitleEn,
            definition.TitleZh
        );

        long elapsedSeconds = snapshot.ActiveElapsedUsec / 1_000_000;
        long totalElapsedSeconds = snapshot.TotalElapsedUsec / 1_000_000;
        var timingKo = $"단계 {elapsedSeconds}초 · 전체 {totalElapsedSeconds}초";
        var timingEn = $"Stage {elapsedSeconds}s · Total {totalElapsedSeconds}s";
        var timingZh = $"阶段 {elapsedSeconds}秒 · 总计 {totalElapsedSeconds}秒";
        string timing = Loc.Select(timingKo, timingEn, timingZh);
        if (
            definition.ProgressKind != StartupProgressKind.Indeterminate
            && snapshot.Progress.IsKnown
        )
        {
            _progressBar.Visible = true;
            _progressBar.Value = (double)snapshot.Progress.Done / snapshot.Progress.Total * 100;
            _detailLabel.Text = $"{snapshot.Progress.Done} / {snapshot.Progress.Total} · {timing}";
        }
        else
        {
            _progressBar.Visible = false;
            var progressKo = $"진행 중 · {timingKo}";
            var progressEn = $"In progress · {timingEn}";
            var progressZh = $"进行中 · {timingZh}";
            _detailLabel.Text = Loc.Select(progressKo, progressEn, progressZh);
        }

        if (snapshot.WatchdogPolicy != StartupWatchdogPolicy.NoneForUserWait)
        {
            string watchdog = Loc.Select(
                definition.WatchdogKo,
                definition.WatchdogEn,
                definition.WatchdogZh
            );
            _detailLabel.Text = $"{watchdog} · {timing}";
        }

        if (forceTimeline && IsAlive(_completedLabel))
            _completedLabel.Text = BuildCompletedStageText();
    }

    private static string BuildCompletedStageText()
    {
        var items = StartupPerformanceTracker
            .GetEvents()
            .Where(item => item.Kind == StartupTimelineEventKind.Terminal)
            .TakeLast(RecentCompletedStageCount)
            .Select(item =>
            {
                var definition = StartupStageCatalog.Get(item.Stage);
                string title = Loc.Select(
                    definition.TitleKo,
                    definition.TitleEn,
                    definition.TitleZh
                );
                string marker = item.Terminal switch
                {
                    StartupStageTerminal.Completed => "✓",
                    StartupStageTerminal.Skipped => "–",
                    _ => "!",
                };
                return $"{marker} {title}";
            });
        return string.Join("   ", items);
    }

    private static bool IsAlive(GodotObject value) =>
        value != null && GodotObject.IsInstanceValid(value);
}
