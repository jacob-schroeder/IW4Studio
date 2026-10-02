using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Iw4Radiant;

internal static class MacApplicationIcon
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

    internal static unsafe void Apply()
    {
        // Avalonia's macOS Window.SetIcon is a no-op; set the Dock icon for unbundled runs too.
        using Stream source = AssetLoader.Open(new Uri("avares://Iw4Radiant/Resources/iw4radiant_logo.png"));
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);

        nint alloc = RegisterSelector("alloc");
        nint release = RegisterSelector("release");
        nint data;
        fixed (byte* buffer = bytes.GetBuffer())
        {
            data = Send(Send(GetClass("NSData"), alloc), RegisterSelector("initWithBytes:length:"),
                (nint)buffer, (nuint)bytes.Length);
        }

        nint image = 0;
        try
        {
            image = Send(Send(GetClass("NSImage"), alloc), RegisterSelector("initWithData:"), data);
            if (image != 0)
            {
                nint application = Send(GetClass("NSApplication"), RegisterSelector("sharedApplication"));
                SendVoid(application, RegisterSelector("setApplicationIconImage:"), image);
            }
        }
        finally
        {
            SendVoid(image, release);
            SendVoid(data, release);
        }
    }

    [DllImport(ObjCLibrary, EntryPoint = "objc_getClass")]
    private static extern nint GetClass(string name);

    [DllImport(ObjCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint RegisterSelector(string name);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint bytes, nuint length);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector, nint argument);
}
