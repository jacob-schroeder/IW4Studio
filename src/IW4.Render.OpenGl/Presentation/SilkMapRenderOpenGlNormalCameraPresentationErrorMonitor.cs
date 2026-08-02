using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Presentation;

internal enum MapRenderOpenGlPresentationErrorValidationPoint
{
    CapturedOnly,
    Forced,
    FrameBoundary
}

internal interface IMapRenderOpenGlPresentationErrorApi
{
    IDisposable? TryInstallDebugCallback(DebugProc callback);

    GLEnum GetError();
}

/// <summary>
/// Captures OpenGL debug output without synchronously interrogating the driver
/// after each presentation stage. If debug callbacks are unavailable, errors
/// are drained periodically and at infrequent resource lifecycle boundaries.
/// </summary>
internal sealed class MapRenderOpenGlPresentationErrorMonitor : IDisposable
{
    internal const int DeferredValidationFrameInterval = 60;

    private readonly IMapRenderOpenGlPresentationErrorApi _api;
    private readonly ConcurrentQueue<DebugMessage> _capturedMessages = new();
    private readonly DebugProc _debugCallback;
    private readonly IDisposable? _debugRegistration;
    private string _activeStage = "outside default normal-camera presentation";
    private int _framesSinceDeferredValidation;
    private bool _disposed;

    public MapRenderOpenGlPresentationErrorMonitor(
        IMapRenderOpenGlPresentationErrorApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
        _debugCallback = CaptureDebugMessage;
        _debugRegistration = _api.TryInstallDebugCallback(_debugCallback);
    }

    internal bool UsesDebugOutput => _debugRegistration is not null;

    public void BeginStage(string stage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        Volatile.Write(ref _activeStage, stage);
    }

    public void RequireNoErrors(
        string stage,
        MapRenderOpenGlPresentationErrorValidationPoint validationPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);

        if (UsesDebugOutput)
        {
            ThrowCapturedDebugErrors(stage);
            return;
        }

        switch (validationPoint)
        {
            case MapRenderOpenGlPresentationErrorValidationPoint.CapturedOnly:
                return;
            case MapRenderOpenGlPresentationErrorValidationPoint.Forced:
                _framesSinceDeferredValidation = 0;
                break;
            case MapRenderOpenGlPresentationErrorValidationPoint.FrameBoundary:
                _framesSinceDeferredValidation++;
                if (_framesSinceDeferredValidation <
                    DeferredValidationFrameInterval)
                {
                    return;
                }
                _framesSinceDeferredValidation = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(validationPoint),
                    validationPoint,
                    null);
        }

        ThrowPolledErrors(stage);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        List<Exception>? failures = null;
        try
        {
            if (UsesDebugOutput)
            {
                ThrowCapturedDebugErrors(
                    "default presentation monitor disposal");
            }
            else
            {
                ThrowPolledErrors(
                    "default presentation monitor disposal");
            }
        }
        catch (Exception failure)
        {
            failures = [failure];
        }
        finally
        {
            _disposed = true;
            try
            {
                _debugRegistration?.Dispose();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
            GC.KeepAlive(_debugCallback);
        }

        if (failures is { Count: 1 })
            throw failures[0];
        if (failures is { Count: > 1 })
        {
            throw new AggregateException(
                "OpenGL presentation error-monitor disposal failed.",
                failures);
        }
    }

    private void CaptureDebugMessage(
        GLEnum source,
        GLEnum type,
        int id,
        GLEnum severity,
        int length,
        nint message,
        nint userParameter)
    {
        try
        {
            string text = message == 0 || length <= 0
                ? string.Empty
                : Marshal.PtrToStringUTF8(message, length) ?? string.Empty;
            _capturedMessages.Enqueue(new DebugMessage(
                Volatile.Read(ref _activeStage),
                source,
                type,
                id,
                severity,
                text));
        }
        catch (Exception exception)
        {
            // Exceptions must never escape through the unmanaged callback. A
            // decode failure is itself retained as an error diagnostic.
            _capturedMessages.Enqueue(new DebugMessage(
                Volatile.Read(ref _activeStage),
                source,
                (GLEnum)DebugType.DebugTypeError,
                id,
                severity,
                $"OpenGL debug-message decoding failed: " +
                $"{exception.GetType().Name}: {exception.Message}"));
        }
    }

    private void ThrowCapturedDebugErrors(string validationStage)
    {
        List<DebugMessage>? errors = null;
        while (_capturedMessages.TryDequeue(out DebugMessage message))
        {
            string formatted = Format(message);
            Trace.WriteLine(formatted);
            if (message.Type == (GLEnum)DebugType.DebugTypeError)
            {
                errors ??= [];
                errors.Add(message);
            }
        }

        if (errors is null)
            return;

        throw new InvalidOperationException(
            $"Default normal-camera presentation failed at " +
            $"{validationStage} with OpenGL debug error(s): " +
            string.Join(" | ", errors.Select(Format)));
    }

    private void ThrowPolledErrors(string stage)
    {
        List<GLEnum>? errors = null;
        GLEnum error;
        while ((error = _api.GetError()) != GLEnum.NoError)
        {
            errors ??= [];
            errors.Add(error);
        }

        if (errors is null)
            return;

        throw new InvalidOperationException(
            $"Default normal-camera presentation failed at {stage} with " +
            $"deferred OpenGL error(s): {string.Join(", ", errors)}.");
    }

    private static string Format(DebugMessage message) =>
        $"stage={message.Stage}, source={message.Source}, " +
        $"type={message.Type}, id={message.Id}, " +
        $"severity={message.Severity}, message={message.Text}";

    private readonly record struct DebugMessage(
        string Stage,
        GLEnum Source,
        GLEnum Type,
        int Id,
        GLEnum Severity,
        string Text);
}

