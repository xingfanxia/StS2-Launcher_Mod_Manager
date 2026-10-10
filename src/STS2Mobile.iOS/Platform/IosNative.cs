using System;
using System.Runtime.InteropServices;

namespace STS2Mobile.Platform;

// Functions exported by ios/Sts2Native.mm from the app executable.
internal static class IosNative
{
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern IntPtr dlsym(IntPtr handle, string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr KeyCall(out int status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr TextCall([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeCall(IntPtr value);

    private static T Function<T>(string name)
        where T : Delegate
    {
        var symbol = dlsym(new IntPtr(-2), name); // RTLD_DEFAULT on Darwin.
        if (symbol == IntPtr.Zero)
            throw new InvalidOperationException($"Native function {name} is missing from the app");
        return Marshal.GetDelegateForFunctionPointer<T>(symbol);
    }

    private static string Take(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
            return null;
        try
        {
            return Marshal.PtrToStringUTF8(pointer);
        }
        finally
        {
            Function<FreeCall>("sts2_free")(pointer);
        }
    }

    // Null when the Keychain cannot provide the key; status is the OSStatus.
    public static byte[] VaultKey(out int status)
    {
        var text = Take(Function<KeyCall>("sts2_vault_key")(out status));
        return text == null ? null : Convert.FromBase64String(text);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidCall();

    public static void ShowRestartRequired() => Function<VoidCall>("sts2_restart_required")();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StatsCall(out nuint used, out nuint high, out nuint size);

    // Bytes of the JIT code arena in use now, at its peak, and in total.
    public static (long Used, long High, long Size) JitArenaStats()
    {
        Function<StatsCall>("sts2_jit_stats")(out var used, out var high, out var size);
        return ((long)used, (long)high, (long)size);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong FootprintCall();

    private static FootprintCall _footprint;

    // Bytes the system charges to the app; its memory limit applies to this number.
    public static long MemoryFootprint() =>
        (long)(_footprint ??= Function<FootprintCall>("sts2_memory_footprint"))();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CompileCall(IntPtr method);

    private static CompileCall _compile;

    // Compiles a method without running its class constructor. False when it could not be compiled.
    public static bool CompileOnly(RuntimeMethodHandle method) =>
        (_compile ??= Function<CompileCall>("sts2_jit_compile_only"))(method.Value) != IntPtr.Zero;

    // Bytes the app may still take before the system ends it for memory.
    public static long MemoryAvailable() => (long)Function<FootprintCall>("sts2_memory_available")();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void BreakdownCall(ulong* values);

    private static BreakdownCall _breakdown;

    // Footprint by kind, in bytes: graphics, malloc heap in use, compressed, anonymous.
    public static unsafe (long Graphics, long Heap, long Compressed, long Anonymous) MemoryBreakdown()
    {
        var values = stackalloc ulong[4];
        (_breakdown ??= Function<BreakdownCall>("sts2_memory_breakdown"))(values);
        return ((long)values[0], (long)values[1], (long)values[2], (long)values[3]);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void WatchCall(int milliseconds);

    private static VoidCall _stallTick;

    // Prints the main thread's native stack when no frame completes for this long.
    public static void WatchForStalls(int milliseconds) =>
        Function<WatchCall>("sts2_stall_watch")(milliseconds);

    public static void StallTick() => (_stallTick ??= Function<VoidCall>("sts2_stall_tick"))();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ProfileCall(int milliseconds, int intervalMicroseconds);

    // Samples the main thread and prints the busiest functions to the log.
    public static void Profile(int milliseconds, int intervalMicroseconds) =>
        Function<ProfileCall>("sts2_profile")(milliseconds, intervalMicroseconds);

    public static byte[] QrPng(string value)
    {
        var text = Take(Function<TextCall>("sts2_qr_png")(value));
        return text == null ? null : Convert.FromBase64String(text);
    }
}
