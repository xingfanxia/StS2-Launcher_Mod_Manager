using System.Runtime.InteropServices;

namespace STS2MobileIos;

internal static class NativeBridge
{
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern IntPtr dlsym(IntPtr handle, string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StatusCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ReadCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WriteCall([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ImageCall([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeCall(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PortCall(int port);

    public static void BrowseLan() => Function<StatusCall>("sts2_lan_browse")();
    public static void StopLanBrowse() => Function<StatusCall>("sts2_lan_browse_stop")();
    public static void PublishLan(ushort port) => Function<PortCall>("sts2_lan_publish")(port);
    public static void StopLanPublish() => Function<StatusCall>("sts2_lan_publish_stop")();
    public static string LanSnapshot() => Read("sts2_lan_snapshot");

    private static T Function<T>(string name) where T : Delegate
    {
        var symbol = dlsym(new IntPtr(-2), name); // RTLD_DEFAULT on Darwin.
        if (symbol == IntPtr.Zero)
            throw new InvalidOperationException("Native iOS support is missing; regenerate the app host.");
        return Marshal.GetDelegateForFunctionPointer<T>(symbol);
    }

    public static int ActivateAudio() => Function<StatusCall>("sts2_audio_activate")();
    public static string ReadCredential() => Read("sts2_keychain_read");
    public static byte[] QrPng(string value)
    {
        var pointer = Function<ImageCall>("sts2_qr_png")(value);
        if (pointer == IntPtr.Zero)
            throw new InvalidOperationException("Could not draw Steam login QR code.");
        try { return Convert.FromBase64String(Marshal.PtrToStringUTF8(pointer)); }
        finally { Function<FreeCall>("sts2_free")(pointer); }
    }
    public static void WriteCredential(string value)
    {
        if (Function<WriteCall>("sts2_keychain_write")(value) != 0)
            throw new InvalidOperationException("Could not save the Steam login in Keychain.");
    }
    public static void DeleteCredential()
    {
        if (Function<StatusCall>("sts2_keychain_delete")() != 0)
            throw new InvalidOperationException("Could not remove the Steam login from Keychain.");
    }
    private static string Read(string name)
    {
        var pointer = Function<ReadCall>(name)();
        if (pointer == IntPtr.Zero)
            return null;
        try { return Marshal.PtrToStringUTF8(pointer); }
        finally { Function<FreeCall>("sts2_free")(pointer); }
    }
}
