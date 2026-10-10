using Godot;

namespace STS2Mobile;

// Encrypts small secrets (credential vault, ownership marker) with a key the
// operating system keeps for this app. Encrypt/Decrypt return null on failure.
public interface ISecretProtector
{
    bool Available { get; }
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

// Services the host platform provides to the shared launcher. Android is the
// default; another platform replaces members before ModEntry applies patches.
public static class PlatformHost
{
    public static ISecretProtector Secrets { get; set; } = new AndroidSecretProtector();

    // Draws a QR code for a URL, or null when the platform cannot. QR login is
    // offered only when this is set.
    public static System.Func<string, Texture2D> QrCodeRenderer { get; set; }

    // Restarts the app so a changed game install or account takes effect. A
    // platform that cannot relaunch itself tells the user to reopen the app.
    public static System.Action RestartApp { get; set; } =
        () => Launcher.LauncherModel.GetGodotApp()?.Call("restartApp");

    // Extra preparation a downloaded game directory needs before it can be
    // activated on this platform; throws when it cannot be done.
    public static System.Action<string> PrepareGamePack { get; set; }

    // Whether each Steam connection logs on with its own login id.
    public static bool UniqueSteamLoginIds { get; set; }

    // Font resources the launcher adds behind the system font for scripts the
    // system fallback does not cover on this platform.
    public static string[] LauncherFallbackFonts { get; set; } = System.Array.Empty<string>();

    // Whether the first hand of a session is dealt under a loading cover until
    // its render pipelines have compiled. Android always does.
    public static bool CoverFirstHand { get; set; }

    // Whether shared storage needs a user-granted permission before use.
    public static bool RequiresStoragePermission { get; set; } = true;
}

internal sealed class AndroidSecretProtector : ISecretProtector
{
    public bool Available => GetGodotApp() != null;

    public string Encrypt(string plainText) => (string)GetGodotApp()?.Call("encryptString", plainText);

    public string Decrypt(string cipherText) => (string)GetGodotApp()?.Call("decryptString", cipherText);

    private static GodotObject GetGodotApp()
    {
        try
        {
            var jcw = Engine.GetSingleton("JavaClassWrapper");
            var wrapper = (GodotObject)
                jcw.Call("wrap", "com.game.sts2launcher.modmanager.GodotApp");
            return (GodotObject)wrapper.Call("getInstance");
        }
        catch
        {
            return null;
        }
    }
}
