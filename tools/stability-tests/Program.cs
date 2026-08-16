using STS2Mobile.Launcher;
using STS2Mobile.Launcher.Components;

var root = Path.Combine(Path.GetTempPath(), $"sts2-stability-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

try
{
    Run(
        "fresh install requires warmup",
        () =>
        {
            var state = new ShaderWarmupState(root, version: 7);
            var result = state.Check();
            Assert(result.NeedsWarmup, result.Reason);
            Assert(!result.RecoveredInterruptedAttempt, "fresh install must not be recovery");
        }
    );

    Run(
        "completed warmup is skipped",
        () =>
        {
            Reset(root);
            var state = new ShaderWarmupState(root, version: 7);
            state.Begin();
            state.Complete();

            var result = new ShaderWarmupState(root, version: 7).Check();
            Assert(!result.NeedsWarmup, result.Reason);
            Assert(!result.RecoveredInterruptedAttempt, "normal completion is not recovery");
            Assert(result.Outcome == ShaderWarmupOutcome.Completed, "completed outcome");
            Assert(!File.Exists(state.AttemptMarkerPath), "attempt marker must be removed");
        }
    );

    Run(
        "interrupted warmup cannot create a crash loop",
        () =>
        {
            Reset(root);
            var firstProcess = new ShaderWarmupState(root, version: 7);
            firstProcess.Begin();

            var nextProcess = new ShaderWarmupState(root, version: 7);
            var result = nextProcess.Check();
            Assert(!result.NeedsWarmup, result.Reason);
            Assert(result.RecoveredInterruptedAttempt, "interrupted attempt must be recovered");
            Assert(result.Outcome == ShaderWarmupOutcome.Interrupted, "interrupted outcome");
            Assert(
                File.ReadAllText(nextProcess.CompletedMarkerPath).Trim() == "7",
                "recovery marker"
            );
            Assert(!File.Exists(nextProcess.AttemptMarkerPath), "recovered attempt marker removed");
        }
    );

    Run(
        "shader warmup persists every bypass outcome without retrying",
        () =>
        {
            foreach (
                var outcome in new[]
                {
                    ShaderWarmupOutcome.DeferredMemoryPressure,
                    ShaderWarmupOutcome.FailedButBypassed,
                }
            )
            {
                Reset(root);
                var state = new ShaderWarmupState(root, version: 7);
                state.Begin();
                state.Complete(outcome, "bounded diagnostic reason");

                var result = new ShaderWarmupState(root, version: 7).Check();
                Assert(!result.NeedsWarmup, $"{outcome} must not repeat");
                Assert(result.Outcome == outcome, $"{outcome} must round-trip");
                Assert(result.Reason == "bounded diagnostic reason", "diagnostic reason");
                Assert(!File.Exists(state.AttemptMarkerPath), "attempt marker must be removed");
            }
        }
    );

    Run(
        "old-version interruption does not suppress current warmup",
        () =>
        {
            Reset(root);
            new ShaderWarmupState(root, version: 6).Begin();

            var result = new ShaderWarmupState(root, version: 7).Check();
            Assert(result.NeedsWarmup, result.Reason);
            Assert(
                !result.RecoveredInterruptedAttempt,
                "old attempt must not count as current recovery"
            );
        }
    );

    Run(
        "completion observed after producer finishes early",
        () =>
        {
            var operation = new ShaderWarmupOperation();
            operation.Complete(restartRequired: false);
            Assert(operation.Completion.IsCompletedSuccessfully, "completion signal was lost");
            Assert(!operation.Completion.Result, "unexpected restart result");
        }
    );

    Run(
        "shader scan keeps the live material set batch-bounded",
        () =>
        {
            var repository = FindRepositoryRoot();
            var warmup = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "ShaderWarmupScreen.cs")
            );
            Assert(
                warmup.Contains("using var packed", StringComparison.Ordinal),
                "each scanned PackedScene must be released"
            );
            Assert(
                warmup.Contains("ResourceLoader.CacheMode.IgnoreDeep", StringComparison.Ordinal),
                "warmup must bypass the reusable resource cache"
            );
            Assert(
                !warmup.Contains("ResourceLoader.CacheMode.Reuse", StringComparison.Ordinal),
                "warmup must not repopulate the reusable resource cache"
            );
            Assert(
                warmup.Contains("CollectWarmupResourcePaths(", StringComparison.Ordinal),
                "warmup must enumerate lightweight paths before loading resources"
            );
            Assert(
                warmup.Contains("if (batch.Count >= BatchSize)", StringComparison.Ordinal)
                    && warmup.Contains("await FlushMaterialBatchAsync", StringComparison.Ordinal),
                "warmup must render and release a bounded batch before loading more resources"
            );
            Assert(
                warmup.Contains("material.Dispose();", StringComparison.Ordinal),
                "warmup must explicitly release each uncached material after rendering"
            );
            Assert(
                !warmup.Contains("ResourceLoader.Load<Material>", StringComparison.Ordinal)
                    && !warmup.Contains("ResourceLoader.Load<Shader>", StringComparison.Ordinal),
                "a typed load of a generic .tres can throw before the mismatched resource is released"
            );
            Assert(
                warmup.Contains("resource.Dispose();", StringComparison.Ordinal),
                "every loose non-material resource probe must be explicitly released"
            );
            Assert(
                !warmup.Contains("CollectMaterialsAsync()", StringComparison.Ordinal)
                    && !warmup.Contains(
                        "new Dictionary<string, Material>()",
                        StringComparison.Ordinal
                    ),
                "warmup must never retain the complete material graph before rendering"
            );
        }
    );

    Run(
        "shader warmup defers before Android memory pressure becomes LMK",
        () =>
        {
            const long mib = 1024L * 1024L;
            var healthy = ShaderWarmupMemoryPolicy.Evaluate(
                new ShaderWarmupMemorySnapshot(
                    trimLevel: 0,
                    systemLowMemory: false,
                    availableBytes: 3_000 * mib,
                    lowMemoryThresholdBytes: 512 * mib,
                    totalBytes: 12_000 * mib,
                    processPssBytes: 1_900 * mib
                )
            );
            Assert(!healthy.ShouldDefer, healthy.Reason);

            var trimPressure = ShaderWarmupMemoryPolicy.Evaluate(
                new ShaderWarmupMemorySnapshot(
                    10,
                    false,
                    3_000 * mib,
                    512 * mib,
                    12_000 * mib,
                    900 * mib
                )
            );
            Assert(trimPressure.ShouldDefer, "TRIM_MEMORY_RUNNING_LOW must stop optional warmup");

            var systemLow = ShaderWarmupMemoryPolicy.Evaluate(
                new ShaderWarmupMemorySnapshot(
                    0,
                    true,
                    900 * mib,
                    512 * mib,
                    6_000 * mib,
                    900 * mib
                )
            );
            Assert(systemLow.ShouldDefer, "ActivityManager low-memory state must stop warmup");

            var lowHeadroom = ShaderWarmupMemoryPolicy.Evaluate(
                new ShaderWarmupMemorySnapshot(
                    0,
                    false,
                    900 * mib,
                    512 * mib,
                    4_000 * mib,
                    900 * mib
                )
            );
            Assert(lowHeadroom.ShouldDefer, "system reserve must remain above the LMK threshold");

            var processBudget = ShaderWarmupMemoryPolicy.Evaluate(
                new ShaderWarmupMemorySnapshot(
                    0,
                    false,
                    3_000 * mib,
                    512 * mib,
                    4_000 * mib,
                    1_400 * mib
                )
            );
            Assert(processBudget.ShouldDefer, "low-RAM devices need a bounded process PSS budget");

            var unavailable = ShaderWarmupMemoryPolicy.Evaluate(
                ShaderWarmupMemorySnapshot.Unavailable
            );
            Assert(
                !unavailable.ShouldDefer,
                "missing telemetry must preserve the existing bounded path"
            );

            var repository = FindRepositoryRoot();
            var warmup = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "ShaderWarmupScreen.cs")
            );
            Assert(
                warmup.Contains("BeginWarmupMemoryMonitoring", StringComparison.Ordinal)
                    && warmup.Contains("EndWarmupMemoryMonitoring", StringComparison.Ordinal)
                    && warmup.Contains(
                        "ShaderWarmupMemoryPolicy.Evaluate",
                        StringComparison.Ordinal
                    ),
                "the physical warmup path must own and consume Android memory monitoring"
            );
            Assert(
                warmup.Contains("WarmupDeferredForMemoryException", StringComparison.Ordinal)
                    && warmup.Contains("deferred for memory safety", StringComparison.Ordinal),
                "memory pressure must take an explicit non-failure completion path"
            );
        }
    );

    Run(
        "startup recovery payload is bounded and fails closed",
        () =>
        {
            var request = StartupRecoveryRequest.Parse("1\n2\nmod-loading\nExampleMod\nLOW_MEMORY");
            Assert(request.Pending, "pending request");
            Assert(request.FailureCount == 2, "failure count");
            Assert(request.Stage == "mod-loading", "stage");
            Assert(request.ModCandidate == "ExampleMod", "candidate");
            Assert(request.Reason == "LOW_MEMORY", "reason");

            Assert(!StartupRecoveryRequest.Parse("torn").Pending, "torn payload fails closed");
            Assert(
                !StartupRecoveryRequest.Parse("1\n2\nmod-loading\n../escape\nCRASH").Pending,
                "path-like candidate fails closed"
            );
        }
    );

    Run(
        "Safe Mode and mod isolation are session-only path filters",
        () =>
        {
            var mods = new[]
            {
                new RecoveryModDescriptor("A", "/mods/a", Array.Empty<string>()),
                new RecoveryModDescriptor("B", "/mods/b", new[] { "A" }),
                new RecoveryModDescriptor("C", "/mods/c", Array.Empty<string>()),
                new RecoveryModDescriptor("D", "/mods/d", Array.Empty<string>()),
            };

            var normal = ModRecoveryPolicy.Build(RecoveryAction.ContinueNormally, "C", mods);
            Assert(!normal.FiltersMods, "normal launch cannot filter mods");
            Assert(normal.ShouldExposeFile("/mods", "/mods/c/C.dll"), "normal visibility");

            var safe = ModRecoveryPolicy.Build(RecoveryAction.SafeMode, "C", mods);
            Assert(safe.FiltersMods && safe.SkipOptionalWarmup, "Safe Mode contract");
            Assert(safe.ShouldExposeDirectory("/mods", "/mods"), "scan root remains valid");
            Assert(!safe.ShouldExposeDirectory("/mods", "/mods/a"), "all mod dirs hidden");
            Assert(!safe.ShouldExposeFile("/mods", "/mods/root.json"), "root mod hidden");

            var exclude = ModRecoveryPolicy.Build(RecoveryAction.ExcludeCandidate, "C", mods);
            Assert(exclude.ShouldExposeDirectory("/mods", "/mods/a"), "other mod stays visible");
            Assert(!exclude.ShouldExposeFile("/mods", "/mods/c/C.dll"), "candidate hidden");
            Assert(
                exclude.ShouldExposeFile("/mods", "/mods/unmanaged.json"),
                "candidate exclusion cannot silently hide unrelated root mods"
            );
            Assert(
                ModRecoveryPolicy
                    .Build(RecoveryAction.ExcludeCandidate, "missing", mods)
                    .SkipOptionalWarmup,
                "unknown candidate must fall back to Safe Mode"
            );

            var bisect = ModRecoveryPolicy.Build(RecoveryAction.BisectFirstHalf, "C", mods);
            Assert(bisect.ShouldExposeDirectory("/mods", "/mods/a"), "first half A");
            Assert(bisect.ShouldExposeDirectory("/mods", "/mods/b"), "first half B");
            Assert(!bisect.ShouldExposeDirectory("/mods", "/mods/c"), "second half C hidden");
            Assert(!bisect.ShouldExposeDirectory("/mods", "/mods/d"), "second half D hidden");

            var repository = FindRepositoryRoot();
            var loader = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "ModLoaderPatches.cs")
            );
            var fileIo = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "ExternalModsFileIo.cs")
            );
            var launch = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );
            Assert(
                loader.Contains("TryLoadModPrefix", StringComparison.Ordinal)
                    && loader.Contains("RecordModCandidate", StringComparison.Ordinal)
                    && loader.Contains("TryLoadModPostfix", StringComparison.Ordinal)
                    && loader.Contains("RecordModSuccessful", StringComparison.Ordinal),
                "mod execution boundary must journal candidate and success"
            );
            Assert(
                fileIo.Contains(
                    "ModRecoverySession.ShouldExposeDirectory",
                    StringComparison.Ordinal
                )
                    && fileIo.Contains(
                        "ModRecoverySession.ShouldExposeFile",
                        StringComparison.Ordinal
                    ),
                "every game mod scan must consume the session-only filter"
            );
            Assert(
                launch.IndexOf("ResolveRecoveryAsync", StringComparison.Ordinal)
                    < launch.IndexOf("WaitForLaunch", StringComparison.Ordinal)
                    && launch.Contains("SkipOptionalWarmup", StringComparison.Ordinal)
                    && launch.Contains("ShowRecoverySuccessAsync", StringComparison.Ordinal),
                "recovery must resolve before PLAY, skip optional warmup, and expose normal restart"
            );
            Assert(
                !fileIo.Contains("Directory.Move", StringComparison.Ordinal)
                    && !fileIo.Contains("Directory.Delete", StringComparison.Ordinal),
                "session isolation must never mutate real mod directories"
            );
        }
    );

    Run(
        "Android mod configuration resolves to app-private XDG storage",
        () =>
        {
            var repository = FindRepositoryRoot();
            var modEntry = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "ModEntry.cs")
            );
            var godotApp = File.ReadAllText(
                Path.Combine(
                    repository,
                    "android",
                    "src",
                    "com",
                    "game",
                    "sts2launcher",
                    "modmanager",
                    "GodotApp.java"
                )
            );
            Assert(
                godotApp.Contains(
                    "android.system.Os.setenv(\"XDG_CONFIG_HOME\"",
                    StringComparison.Ordinal
                )
                    && modEntry.Contains(
                        "GetEnvironmentVariable(\"XDG_CONFIG_HOME\")",
                        StringComparison.Ordinal
                    )
                    && !modEntry.Contains(
                        "Path.Combine(OS.GetUserDataDir()",
                        StringComparison.Ordinal
                    ),
                "mods must not inherit Android's unwritable /data/.config fallback"
            );

            if (OperatingSystem.IsLinux())
            {
                var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                var expected = Path.Combine(root, "xdg-config");
                try
                {
                    Directory.CreateDirectory(expected);
                    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", expected);
                    Assert(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                            == expected,
                        ".NET Unix ApplicationData must honor XDG_CONFIG_HOME"
                    );
                }
                finally
                {
                    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
                }
            }
        }
    );

    Run(
        "dialog teardown fallback is one-shot",
        () =>
        {
            var completion = new DialogCompletion<string>("cancelled");
            completion.CompleteFallback();
            completion.Complete("late button result");
            Assert(completion.Task.IsCompletedSuccessfully, "teardown completion was lost");
            Assert(completion.Task.Result == "cancelled", "first completion must win");
        }
    );

    Run(
        "dialog button result wins before teardown",
        () =>
        {
            var completion = new DialogCompletion<string>("cancelled");
            completion.Complete("selected");
            completion.CompleteFallback();
            Assert(completion.Task.Result == "selected", "teardown must not overwrite a choice");
        }
    );

    Run(
        "awaited dialogs complete on every tree teardown",
        () =>
        {
            var repository = FindRepositoryRoot();
            var resultDialogs = new[]
            {
                "ProfilePickerDialog.cs",
                "BackupRestorePickerDialog.cs",
                "ProfileCopyPickerDialog.cs",
                "LocalOnlyMenuDialog.cs",
                "CloudConflictDialog.cs",
            };

            foreach (var fileName in resultDialogs)
            {
                var source = File.ReadAllText(
                    Path.Combine(
                        repository,
                        "src",
                        "STS2Mobile",
                        "Launcher",
                        "Components",
                        fileName
                    )
                );
                Assert(
                    source.Contains("ModalGate.Register(this);", StringComparison.Ordinal),
                    $"{fileName} must register Android Back handling"
                );
                Assert(
                    source.Contains(
                        "TreeExiting += _completion.CompleteFallback;",
                        StringComparison.Ordinal
                    ),
                    $"{fileName} must resolve its awaited result on teardown"
                );
            }

            var simpleResult = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "Components",
                    "SimpleResultDialog.cs"
                )
            );
            Assert(
                simpleResult.Contains("TreeExiting += FireClosed;", StringComparison.Ordinal),
                "SimpleResultDialog must raise Closed on teardown"
            );

            var branchPicker = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "Components",
                    "BranchPickerDialog.cs"
                )
            );
            Assert(
                branchPicker.Contains("ModalGate.Register(this);", StringComparison.Ordinal),
                "BranchPickerDialog must register Android Back handling"
            );
            Assert(
                branchPicker.Contains("TreeExiting += FireCancelled;", StringComparison.Ordinal),
                "BranchPickerDialog must cancel its awaited picker on teardown"
            );
        }
    );

    Run(
        "launcher initialization failure has an explicit caller path",
        () =>
        {
            var repository = FindRepositoryRoot();
            var launcherUi = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "LauncherUI.cs")
            );
            Assert(
                launcherUi.Contains("public bool Initialize()", StringComparison.Ordinal),
                "LauncherUI.Initialize must report failure instead of returning half-initialized"
            );

            var launcherPatches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );
            Assert(
                launcherPatches.Contains("if (launcher.Initialize())", StringComparison.Ordinal),
                "game startup must branch on launcher initialization"
            );

            var modEntry = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "ModEntry.cs")
            );
            Assert(
                modEntry.Contains("if (!launcher.Initialize())", StringComparison.Ordinal),
                "standalone startup must surface launcher initialization failure"
            );
        }
    );

    Run(
        "atlas rebuild overlay releases launcher input before PLAY wait",
        () =>
        {
            var repository = FindRepositoryRoot();
            var launcherPatches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );
            var initialized = launcherPatches.IndexOf(
                "PatchHelper.Log(\"Launcher UI displayed\");",
                StringComparison.Ordinal
            );
            var overlayHidden = launcherPatches.IndexOf(
                "Call(\"hideLoadingOverlay\")",
                StringComparison.Ordinal
            );
            var playWait = launcherPatches.IndexOf(
                "await launcher.WaitForLaunch()",
                StringComparison.Ordinal
            );

            Assert(initialized >= 0, "game launcher must report successful initialization");
            Assert(overlayHidden >= 0, "game launcher must dismiss the native rebuild overlay");
            Assert(playWait >= 0, "game launcher must await explicit PLAY");
            Assert(
                initialized < overlayHidden && overlayHidden < playWait,
                "the touch-swallowing native rebuild overlay must be hidden once launcher UI is ready"
            );
        }
    );

    Run(
        "whole-launcher teardown releases pending launch and auth waits",
        () =>
        {
            var repository = FindRepositoryRoot();
            var model = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "LauncherModel.cs")
            );
            Assert(
                model.Contains("public Task<bool> WaitForLaunch()", StringComparison.Ordinal),
                "launch wait must communicate normal PLAY versus teardown"
            );
            Assert(
                model.Contains("_launchTcs?.TrySetResult(false);", StringComparison.Ordinal),
                "disposing a launcher must release the pending launch wait"
            );
            Assert(
                model.Contains("_codeTcs?.TrySetCanceled();", StringComparison.Ordinal),
                "disposing a launcher must release a pending Steam Guard wait"
            );

            var patches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );
            Assert(
                patches.Contains("if (await launcher.WaitForLaunch())", StringComparison.Ordinal),
                "game handoff must distinguish PLAY from unexpected launcher teardown"
            );
            Assert(
                patches.Contains("if (!userLaunched)", StringComparison.Ordinal)
                    && patches.Contains("CloudSyncEnabled = false;", StringComparison.Ordinal),
                "a teardown fallback must not run cloud sync without PLAY"
            );
        }
    );

    Run(
        "startup transitions keep Back and deferred UI lifecycle-safe",
        () =>
        {
            var repository = FindRepositoryRoot();
            var launcherRoot = Path.Combine(repository, "src", "STS2Mobile", "Launcher");
            var cloudOverlay = File.ReadAllText(Path.Combine(launcherRoot, "CloudSyncOverlay.cs"));
            var warmup = File.ReadAllText(Path.Combine(launcherRoot, "ShaderWarmupScreen.cs"));
            var launcherUi = File.ReadAllText(Path.Combine(launcherRoot, "LauncherUI.cs"));
            var inputPatches = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "GameInputSuppressPatches.cs"
                )
            );
            var launcherPatches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );

            foreach (var source in new[] { cloudOverlay, warmup })
            {
                Assert(
                    source.Contains("StartupInputGate.Enter(this);", StringComparison.Ordinal)
                        && source.Contains(
                            "StartupInputGate.Exit(this);",
                            StringComparison.Ordinal
                        ),
                    "every full-screen startup transition must hold the shared input gate"
                );
                Assert(
                    source.Contains("NotificationWMGoBackRequest", StringComparison.Ordinal)
                        && source.Contains(
                            "StartupInputGate.HandleBack();",
                            StringComparison.Ordinal
                        ),
                    "startup transitions must route Android Back through the shared gate"
                );
            }

            Assert(
                inputPatches.Contains("!StartupInputGate.Active", StringComparison.Ordinal),
                "raw game input must stay suppressed after LauncherUI is freed"
            );
            Assert(
                launcherPatches.Contains(
                    "using var startupInputLease = StartupInputGate.Hold(gameNode);",
                    StringComparison.Ordinal
                ),
                "the input gate must span every PLAY-to-GameStartup transition"
            );
            Assert(
                launcherUi.Contains(
                    "tree.AutoAcceptQuit = !StartupInputGate.Active;",
                    StringComparison.Ordinal
                ),
                "launcher teardown must not reopen auto-quit under a startup transition"
            );
            Assert(
                cloudOverlay.Contains(
                    "GodotObject.IsInstanceValid(this) || !IsInsideTree()",
                    StringComparison.Ordinal
                )
                    && cloudOverlay.Contains("IsAlive(_statusLabel)", StringComparison.Ordinal)
                    && cloudOverlay.Contains("IsAlive(_progressBar)", StringComparison.Ordinal),
                "deferred cloud UI callbacks must reject freed nodes and controls"
            );
        }
    );

    Run(
        "cloud drain waits never poll on the Godot main thread",
        () =>
        {
            var repository = FindRepositoryRoot();
            var patchesRoot = Path.Combine(repository, "src", "STS2Mobile", "Patches");
            var lifecycle = File.ReadAllText(Path.Combine(patchesRoot, "AppLifecyclePatches.cs"));
            var launcher = File.ReadAllText(Path.Combine(patchesRoot, "LauncherPatches.cs"));

            Assert(
                lifecycle.Contains("_ = Task.Run(() =>", StringComparison.Ordinal)
                    && lifecycle.Contains(
                        "Callable.From(RestartAfterQuitFlush).CallDeferred();",
                        StringComparison.Ordinal
                    ),
                "background/quit cloud drains must run off-main and defer restart"
            );
            Assert(
                lifecycle.Contains(
                    "Interlocked.Exchange(ref _quitRestartInProgress, 1)",
                    StringComparison.Ordinal
                ),
                "repeated Quit calls must not start concurrent drain/restart operations"
            );
            Assert(
                lifecycle.Contains(
                    "Failed to queue deferred quit restart",
                    StringComparison.Ordinal
                )
                    && CountOccurrences(
                        lifecycle,
                        "Interlocked.Exchange(ref _quitRestartInProgress, 0)"
                    ) >= 3,
                "every deferred-restart failure path must release the one-shot latch"
            );
            Assert(
                lifecycle.Contains(
                    "Interlocked.Exchange(ref _backgroundFlushInProgress, 1)",
                    StringComparison.Ordinal
                )
                    && lifecycle.Contains(
                        "Interlocked.Exchange(ref _backgroundFlushInProgress, 0)",
                        StringComparison.Ordinal
                    )
                    && CountOccurrences(
                        lifecycle,
                        "Interlocked.Exchange(ref _backgroundFlushInProgress, 0)"
                    ) >= 2,
                "repeated background callbacks must coalesce concurrent cloud drains"
            );
            Assert(
                !lifecycle.Contains(
                    "SteamKit2CloudSaveStore.Instance?.Flush(300_000)",
                    StringComparison.Ordinal
                ),
                "Quit must not synchronously poll cloud writes for five minutes"
            );

            const string asyncFlush = "await Task.Run(() => cloudStore.Flush(timeoutMs: 300_000))";
            Assert(
                CountOccurrences(launcher, asyncFlush) == 2,
                "both conflict verification flushes must be awaited off-main"
            );
        }
    );

    Run(
        "APK build enforces both compatibility audits",
        () =>
        {
            var repository = FindRepositoryRoot();
            var buildScript = File.ReadAllText(Path.Combine(repository, "scripts", "build.sh"));
            Assert(
                buildScript.Contains(
                    "tools/memberref-audit/audit.csproj",
                    StringComparison.Ordinal
                ),
                "APK build must reject direct/interface contract mismatches"
            );
            Assert(
                buildScript.Contains(
                    "tools/patch-target-audit/audit.csproj",
                    StringComparison.Ordinal
                ),
                "APK build must reject reflection/Harmony target mismatches"
            );

            var dockerBuild = File.ReadAllText(Path.Combine(repository, "docker", "build-apk.sh"));
            foreach (
                var requiredCheck in new[]
                {
                    "tools/stability-tests/stability-tests.csproj",
                    "tools/stability-tests-java/run.sh",
                    "tools/memberref-audit/tests/run.sh",
                    "tools/patch-target-audit/tests/run.sh",
                    "tools/workshop-sync-tests/workshop-sync-tests.csproj",
                }
            )
            {
                Assert(
                    dockerBuild.Contains(requiredCheck, StringComparison.Ordinal),
                    $"container APK build must run {requiredCheck}"
                );
            }
        }
    );

    Run(
        "previous-exit diagnostics never block Activity onCreate",
        () =>
        {
            var repository = FindRepositoryRoot();
            var godotApp = File.ReadAllText(
                Path.Combine(
                    repository,
                    "android",
                    "src",
                    "com",
                    "game",
                    "sts2launcher",
                    "modmanager",
                    "GodotApp.java"
                )
            );
            Assert(
                godotApp.Contains("startPreviousExitReport();", StringComparison.Ordinal),
                "Activity startup must schedule previous-exit reporting"
            );
            Assert(
                godotApp.Contains(
                    "new Thread(this::reportPreviousProcessExits",
                    StringComparison.Ordinal
                ),
                "exit history and ANR trace reads must run off the UI thread"
            );
        }
    );

    Run(
        "startup recovery journal spans Android and launcher stages without private data",
        () =>
        {
            var repository = FindRepositoryRoot();
            var java = File.ReadAllText(
                Path.Combine(
                    repository,
                    "android",
                    "src",
                    "com",
                    "game",
                    "sts2launcher",
                    "modmanager",
                    "GodotApp.java"
                )
            );
            var bridge = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "StartupRecoveryBridge.cs"
                )
            );
            var patches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );

            Assert(
                java.Contains("STARTUP_RECOVERY_PREF", StringComparison.Ordinal)
                    && java.Contains(".commit();", StringComparison.Ordinal)
                    && java.Contains("reconcileStartupExit", StringComparison.Ordinal),
                "Android must durably reconcile process exits with startup attempts"
            );
            Assert(
                bridge.Contains("SHA256.HashData", StringComparison.Ordinal)
                    && !bridge.Contains("SavedAccountName", StringComparison.Ordinal)
                    && !bridge.Contains("SavedRefreshToken", StringComparison.Ordinal),
                "the persisted configuration identity must be opaque and credential-free"
            );
            foreach (
                var stage in new[]
                {
                    "launcher-ready",
                    "play-requested",
                    "cloud-sync",
                    "shader-warmup",
                    "game-startup",
                    "game-ready",
                }
            )
            {
                Assert(
                    patches.Contains($"\"{stage}\"", StringComparison.Ordinal),
                    $"launcher startup must journal {stage}"
                );
            }
            Assert(
                patches.IndexOf(
                    "StartupRecoveryBridge.RecordStage(\"game-startup\")",
                    StringComparison.Ordinal
                )
                    < patches.IndexOf(
                        "await (Task)gameStartup.Invoke(game, null);",
                        StringComparison.Ordinal
                    )
                    && patches.IndexOf(
                        "StartupRecoveryBridge.MarkHealthy(\"game-ready\")",
                        StringComparison.Ordinal
                    )
                        > patches.IndexOf(
                            "await (Task)gameStartup.Invoke(game, null);",
                            StringComparison.Ordinal
                        ),
                "game startup may be marked healthy only after the awaited startup succeeds"
            );
        }
    );

    Console.WriteLine("All stability tests passed.");
    return 0;
}
finally
{
    Directory.Delete(root, recursive: true);
}

static void Run(string name, Action test)
{
    test();
    Console.WriteLine($"PASS {name}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Reset(string path)
{
    foreach (var file in Directory.EnumerateFiles(path))
        File.Delete(file);
}

static int CountOccurrences(string text, string value)
{
    int count = 0;
    int offset = 0;
    while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += value.Length;
    }
    return count;
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "src", "STS2Mobile", "STS2Mobile.csproj")))
            return directory.FullName;
        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not find repository root from current directory.");
}
