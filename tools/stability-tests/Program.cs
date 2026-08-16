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
            var state = new ShaderWarmupState(root, version: 5);
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
            var state = new ShaderWarmupState(root, version: 5);
            state.Begin();
            state.Complete();

            var result = new ShaderWarmupState(root, version: 5).Check();
            Assert(!result.NeedsWarmup, result.Reason);
            Assert(!result.RecoveredInterruptedAttempt, "normal completion is not recovery");
            Assert(!File.Exists(state.AttemptMarkerPath), "attempt marker must be removed");
        }
    );

    Run(
        "interrupted warmup cannot create a crash loop",
        () =>
        {
            Reset(root);
            var firstProcess = new ShaderWarmupState(root, version: 5);
            firstProcess.Begin();

            var nextProcess = new ShaderWarmupState(root, version: 5);
            var result = nextProcess.Check();
            Assert(!result.NeedsWarmup, result.Reason);
            Assert(result.RecoveredInterruptedAttempt, "interrupted attempt must be recovered");
            Assert(
                File.ReadAllText(nextProcess.CompletedMarkerPath).Trim() == "5",
                "recovery marker"
            );
            Assert(!File.Exists(nextProcess.AttemptMarkerPath), "recovered attempt marker removed");
        }
    );

    Run(
        "old-version interruption does not suppress current warmup",
        () =>
        {
            Reset(root);
            new ShaderWarmupState(root, version: 4).Begin();

            var result = new ShaderWarmupState(root, version: 5).Check();
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
        "shader scan does not retain the scene resource cache",
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
            Assert(
                modEntry.Contains(
                    "Path.Combine(OS.GetUserDataDir(), \".config\")",
                    StringComparison.Ordinal
                )
                    && modEntry.Contains(
                        "SetEnvironmentVariable(\"XDG_CONFIG_HOME\", configDir)",
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
