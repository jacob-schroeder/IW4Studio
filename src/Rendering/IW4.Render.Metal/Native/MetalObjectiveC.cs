using System.Runtime.InteropServices;

namespace IW4.Render.Metal.Native;

internal static partial class MetalObjectiveC
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    private static readonly nint s_alloc = GetSelector("alloc");
    private static readonly nint s_contentView = GetSelector("contentView");
    private static readonly nint s_init = GetSelector("init");
    private static readonly nint s_gpuEndTime = GetSelector("GPUEndTime");
    private static readonly nint s_gpuStartTime = GetSelector("GPUStartTime");
    private static readonly nint s_layer = GetSelector("layer");
    private static readonly nint s_nextDrawable = GetSelector("nextDrawable");
    private static readonly nint s_release = GetSelector("release");
    private static readonly nint s_retain = GetSelector("retain");
    private static readonly nint s_setDrawableSize =
        GetSelector("setDrawableSize:");
    private static readonly nint s_setLayer = GetSelector("setLayer:");
    private static readonly nint s_setWantsLayer =
        GetSelector("setWantsLayer:");
    private static readonly nint s_wantsLayer = GetSelector("wantsLayer");

    internal static nint CreateInstance(string className)
    {
        nint objectClass = objc_getClass(className);
        if (objectClass == 0)
        {
            throw new PlatformNotSupportedException(
                $"The Objective-C class '{className}' is unavailable.");
        }

        nint allocated = IntPtr_objc_msgSend(objectClass, s_alloc);
        if (allocated == 0)
        {
            throw new InvalidOperationException(
                $"The Objective-C class '{className}' could not be allocated.");
        }

        nint initialized = IntPtr_objc_msgSend(allocated, s_init);
        if (initialized == 0)
        {
            throw new InvalidOperationException(
                $"The Objective-C class '{className}' could not be initialized.");
        }

        return initialized;
    }

    internal static nint GetContentView(nint window) =>
        IntPtr_objc_msgSend(window, s_contentView);

    internal static nint GetLayer(nint view) =>
        IntPtr_objc_msgSend(view, s_layer);

    internal static bool GetWantsLayer(nint view) =>
        bool_objc_msgSend(view, s_wantsLayer);

    internal static nint GetNextDrawable(nint layer) =>
        IntPtr_objc_msgSend(layer, s_nextDrawable);

    // SharpMetal 1.1 generated these two CFTimeInterval properties as
    // IntPtr-returning calls. Keep the ABI-correct double dispatch isolated
    // here until the binding package corrects them.
    internal static double GetGpuStartTime(nint commandBuffer) =>
        double_objc_msgSend(commandBuffer, s_gpuStartTime);

    internal static double GetGpuEndTime(nint commandBuffer) =>
        double_objc_msgSend(commandBuffer, s_gpuEndTime);

    internal static void SetDrawableSize(
        nint layer,
        int pixelWidth,
        int pixelHeight)
    {
        CGSize size = new(pixelWidth, pixelHeight);
        CGSize_objc_msgSend(layer, s_setDrawableSize, size);
    }

    internal static void SetLayer(nint view, nint layer) =>
        IntPtrArgument_objc_msgSend(view, s_setLayer, layer);

    internal static void SetWantsLayer(nint view, bool value) =>
        boolArgument_objc_msgSend(view, s_setWantsLayer, value);

    internal static void Retain(nint value)
    {
        if (value != 0)
            IntPtr_objc_msgSend(value, s_retain);
    }

    internal static void Release(nint value)
    {
        if (value != 0)
            void_objc_msgSend(value, s_release);
    }

    private static nint GetSelector(string name)
    {
        nint selector = sel_registerName(name);
        if (selector == 0)
        {
            throw new InvalidOperationException(
                $"The Objective-C selector '{name}' is unavailable.");
        }

        return selector;
    }

    [LibraryImport(
        ObjectiveCLibrary,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(
        ObjectiveCLibrary,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint IntPtr_objc_msgSend(
        nint receiver,
        nint selector);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint IntPtr_objc_msgSend(
        nint receiver,
        nint selector,
        nint value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial double double_objc_msgSend(
        nint receiver,
        nint selector);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void IntPtrArgument_objc_msgSend(
        nint receiver,
        nint selector,
        nint value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool bool_objc_msgSend(
        nint receiver,
        nint selector);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void boolArgument_objc_msgSend(
        nint receiver,
        nint selector,
        [MarshalAs(UnmanagedType.I1)] bool value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void CGSize_objc_msgSend(
        nint receiver,
        nint selector,
        CGSize value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void void_objc_msgSend(
        nint receiver,
        nint selector);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGSize(double width, double height)
    {
        private readonly double _width = width;
        private readonly double _height = height;
    }
}