internal sealed unsafe class SilkMapRenderOpenGlPresentationErrorApi :
    IMapRenderOpenGlPresentationErrorApi
{
    private readonly GL _gl;

    public SilkMapRenderOpenGlPresentationErrorApi(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
    }

    public IDisposable? TryInstallDebugCallback(DebugProc callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        int majorVersion = _gl.GetInteger(GetPName.MajorVersion);
        int minorVersion = _gl.GetInteger(GetPName.MinorVersion);
        bool supportsCoreDebug = majorVersion > 4 ||
                                 (majorVersion == 4 && minorVersion >= 3);
        bool supportsKhrDebug =
            _gl.IsExtensionPresent("GL_KHR_debug");
        bool supportsArbDebug =
            _gl.IsExtensionPresent("GL_ARB_debug_output");
        if (!supportsCoreDebug && !supportsKhrDebug && !supportsArbDebug)
            return null;

        string entryPoint = supportsCoreDebug || supportsKhrDebug
            ? "glDebugMessageCallback"
            : "glDebugMessageCallbackARB";
        if (!_gl.Context.TryGetProcAddress(entryPoint, out nint address) ||
            address == 0)
        {
            return null;
        }

        // A callback is context-global. Never replace a diagnostic callback
        // installed by another renderer component or the host application.
        nint existingCallback =
            (nint)_gl.GetPointer(GetPointervPName.DebugCallbackFunction);
        if (existingCallback != 0)
            return null;

        var setter = Marshal.GetDelegateForFunctionPointer<
            SetDebugMessageCallback>(address);
        nint callbackAddress = Marshal.GetFunctionPointerForDelegate(callback);
        bool usesCoreEnable = supportsCoreDebug || supportsKhrDebug;
        bool enabledDebugOutput = false;
        if (usesCoreEnable && !_gl.IsEnabled(EnableCap.DebugOutput))
        {
            _gl.Enable(EnableCap.DebugOutput);
            enabledDebugOutput = true;
        }

        setter(callbackAddress, 0);
        nint installedCallback =
            (nint)_gl.GetPointer(GetPointervPName.DebugCallbackFunction);
        if (installedCallback == 0)
        {
            if (enabledDebugOutput)
                _gl.Disable(EnableCap.DebugOutput);
            return null;
        }

        return new DebugCallbackRegistration(
            _gl,
            setter,
            callbackAddress,
            enabledDebugOutput);
    }

    public GLEnum GetError() => _gl.GetError();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetDebugMessageCallback(
        nint callback,
        nint userParameter);

    private sealed class DebugCallbackRegistration : IDisposable
    {
        private readonly GL _gl;
        private readonly SetDebugMessageCallback _setter;
        private readonly nint _callbackAddress;
        private readonly bool _disableDebugOutput;
        private bool _disposed;

        public DebugCallbackRegistration(
            GL gl,
            SetDebugMessageCallback setter,
            nint callbackAddress,
            bool disableDebugOutput)
        {
            _gl = gl;
            _setter = setter;
            _callbackAddress = callbackAddress;
            _disableDebugOutput = disableDebugOutput;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            nint installedCallback =
                (nint)_gl.GetPointer(GetPointervPName.DebugCallbackFunction);
            if (installedCallback != _callbackAddress)
                return;

            _setter(0, 0);
            if (_disableDebugOutput)
                _gl.Disable(EnableCap.DebugOutput);
        }
    }
}
