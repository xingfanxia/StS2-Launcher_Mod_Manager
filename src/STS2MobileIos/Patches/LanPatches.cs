namespace STS2MobileIos.Patches;

public static class LanPatches
{
    public static bool OpenMenuPrefix()
    {
        Lan.LanPanel.Open?.Invoke();
        return false;
    }
}
