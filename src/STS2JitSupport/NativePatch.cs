using System.Runtime.InteropServices;

namespace STS2JitSupport;

public static class NativePatch
{
    [DllImport("__Internal", EntryPoint = "sts2_jit_patch")]
    private static extern unsafe int Patch(nint target, byte* source, int size, byte* backup, int backupSize);

    public static unsafe void PatchData(int kind, nint target, ReadOnlySpan<byte> data, Span<byte> backup)
    {
        fixed (byte* source = data)
        fixed (byte* saved = backup)
        {
            int result = Patch(target, source, data.Length, saved, backup.Length);
            if (result != 0) throw new InvalidOperationException($"iOS native patch failed: {result}");
        }
    }
}
