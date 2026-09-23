using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace IW4.Render.WebGpu;

// Owns one bake's immutable bounds tree and one in-flight GPU ray batch.
// Exact surface/material evaluation remains with the caller on the CPU.
public sealed unsafe class WebGpuRayTraversal : IDisposable
{
    public const int MaximumRayCount = 32768;
    public const int ResultStride = 17; // Count followed by up to sixteen surface indices.
    private const float CoordinateLimit = 1073741824f; // 2^30: bounded WGSL arithmetic.
    private const float MinimumDirection = 1f / CoordinateLimit;

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct BoundsNode(Vector3 minimum, int escape, Vector3 maximum, int surface)
    {
        public readonly Vector3 Minimum = minimum;
        public readonly int Escape = escape;
        public readonly Vector3 Maximum = maximum;
        public readonly int Surface = surface;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public readonly struct Ray(Vector3 origin, Vector3 direction)
    {
        [FieldOffset(0)] public readonly Vector3 Origin = origin;
        [FieldOffset(16)] public readonly Vector3 Direction = direction;
    }

    private readonly WebGPU _api;
    private readonly delegate* unmanaged[Cdecl]<Device*, uint, ulong*, uint> _pollDevice;
    private readonly PfnRequestAdapterCallback _adapterCallback;
    private readonly PfnRequestDeviceCallback _deviceCallback;
    private readonly PfnBufferMapCallback _mapCallback;
    private readonly PfnDeviceLostCallback _lostCallback;
    private readonly PfnErrorCallback _errorCallback;
    private Instance* _instance;
    private Adapter* _adapter;
    private Device* _device;
    private Queue* _queue;
    private ShaderModule* _shader;
    private ComputePipeline* _pipeline;
    private BindGroupLayout* _layout;
    private BindGroup* _bindings;
    private Buffer* _nodes;
    private Buffer* _rays;
    private Buffer* _results;
    private Buffer* _readback;
    private Buffer* _parameters;
    private int _adapterComplete, _deviceComplete, _mapComplete;
    private int _pendingRayCount;
    private RequestAdapterStatus _adapterStatus;
    private RequestDeviceStatus _deviceStatus;
    private BufferMapAsyncStatus _mapStatus;
    private string? _error, _deviceLost;
    private bool _disposed, _unavailable;

    private WebGpuRayTraversal(WebGPU api, nint pollDevice)
    {
        _api = api;
        _pollDevice = (delegate* unmanaged[Cdecl]<Device*, uint, ulong*, uint>)pollDevice;
        _adapterCallback = new(OnAdapter);
        _deviceCallback = new(OnDevice);
        _mapCallback = new(OnMap);
        _lostCallback = new(OnDeviceLost);
        _errorCallback = new(OnError);
    }

    public static WebGpuRayTraversal? TryCreate(ReadOnlySpan<BoundsNode> nodes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Stay within WebGPU's default maximum storage-buffer binding size.
        if (nodes.IsEmpty || (ulong)nodes.Length * (ulong)sizeof(BoundsNode) > 128 * 1024 * 1024) return null;
        for (int index = 0; index < nodes.Length; index++)
        {
            ref readonly BoundsNode node = ref nodes[index];
            if (node.Escape <= index || node.Escape > nodes.Length ||
                node.Minimum.X > node.Maximum.X || node.Minimum.Y > node.Maximum.Y || node.Minimum.Z > node.Maximum.Z)
                throw new ArgumentException("The ray bounds tree has invalid bounds or escape indices.", nameof(nodes));
            if (!SupportedCoordinate(node.Minimum) || !SupportedCoordinate(node.Maximum)) return null;
        }
        WebGPU api;
        try { api = WebGPU.GetApi(); }
        catch (FileNotFoundException) { return null; }
        catch (DllNotFoundException) { return null; }
        catch (BadImageFormatException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        // Use the same loaded library as Silk, and resolve before starting any
        // callback whose lifetime depends on this native-only polling function.
        if (!api.Context.TryGetProcAddress("wgpuDevicePoll", out nint pollDevice))
        {
            api.Dispose();
            return null;
        }
        var traversal = new WebGpuRayTraversal(api, pollDevice);
        try
        {
            if (traversal.Initialize(nodes, cancellationToken)) return traversal;
            traversal.Dispose();
            return null;
        }
        catch (Exception exception) when (IsDeviceFailure(exception))
        {
            traversal.Dispose();
            return null;
        }
        catch
        {
            traversal.Dispose();
            throw;
        }
    }

    private bool Initialize(ReadOnlySpan<BoundsNode> nodes, CancellationToken cancellationToken)
    {
        var instanceDescriptor = new InstanceDescriptor();
        _instance = _api.CreateInstance(&instanceDescriptor);
        if (_instance == null) return false;
        var adapterOptions = new RequestAdapterOptions { PowerPreference = PowerPreference.HighPerformance };
        _api.InstanceRequestAdapter(_instance, &adapterOptions, _adapterCallback, null);
        WaitFor(ref _adapterComplete);
        cancellationToken.ThrowIfCancellationRequested();
        if (_adapterStatus != RequestAdapterStatus.Success || _adapter == null) return false;
        var properties = new AdapterProperties();
        _api.AdapterGetProperties(_adapter, &properties);
        if (properties.AdapterType == AdapterType.Cpu) return false;
        var deviceDescriptor = new DeviceDescriptor { DeviceLostCallback = _lostCallback };
        _api.AdapterRequestDevice(_adapter, &deviceDescriptor, _deviceCallback, null);
        WaitFor(ref _deviceComplete);
        cancellationToken.ThrowIfCancellationRequested();
        if (_deviceStatus != RequestDeviceStatus.Success || _device == null) return false;
        _api.DeviceSetUncapturedErrorCallback(_device, _errorCallback, null);
        _queue = _api.DeviceGetQueue(_device);

        using Stream source = typeof(WebGpuRayTraversal).Assembly.GetManifestResourceStream(
            "IW4.Render.WebGpu.RayTraversal.wgsl") ?? throw new InvalidOperationException("The GPU ray shader is missing.");
        using var reader = new StreamReader(source);
        nint code = SilkMarshal.StringToPtr(reader.ReadToEnd(), NativeStringEncoding.UTF8);
        nint entry = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);
        try
        {
            var wgsl = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = (byte*)code
            };
            var shaderDescriptor = new ShaderModuleDescriptor { NextInChain = &wgsl.Chain };
            _shader = _api.DeviceCreateShaderModule(_device, &shaderDescriptor);
            ThrowIfError();
            var pipelineDescriptor = new ComputePipelineDescriptor
            {
                Compute = new ProgrammableStageDescriptor { Module = _shader, EntryPoint = (byte*)entry }
            };
            _pipeline = _api.DeviceCreateComputePipeline(_device, &pipelineDescriptor);
            ThrowIfError();
        }
        finally
        {
            SilkMarshal.Free(entry);
            SilkMarshal.Free(code);
        }
        cancellationToken.ThrowIfCancellationRequested();
        ulong nodeBytes = checked((ulong)nodes.Length * (ulong)sizeof(BoundsNode));
        ulong rayBytes = (ulong)MaximumRayCount * (ulong)sizeof(Ray);
        ulong resultBytes = (ulong)MaximumRayCount * ResultStride * sizeof(int);
        _nodes = CreateBuffer(nodeBytes, BufferUsage.Storage | BufferUsage.CopyDst);
        _rays = CreateBuffer(rayBytes, BufferUsage.Storage | BufferUsage.CopyDst);
        _results = CreateBuffer(resultBytes, BufferUsage.Storage | BufferUsage.CopySrc);
        _readback = CreateBuffer(resultBytes, BufferUsage.MapRead | BufferUsage.CopyDst);
        _parameters = CreateBuffer(16, BufferUsage.Uniform | BufferUsage.CopyDst);
        fixed (BoundsNode* pointer = nodes) _api.QueueWriteBuffer(_queue, _nodes, 0, pointer, (nuint)nodeBytes);
        _layout = _api.ComputePipelineGetBindGroupLayout(_pipeline, 0);
        BindGroupEntry* entries = stackalloc BindGroupEntry[4];
        entries[0] = new BindGroupEntry { Binding = 0, Buffer = _nodes, Size = nodeBytes };
        entries[1] = new BindGroupEntry { Binding = 1, Buffer = _rays, Size = rayBytes };
        entries[2] = new BindGroupEntry { Binding = 2, Buffer = _results, Size = resultBytes };
        entries[3] = new BindGroupEntry { Binding = 3, Buffer = _parameters, Size = 16 };
        var bindings = new BindGroupDescriptor { Layout = _layout, EntryCount = 4, Entries = entries };
        _bindings = _api.DeviceCreateBindGroup(_device, &bindings);
        ThrowIfError();
        return true;
    }

