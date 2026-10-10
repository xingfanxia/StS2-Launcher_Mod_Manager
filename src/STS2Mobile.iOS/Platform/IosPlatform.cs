using System;
using System.IO;
using Godot;

namespace STS2Mobile.Platform;

// iOS implementations of the services in PlatformHost, plus the process
// environment the shared code expects before Harmony runs.
internal static class IosPlatform
{
    // The app container. Godot's OS singleton is not safe to call this early.
    public static string Home => System.Environment.GetEnvironmentVariable("HOME") ?? "";
    public static string Documents => Path.Combine(Home, "Documents");

    public static void Initialize()
    {
        PlatformHost.Secrets = new KeychainSecretProtector();
        PlatformHost.QrCodeRenderer = RenderQrCode;
        PlatformHost.RequiresStoragePermission = false;
        PlatformHost.CoverFirstHand = true;
        PlatformHost.LauncherFallbackFonts = new[]
        {
            "res://fonts/zhs/NotoSansMonoCJKsc-Regular.otf",
            "res://fonts/jpn/NotoSansCJKjp-Regular.otf",
        };
        PlatformHost.UniqueSteamLoginIds = true;
        PlatformHost.PrepareGamePack = gameDirectory =>
        {
            var assemblies = Directory.GetFiles(gameDirectory, "sts2.dll", SearchOption.AllDirectories);
            if (assemblies.Length != 1)
                throw new InvalidDataException(
                    $"Expected one game assembly in the download, found {assemblies.Length}"
                );
            IosGamePack.Prepare(
                Path.Combine(gameDirectory, "SlayTheSpire2.pck"),
                assemblies[0],
                PatchHelper.Log
            );
        };
        PlatformHost.RestartApp = () =>
        {
            PatchHelper.Log("[Launcher] Restart required; asking the user to reopen the app");
            IosNative.ShowRestartRequired();
        };

        // iOS already points TMPDIR at the container; MonoMod only needs it to exist.
        var temporary = Path.GetTempPath();
        Directory.CreateDirectory(temporary);
        var config = Path.Combine(Home, "Library", "Application Support", "config");
        Directory.CreateDirectory(config);
        System.Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", config);
        Console.Error.WriteLine($"[STS2Mobile] [Diag] iOS TMPDIR={temporary} XDG_CONFIG_HOME={config}");

        // Test hook: STS2_DEBUG_DISABLE_MODS=A,B stashes those mod folders the way
        // the Mod Manager's disable switch does, so a host-driven run can vary the set.
        foreach (var name in (System.Environment.GetEnvironmentVariable("STS2_DEBUG_DISABLE_MODS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var source = Path.Combine(AppPaths.ExternalModsDir, name);
            var target = Path.Combine(AppPaths.DisabledModsDir, name);
            if (name.Contains('/') || name.Contains("..") || !Directory.Exists(source) || Directory.Exists(target))
                continue;
            Directory.CreateDirectory(AppPaths.DisabledModsDir);
            Directory.Move(source, target);
            Console.Error.WriteLine($"[STS2Mobile] [Diag] Test hook disabled mod {name}");
        }
        foreach (var name in (System.Environment.GetEnvironmentVariable("STS2_DEBUG_ENABLE_MODS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var source = Path.Combine(AppPaths.DisabledModsDir, name);
            var target = Path.Combine(AppPaths.ExternalModsDir, name);
            if (name.Contains('/') || name.Contains("..") || !Directory.Exists(source) || Directory.Exists(target))
                continue;
            Directory.Move(source, target);
            Console.Error.WriteLine($"[STS2Mobile] [Diag] Test hook enabled mod {name}");
        }
    }

    // The iOS base library throws from Process.StartTime, which SteamKit2 reads
    // when it creates a client. Any stable time for this process is enough.
    private static readonly DateTime ProcessStart = DateTime.Now;

    public static void ApplyPatches(HarmonyLib.Harmony harmony)
    {
        var getter = HarmonyLib.AccessTools.PropertyGetter(
            typeof(System.Diagnostics.Process),
            nameof(System.Diagnostics.Process.StartTime)
        );
        harmony.Patch(
            getter,
            prefix: new HarmonyLib.HarmonyMethod(typeof(IosPlatform), nameof(StartTimePrefix))
        );
        PatchHelper.Log("Patched Process.StartTime (iOS)");

        // Godot reports the read-only app bundle as its data directory on iOS.
        // The shared code keeps the game, accounts and saves under it, as the
        // Android build does under its files directory.
        harmony.Patch(
            HarmonyLib.AccessTools.Method(typeof(OS), nameof(OS.GetDataDir)),
            prefix: new HarmonyLib.HarmonyMethod(typeof(IosPlatform), nameof(DataDirPrefix))
        );
        PatchHelper.Log("Patched OS.GetDataDir (iOS)");
        // When the engine prints an error it asks for the C# stack, and GodotSharp
        // reads each frame's parameter list. Runtime-generated wrapper frames have
        // none that Mono can describe, and asking aborts the process.
        var describeFrame = HarmonyLib.AccessTools.Method(
            typeof(GodotObject).Assembly.GetType("Godot.DebuggingUtils"),
            "GetStackFrameMethodDecl"
        );
        if (describeFrame != null)
        {
            harmony.Patch(
                describeFrame,
                prefix: new HarmonyLib.HarmonyMethod(typeof(IosPlatform), nameof(DescribeFramePrefix))
            );
            PatchHelper.Log("Patched DebuggingUtils.GetStackFrameMethodDecl (iOS)");
        }
        else
        {
            PatchHelper.Log("DebuggingUtils.GetStackFrameMethodDecl not found; engine error traces stay unguarded");
        }

        // System font fallback cannot supply CJK glyphs here: since iOS 18 the system
        // Chinese font uses a glyph format FreeType does not read. Mods draw text
        // with the engine's default font or a SystemFont and rely on that fallback,
        // so both get the game pack's CJK fonts behind them instead.
        _harmony = harmony;
        Callable.From(AddFallbacksToDefaultFonts).CallDeferred();
        STS2Mobile.Patches.DamageMeterResumePatches.Apply();

        IosTouchHover.Start();
        IosParticles.Start();
        IosJitWarmup.Start();
        IosFrameStats.StartIfRequested();
        IosAutomation.StartIfRequested();
    }

    private static HarmonyLib.Harmony _harmony;
    private static Godot.Collections.Array<Font> _cjkFonts;

    private static Godot.Collections.Array<Font> CjkFonts()
    {
        if (_cjkFonts != null)
            return _cjkFonts;
        var fonts = new Godot.Collections.Array<Font>();
        foreach (var path in PlatformHost.LauncherFallbackFonts)
        {
            if (ResourceLoader.Exists(path) && ResourceLoader.Load(path) is Font font)
                fonts.Add(font);
        }
        return _cjkFonts = fonts;
    }

    // PingFang is installed on every iOS device and is the one name FreeType
    // cannot read there, so a font that resolves to it draws nothing at all.
    private static void SystemFontNamesPrefix(ref string[] value)
    {
        if (value == null)
            return;
        var readable = new System.Collections.Generic.List<string>();
        foreach (var name in value)
        {
            if (name != null && !name.StartsWith("PingFang", StringComparison.OrdinalIgnoreCase))
                readable.Add(name);
        }
        if (readable.Count == 0)
            readable.Add("sans-serif");
        value = readable.ToArray();
    }

    private static void SystemFontNamesPostfix(SystemFont __instance)
    {
        try
        {
            if (CjkFonts().Count > 0 && __instance.Fallbacks.Count == 0)
                __instance.Fallbacks = CjkFonts();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Fonts] SystemFont fallback failed: {ex.Message}");
        }
    }

    // Appends the CJK fonts behind a font that lacks them. Returns true when changed.
    private static bool EnsureCjkFallback(Font font)
    {
        if (font == null || CjkFonts().Contains(font))
            return false;
        var fallbacks = font.Fallbacks;
        var changed = false;
        foreach (var cjk in CjkFonts())
        {
            if (!fallbacks.Contains(cjk))
            {
                fallbacks.Add(cjk);
                changed = true;
            }
        }
        if (changed)
            font.Fallbacks = fallbacks;
        return changed;
    }

    private static void FontAssignedPrefix(object[] __args)
    {
        try
        {
            foreach (var argument in __args)
            {
                if (argument is Font font)
                    EnsureCjkFallback(font);
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Fonts] Fallback for an assigned font failed: {ex.Message}");
        }
    }

    private static void AddFallbacksToDefaultFonts()
    {
        if (Engine.GetMainLoop() is not SceneTree)
        {
            Callable.From(AddFallbacksToDefaultFonts).CallDeferred();
            return;
        }
        // Engine types cannot be patched before the scene tree exists.
        try
        {
            _harmony.Patch(
                HarmonyLib.AccessTools.PropertySetter(typeof(SystemFont), nameof(SystemFont.FontNames)),
                prefix: new HarmonyLib.HarmonyMethod(typeof(IosPlatform), nameof(SystemFontNamesPrefix)),
                postfix: new HarmonyLib.HarmonyMethod(typeof(IosPlatform), nameof(SystemFontNamesPostfix))
            );
            PatchHelper.Log("Patched SystemFont.FontNames (iOS)");
            if (System.Environment.GetEnvironmentVariable("STS2_FRAME_STATS") == "1")
            {
                var probe = new SystemFont { FontNames = new[] { "Microsoft YaHei", "PingFang SC", "Arial" } };
                PatchHelper.Log(
                    $"[Fonts] probe names=[{string.Join(", ", probe.FontNames)}] fallbacks={probe.Fallbacks.Count}"
                );
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Fonts] SystemFont could not be patched: {ex.GetType().Name}: {ex.Message}");
        }

        // Mods also hand their own Latin fonts to controls and themes and count
        // on system fallback for Chinese text; give those fonts the same fallback.
        try
        {
            var ensure = new HarmonyLib.HarmonyMethod(typeof(IosPlatform), nameof(FontAssignedPrefix));
            foreach (var method in new[]
            {
                HarmonyLib.AccessTools.Method(typeof(Control), nameof(Control.AddThemeFontOverride)),
                HarmonyLib.AccessTools.Method(typeof(Theme), nameof(Theme.SetFont)),
                HarmonyLib.AccessTools.PropertySetter(typeof(Theme), nameof(Theme.DefaultFont)),
                HarmonyLib.AccessTools.PropertySetter(typeof(LabelSettings), nameof(LabelSettings.Font)),
            })
            {
                if (method != null)
                    _harmony.Patch(method, prefix: ensure);
            }
            PatchHelper.Log("Patched font assignment (iOS)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Fonts] Font assignment could not be patched: {ex.GetType().Name}: {ex.Message}");
        }

        // The game limits itself to 60 frames with the engine's sleep-based limiter.
        // On a 120 Hz display that presents frames alternately two and three
        // refreshes apart, which reads as constant judder. Frames are paced by the
        // display instead (vertical sync stays on), so a limit below the refresh
        // rate is dropped.
        try
        {
            _harmony.Patch(
                HarmonyLib.AccessTools.PropertySetter(typeof(Engine), nameof(Engine.MaxFps)),
                prefix: new HarmonyLib.HarmonyMethod(typeof(IosPlatform), nameof(MaxFpsPrefix))
            );
            if (Engine.MaxFps != 0)
                Engine.MaxFps = Engine.MaxFps;
            PatchHelper.Log("Patched Engine.MaxFps (iOS)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Display] Frame limit could not be patched: {ex.GetType().Name}: {ex.Message}");
        }

        // There is no mouse cursor here. The game sets cursor shape and mouse mode
        // whenever a card is aimed, and the engine answers each call with a warning
        // plus a captured C# backtrace, which costs frame time during a drag.
        try
        {
            var skip = new HarmonyLib.HarmonyMethod(typeof(IosPlatform), nameof(SkipCall));
            foreach (var method in new[]
            {
                HarmonyLib.AccessTools.PropertySetter(typeof(Input), nameof(Input.MouseMode)),
                HarmonyLib.AccessTools.Method(typeof(Input), nameof(Input.SetCustomMouseCursor)),
                HarmonyLib.AccessTools.Method(typeof(Input), nameof(Input.SetDefaultCursorShape)),
            })
            {
                if (method != null)
                    _harmony.Patch(method, prefix: skip);
            }
            PatchHelper.Log("Patched Input mouse cursor calls (iOS)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Input] Mouse cursor calls could not be patched: {ex.GetType().Name}: {ex.Message}");
        }

        var extended = 0;
        foreach (var font in new[]
        {
            ThemeDB.FallbackFont,
            ThemeDB.GetDefaultTheme()?.DefaultFont,
            ThemeDB.GetProjectTheme()?.DefaultFont,
        })
        {
            if (!EnsureCjkFallback(font))
                continue;
            extended++;
        }
        PatchHelper.Log($"[Fonts] CJK fallback added to {extended} default fonts ({CjkFonts().Count} fonts)");
    }

    private static bool SkipCall() => false;

    private static void MaxFpsPrefix(ref int value)
    {
        var refresh = DisplayServer.ScreenGetRefreshRate();
        if (value > 0 && refresh > 0 && value < refresh - 1)
        {
            PatchHelper.Log($"[Display] Frame limit {value} dropped; pacing by the {refresh:F0} Hz display");
            value = 0;
        }
    }

    private static bool DescribeFramePrefix(System.Diagnostics.StackFrame frame, out string methodDecl)
    {
        var method = frame?.GetMethod();
        methodDecl =
            method == null ? "" : (method.DeclaringType?.FullName ?? "<unknown>") + "." + method.Name + "()";
        return false;
    }

    private static bool DataDirPrefix(ref string __result)
    {
        __result = Documents;
        return false;
    }

    private static bool StartTimePrefix(ref DateTime __result)
    {
        __result = ProcessStart;
        return false;
    }

    private static Texture2D RenderQrCode(string url)
    {
        var png = IosNative.QrPng(url);
        var image = new Image();
        if (png == null || image.LoadPngFromBuffer(png) != Error.Ok)
            return null;
        return ImageTexture.CreateFromImage(image);
    }
}
