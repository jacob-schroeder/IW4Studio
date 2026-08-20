using System.Runtime.InteropServices;

namespace IW4.Render.Metal.Native;

internal static class MetalNativeFrameworks
{
    private const string AppKitPath =
        "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string MetalPath =
        "/System/Library/Frameworks/Metal.framework/Metal";
    private const string QuartzCorePath =
        "/System/Library/Frameworks/QuartzCore.framework/QuartzCore";

    private static readonly nint s_appKit = NativeLibrary.Load(AppKitPath);
    private static readonly nint s_metal = NativeLibrary.Load(MetalPath);
    private static readonly nint s_quartzCore =
        NativeLibrary.Load(QuartzCorePath);

    internal static void EnsureLoaded()
    {
        _ = s_appKit;
        _ = s_metal;
        _ = s_quartzCore;
    }
}
