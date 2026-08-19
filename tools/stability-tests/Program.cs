using STS2Mobile.Launcher;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Steam;

var root = Path.Combine(Path.GetTempPath(), $"sts2-stability-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

try
{
    Run(
        "completed Workshop installs clear stale update badges",
        () =>
        {
            Assert(
                WorkshopUpdateStatus.ShouldShowUpdateAvailable(
                    plannedAsUpdate: true,
                    installedTimeUpdated: 100,
                    downloadCompleted: false,
                    downloadedTimeUpdated: 200
                ),
                "an unfinished planned update disappeared early"
            );
            Assert(
                !WorkshopUpdateStatus.ShouldShowUpdateAvailable(
                    plannedAsUpdate: true,
                    installedTimeUpdated: 200,
                    downloadCompleted: true,
                    downloadedTimeUpdated: 200
                ),
                "a completed, persisted update kept the stale Update available badge"
            );
            Assert(
                WorkshopUpdateStatus.ShouldShowUpdateAvailable(
                    plannedAsUpdate: true,
                    installedTimeUpdated: 100,
                    downloadCompleted: true,
                    downloadedTimeUpdated: 200
                ),
                "a queue completion without a persisted registry revision hid a real update"
            );

            var repository = FindRepositoryRoot();
            var manager = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "Sections",
                    "ModManagerSection.cs"
                )
            );
            var subscribed = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "Sections",
                    "WorkshopSubscribedPane.cs"
                )
            );
            var snapshotPublish = subscribed.IndexOf(
                "_updateAvailablePfids = new HashSet<ulong>",
                StringComparison.Ordinal
            );
            var firstEnqueue = subscribed.IndexOf("_queue.Enqueue(item)", StringComparison.Ordinal);
            Assert(
                manager.Contains(
                    "_subscribedPane.NotifyQueueChanged(queueIdle: !busy)",
                    StringComparison.Ordinal
                )
                    && subscribed.Contains(
                        "private void ReconcileCompletedUpdates()",
                        StringComparison.Ordinal
                    )
                    && subscribed.Contains(
                        "persisted.TimeUpdated < queueEntry.Item.TimeUpdated",
                        StringComparison.Ordinal
                    )
                    && snapshotPublish >= 0
                    && firstEnqueue >= 0
                    && snapshotPublish < firstEnqueue,
                "hidden SUBSCRIBED state was not reconciled when DOWNLOADS became idle"
            );
        }
    );

    Run(
        "launcher self-update uses this fork and only its exact signed APK name",
        () =>
        {
            Assert(
                LauncherReleaseChannel.LatestReleaseApiUrl.Contains(
                    "xingfanxia/StS2-Launcher_Mod_Manager",
                    StringComparison.Ordinal
                ),
                "automatic update checks still target upstream instead of this fork"
            );
            Assert(
                LauncherReleaseChannel.IsExpectedApkAsset("StS2Launcher-v0.4.6.apk", "0.4.6"),
                "the canonical release APK was rejected"
            );
            Assert(
                !LauncherReleaseChannel.IsExpectedApkAsset("StS2Launcher-v0.4.6-debug.apk", "0.4.6")
                    && !LauncherReleaseChannel.IsExpectedApkAsset("another-launcher.apk", "0.4.6")
                    && !LauncherReleaseChannel.IsExpectedApkAsset(
                        "StS2Launcher-v0.4.6.apk.sha256",
                        "0.4.6"
                    ),
                "an unrelated/debug/checksum asset crossed the APK install boundary"
            );
            Assert(
                LauncherReleaseChannel.IsExpectedDownloadUrl(
                    "https://github.com/xingfanxia/StS2-Launcher_Mod_Manager/releases/download/v0.4.6/StS2Launcher-v0.4.6.apk"
                )
                    && !LauncherReleaseChannel.IsExpectedDownloadUrl(
                        "https://example.com/StS2Launcher-v0.4.6.apk"
                    )
                    && !LauncherReleaseChannel.IsExpectedDownloadUrl(
                        "http://github.com/xingfanxia/StS2-Launcher_Mod_Manager/releases/download/v0.4.6/StS2Launcher-v0.4.6.apk"
                    ),
                "a non-HTTPS or non-fork download URL crossed the update boundary"
            );
        }
    );

    Run(
        "Mono-invalid mod IL is attributed and quarantined without rewriting third-party DLLs",
        () =>
        {
            var repository = FindRepositoryRoot();
            var loaderPatch = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "ModLoaderPatches.cs")
            );
            var compatibility = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "ModRuntimeCompatibility.cs"
                )
            );
            var android = File.ReadAllText(
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
                loaderPatch.Contains(
                    "nameof(CallModInitializerTranspiler)",
                    StringComparison.Ordinal
                )
                    && loaderPatch.Contains("nameof(MethodBase.Invoke)", StringComparison.Ordinal)
                    && loaderPatch.Contains(
                        "ModRuntimeCompatibility.IsIncompatible(mod.assembly)",
                        StringComparison.Ordinal
                    )
                    && compatibility.Contains("InvalidProgramException", StringComparison.Ordinal)
                    && compatibility.Contains("return method.Invoke", StringComparison.Ordinal)
                    && compatibility.Contains("throw;", StringComparison.Ordinal)
                    && compatibility.Contains(
                        "ModAssemblyRegistry.IsModAssembly",
                        StringComparison.Ordinal
                    )
                    && !compatibility.Contains("LoadFromStream", StringComparison.Ordinal)
                    && android.Contains(
                        "showModRuntimeCompatibilityNotice",
                        StringComparison.Ordinal
                    )
                    && !android.Contains("sts2_mod_compat", StringComparison.Ordinal),
                "Mono-invalid mod quarantine changed DLL bytes or lost the initializer boundary"
            );
        }
    );

    Run(
        "Steam cloud enumeration requests content hashes",
        () =>
        {
            var repository = FindRepositoryRoot();
            var cache = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Steam", "CloudFileCache.cs")
            );

            Assert(
                cache.Contains("extended_details = true", StringComparison.Ordinal),
                "EnumerateUserFiles must request extended metadata or file_sha is omitted"
            );
        }
    );

    Run(
        "Steam manifest hashes skip unchanged whole-file cloud transfers",
        () =>
        {
            Assert(
                CloudContentHash.Matches("A9993E364706816ABA3E25717850C26C9CD0D89D", "abc"),
                "SHA-1 matching must be case-insensitive"
            );
            Assert(
                CloudContentHash.Matches("sha1:a9993e364706816aba3e25717850c26c9cd0d89d", "abc"),
                "Steam SHA-1 prefixes must be accepted"
            );
            Assert(
                CloudContentHash.Matches("qZk+NkcGgWq6PiVxeFDCbJzQ2J0=", "abc"),
                "20-byte Base64 manifest hashes must be accepted"
            );
            Assert(
                CloudContentHash.Matches("da39a3ee5e6b4b0d3255bfef95601890afd80709", ""),
                "empty saves must use the canonical raw-content SHA-1"
            );
            Assert(
                !CloudContentHash.Matches("a9993e364706816aba3e25717850c26c9cd0d89d", "changed"),
                "changed content must not be skipped"
            );
            Assert(
                !CloudContentHash.Matches("not-a-steam-sha", "abc"),
                "unknown manifest hash formats must fail open to transfer"
            );
        }
    );

    Run(
        "bounded cloud downloads overlap without exceeding their connection budget",
        () =>
        {
            const int concurrency = 4;
            var sync = new object();
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var saturated = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            int active = 0;
            int maximum = 0;
            int completed = 0;

            var run = BoundedAsyncWork.ForEachAsync(
                Enumerable.Range(0, 11).ToArray(),
                concurrency,
                async _ =>
                {
                    lock (sync)
                    {
                        active++;
                        maximum = Math.Max(maximum, active);
                        if (active == concurrency)
                            saturated.TrySetResult(true);
                    }

                    await release.Task.ConfigureAwait(false);

                    lock (sync)
                    {
                        active--;
                        completed++;
                    }
                }
            );

            Assert(
                saturated.Task.Wait(TimeSpan.FromSeconds(2)),
                "the bounded worker never overlapped independent downloads"
            );
            lock (sync)
                Assert(maximum == concurrency, "the worker exceeded its concurrency budget");

            release.TrySetResult(true);
            Assert(run.Wait(TimeSpan.FromSeconds(2)), "the bounded worker did not drain");
            lock (sync)
            {
                Assert(active == 0, "an async worker remained active after drain");
                Assert(completed == 11, "the bounded worker lost an item");
            }
        }
    );

    Run(
        "Android login autofill uses the OS credential boundary without crossing values",
        () =>
        {
            var repository = FindRepositoryRoot();
            var android = File.ReadAllText(
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
                    "AndroidLoginAutofillBridge.cs"
                )
            );
            var login = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "Sections",
                    "LoginSection.cs"
                )
            );

            Assert(
                android.Contains(
                    "configureLoginAutofill(String fieldType, String normalizedAnchor)",
                    StringComparison.Ordinal
                )
                    && android.Contains("GodotEditText", StringComparison.Ordinal)
                    && android.Contains("View.AUTOFILL_HINT_USERNAME", StringComparison.Ordinal)
                    && android.Contains("View.AUTOFILL_HINT_PASSWORD", StringComparison.Ordinal)
                    && android.Contains("AutofillManager", StringComparison.Ordinal)
                    && android.Contains("setAutofillHints(hint)", StringComparison.Ordinal)
                    && android.Contains("View.IMPORTANT_FOR_AUTOFILL_YES", StringComparison.Ordinal)
                    && android.Contains("positionLoginAutofillAnchor", StringComparison.Ordinal)
                    && android.Contains("editText.setX(left)", StringComparison.Ordinal)
                    && android.Contains("editText.setY(top)", StringComparison.Ordinal)
                    && android.Contains(
                        "onWindowFocusChanged(boolean hasFocus)",
                        StringComparison.Ordinal
                    )
                    && android.Contains(
                        "restoreGodotEditTextBounds(editText)",
                        StringComparison.Ordinal
                    )
                    && android.Contains("editText.clearFocus()", StringComparison.Ordinal)
                    && android.Contains("requestAutofill(editText)", StringComparison.Ordinal),
                "the Android bridge must annotate Godot's existing native editor"
            );
            Assert(
                android.Contains("clearLoginAutofill()", StringComparison.Ordinal)
                    && android.Contains("autofillManager.cancel()", StringComparison.Ordinal)
                    && android.Contains(
                        "setAutofillHints((String[]) null)",
                        StringComparison.Ordinal
                    ),
                "field changes and login exit must terminate stale autofill sessions"
            );
            Assert(
                bridge.Contains(
                    "Configure(LoginAutofillField field, Control anchor)",
                    StringComparison.Ordinal
                )
                    && bridge.Contains("anchor.GetGlobalRect()", StringComparison.Ordinal)
                    && bridge.Contains("anchor.GetViewportRect()", StringComparison.Ordinal)
                    && bridge.Contains(
                        ".Call(\"configureLoginAutofill\", fieldType, normalizedAnchor)",
                        StringComparison.Ordinal
                    )
                    && bridge.Contains(".Call(\"clearLoginAutofill\")", StringComparison.Ordinal)
                    && !bridge.Contains(".Text", StringComparison.Ordinal),
                "the managed bridge must pass only a fixed field-type token, never a credential"
            );
            Assert(
                login.Contains(
                    "ConfigureAutofill(UsernameField, LoginAutofillField.Username)",
                    StringComparison.Ordinal
                )
                    && login.Contains(
                        "ConfigureAutofill(_passwordField, LoginAutofillField.Password)",
                        StringComparison.Ordinal
                    )
                    && login.Contains("field.FocusEntered", StringComparison.Ordinal)
                    && login.Contains("field.GuiInput", StringComparison.Ordinal)
                    && login.Contains("InputEventScreenTouch", StringComparison.Ordinal)
                    && login.Contains("VisibilityChanged", StringComparison.Ordinal)
                    && !login.Contains(
                        "FocusExited += AndroidLoginAutofillBridge.Clear",
                        StringComparison.Ordinal
                    ),
                "both login fields must request on focus and retry taps without cancelling provider UI"
            );
        }
    );

    Run(
        "Quick Restart compatibility is exact and idle processing is impossible",
        () =>
        {
            Assert(
                QuickRestartPerformancePolicy.MatchesIdentity(
                    "QuickRestart",
                    new Version(1, 0, 0, 0),
                    Guid.Parse("726d9381-e101-4663-82cf-2131b6ec3fdb"),
                    "d1584ccfa73e8c727b943771a5c3f65129c7f2327f3f1f354ae39e482b6c9973",
                    isExternalModAssembly: true
                ),
                "the measured Quick Restart v2.0.0 assembly must match"
            );
            Assert(
                !QuickRestartPerformancePolicy.MatchesIdentity(
                    "QuickRestartUpdated",
                    new Version(1, 0, 0, 0),
                    Guid.Parse("726d9381-e101-4663-82cf-2131b6ec3fdb"),
                    "d1584ccfa73e8c727b943771a5c3f65129c7f2327f3f1f354ae39e482b6c9973",
                    isExternalModAssembly: true
                ),
                "a same-shape assembly with another name must fail open"
            );
            Assert(
                !QuickRestartPerformancePolicy.MatchesIdentity(
                    "QuickRestart",
                    new Version(1, 0, 0, 1),
                    Guid.Parse("726d9381-e101-4663-82cf-2131b6ec3fdb"),
                    "d1584ccfa73e8c727b943771a5c3f65129c7f2327f3f1f354ae39e482b6c9973",
                    isExternalModAssembly: true
                ),
                "an updated assembly version must fail open"
            );
            Assert(
                !QuickRestartPerformancePolicy.MatchesIdentity(
                    "QuickRestart",
                    new Version(1, 0, 0, 0),
                    Guid.NewGuid(),
                    "d1584ccfa73e8c727b943771a5c3f65129c7f2327f3f1f354ae39e482b6c9973",
                    isExternalModAssembly: true
                ),
                "an updated MVID must fail open"
            );
            Assert(
                !QuickRestartPerformancePolicy.MatchesIdentity(
                    "QuickRestart",
                    new Version(1, 0, 0, 0),
                    Guid.Parse("726d9381-e101-4663-82cf-2131b6ec3fdb"),
                    "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
                    isExternalModAssembly: true
                ),
                "a different assembly hash must fail open"
            );
            Assert(
                !QuickRestartPerformancePolicy.MatchesIdentity(
                    "QuickRestart",
                    new Version(1, 0, 0, 0),
                    Guid.Parse("726d9381-e101-4663-82cf-2131b6ec3fdb"),
                    "d1584ccfa73e8c727b943771a5c3f65129c7f2327f3f1f354ae39e482b6c9973",
                    isExternalModAssembly: false
                ),
                "an assembly outside the mod tree must never be patched"
            );

            Assert(
                QuickRestartPerformancePolicy.AfterInput(isHolding: false, triggered: false)
                    == QuickRestartProcessAction.ResetAndDisable,
                "idle input must reset once and disable processing"
            );
            Assert(
                QuickRestartPerformancePolicy.AfterInput(isHolding: true, triggered: false)
                    == QuickRestartProcessAction.Enable,
                "a real hold must enable processing"
            );
            Assert(
                QuickRestartPerformancePolicy.AfterProcess(isHolding: true, triggered: false)
                    == QuickRestartProcessAction.KeepEnabled,
                "an incomplete hold must continue"
            );
            Assert(
                QuickRestartPerformancePolicy.AfterProcess(isHolding: true, triggered: true)
                    == QuickRestartProcessAction.Disable,
                "a triggered restart must stop processing immediately"
            );
            Assert(
                QuickRestartPerformancePolicy.AfterProcess(isHolding: false, triggered: false)
                    == QuickRestartProcessAction.Disable,
                "release must make idle processing impossible"
            );
        }
    );

    Run(
        "covered startup skips only the hidden logo without leaking session state",
        () =>
        {
            Assert(
                !CoveredStartupLogoPolicy.ShouldSkipLogo(false),
                "normal game startup must preserve the logo"
            );
            Assert(
                CoveredStartupLogoPolicy.ShouldSkipLogo(true),
                "the game's existing skip request must remain authoritative"
            );

            using (CoveredStartupLogoPolicy.Enter(launcherSurfaceCoversStartup: false))
                Assert(
                    !CoveredStartupLogoPolicy.ShouldSkipLogo(false),
                    "a failed launcher progress surface must not hide the logo"
                );

            var outer = CoveredStartupLogoPolicy.Enter(launcherSurfaceCoversStartup: true);
            Assert(
                CoveredStartupLogoPolicy.ShouldSkipLogo(false),
                "a covered startup must skip the hidden logo"
            );
            using (CoveredStartupLogoPolicy.Enter(launcherSurfaceCoversStartup: true))
                Assert(
                    CoveredStartupLogoPolicy.ShouldSkipLogo(false),
                    "nested startup scopes must remain active"
                );
            Assert(
                CoveredStartupLogoPolicy.ShouldSkipLogo(false),
                "disposing a nested scope must preserve its outer scope"
            );
            outer.Dispose();
            outer.Dispose();
            Assert(
                !CoveredStartupLogoPolicy.ShouldSkipLogo(false),
                "scope disposal must be idempotent and restore normal startup"
            );
        }
    );

    Run(
        "launcher language codes preserve upgrades and classify Chinese scripts",
        () =>
        {
            Assert(
                LauncherLanguageCodes.TryParsePreference("ko", out var korean)
                    && korean == LauncherLanguage.Korean,
                "legacy ko preference must remain readable"
            );
            Assert(
                LauncherLanguageCodes.TryParsePreference("en", out var english)
                    && english == LauncherLanguage.English,
                "legacy en preference must remain readable"
            );
            foreach (var value in new[] { "zh", "zh-Hans", "zh_CN", "zh-SG" })
                Assert(
                    LauncherLanguageCodes.TryParsePreference(value, out var chinese)
                        && chinese == LauncherLanguage.SimplifiedChinese,
                    $"simplified Chinese preference not recognized: {value}"
                );
            Assert(
                !LauncherLanguageCodes.TryParsePreference("broken", out _),
                "unknown persisted language must be rejected"
            );
            Assert(
                LauncherLanguageCodes.ToPreferenceValue(LauncherLanguage.SimplifiedChinese)
                    == "zh-Hans",
                "simplified Chinese must use the canonical persisted code"
            );

            foreach (var locale in new[] { "zh-Hans", "zh_CN", "zh-SG", "zh" })
                Assert(
                    LauncherLanguageCodes.FromSystemLocale(locale)
                        == LauncherLanguage.SimplifiedChinese,
                    $"simplified Chinese locale not selected: {locale}"
                );
            foreach (var locale in new[] { "zh-Hant", "zh_TW", "zh-HK", "zh_MO" })
                Assert(
                    LauncherLanguageCodes.FromSystemLocale(locale) == LauncherLanguage.English,
                    $"traditional Chinese locale must not silently select Simplified: {locale}"
                );
            Assert(
                LauncherLanguageCodes.FromSystemLocale("ko-KR") == LauncherLanguage.Korean,
                "Korean system locale must select Korean"
            );
            Assert(
                LauncherLanguageCodes.FromSystemLocale("fr-FR") == LauncherLanguage.English,
                "other locales must retain the existing English default"
            );
        }
    );

    Run(
        "language selector is prominent, three-valued, and touch-sized",
        () =>
        {
            var repository = FindRepositoryRoot();
            var components = Path.Combine(
                repository,
                "src",
                "STS2Mobile",
                "Launcher",
                "Components"
            );
            var selector = File.ReadAllText(Path.Combine(components, "LanguageToggle.cs"));
            var view = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "LauncherView.cs")
            );
            var registry = File.ReadAllText(
                Path.Combine(components, "LocalizedTextRegistry.cs")
            );

            Assert(
                selector.Contains("class LanguageSelector", StringComparison.Ordinal)
                    && selector.Contains("new OptionButton()", StringComparison.Ordinal)
                    && selector.Contains("\"한국어\"", StringComparison.Ordinal)
                    && selector.Contains("\"English\"", StringComparison.Ordinal)
                    && selector.Contains("\"简体中文\"", StringComparison.Ordinal)
                    && selector.Contains("\"LANG\"", StringComparison.Ordinal)
                    && selector.Contains("Ui.TouchHeight", StringComparison.Ordinal)
                    && selector.Contains("_lastReportedAudit = null", StringComparison.Ordinal)
                    && !selector.Contains("EN · ON", StringComparison.Ordinal)
                    && !selector.Contains("EN · OFF", StringComparison.Ordinal),
                "legacy binary toggle or undersized selector remains"
            );
            int selectorMount = view.IndexOf("new LanguageSelector", StringComparison.Ordinal);
            int loginMount = view.IndexOf("Login = new LoginSection", StringComparison.Ordinal);
            int consoleMount = view.IndexOf("var logHeader", StringComparison.Ordinal);
            Assert(
                selectorMount >= 0
                    && selectorMount < loginMount
                    && selectorMount < consoleMount
                    && view.Split("new LanguageSelector", StringSplitOptions.None).Length == 2,
                "language selector must have one mount beside the primary title before login"
            );
            Assert(
                registry.Contains("WatchedProperty.OptionItem", StringComparison.Ordinal)
                    && registry.Contains("SetItemText", StringComparison.Ordinal),
                "localized dropdown items must update immediately with the selected language"
            );
        }
    );

    Run(
        "startup performance catalog is closed, localized, and watchdog-owned",
        () =>
        {
            var definitions = StartupStageCatalog.All;
            Assert(
                definitions.Select(item => item.Id).Distinct().Count() == definitions.Count,
                "stage ids must be unique"
            );
            Assert(definitions.Count == 16, "the closed startup catalog must cover all owners");

            foreach (var definition in definitions)
            {
                Assert(
                    !string.IsNullOrWhiteSpace(definition.TitleKo)
                        && !string.IsNullOrWhiteSpace(definition.TitleEn)
                        && !string.IsNullOrWhiteSpace(definition.TitleZh)
                        && !string.IsNullOrWhiteSpace(definition.WatchdogKo)
                        && !string.IsNullOrWhiteSpace(definition.WatchdogEn)
                        && !string.IsNullOrWhiteSpace(definition.WatchdogZh),
                    $"localized stage copy missing for {definition.Id}"
                );
                Assert(
                    System.Text.RegularExpressions.Regex.IsMatch(
                        definition.TitleKo,
                        "[\\uAC00-\\uD7AF]"
                    ),
                    $"Korean title missing for {definition.Id}"
                );
                Assert(
                    !System.Text.RegularExpressions.Regex.IsMatch(
                        definition.TitleEn,
                        "[\\uAC00-\\uD7AF]"
                    ),
                    $"English title contains Hangul for {definition.Id}"
                );
                Assert(
                    System.Text.RegularExpressions.Regex.IsMatch(
                        definition.TitleZh,
                        "[\\u3400-\\u9FFF]"
                    )
                        && !System.Text.RegularExpressions.Regex.IsMatch(
                            definition.TitleZh,
                            "[\\uAC00-\\uD7AF]"
                        ),
                    $"Simplified Chinese title missing or contains Hangul for {definition.Id}"
                );

                if (definition.WatchdogPolicy == StartupWatchdogPolicy.NoneForUserWait)
                    Assert(
                        definition.WatchdogAfterUsec == 0,
                        $"user-owned wait must not time out: {definition.Id}"
                    );
                else
                    Assert(
                        definition.WatchdogAfterUsec > 0,
                        $"owned work must define a watchdog: {definition.Id}"
                    );

                foreach (var next in definition.AllowedNext)
                    _ = StartupStageCatalog.Get(next);
            }
        }
    );

    Run(
        "startup performance timeline closes normal and optional paths",
        () =>
        {
            var timeline = new StartupPerformanceTimeline();
            long now = 1_000;

            Assert(
                timeline.TryBegin(StartupStageId.LauncherCreation, now++, out var error),
                $"launcher begin: {error}"
            );
            Assert(
                timeline.TryEnd(
                    StartupStageId.LauncherCreation,
                    StartupStageTerminal.Completed,
                    now++,
                    out error
                ),
                $"launcher end: {error}"
            );
            Assert(
                timeline.TryBegin(StartupStageId.LauncherReady, now++, out error)
                    && timeline.TryEnd(
                        StartupStageId.LauncherReady,
                        StartupStageTerminal.Completed,
                        now++,
                        out error
                    ),
                $"launcher ready: {error}"
            );
            Assert(
                timeline.TryBegin(StartupStageId.UserWait, now++, out error)
                    && timeline.TryEnd(
                        StartupStageId.UserWait,
                        StartupStageTerminal.Completed,
                        now++,
                        out error
                    ),
                $"user wait: {error}"
            );
            Assert(
                timeline.TrySkip(StartupStageId.CloudSync, now++, out error),
                $"cloud skip: {error}"
            );
            Assert(
                timeline.TrySkip(StartupStageId.ShaderWarmup, now++, out error),
                $"warmup skip: {error}"
            );
            Assert(
                timeline.TryBegin(StartupStageId.GameSettings, now++, out error)
                    && timeline.TryEnd(
                        StartupStageId.GameSettings,
                        StartupStageTerminal.Completed,
                        now++,
                        out error
                    ),
                $"settings: {error}"
            );
            Assert(
                timeline.TryBegin(StartupStageId.GameStartup, now++, out error)
                    && timeline.TryEnd(
                        StartupStageId.GameStartup,
                        StartupStageTerminal.Completed,
                        now++,
                        out error
                    ),
                $"game startup: {error}"
            );
            Assert(
                timeline.TryBegin(StartupStageId.GameReady, now++, out error)
                    && timeline.TryEnd(
                        StartupStageId.GameReady,
                        StartupStageTerminal.Completed,
                        now++,
                        out error
                    ),
                $"game ready: {error}"
            );

            Assert(timeline.ActiveStage == null, "normal timeline must close");
            Assert(
                timeline.LastTerminalStage == StartupStageId.GameReady,
                "game-ready must be terminal"
            );
            Assert(
                !timeline.TryEnd(
                    StartupStageId.GameReady,
                    StartupStageTerminal.Completed,
                    now++,
                    out error
                )
                    && error == StartupTimelineError.NoActiveStage,
                "duplicate terminal callbacks must be ignored explicitly"
            );
            Assert(
                !timeline.TryBegin(StartupStageId.AndroidProcess, now++, out error)
                    && error == StartupTimelineError.IllegalTransition,
                "a closed timeline must reject backward stage movement"
            );
        }
    );

    Run(
        "startup timeline handles degraded retry, teardown, and bounded watchdogs",
        () =>
        {
            var timeline = new StartupPerformanceTimeline(capacity: 16);
            Assert(
                timeline.TryBegin(StartupStageId.AndroidProcess, 0, out var error)
                    && timeline.TryEnd(
                        StartupStageId.AndroidProcess,
                        StartupStageTerminal.Completed,
                        10,
                        out error
                    ),
                $"android process: {error}"
            );
            Assert(
                timeline.TryBegin(StartupStageId.CacheSync, 20, out error),
                $"cache begin: {error}"
            );
            Assert(
                timeline.CheckWatchdog(59_000_020) == StartupWatchdogPolicy.NoneForUserWait,
                "watchdog must not fire early"
            );
            Assert(
                timeline.CheckWatchdog(60_000_020) == StartupWatchdogPolicy.DegradeAndContinue,
                "cache watchdog policy"
            );
            Assert(
                timeline.CheckWatchdog(61_000_020) == StartupWatchdogPolicy.DegradeAndContinue,
                "watchdog policy remains visible after first marker"
            );
            Assert(
                timeline.Snapshot().Count(item => item.Kind == StartupTimelineEventKind.Watchdog)
                    == 1,
                "one stage may emit only one watchdog marker"
            );
            Assert(
                timeline.TryEnd(
                    StartupStageId.CacheSync,
                    StartupStageTerminal.Degraded,
                    61_000_030,
                    out error
                )
                    && timeline.TryBegin(StartupStageId.CacheSync, 61_000_040, out error)
                    && timeline.TryEnd(
                        StartupStageId.CacheSync,
                        StartupStageTerminal.Recovery,
                        61_000_050,
                        out error
                    ),
                $"retry/recovery teardown: {error}"
            );
            Assert(timeline.ActiveStage == null, "recovery teardown must close active work");
        }
    );

    Run(
        "startup progress is truthful, sparse, bounded, and numeric-only",
        () =>
        {
            var timeline = new StartupPerformanceTimeline(capacity: 8);
            Assert(
                timeline.TryBegin(StartupStageId.AndroidProcess, 0, out var error)
                    && timeline.TryEnd(
                        StartupStageId.AndroidProcess,
                        StartupStageTerminal.Completed,
                        1,
                        out error
                    )
                    && timeline.TryBegin(StartupStageId.CacheSync, 2, out error),
                $"progress setup: {error}"
            );

            for (int done = 0; done <= 100; done++)
            {
                Assert(
                    timeline.TryReportProgress(
                        StartupStageId.CacheSync,
                        done,
                        100,
                        2L + done * 50_000L,
                        out error
                    ),
                    $"progress {done}: {error}"
                );
            }

            Assert(
                timeline.CurrentProgress == new StartupProgress(100, 100),
                "UI progress must keep the exact latest units"
            );
            Assert(timeline.EventCount <= 8, "event ring must remain bounded");
            Assert(
                timeline.Snapshot().Count(item => item.Kind == StartupTimelineEventKind.Progress)
                    < 100,
                "telemetry must not record one event per work item"
            );
            Assert(
                !timeline.TryReportProgress(StartupStageId.CacheSync, 99, 100, 6_000_000, out error)
                    && error == StartupTimelineError.InvalidProgress,
                "progress may not move backward"
            );
            Assert(
                !timeline.TryReportProgress(
                    StartupStageId.CacheSync,
                    100,
                    101,
                    6_000_001,
                    out error
                )
                    && error == StartupTimelineError.InvalidProgress,
                "a displayed determinate total may not change"
            );

            string encoded = timeline.EncodeSanitized();
            Assert(encoded.Length < 1_024, "bounded summary size");
            Assert(encoded.StartsWith("v1\n", StringComparison.Ordinal), "schema version");
            Assert(
                encoded.All(character =>
                    character == 'v'
                    || character == '|'
                    || character == '\n'
                    || char.IsAsciiDigit(character)
                ),
                "sanitized timeline must contain only schema punctuation and numeric fields"
            );
            Assert(
                timeline.EncodeTerminalDurations() == "v2;1|1;",
                "compact stage totals must survive bounded event-ring eviction"
            );
        }
    );

    Run(
        "startup observability stays separate, truthful, and privacy-bounded",
        () =>
        {
            var repository = FindRepositoryRoot();
            string launcherRoot = Path.Combine(repository, "src", "STS2Mobile", "Launcher");
            string patchesRoot = Path.Combine(repository, "src", "STS2Mobile", "Patches");
            var tracker = File.ReadAllText(
                Path.Combine(launcherRoot, "StartupPerformanceTracker.cs")
            );
            var timeline = File.ReadAllText(
                Path.Combine(launcherRoot, "StartupPerformanceTimeline.cs")
            );
            var overlay = File.ReadAllText(Path.Combine(launcherRoot, "StartupProgressOverlay.cs"));
            var recoveryBridge = File.ReadAllText(
                Path.Combine(launcherRoot, "StartupRecoveryBridge.cs")
            );
            var launcherPatches = File.ReadAllText(Path.Combine(patchesRoot, "LauncherPatches.cs"));
            var coveredLogoPatches = File.ReadAllText(
                Path.Combine(patchesRoot, "CoveredStartupLogoPatches.cs")
            );
            var modPatches = File.ReadAllText(Path.Combine(patchesRoot, "ModLoaderPatches.cs"));
            var cloudOverlay = File.ReadAllText(Path.Combine(launcherRoot, "CloudSyncOverlay.cs"));
            var warmup = File.ReadAllText(Path.Combine(launcherRoot, "ShaderWarmupScreen.cs"));
            string androidRoot = Path.Combine(
                repository,
                "android",
                "src",
                "com",
                "game",
                "sts2launcher",
                "modmanager"
            );
            var androidApp = File.ReadAllText(Path.Combine(androidRoot, "GodotApp.java"));
            var nativeTimeline = File.ReadAllText(
                Path.Combine(androidRoot, "StartupPerformanceTimeline.java")
            );

            Assert(
                recoveryBridge.Contains(
                    "StartupPerformanceTracker.BeginManagedStartup();",
                    StringComparison.Ordinal
                )
                    && recoveryBridge.Contains(
                        "Call(\"recordStartupStage\", stage)",
                        StringComparison.Ordinal
                    ),
                "performance startup must begin beside, not replace, crash recovery journaling"
            );
            Assert(
                timeline.Contains("StartupTimelineEvent[] _events", StringComparison.Ordinal)
                    && timeline.Contains("capacity is < 8 or > 256", StringComparison.Ordinal)
                    && timeline.Contains("EncodeSanitized", StringComparison.Ordinal)
                    && !timeline.Contains("System.IO", StringComparison.Ordinal),
                "performance telemetry must remain bounded and free of hot-path file writes"
            );
            Assert(
                tracker.Contains("[StartupPerformance/Summary]", StringComparison.Ordinal)
                    && tracker.Contains("EncodeTerminalDurations()", StringComparison.Ordinal)
                    && androidApp.Contains(
                        "[StartupPerformance/NativeSummary]",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "startupPerformanceTimeline.encode().replace('\\n', ';')",
                        StringComparison.Ordinal
                    ),
                "each completed startup must expose one bounded numeric stage summary"
            );
            foreach (
                var forbidden in new[]
                {
                    "SavedAccountName",
                    "SavedRefreshToken",
                    "ModCandidate",
                    "DeviceId",
                    "FilePath",
                }
            )
            {
                Assert(
                    !tracker.Contains(forbidden, StringComparison.Ordinal)
                        && !timeline.Contains(forbidden, StringComparison.Ordinal)
                        && !nativeTimeline.Contains(forbidden, StringComparison.Ordinal),
                    $"startup performance schema contains private field {forbidden}"
                );
            }

            Assert(
                androidApp.Contains("PROCESS_ENTRY_USEC =", StringComparison.Ordinal)
                    && androidApp.Contains(
                        "splashScreen.setKeepOnScreenCondition(() -> !overlayHandoffReady)",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "showStartupOverlay(wipingAtlas || debugAtlasOverlayPreview)",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "completeNativeStartupPerformance()",
                        StringComparison.Ordinal
                    )
                    && recoveryBridge.Contains(
                        "Call(\"completeNativeStartupPerformance\")",
                        StringComparison.Ordinal
                    ),
                "native monotonic stages must hand off without a splash-to-Godot black gap"
            );
            int previousExitStart = androidApp.IndexOf(
                "startPreviousExitReport();",
                StringComparison.Ordinal
            );
            int godotSuperCreate = androidApp.IndexOf(
                "super.onCreate(savedInstanceState);",
                StringComparison.Ordinal
            );
            Assert(
                previousExitStart >= 0
                    && godotSuperCreate > previousExitStart
                    && androidApp.Contains(
                        "previousExitReportGate.markActivityReady()",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "previousExitReportGate.markQueryComplete()",
                        StringComparison.Ordinal
                    ),
                "previous-exit I/O must overlap bootstrap while prompt finalization waits for Activity readiness"
            );
            Assert(
                nativeTimeline.Contains("private final Event[] events", StringComparison.Ordinal)
                    && nativeTimeline.Contains("String encode()", StringComparison.Ordinal)
                    && !nativeTimeline.Contains("String label", StringComparison.Ordinal),
                "native startup schema must also remain bounded and numeric-only"
            );

            Assert(
                overlay.Contains("RefreshIntervalSeconds = 0.25", StringComparison.Ordinal)
                    && overlay.Contains(
                        "(double)snapshot.Progress.Done / snapshot.Progress.Total * 100",
                        StringComparison.Ordinal
                    )
                    && overlay.Contains("_progressBar.Visible = false", StringComparison.Ordinal)
                    && !overlay.Contains("Value +=", StringComparison.Ordinal),
                "visible startup progress must use real units and never time-driven percentages"
            );
            Assert(
                overlay.Contains("BeginMainThreadHandoff", StringComparison.Ordinal)
                    && overlay.Contains("PublishNativeSnapshot", StringComparison.Ordinal)
                    && overlay.Contains("\"showManagedStartupProgress\"", StringComparison.Ordinal)
                    && androidApp.Contains(
                        "public void showManagedStartupProgress(",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "SystemClock.elapsedRealtime() - managedStageAnchorRealtimeMs",
                        StringComparison.Ordinal
                    )
                    && tracker.Contains("TotalElapsedUsec", StringComparison.Ordinal)
                    && tracker.Contains("_postPlaySinceUsec", StringComparison.Ordinal)
                    && overlay.Contains(
                        "Loc.Tr(\"단계 \", \"Stage \", \"阶段 \")",
                        StringComparison.Ordinal
                    )
                    && overlay.Contains(
                        "Loc.Tr(\" · 전체 \", \" · Total \", \" · 总计 \")",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "SystemClock.elapsedRealtime() - managedStartupAnchorRealtimeMs",
                        StringComparison.Ordinal
                    ),
                "PLAY progress must show stage and total elapsed on the Android UI thread while Godot startup blocks"
            );
            Assert(
                launcherPatches.IndexOf(
                    "StartupPerformanceTracker.AdvanceTo(StartupStageId.UserWait)",
                    StringComparison.Ordinal
                )
                    < launcherPatches.IndexOf(
                        "await launcher.WaitForLaunch()",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "StartupPerformanceTracker.AdvanceTo(StartupStageId.CloudSync)",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "StartupStageId.GameSettings",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "StartupPerformanceTracker.AdvanceTo(StartupStageId.GameStartup)",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "StartupPerformanceTracker.MarkGameReady()",
                        StringComparison.Ordinal
                    ),
                "PLAY wait and owned startup work must have distinct real boundaries"
            );
            Assert(
                coveredLogoPatches.Contains("AccessTools.DeclaredMethod(", StringComparison.Ordinal)
                    && coveredLogoPatches.Contains("\"LaunchMainMenu\"", StringComparison.Ordinal)
                    && coveredLogoPatches.Contains(
                        "new[] { typeof(bool) }",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "CoveredStartupLogoPolicy.Enter(startupProgressCoversGameStartup)",
                        StringComparison.Ordinal
                    )
                    && !coveredLogoPatches.Contains("SkipIntroLogo", StringComparison.Ordinal),
                "covered startup may skip only the hidden logo without mutating its saved preference"
            );
            int gameSettingsStage = launcherPatches.IndexOf(
                "StartupPerformanceTracker.AdvanceTo(StartupStageId.GameSettings",
                StringComparison.Ordinal
            );
            int nativeHandoff = launcherPatches.IndexOf(
                "startupProgressOverlay.BeginMainThreadHandoff()",
                StringComparison.Ordinal
            );
            int settingsLoad = launcherPatches.IndexOf(
                "SaveManager.Instance.InitSettingsData()",
                StringComparison.Ordinal
            );
            Assert(
                gameSettingsStage >= 0
                    && gameSettingsStage < nativeHandoff
                    && nativeHandoff < settingsLoad,
                "the first post-PLAY stage must be published before synchronous game settings work"
            );
            Assert(
                androidApp.Contains("debug_startup_stage_delay_seconds", StringComparison.Ordinal)
                    && androidApp.Contains(
                        "consumeDebugStartupStageDelaySeconds",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains(
                        "debugStartupStageDelaySeconds > 20",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "ApplyDebugStartupStageDelayAsync",
                        StringComparison.Ordinal
                    )
                    && launcherPatches.Contains(
                        "await Task.Delay(TimeSpan.FromSeconds(seconds))",
                        StringComparison.Ordinal
                    ),
                "watchdog UI proof must use a bounded debug-only startup-stage hold"
            );
            Assert(
                androidApp.Contains("debug_preview_atlas_overlay", StringComparison.Ordinal)
                    && androidApp.Contains(
                        "showStartupOverlay(wipingAtlas || debugAtlasOverlayPreview)",
                        StringComparison.Ordinal
                    )
                    && androidApp.Contains("no cache mutation", StringComparison.Ordinal),
                "atlas overlay proof must reuse production copy without deleting cache"
            );
            int gameReady = launcherPatches.IndexOf(
                "StartupPerformanceTracker.MarkGameReady()",
                StringComparison.Ordinal
            );
            int hideManagedSurface = launcherPatches.IndexOf(
                "startupProgressOverlay.Visible = false",
                StringComparison.Ordinal
            );
            int renderedGameFrame =
                hideManagedSurface >= 0
                    ? launcherPatches.IndexOf(
                        "SceneTree.SignalName.ProcessFrame",
                        hideManagedSurface,
                        StringComparison.Ordinal
                    )
                    : -1;
            int endNativeHandoff = launcherPatches.IndexOf(
                "startupProgressOverlay.EndMainThreadHandoff()",
                StringComparison.Ordinal
            );
            Assert(
                gameReady < hideManagedSurface
                    && hideManagedSurface < renderedGameFrame
                    && renderedGameFrame < endNativeHandoff,
                "the native surface must remain until a Godot game frame has replaced stale PLAY UI"
            );
            Assert(
                modPatches.Contains(
                    "StartupPerformanceTracker.AdvanceTo(StartupStageId.ModDiscovery)",
                    StringComparison.Ordinal
                )
                    && modPatches.Contains(
                        "StartupPerformanceTracker.AdvanceTo(StartupStageId.ModLoad)",
                        StringComparison.Ordinal
                    )
                    && !modPatches.Contains(
                        "ReportProgress(StartupStageId.ModLoad, modId",
                        StringComparison.Ordinal
                    ),
                "mod spans must be stage-only and never include mod ids"
            );
            Assert(
                cloudOverlay.Contains(
                    "ReportProgress(StartupStageId.CloudSync, done, total)",
                    StringComparison.Ordinal
                ) && warmup.Contains("StartupStageId.ShaderWarmup", StringComparison.Ordinal),
                "determinate UI values must originate in real cloud/warmup work units"
            );
        }
    );

    Run(
        "frame-time summary uses nearest-rank percentiles and long-frame thresholds",
        () =>
        {
            var samples = Enumerable.Range(1, 100).Select(value => (long)value * 1_000).ToArray();
            var summary = FrameTimeSummary.Create(samples, frameBudgetUsec: 10_000);

            Assert(summary.Count == 100, "sample count");
            Assert(summary.P50Usec == 50_000, "p50 nearest rank");
            Assert(summary.P95Usec == 95_000, "p95 nearest rank");
            Assert(summary.P99Usec == 99_000, "p99 nearest rank");
            Assert(summary.MaxUsec == 100_000, "max interval");
            Assert(summary.FrameBudgetUsec == 10_000, "frame budget");
            Assert(summary.Over1XBudget == 90, ">1x budget count");
            Assert(summary.Over2XBudget == 80, ">2x budget count");
            Assert(summary.Over3XBudget == 70, ">3x budget count");
            Assert(summary.MaxConsecutiveOver2X == 80, "consecutive >2x frames");
            Assert(summary.Over50Ms == 50, ">50 ms count");
            Assert(summary.Over100Ms == 0, ">100 ms is strict");
            Assert(summary.Over250Ms == 0, ">250 ms count");
        }
    );

    Run(
        "frame metric validation probe is debug-only and bounded",
        () =>
        {
            var repository = FindRepositoryRoot();
            var android = File.ReadAllText(
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
            var probe = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "DebugFrameTimeProbe.cs")
            );
            var deckMutationProbe = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "DebugDeckCacheMutationProbe.cs"
                )
            );
            var gameplayWarmupSource = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "GameplayPipelineWarmup.cs"
                )
            );
            var modEntry = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "ModEntry.cs")
            );
            var quickRestartCompat = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "QuickRestartPerformanceCompatPatches.cs"
                )
            );
            var modLoadProbe = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "DebugModLoadTimingPatches.cs"
                )
            );
            var recoveryFlow = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "StartupRecoveryFlow.cs")
            );

            Assert(
                android.Contains("consumeDebugFrameProbe(String point)", StringComparison.Ordinal)
                    && android.Contains(
                        "!BuildConfig.VERSION_NAME.contains(\"-debug\")",
                        StringComparison.Ordinal
                    ),
                "production must not expose the frame fault"
            );
            Assert(
                probe.Contains("ValidationTargetIntervals = 180", StringComparison.Ordinal)
                    && probe.Contains("LongCaptureUsec = 120_000_000", StringComparison.Ordinal)
                    && probe.Contains("MenuCaptureUsec = 60_000_000", StringComparison.Ordinal)
                    && probe.Contains("MenuSettleUsec = 5_000_000", StringComparison.Ordinal)
                    && probe.Contains("MaxSpikeMarkers = 64", StringComparison.Ordinal)
                    && probe.Contains("InjectedStallMs = 100", StringComparison.Ordinal)
                    && probe.Contains("Thread.Sleep(InjectedStallMs)", StringComparison.Ordinal)
                    && probe.Contains("BeginGameplayInteractiveSegment()", StringComparison.Ordinal)
                    && probe.Contains(
                        "_segment = \"gameplay-interactive\"",
                        StringComparison.Ordinal
                    )
                    && probe.Contains("segment={probe._segment}", StringComparison.Ordinal)
                    && probe.Contains(
                        "_tree.ProcessFrame -= OnProcessFrame",
                        StringComparison.Ordinal
                    )
                    && probe.Contains("game-menu-idle", StringComparison.Ordinal)
                    && probe.Contains("ShouldAutoContinueRecoverySession", StringComparison.Ordinal)
                    && recoveryFlow.Contains(
                        "debug capture continuing session automatically",
                        StringComparison.Ordinal
                    ),
                "the probe must inject one bounded timing fault and detach"
            );
            Assert(
                android.Contains("\"game-baseline-120\"", StringComparison.Ordinal)
                    && android.Contains("\"game-baseline-safe-120\"", StringComparison.Ordinal)
                    && android.Contains(
                        "STS2_DISABLE_GAMEPLAY_PERFORMANCE_FIXES",
                        StringComparison.Ordinal
                    )
                    && android.Contains(
                        "[FrameProbe] gameplay baseline unavailable",
                        StringComparison.Ordinal
                    )
                    && probe.Contains("game-baseline-120", StringComparison.Ordinal)
                    && probe.Contains("game-baseline-safe-120", StringComparison.Ordinal)
                    && modEntry.Contains(
                        "GameplayPerformanceDisableEnvironmentVariable",
                        StringComparison.Ordinal
                    )
                    && modEntry.Contains(
                        "disableGameplayPerformanceFixes",
                        StringComparison.Ordinal
                    )
                    && modEntry.Contains(
                        "GameLoadFramePacingPatches.Apply(_harmony)",
                        StringComparison.Ordinal
                    )
                    && modEntry.Contains(
                        "DeckViewPerformancePatches.Apply(_harmony)",
                        StringComparison.Ordinal
                    ),
                "the same debug APK must provide a one-variable gameplay-fix baseline"
            );
            Assert(
                modEntry.IndexOf("ModAssemblyRegistry.Install()", StringComparison.Ordinal)
                    < modEntry.IndexOf(
                        "QuickRestartPerformanceCompatPatches.Install(_harmony)",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains(
                        "ModAssemblyRegistry.IsModAssembly(assembly)",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains(
                        "assembly.ManifestModule.ModuleVersionId",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains(
                        "SHA256.HashData(stream)",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains(
                        "BindingFlags.DeclaredOnly",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains("_attempted", StringComparison.Ordinal)
                    && quickRestartCompat.Contains(
                        "TryRemoveLifecyclePatches",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains("fail-open", StringComparison.Ordinal),
                "Quick Restart compatibility must load late, patch once, and fail open on exact-target mismatch"
            );
            Assert(
                quickRestartCompat.Contains(
                    "GetNestedType(\n                \"PauseMenuButtonPatch\"",
                    StringComparison.Ordinal
                )
                    && quickRestartCompat.Contains(
                        "ExactStaticMethod(\n                pauseMenuButtonPatchType,\n                \"OnPressed\"",
                        StringComparison.Ordinal
                    ),
                "Quick Restart pause-button behavior probe must target the exact nested owner"
            );
            Assert(
                quickRestartCompat.Contains(
                    "STS2_DEBUG_QUICK_RESTART_PROBE",
                    StringComparison.Ordinal
                )
                    && quickRestartCompat.Contains(
                        "[QuickRestartProbe] summary segment=",
                        StringComparison.Ordinal
                    )
                    && quickRestartCompat.Contains("file_exists_calls=", StringComparison.Ordinal)
                    && android.Contains(
                        "debug_quick_restart_method_probe",
                        StringComparison.Ordinal
                    )
                    && android.Contains(
                        "STS2_DISABLE_QUICK_RESTART_PERFORMANCE_FIX",
                        StringComparison.Ordinal
                    )
                    && probe.Contains(
                        "game-quickrestart-baseline-partition-120",
                        StringComparison.Ordinal
                    )
                    && probe.Contains("game-quickrestart-partition-120", StringComparison.Ordinal),
                "Quick Restart A/B and per-method wrappers must remain explicit debug-only instrumentation"
            );
            Assert(
                modLoadProbe.Contains("STS2_DEBUG_MOD_LOAD_TIMING", StringComparison.Ordinal)
                    && modLoadProbe.Contains(
                        "ModManager.CallModInitializer(Type)",
                        StringComparison.Ordinal
                    )
                    && modLoadProbe.Contains("nameof(Harmony.PatchAll)", StringComparison.Ordinal)
                    && modLoadProbe.Contains("item={_currentOrdinal}", StringComparison.Ordinal)
                    && !modLoadProbe.Contains("manifest.id", StringComparison.Ordinal)
                    && !modLoadProbe.Contains("assembly.Location", StringComparison.Ordinal)
                    && android.Contains("debug_mod_load_probe", StringComparison.Ordinal)
                    && android.Contains("STS2_DEBUG_MOD_LOAD_TIMING", StringComparison.Ordinal),
                "mod-load attribution must be debug-only, anonymous, and span initializer/PatchAll owners"
            );
            var transitionTrace = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "DebugTransitionTimingPatches.cs"
                )
            );
            var modGuardAlert = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "ModGuardAlert.cs")
            );
            Assert(
                transitionTrace.Contains(
                    "DebugFrameTimeProbe.IsGameCaptureActive",
                    StringComparison.Ordinal
                )
                    && transitionTrace.Contains(
                        "GetEnvironmentVariable(ArmEnvironmentVariable)",
                        StringComparison.Ordinal
                    )
                    && android.Contains(
                        "Os.setenv(\"STS2_DEBUG_TRANSITION_TIMING\", \"1\", true)",
                        StringComparison.Ordinal
                    )
                    && transitionTrace.Contains(
                        "LogThresholdUsec = 2_000",
                        StringComparison.Ordinal
                    )
                    && transitionTrace.Contains("[FrameTrace]", StringComparison.Ordinal),
                "transition attribution must install only for an explicitly armed debug game capture"
            );
            Assert(
                modGuardAlert.Contains(
                    "GetEnvironmentVariable(QaToolsEnvironmentVariable)",
                    StringComparison.Ordinal
                )
                    && android.Contains(
                        "Os.setenv(\"STS2_DEBUG_QA_TOOLS\", \"1\", true)",
                        StringComparison.Ordinal
                    ),
                "the QA file watcher must not poll external storage in production gameplay"
            );
            var deckViewCache = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "DeckViewPerformancePatches.cs"
                )
            );
            Assert(
                deckViewCache.Contains(
                    "ReferenceEquals(player, cachedPlayer)",
                    StringComparison.Ordinal
                )
                    && deckViewCache.Contains(
                        "WeakReference<GodotObject>",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains("WeakReference<object>", StringComparison.Ordinal)
                    && !deckViewCache.Contains(
                        "WeakReference<NDeckViewScreen>",
                        StringComparison.Ordinal
                    )
                    && !deckViewCache.Contains("WeakReference<Player>", StringComparison.Ordinal)
                    && !deckViewCache.Contains("WeakReference<CardModel>", StringComparison.Ordinal)
                    && deckViewCache.Contains(
                        "GodotObject.IsInstanceValid(cached)",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains(
                        "NDebugAudioManager.Instance?.Play(\"map_open.mp3\")",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains(
                        "NCapstoneContainer.Instance.Open(cached)",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains(
                        "nameof(NDeckViewScreen.AfterCapstoneClosed)",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains(
                        "ReferenceEquals(NCapstoneContainer.Instance.CurrentCapstoneScreen, cached)",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains("cached.QueueFree()", StringComparison.Ordinal)
                    && deckViewCache.Split("if (TestMode.IsOn)", StringSplitOptions.None).Length
                        >= 3
                    && deckViewCache.Contains(
                        "__instance.Visible = false",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains("cached.Visible = true", StringComparison.Ordinal),
                "deck view reuse must preserve behavior without eagerly binding game assemblies in launcher-only mode"
            );
            Assert(
                deckViewCache.Contains(
                    "PileContentsChangedMethod = \"OnPileContentsChanged\"",
                    StringComparison.Ordinal
                )
                    && deckViewCache.Contains(
                        "OnPileContentsChangedPrefix",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains("if (__instance.Visible)", StringComparison.Ordinal)
                    && deckViewCache.Contains(
                        "ClearCachedScreen(queueFree: true)",
                        StringComparison.Ordinal
                    ),
                "a hidden cached deck must be invalidated without rebuilding its card grid"
            );
            Assert(
                deckViewCache.Contains(
                    "card.Upgraded += OnCachedCardUpgraded",
                    StringComparison.Ordinal
                )
                    && deckViewCache.Contains(
                        "card.Upgraded -= OnCachedCardUpgraded",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains(
                        "TreeExiting += OnCachedScreenTreeExiting",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains(
                        "TreeExiting -= OnCachedScreenTreeExiting",
                        StringComparison.Ordinal
                    )
                    && deckViewCache.Contains("ClearCachedScreen", StringComparison.Ordinal)
                    && deckViewCache.Contains("_invalidateAfterClose", StringComparison.Ordinal),
                "card upgrades and run-tree teardown must invalidate the retained deck without leaking subscriptions"
            );
            Assert(
                android.Contains("consumeDebugDeckCacheMutationProbe()", StringComparison.Ordinal)
                    && android.Contains(
                        "intent.removeExtra(\"debug_deck_cache_mutation_probe\")",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmupSource.Contains(
                        "DebugDeckCacheMutationProbe.TryRunAsync(hand, player)",
                        StringComparison.Ordinal
                    )
                    && deckMutationProbe.Contains(
                        "DebugFrameTimeProbe.IsGameCaptureActive",
                        StringComparison.Ordinal
                    )
                    && deckMutationProbe.Contains(
                        "player.Deck.AddInternal",
                        StringComparison.Ordinal
                    )
                    && deckMutationProbe.Contains(
                        "player.Deck.RemoveInternal",
                        StringComparison.Ordinal
                    )
                    && deckMutationProbe.Contains("UpgradeInternal()", StringComparison.Ordinal)
                    && deckMutationProbe.Contains("DowngradeInternal()", StringComparison.Ordinal)
                    && deckMutationProbe.Contains("finally", StringComparison.Ordinal)
                    && deckMutationProbe.Contains("RestoreMutations", StringComparison.Ordinal)
                    && deckMutationProbe.Contains("pass={Bit(pass)}", StringComparison.Ordinal)
                    && !deckMutationProbe.Contains(".Title", StringComparison.Ordinal)
                    && !deckMutationProbe.Contains(".Id", StringComparison.Ordinal),
                "the debug deck mutation proof must be explicit, reversible, and identity-free"
            );
            var framePacing = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Patches",
                    "GameLoadFramePacingPatches.cs"
                )
            );
            Assert(
                framePacing.Contains("LoadRunFramePaced", StringComparison.Ordinal)
                    && framePacing.Contains("StartCombatFramePaced", StringComparison.Ordinal)
                    && framePacing.Contains(
                        "SceneTree.SignalName.ProcessFrame",
                        StringComparison.Ordinal
                    )
                    && framePacing.Contains("MapDrawingsToLoad = null", StringComparison.Ordinal)
                    && framePacing.Contains(
                        "Hook.AfterRoomEntered(runState, room)",
                        StringComparison.Ordinal
                    )
                    && !framePacing.Contains("PrimeFirstCardRender", StringComparison.Ordinal),
                "frame pacing must preserve load/run side effects without speculative fake gameplay work"
            );
            Assert(
                android.Contains("game-safe-120", StringComparison.Ordinal)
                    && android.Contains("game-safe-300", StringComparison.Ordinal)
                    && android.Contains("game-baseline-safe-120", StringComparison.Ordinal)
                    && android.Contains("consumeDebugModSafeMode()", StringComparison.Ordinal)
                    && android.Contains("game-partition-120", StringComparison.Ordinal)
                    && android.Contains("game-menu-safe-60", StringComparison.Ordinal)
                    && android.Contains("game-menu-partition-60", StringComparison.Ordinal)
                    && android.Contains("consumeDebugModPartition()", StringComparison.Ordinal)
                    && android.Contains("isValidDebugModPartition", StringComparison.Ordinal)
                    && probe.Contains("game-partition-120", StringComparison.Ordinal)
                    && probe.Contains("game-menu-partition-60", StringComparison.Ordinal)
                    && probe.Contains("game-safe-300", StringComparison.Ordinal)
                    && probe.Contains("ExtendedCaptureUsec = 300_000_000", StringComparison.Ordinal)
                    && android.Contains("markDebugFrameSpike", StringComparison.Ordinal),
                "mod comparisons and trace markers must stay behind the debug bridge"
            );
            var gameplayWarmup = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "GameplayPipelineWarmup.cs"
                )
            );
            Assert(
                gameplayWarmup.Contains("OS.HasFeature(\"android\")", StringComparison.Ordinal)
                    && gameplayWarmup.Contains(
                        "hand.GetViewport() is SubViewport",
                        StringComparison.Ordinal
                    )
                    && !gameplayWarmup.Contains("new SubViewport", StringComparison.Ordinal)
                    && gameplayWarmup.Contains("CoverFirstHandAsync(", StringComparison.Ordinal)
                    && gameplayWarmup.Contains("Player player,", StringComparison.Ordinal)
                    && gameplayWarmup.Contains("Action startFirstTurn", StringComparison.Ordinal)
                    && gameplayWarmup.Contains("startFirstTurn();", StringComparison.Ordinal)
                    && gameplayWarmup.Contains("hand.ActiveHolders.Count", StringComparison.Ordinal)
                    && !gameplayWarmup.Contains("hand.Add(", StringComparison.Ordinal)
                    && gameplayWarmup.Contains(
                        "StableFrameWindowMs = 650",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains("MaximumWaitMs = 7_000", StringComparison.Ordinal)
                    && gameplayWarmup.Contains(
                        "RenderingServer.SignalName.FramePostDraw",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains(
                        "NDeckViewScreen.ShowScreen(player)",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains(
                        "GetSubmenuType<NPauseMenu>()",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains(
                        "NCapstoneContainer.Instance.Close()",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains("new ScreenBackground", StringComparison.Ordinal)
                    && gameplayWarmup.Contains(
                        "MouseFilter = Control.MouseFilterEnum.Ignore",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains(
                        "Preparing gameplay rendering",
                        StringComparison.Ordinal
                    )
                    && gameplayWarmup.Contains("cover.QueueFree()", StringComparison.Ordinal),
                "Android must cover the real first hand until its Canvas pipeline set is stable"
            );
            Assert(
                !gameplayWarmup.Contains("MapExposureProbe", StringComparison.Ordinal)
                    && !gameplayWarmup.Contains(
                        "RenderingServer.CanvasItemSetVisible",
                        StringComparison.Ordinal
                    )
                    && !android.Contains("game-map-", StringComparison.Ordinal)
                    && transitionTrace.Contains(
                        "DebugFrameTimeProbe.BeginInteraction(\"map-open\")",
                        StringComparison.Ordinal
                    )
                    && probe.Contains("InteractionSampleCount = 60", StringComparison.Ordinal)
                    && probe.Contains("[InteractionProbe] summary", StringComparison.Ordinal),
                "the rejected first-map exposure candidate must be removed while bounded interaction evidence remains"
            );
            Assert(
                gameplayWarmup.Contains(
                    "DebugFrameTimeProbe.BeginGameplayInteractiveSegment()",
                    StringComparison.Ordinal
                ),
                "the canonical probe must measure 120 seconds after the covered load is revealed"
            );
            Assert(
                framePacing.IndexOf(
                    "CombatManager.Instance.SetUpCombat(room.CombatState)",
                    StringComparison.Ordinal
                )
                    < framePacing.IndexOf(
                        "Hook.AfterRoomEntered(runState, room)",
                        StringComparison.Ordinal
                    )
                    && framePacing.IndexOf(
                        "Hook.AfterRoomEntered(runState, room)",
                        StringComparison.Ordinal
                    )
                        < framePacing.IndexOf(
                            "GameplayPipelineWarmup.CoverFirstHandAsync",
                            StringComparison.Ordinal
                        )
                    && framePacing.Contains(
                        "CombatManager.Instance.AfterCombatRoomLoaded",
                        StringComparison.Ordinal
                    ),
                "the real first turn must start under the cover after room-entry hooks"
            );
        }
    );

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

            var partition = ModRecoveryPolicy.BuildPartition(1, 2, mods);
            Assert(
                partition.Action == RecoveryAction.DiagnosticPartition,
                "debug partition action"
            );
            Assert(!partition.ShouldExposeDirectory("/mods", "/mods/a"), "partition A hidden");
            Assert(!partition.ShouldExposeDirectory("/mods", "/mods/b"), "partition B hidden");
            Assert(partition.ShouldExposeDirectory("/mods", "/mods/c"), "partition C visible");
            Assert(partition.ShouldExposeDirectory("/mods", "/mods/d"), "partition D visible");
            Assert(
                ModRecoveryPolicy.BuildPartition(9, 2, mods).SkipOptionalWarmup,
                "invalid debug partition must fail closed to Safe Mode"
            );

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
        "game update interruption exposes only an old or new complete tuple",
        () =>
        {
            var transactionRoot = Path.Combine(root, "game-transaction");
            Reset(transactionRoot);
            WriteInstall(transactionRoot, "old");

            var partial = GameInstallTransaction.Begin(transactionRoot, forceFresh: false);
            ReplaceInstallFile(partial.StagingGameDirectory, "SlayTheSpire2.pck", "GDPCnew");
            GameInstallTransaction.Recover(transactionRoot);
            AssertInstallTuple(transactionRoot, "old", "partial download");

            foreach (
                var faultPoint in new[]
                {
                    GameInstallFaultPoint.AfterPrepared,
                    GameInstallFaultPoint.AfterActiveRetired,
                    GameInstallFaultPoint.AfterStagedActivated,
                }
            )
            {
                GameInstallTransaction.DiscardStaging(transactionRoot);
                var transaction = GameInstallTransaction.Begin(transactionRoot, forceFresh: true);
                WriteInstallFiles(transaction.StagingGameDirectory, "new");
                transaction.Prepare(
                    GameInstallTuple.Capture(
                        transaction.StagingGameDirectory,
                        "public-beta",
                        "new-build",
                        new Dictionary<uint, ulong> { [2868842] = 5045320271505434676 }
                    )
                );

                if (faultPoint == GameInstallFaultPoint.AfterPrepared)
                {
                    var extraAssembly = Path.Combine(
                        transaction.StagingGameDirectory,
                        "data_sts2_windows_x86_64",
                        "unexpected.dll"
                    );
                    File.WriteAllText(extraAssembly, "mixed");
                    bool rejectedMixedAssemblySet = false;
                    try
                    {
                        transaction.Commit();
                    }
                    catch (InvalidDataException)
                    {
                        rejectedMixedAssemblySet = true;
                    }
                    Assert(
                        rejectedMixedAssemblySet,
                        "activation must reject a changed game assembly set"
                    );
                    File.Delete(extraAssembly);
                }

                try
                {
                    transaction.Commit(point =>
                    {
                        if (point == faultPoint)
                            throw new GameInstallInterruptionException(point.ToString());
                    });
                }
                catch (GameInstallInterruptionException) { }

                GameInstallTransaction.Recover(transactionRoot);
                var (pck, assembly) = ReadInstallTuple(transactionRoot);
                Assert(pck == assembly, $"{faultPoint} produced mixed {pck}/{assembly}");
                Assert(pck is "old" or "new", $"{faultPoint} unknown tuple {pck}");
                if (pck == "new")
                {
                    var active = GameInstallTransaction.ReadActiveTuple(transactionRoot);
                    Assert(active?.Branch == "public-beta", $"{faultPoint} active branch");
                    Assert(active?.BuildId == "new-build", $"{faultPoint} active build");
                }

                GameInstallTransaction.CompleteValidation(transactionRoot);
                Assert(
                    !Directory.Exists(GameInstallTransaction.GetRollbackPath(transactionRoot)),
                    $"{faultPoint} rollback cleanup"
                );
                if (pck == "old")
                    continue;

                // Restore a deterministic old baseline for the next injected window.
                Reset(transactionRoot);
                WriteInstall(transactionRoot, "old");
            }

            var repository = FindRepositoryRoot();
            var downloader = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Steam", "DepotDownloader.cs")
            );
            var model = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "LauncherModel.cs")
            );
            var faultInjector = File.ReadAllText(
                Path.Combine(
                    repository,
                    "src",
                    "STS2Mobile",
                    "Launcher",
                    "GameInstallFaultInjector.cs"
                )
            );
            var android = File.ReadAllText(
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
                downloader.Contains("GameInstallTransaction.Begin", StringComparison.Ordinal)
                    && downloader.Contains("transaction.Prepare", StringComparison.Ordinal)
                    && downloader.Contains("transaction.Commit", StringComparison.Ordinal),
                "depot writes must activate only through the directory transaction"
            );
            Assert(
                !model.Contains(
                    "Directory.Delete(gameDir, recursive: true)",
                    StringComparison.Ordinal
                )
                    && model.IndexOf("SaveSelectedBranch", StringComparison.Ordinal)
                        > model.IndexOf("DownloadAsync", StringComparison.Ordinal),
                "branch selection must publish only after a successful active commit"
            );
            Assert(
                android.Contains("GameInstallRecovery.recover", StringComparison.Ordinal)
                    && android.IndexOf("GameInstallRecovery.recover", StringComparison.Ordinal)
                        < android.IndexOf(
                            "setupAssemblies(gameInstallReady);",
                            StringComparison.Ordinal
                        )
                    && android.Contains(
                        "private void setupAssemblies(boolean copyGameAssemblies)",
                        StringComparison.Ordinal
                    )
                    && android.IndexOf(
                        "Copied \" + count + \" BCL assemblies from assets",
                        StringComparison.Ordinal
                    ) < android.IndexOf("if (!copyGameAssemblies)", StringComparison.Ordinal),
                "Android must always extract launcher assemblies after recovery while refusing partial game assemblies"
            );
            foreach (
                var faultPoint in new[]
                {
                    "after-staging-created",
                    "after-file-verified",
                    "after-depot-manifest-committed",
                    "after-all-depots-verified",
                    "after-pck-patched",
                }
            )
            {
                Assert(
                    downloader.Contains($"Hit(\"{faultPoint}\")", StringComparison.Ordinal),
                    $"missing deterministic update fault hook {faultPoint}"
                );
            }
            Assert(
                downloader.Contains(
                    "transaction.Commit(_faultInjector.Hit)",
                    StringComparison.Ordinal
                ),
                "directory activation must expose every rename fault point"
            );
            Assert(
                faultInjector.Contains(
                    "DllImport(\"libc\", EntryPoint = \"kill\"",
                    StringComparison.Ordinal
                )
                    && faultInjector.Contains("SigKill = 9", StringComparison.Ordinal)
                    && faultInjector.IndexOf(
                        "KillProcess(GetProcessId(), SigKill)",
                        StringComparison.Ordinal
                    )
                        < faultInjector.IndexOf(
                            "Environment.FailFast($\"debug game-install fault",
                            StringComparison.Ordinal
                        ),
                "managed update fault injection must kill the Android host process"
            );
            Assert(
                android.Contains(
                    "clearManagedGameInstallFaultOutsideDebug();",
                    StringComparison.Ordinal
                )
                    && android.Contains(
                        "new File(getFilesDir(), GAME_INSTALL_FAULT_MARKER)",
                        StringComparison.Ordinal
                    )
                    && android.IndexOf(
                        "clearManagedGameInstallFaultOutsideDebug();",
                        StringComparison.Ordinal
                    ) < android.IndexOf("GameInstallRecovery.recover", StringComparison.Ordinal),
                "production startup must discard any debug fault marker before recovery"
            );
            foreach (
                var faultPoint in new[]
                {
                    "before-install-recovery",
                    "after-install-recovery",
                    "before-cache-staging",
                    "after-cache-staging",
                    "before-assembly-sync",
                    "after-assembly-sync",
                }
            )
            {
                Assert(
                    android.Contains(
                        $"maybeInjectDebugGameInstallFault(\"{faultPoint}\")",
                        StringComparison.Ordinal
                    ),
                    $"missing deterministic Android fault hook {faultPoint}"
                );
            }
            Assert(
                android.Contains("debug_renderer_override", StringComparison.Ordinal)
                    && android.Contains(
                        "commands.add(\"gl_compatibility\")",
                        StringComparison.Ordinal
                    )
                    && android.Contains("commands.add(\"opengl3\")", StringComparison.Ordinal)
                    && android.Contains(
                        "VERSION_NAME.contains(\"-debug\")",
                        StringComparison.Ordinal
                    ),
                "renderer capability probe must be explicit and unavailable in production builds"
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
        "renderer recovery is repeated-pre-frame, foreground-only, and one-shot",
        () =>
        {
            var repository = FindRepositoryRoot();
            var javaRoot = Path.Combine(
                repository,
                "android",
                "src",
                "com",
                "game",
                "sts2launcher",
                "modmanager"
            );
            var godotApp = File.ReadAllText(Path.Combine(javaRoot, "GodotApp.java"));
            var policy = File.ReadAllText(Path.Combine(javaRoot, "RendererRecoveryPolicy.java"));
            var patches = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Patches", "LauncherPatches.cs")
            );
            var flow = File.ReadAllText(
                Path.Combine(repository, "src", "STS2Mobile", "Launcher", "StartupRecoveryFlow.cs")
            );

            Assert(
                policy.Contains("RECOVERY_THRESHOLD", StringComparison.Ordinal)
                    && policy.Contains("launcher-awaiting-frame", StringComparison.Ordinal)
                    && !policy.Contains("\"QueuePresentKHR\"", StringComparison.Ordinal),
                "renderer recovery must use durable pre-frame state, never driver log text"
            );
            Assert(
                godotApp.IndexOf("markStartupForeground(false);", StringComparison.Ordinal)
                    < godotApp.IndexOf("super.onPause();", StringComparison.Ordinal),
                "background state must persist before Godot tears down its Surface"
            );
            Assert(
                godotApp.Contains("RENDERER_COMPATIBILITY_ONCE_PREF", StringComparison.Ordinal)
                    && godotApp.Contains(
                        ".remove(RENDERER_COMPATIBILITY_ONCE_PREF)",
                        StringComparison.Ordinal
                    )
                    && godotApp.Contains(
                        "isCompatibilityRendererSession",
                        StringComparison.Ordinal
                    ),
                "compatibility renderer selection must be consumed once and exposed to recovery UI"
            );
            Assert(
                patches.IndexOf("launcher-awaiting-frame", StringComparison.Ordinal)
                    < patches.IndexOf("SceneTree.SignalName.ProcessFrame", StringComparison.Ordinal)
                    && patches.IndexOf(
                        "SceneTree.SignalName.ProcessFrame",
                        StringComparison.Ordinal
                    ) < patches.IndexOf("launcher-ready", StringComparison.Ordinal),
                "launcher-ready must be recorded only after a main-loop frame"
            );
            Assert(
                flow.Contains("Restart with Vulkan", StringComparison.Ordinal)
                    && flow.Contains("Continue in compatibility mode", StringComparison.Ordinal),
                "a compatibility session must offer an explicit Vulkan restore path"
            );
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
            Assert(
                modEntry.Contains("Call(\"hideLoadingOverlay\")", StringComparison.Ordinal),
                "the always-on native overlay must also hand off to a standalone launcher"
            );
            Assert(
                !modEntry.Contains(
                    "Callable.From(() => LauncherModel.GetGodotApp()?.Call",
                    StringComparison.Ordinal
                ),
                "the deferred handoff must be a void callback, not a nullable Variant callback"
            );
        }
    );

    Run(
        "native startup overlay releases launcher input before PLAY wait",
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
            var playReady = launcherPatches.IndexOf(
                "PatchHelper.Log(\"Launcher ready for PLAY\");",
                StringComparison.Ordinal
            );
            var recoveryResolved = launcherPatches.IndexOf(
                "await StartupRecoveryFlow.ResolveRecoveryAsync(launcher)",
                StringComparison.Ordinal
            );

            Assert(initialized >= 0, "game launcher must report successful initialization");
            Assert(overlayHidden >= 0, "game launcher must dismiss the native startup overlay");
            Assert(playWait >= 0, "game launcher must await explicit PLAY");
            Assert(
                initialized < overlayHidden && overlayHidden < playWait,
                "the touch-swallowing native startup overlay must be hidden once launcher UI is ready"
            );
            Assert(
                recoveryResolved >= 0 && playReady > recoveryResolved && playReady < playWait,
                "PLAY readiness must be reported only after recovery resolves and before input wait"
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
            Assert(
                buildScript.Contains("scripts/make-bootstrap-pck.py", StringComparison.Ordinal),
                "APK build must generate the fresh-install Godot bootstrap"
            );

            var dockerBuild = File.ReadAllText(Path.Combine(repository, "docker", "build-apk.sh"));
            foreach (
                var requiredCheck in new[]
                {
                    "tools/stability-tests/stability-tests.csproj",
                    "tools/stability-tests-java/run.sh",
                    "tools/device-performance/tests/run.sh",
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
            Assert(
                dockerBuild.Contains("assets/bootstrap.pck", StringComparison.Ordinal)
                    && dockerBuild.Contains("bootstrap_sha256", StringComparison.Ordinal),
                "container APK build must verify the exact bootstrap inside the final APK"
            );
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

static void WriteInstall(string transactionRoot, string version)
{
    var game = GameInstallTransaction.GetActivePath(transactionRoot);
    Directory.CreateDirectory(game);
    WriteInstallFiles(game, version);
}

static void WriteInstallFiles(string gameDirectory, string version)
{
    Directory.CreateDirectory(gameDirectory);
    Directory.CreateDirectory(Path.Combine(gameDirectory, "data_sts2_windows_x86_64"));
    File.WriteAllText(Path.Combine(gameDirectory, "SlayTheSpire2.pck"), "GDPC" + version);
    File.WriteAllText(Path.Combine(gameDirectory, "data_sts2_windows_x86_64", "sts2.dll"), version);
}

static void ReplaceInstallFile(string gameDirectory, string relativePath, string content)
{
    var path = Path.Combine(gameDirectory, relativePath);
    var temporary = path + ".downloading";
    File.WriteAllText(temporary, content);
    File.Move(temporary, path, overwrite: true);
}

static (string Pck, string Assembly) ReadInstallTuple(string transactionRoot)
{
    var game = GameInstallTransaction.GetActivePath(transactionRoot);
    return (
        File.ReadAllText(Path.Combine(game, "SlayTheSpire2.pck"))[4..],
        File.ReadAllText(Path.Combine(game, "data_sts2_windows_x86_64", "sts2.dll"))
    );
}

static void AssertInstallTuple(string transactionRoot, string expected, string context)
{
    var (pck, assembly) = ReadInstallTuple(transactionRoot);
    Assert(pck == expected && assembly == expected, $"{context}: {pck}/{assembly}");
}

static void Reset(string path)
{
    if (Directory.Exists(path))
        Directory.Delete(path, recursive: true);
    Directory.CreateDirectory(path);
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
