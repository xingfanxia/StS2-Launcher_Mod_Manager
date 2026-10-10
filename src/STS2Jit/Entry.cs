using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace STS2Jit;

public static class Entry
{
    [DllImport("__Internal", EntryPoint = "sts2_jit_log")]
    private static extern void Log(string message);

    private sealed class LogWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void WriteLine(string value) => Log(value);
    }

    [UnmanagedCallersOnly]
    public static int Start()
    {
        try
        {
            Console.SetOut(new LogWriter());
            Console.SetError(Console.Out);
            var harmony = new Harmony("sts2.ios.jit.mobile");
            Assembly game = typeof(ModManager).Assembly;
            Assembly mobile = typeof(STS2MobileIos.MobileUi).Assembly;
            using var manifest = JsonDocument.Parse(typeof(Entry).Assembly.GetManifestResourceStream("mobile-hooks.json")!);
            foreach (var patch in manifest.RootElement.GetProperty("patches").EnumerateArray())
            {
                string hookType = patch.GetProperty("hookType").GetString()!;
                Type targetType = game.GetType(patch.GetProperty("targetType").GetString()!, true)!;
                string methodName = patch.GetProperty("targetMethod").GetString()!;
                var target = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(method => method.Name == methodName && method.DeclaringType == targetType);
                var hook = new HarmonyMethod(mobile.GetType(hookType, true)!, patch.GetProperty("hookMethod").GetString()!);
                if (patch.GetProperty("kind").GetString() == "prefix") harmony.Patch(target, prefix: hook);
                else harmony.Patch(target, postfix: hook);
            }
            harmony.Patch(AccessTools.Method(typeof(ModManager), nameof(ModManager.Initialize)),
                prefix: new HarmonyMethod(typeof(Entry), nameof(InitializeMods)));
            harmony.Patch(AccessTools.Method(typeof(ModManager), "ReadModsInDirRecursive"),
                prefix: new HarmonyMethod(typeof(Entry), nameof(ScanMods)));
            harmony.Patch(AccessTools.Method(game.GetType("MegaCrit.Sts2.Core.Nodes.NGame"), "_EnterTree"),
                prefix: new HarmonyMethod(typeof(Entry), nameof(EnableLogging)));
            Log("[JIT] Mobile hooks ready; managed mods enabled in Documents/mods");
            return 0;
        }
        catch (Exception error)
        {
            Log("[JIT] Game initialization failed: " + error);
            return 1;
        }
    }

    private static string ModRoot => Path.Combine(OS.GetUserDataDir(), "mods");
    public static void EnableLogging() => MegaCrit.Sts2.Core.Logging.Log.LogCallback +=
        (level, message, _) => Log($"[Game/{level}] {message}");
    private static string DesktopModRoot => Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "mods");

    public static void InitializeMods(ref IModManagerFileIo fileIo, ref ModSettings settings)
    {
        Directory.CreateDirectory(ModRoot);
        fileIo = new ModFileIo(fileIo);
        settings ??= new ModSettings();
    }

    public static void ScanMods(ref string path)
    {
        if (path == DesktopModRoot) path = ModRoot;
    }

    private sealed class ModFileIo(IModManagerFileIo original) : IModManagerFileIo
    {
        public bool DirectoryExists(string path) => path == DesktopModRoot || original.DirectoryExists(path);
        public string[] GetDirectoriesAt(string path) => original.GetDirectoriesAt(path);
        public string[] GetFilesAt(string path) => original.GetFilesAt(path);
        public bool FileExists(string path) => original.FileExists(path);
        public Stream OpenStream(string path, Godot.FileAccess.ModeFlags mode) => original.OpenStream(path, mode);
        public void MakeDirRecursive(string path) => original.MakeDirRecursive(path);
        public Error CopyFile(string sourcePath, string destinationPath) => original.CopyFile(sourcePath, destinationPath);
    }
}
