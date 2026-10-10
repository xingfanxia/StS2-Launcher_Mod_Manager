using Godot;

namespace STS2MobileIos.Patches;

public static class AudioPatches
{
    public static void Activate()
    {
        try
        {
            int status = NativeBridge.ActivateAudio();
            if (status != 0)
                PatchHelper.Log($"Unable to activate iOS audio: {status}");
        }
        catch (Exception error)
        {
            PatchHelper.Log(error.Message);
        }
    }

    public static void ReadyPostfix(Node __instance)
    {
        Activate();
    }
}
