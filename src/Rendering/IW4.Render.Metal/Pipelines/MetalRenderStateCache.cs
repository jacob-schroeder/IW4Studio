using System.Numerics;
using System.Runtime.Versioning;

using IW4.Render.Execution;
using IW4.Render.Metal.Targets;
using IW4.Render.Techniques;

using SharpMetal.Metal;

namespace IW4.Render.Metal.Pipelines;

/// <summary>
/// Owns the immutable Metal depth/stencil objects and applies the remaining
/// RSX raster state to a live encoder. Blend and color-write state are part of
/// a Metal render pipeline and are configured by <see cref="MetalPipelineCache"/>.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalRenderStateCache : IDisposable
{
    private const float Ps3Depth24Maximum = 16_777_215f;
    private readonly MTLDevice _device;
    private readonly bool _emulatesDepth24;
    private readonly Dictionary<RenderState, MTLDepthStencilState> _states = [];
    private bool _disposed;
    private bool _hasInheritedDepthBias;
    private float _inheritedDepthBias;
    private float _inheritedSlopeScale;
    private bool _hasCurrentDepthBias;
    private Vector2 _currentDepthBias;

    internal MetalRenderStateCache(
        MTLDevice device,
        MetalDepthStencilFormatSelection depthStencilFormat)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        ArgumentNullException.ThrowIfNull(depthStencilFormat);
        _device = device;
        _emulatesDepth24 = depthStencilFormat.EmulatesDepth24;
    }

    internal Vector2 CurrentDepthBias => _hasCurrentDepthBias
        ? _currentDepthBias
        : throw new InvalidOperationException(
            "Metal polygon offset has not been initialized for this encoder.");

    internal MTLDepthStencilState GetOrCreate(RenderState authoredState)
    {
        ThrowIfDisposed();
        RenderState state = Effective(authoredState);
        if (_states.TryGetValue(state, out MTLDepthStencilState cached))
            return cached;

        IReadOnlyList<string> blockers =
            RenderStateExecutionCapability.FindBlockers(state);
        if (blockers.Count != 0)
        {
            throw new InvalidOperationException(
                $"Metal cannot execute the authored render state: {string.Join('|', blockers)}");
        }

        var descriptor = new MTLDepthStencilDescriptor
        {
            DepthCompareFunction = state.DepthTestEnabled
                ? ToCompare(state.DepthFunc)
                : MTLCompareFunction.Always,
            IsDepthWriteEnabled = state.DepthWriteEnabled
        };
        try
        {
            if (state.Stencil.Enabled)
            {
                using MTLStencilDescriptor front = CreateStencil(state.Stencil.Front);
                using MTLStencilDescriptor back = CreateStencil(state.Stencil.Back);
                descriptor.FrontFaceStencil = front;
                descriptor.BackFaceStencil = back;
            }

            MTLDepthStencilState result = _device.NewDepthStencilState(descriptor);
            if (result.NativePtr == 0)
                throw new InvalidOperationException("Metal failed to create a depth/stencil state.");
            _states.Add(state, result);
            return result;
        }
        finally
        {
            descriptor.Dispose();
        }
    }

    internal void ApplyRasterState(
        MTLRenderCommandEncoder encoder,
        RenderState authoredState)
    {
        ThrowIfDisposed();
        if (encoder.NativePtr == 0)
            throw new ArgumentException("A Metal render encoder is required.", nameof(encoder));

        RenderState state = Effective(authoredState);
        encoder.SetDepthStencilState(GetOrCreate(state));
        encoder.SetFrontFacingWinding(MTLWinding.CounterClockwise);
        encoder.SetCullMode(Cull.Resolve(state) switch
        {
            CullMode.Disabled => MTLCullMode.None,
            CullMode.Front => MTLCullMode.Front,
            CullMode.Back => MTLCullMode.Back,
            _ => throw new InvalidOperationException(
                "The authored cull tuple is not executable.")
        });
        encoder.SetTriangleFillMode(state.PolygonMode switch
        {
            RsxPolygonMode.Fill => MTLTriangleFillMode.Fill,
            RsxPolygonMode.Line => MTLTriangleFillMode.Lines,
            _ => throw new InvalidOperationException(
                "The authored polygon mode is not executable.")
        });
        if (state.Stencil.Enabled)
        {
            encoder.SetStencilReferenceValues(
                checked((uint)state.Stencil.Front.Reference),
                checked((uint)state.Stencil.Back.Reference));
        }

        switch (state.PolygonOffsetMode)
        {
            case RenderPolygonOffsetMode.Disabled:
                _hasInheritedDepthBias = true;
                _inheritedDepthBias = 0f;
                _inheritedSlopeScale = 0f;
                break;
            case RenderPolygonOffsetMode.Explicit:
                _hasInheritedDepthBias = true;
                _inheritedDepthBias = ConvertPs3PolygonOffsetUnits(
                    state.PolygonOffsetUnits);
                _inheritedSlopeScale = state.PolygonOffsetFactor;
                break;
            case RenderPolygonOffsetMode.Inherit:
                if (!_hasInheritedDepthBias)
                {
                    throw new InvalidOperationException(
                        "An inherited polygon offset has no prior Metal state.");
                }
                break;
            default:
                throw new InvalidOperationException(
                    "The authored polygon-offset mode is not executable.");
        }

        ApplyResolvedDepthBias(
            encoder,
            _inheritedDepthBias,
            _inheritedSlopeScale);
    }

    internal void ApplyDepthBiasOverride(
        MTLRenderCommandEncoder encoder,
        float polygonOffsetFactor,
        float polygonOffsetUnits)
    {
        ThrowIfDisposed();
        if (encoder.NativePtr == 0)
            throw new ArgumentException("A Metal render encoder is required.", nameof(encoder));
        if (!float.IsFinite(polygonOffsetFactor) ||
            !float.IsFinite(polygonOffsetUnits))
        {
            throw new ArgumentOutOfRangeException(
                nameof(polygonOffsetFactor),
                "Metal polygon-offset overrides must be finite.");
        }

        ApplyResolvedDepthBias(
            encoder,
            ConvertPs3PolygonOffsetUnits(polygonOffsetUnits),
            polygonOffsetFactor);
    }

    internal void ResetEncoderInheritance()
    {
        _hasInheritedDepthBias = false;
        _inheritedDepthBias = 0f;
        _inheritedSlopeScale = 0f;
        _hasCurrentDepthBias = false;
        _currentDepthBias = default;
    }

    internal static float ConvertPs3PolygonOffsetUnits(float units) =>
        units / Ps3Depth24Maximum;

    internal static RenderState Effective(RenderState state) =>
        state.HasState ? state : RenderState.Default with { HasState = true };

    private void ApplyResolvedDepthBias(
        MTLRenderCommandEncoder encoder,
        float constantDepthBias,
        float slopeScale)
    {
        _hasCurrentDepthBias = true;
        _currentDepthBias = new Vector2(constantDepthBias, slopeScale);
        if (_emulatesDepth24)
        {
            // D32 applies hardware bias after a fragment depth export. Keep it
            // disabled so the emulation shader can add the RSX bias before the
            // result is snapped to the fixed-point D24 grid.
            encoder.SetDepthBias(0f, 0f, 0f);
            return;
        }

        encoder.SetDepthBias(constantDepthBias, slopeScale, 0f);
    }

    internal static void ConfigureColorAttachment(
        MTLRenderPipelineColorAttachmentDescriptor attachment,
        RenderState authoredState,
        MTLPixelFormat pixelFormat)
    {
        if (attachment.NativePtr == 0)
            throw new ArgumentException("A Metal color attachment is required.", nameof(attachment));

        RenderState state = Effective(authoredState);
        attachment.PixelFormat = pixelFormat;
        attachment.WriteMask = ToColorWriteMask(state.ColorMask);
        attachment.IsBlendingEnabled = state.BlendEnabled;
        if (!state.BlendEnabled)
            return;

        attachment.RgbBlendOperation = ToBlendOperation(state.BlendEquationRgb);
        attachment.AlphaBlendOperation = ToBlendOperation(state.BlendEquationAlpha);
        attachment.SourceRGBBlendFactor = ToBlendFactor(state.BlendSourceRgb);
        attachment.DestinationRGBBlendFactor = ToBlendFactor(state.BlendDestinationRgb);
        attachment.SourceAlphaBlendFactor = ToBlendFactor(state.BlendSourceAlpha);
        attachment.DestinationAlphaBlendFactor = ToBlendFactor(state.BlendDestinationAlpha);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (MTLDepthStencilState state in _states.Values)
            state.Dispose();
        _states.Clear();
    }

    private static MTLStencilDescriptor CreateStencil(StencilFaceState state)
    {
        var descriptor = new MTLStencilDescriptor
        {
            StencilCompareFunction = ToCompare(state.Function),
            ReadMask = state.CompareMask,
            // The IW4 Scene target owns one eight-bit stencil plane. The
            // native material writer always enables that complete plane.
            WriteMask = byte.MaxValue,
            StencilFailureOperation = ToStencilOperation(state.FailOperation),
            DepthFailureOperation = ToStencilOperation(state.DepthFailOperation),
            DepthStencilPassOperation = ToStencilOperation(state.PassOperation)
        };
        return descriptor;
    }

    private static MTLCompareFunction ToCompare(RsxCompareFunction value) =>
        value switch
        {
            RsxCompareFunction.Never => MTLCompareFunction.Never,
            RsxCompareFunction.Less => MTLCompareFunction.Less,
            RsxCompareFunction.Equal => MTLCompareFunction.Equal,
            RsxCompareFunction.LessThanOrEqual => MTLCompareFunction.LessEqual,
            RsxCompareFunction.Greater => MTLCompareFunction.Greater,
            RsxCompareFunction.NotEqual => MTLCompareFunction.NotEqual,
            RsxCompareFunction.GreaterThanOrEqual => MTLCompareFunction.GreaterEqual,
            RsxCompareFunction.Always => MTLCompareFunction.Always,
            _ => throw new InvalidOperationException(
                $"Unsupported RSX comparison function 0x{(uint)value:X4}.")
        };

    private static MTLStencilOperation ToStencilOperation(
        RsxStencilOperation value) => value switch
    {
        RsxStencilOperation.Zero => MTLStencilOperation.Zero,
        RsxStencilOperation.Invert => MTLStencilOperation.Invert,
        RsxStencilOperation.Keep => MTLStencilOperation.Keep,
        RsxStencilOperation.Replace => MTLStencilOperation.Replace,
        RsxStencilOperation.IncrementSaturate => MTLStencilOperation.IncrementClamp,
        RsxStencilOperation.DecrementSaturate => MTLStencilOperation.DecrementClamp,
        RsxStencilOperation.IncrementWrap => MTLStencilOperation.IncrementWrap,
        RsxStencilOperation.DecrementWrap => MTLStencilOperation.DecrementWrap,
        _ => throw new InvalidOperationException(
            $"Unsupported RSX stencil operation 0x{(uint)value:X4}.")
    };

    private static MTLColorWriteMask ToColorWriteMask(RsxColorMask value)
    {
        MTLColorWriteMask result = MTLColorWriteMask.None;
        if ((value & RsxColorMask.Red) != 0)
            result |= MTLColorWriteMask.Red;
        if ((value & RsxColorMask.Green) != 0)
            result |= MTLColorWriteMask.Green;
        if ((value & RsxColorMask.Blue) != 0)
            result |= MTLColorWriteMask.Blue;
        if ((value & RsxColorMask.Alpha) != 0)
            result |= MTLColorWriteMask.Alpha;
        return result;
    }

    private static MTLBlendOperation ToBlendOperation(
        RsxBlendEquation value) => value switch
    {
        RsxBlendEquation.Add => MTLBlendOperation.Add,
        RsxBlendEquation.Subtract => MTLBlendOperation.Subtract,
        RsxBlendEquation.ReverseSubtract => MTLBlendOperation.ReverseSubtract,
        RsxBlendEquation.Minimum => MTLBlendOperation.Min,
        RsxBlendEquation.Maximum => MTLBlendOperation.Max,
        _ => throw new InvalidOperationException(
            $"Unsupported RSX blend equation 0x{(uint)value:X4}.")
    };

    private static MTLBlendFactor ToBlendFactor(RsxBlendFactor value) =>
        value switch
        {
            RsxBlendFactor.Zero => MTLBlendFactor.Zero,
            RsxBlendFactor.One => MTLBlendFactor.One,
            RsxBlendFactor.SourceColor => MTLBlendFactor.SourceColor,
            RsxBlendFactor.OneMinusSourceColor => MTLBlendFactor.OneMinusSourceColor,
            RsxBlendFactor.SourceAlpha => MTLBlendFactor.SourceAlpha,
            RsxBlendFactor.OneMinusSourceAlpha => MTLBlendFactor.OneMinusSourceAlpha,
            RsxBlendFactor.DestinationColor => MTLBlendFactor.DestinationColor,
            RsxBlendFactor.OneMinusDestinationColor => MTLBlendFactor.OneMinusDestinationColor,
            RsxBlendFactor.DestinationAlpha => MTLBlendFactor.DestinationAlpha,
            RsxBlendFactor.OneMinusDestinationAlpha => MTLBlendFactor.OneMinusDestinationAlpha,
            _ => throw new InvalidOperationException(
                $"Unsupported RSX blend factor 0x{(uint)value:X4}.")
        };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