    // Submission allows the caller to shade the preceding batch while this one runs.
    // False means this batch needs the CPU path; only successful submissions need readback.
    public bool TrySubmit(ReadOnlySpan<Ray> rays, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_pendingRayCount != 0) throw new InvalidOperationException("Read the pending GPU ray batch before submitting another.");
        if (rays.Length > MaximumRayCount) throw new ArgumentException("The GPU ray batch exceeds its capacity.", nameof(rays));
        if (rays.IsEmpty) return false;
        if (_unavailable || Volatile.Read(ref _deviceLost) is not null) return false;
        foreach (ref readonly Ray ray in rays)
            if (!SupportedCoordinate(ray.Origin) || !SupportedDirection(ray.Direction)) return false;
        try { return SubmitBatch(rays); }
        catch (Exception exception) when (IsDeviceFailure(exception))
        {
            _unavailable = true;
            return false;
        }
    }

    private bool SubmitBatch(ReadOnlySpan<Ray> rays)
    {
        nuint outputBytes = checked((nuint)(rays.Length * ResultStride * sizeof(int)));
        fixed (Ray* pointer = rays)
            _api.QueueWriteBuffer(_queue, _rays, 0, pointer, checked((nuint)(rays.Length * sizeof(Ray))));
        uint* parameters = stackalloc uint[4] { (uint)rays.Length, ResultStride, 0, 0 };
        _api.QueueWriteBuffer(_queue, _parameters, 0, parameters, 16);
        ThrowIfError();
        CommandEncoder* encoder = null;
        ComputePassEncoder* pass = null;
        CommandBuffer* commands = null;
        try
        {
            var encoderDescriptor = new CommandEncoderDescriptor();
            encoder = _api.DeviceCreateCommandEncoder(_device, &encoderDescriptor);
            var passDescriptor = new ComputePassDescriptor();
            pass = _api.CommandEncoderBeginComputePass(encoder, &passDescriptor);
            _api.ComputePassEncoderSetPipeline(pass, _pipeline);
            _api.ComputePassEncoderSetBindGroup(pass, 0, _bindings, 0, (uint*)null);
            _api.ComputePassEncoderDispatchWorkgroups(pass, ((uint)rays.Length + 63) / 64, 1, 1);
            _api.ComputePassEncoderEnd(pass);
            _api.ComputePassEncoderRelease(pass);
            pass = null;
            _api.CommandEncoderCopyBufferToBuffer(encoder, _results, 0, _readback, 0, outputBytes);
            var commandDescriptor = new CommandBufferDescriptor();
            commands = _api.CommandEncoderFinish(encoder, &commandDescriptor);
            ThrowIfError();
            _api.QueueSubmit(_queue, 1, &commands);
        }
        finally
        {
            if (commands != null) _api.CommandBufferRelease(commands);
            if (pass != null) _api.ComputePassEncoderRelease(pass);
            if (encoder != null) _api.CommandEncoderRelease(encoder);
        }
        Volatile.Write(ref _mapComplete, 0);
        _api.BufferMapAsync(_readback, MapMode.Read, 0, outputBytes, _mapCallback, null);
        _pendingRayCount = rays.Length;
        return true;
    }

    // A negative count in a ray's result row means overflow: use the CPU query.
    public bool TryRead(Span<int> results, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pendingRayCount == 0) throw new InvalidOperationException("No GPU ray batch is pending.");
        int resultCount = _pendingRayCount * ResultStride;
        if (results.Length < resultCount) throw new ArgumentException("The GPU ray result buffer is too small.", nameof(results));
        // Native callbacks cannot be cancelled. Finish this bounded submission
        // before cancellation/disposal so no callback outlives its delegate.
        WaitFor(ref _mapComplete);
        bool mapped = _mapStatus == BufferMapAsyncStatus.Success;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _deviceLost) is not null) return false;
            ThrowIfError();
            if (!mapped) throw new InvalidOperationException($"GPU lighting readback failed: {_mapStatus}.");
            void* pointer = _api.BufferGetConstMappedRange(_readback, 0, (nuint)(resultCount * sizeof(int)));
            if (pointer == null) throw new InvalidOperationException("GPU lighting readback returned no data.");
            new ReadOnlySpan<int>(pointer, resultCount).CopyTo(results);
            return true;
        }
        catch (Exception exception) when (IsDeviceFailure(exception))
        {
            _unavailable = true;
            return false;
        }
        finally
        {
            if (mapped) _api.BufferUnmap(_readback);
            _pendingRayCount = 0;
        }
    }

    private Buffer* CreateBuffer(ulong size, BufferUsage usage)
    {
        var descriptor = new BufferDescriptor { Size = size, Usage = usage };
        Buffer* buffer = _api.DeviceCreateBuffer(_device, &descriptor);
        if (buffer == null) throw new InvalidOperationException("GPU lighting buffer allocation failed.");
        if (Volatile.Read(ref _error) is not null)
        {
            _api.BufferRelease(buffer);
            ThrowIfError();
        }
        return buffer;
    }

    private void WaitFor(ref int completed)
    {
        while (Volatile.Read(ref completed) == 0)
        {
            if (_device != null) _pollDevice(_device, 1, null);
            else Thread.Sleep(1);
        }
    }

    private void ThrowIfError()
    {
        if (Volatile.Read(ref _error) is { } error)
            throw new InvalidOperationException($"GPU lighting: {error}");
    }

    private static bool IsDeviceFailure(Exception exception) => exception is
        InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;

    private static bool SupportedCoordinate(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
        MathF.Abs(value.X) <= CoordinateLimit && MathF.Abs(value.Y) <= CoordinateLimit && MathF.Abs(value.Z) <= CoordinateLimit;

    private static bool SupportedDirection(Vector3 value) => Component(value.X) && Component(value.Y) && Component(value.Z);
    private static bool Component(float value) => value == 0 ||
        float.IsFinite(value) && MathF.Abs(value) >= MinimumDirection && MathF.Abs(value) <= 1;

    private void OnAdapter(RequestAdapterStatus status, Adapter* adapter, byte* message, void* data)
    {
        _adapterStatus = status;
        _adapter = adapter;
        Volatile.Write(ref _adapterComplete, 1);
    }

    private void OnDevice(RequestDeviceStatus status, Device* device, byte* message, void* data)
    {
        _deviceStatus = status;
        _device = device;
        Volatile.Write(ref _deviceComplete, 1);
    }

    private void OnMap(BufferMapAsyncStatus status, void* data)
    {
        _mapStatus = status;
        Volatile.Write(ref _mapComplete, 1);
    }

    private void OnDeviceLost(DeviceLostReason reason, byte* message, void* data) =>
        Volatile.Write(ref _deviceLost, $"{reason}: {ReadMessage(message)}");
    private void OnError(ErrorType type, byte* message, void* data) =>
        Volatile.Write(ref _error, $"{type}: {ReadMessage(message)}");
    private static string? ReadMessage(byte* message) => message == null ? null : SilkMarshal.PtrToString((nint)message);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_pendingRayCount != 0)
        {
            WaitFor(ref _mapComplete);
            if (_mapStatus == BufferMapAsyncStatus.Success) _api.BufferUnmap(_readback);
            _pendingRayCount = 0;
        }
        ReleaseBuffer(_readback);
        ReleaseBuffer(_results);
        ReleaseBuffer(_rays);
        ReleaseBuffer(_nodes);
        ReleaseBuffer(_parameters);
        if (_bindings != null) _api.BindGroupRelease(_bindings);
        if (_layout != null) _api.BindGroupLayoutRelease(_layout);
        if (_pipeline != null) _api.ComputePipelineRelease(_pipeline);
        if (_shader != null) _api.ShaderModuleRelease(_shader);
        if (_queue != null) _api.QueueRelease(_queue);
        if (_device != null) { _api.DeviceDestroy(_device); _api.DeviceRelease(_device); }
        if (_adapter != null) _api.AdapterRelease(_adapter);
        if (_instance != null) _api.InstanceRelease(_instance);
        _mapCallback.Dispose();
        _errorCallback.Dispose();
        _lostCallback.Dispose();
        _deviceCallback.Dispose();
        _adapterCallback.Dispose();
        _api.Dispose();
    }

    private void ReleaseBuffer(Buffer* buffer)
    {
        if (buffer == null) return;
        _api.BufferDestroy(buffer);
        _api.BufferRelease(buffer);
    }

}
