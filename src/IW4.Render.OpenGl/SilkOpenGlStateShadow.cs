using System.Numerics;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

/// <summary>
/// Authoritative state owned by the map renderer's OpenGL context. Every hot
/// render-path mutation goes through this object so unchanged state is not
/// resent through the managed/native boundary.
/// </summary>
internal sealed unsafe class SilkOpenGlStateShadow
{
    private readonly GL _gl;
    private readonly Dictionary<EnableCap, bool> _enabledCaps = [];
    private readonly Dictionary<TextureBindingKey, uint> _textureBindings = [];
    private readonly Dictionary<uint, uint> _samplerBindings = [];
    private readonly Dictionary<UniformKey, int> _uniformInts = [];
    private readonly Dictionary<UniformKey, int> _uniformFloats = [];
    private readonly Dictionary<UniformKey, Vector3Bits> _uniformVector3 = [];
    private readonly Dictionary<UniformKey, Vector4Bits> _uniformVector4 = [];
    private readonly Dictionary<UniformKey, Matrix4x4Bits> _uniformMatrices = [];
    private uint? _program;
    private uint? _vertexArray;
    private uint? _arrayBuffer;
    private uint? _drawFramebuffer;
    private uint? _readFramebuffer;
    private int? _activeTextureUnit;
    private bool? _depthMask;
    private DepthFunction? _depthFunction;
    private FrontFaceDirection? _frontFace;
    private TriangleFace? _cullFace;
    private PolygonMode? _polygonMode;
    private ColorMaskBits? _colorMask;
    private BlendEquationBits? _blendEquation;
    private BlendFunctionBits? _blendFunction;
    private PolygonOffsetBits? _polygonOffset;
    private float? _lineWidth;
    private ViewportBits? _viewport;
    private ScissorBits? _scissor;
    private uint? _stencilMask;
    private SampleMaskBits? _sampleMask;

    public SilkOpenGlStateShadow(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
    }

    public long SubmittedCalls { get; private set; }

    public long ElidedCalls { get; private set; }

    public long ProgramChanges { get; private set; }
    public long VertexArrayChanges { get; private set; }
    public long FramebufferChanges { get; private set; }
    public long BufferChanges { get; private set; }
    public long TextureChanges { get; private set; }
    public long SamplerChanges { get; private set; }
    public long RenderStateChanges { get; private set; }
    public long UniformUpdates { get; private set; }

    public void BeginFrameCounters()
    {
        SubmittedCalls = 0;
        ElidedCalls = 0;
        ProgramChanges = 0;
        VertexArrayChanges = 0;
        FramebufferChanges = 0;
        BufferChanges = 0;
        TextureChanges = 0;
        SamplerChanges = 0;
        RenderStateChanges = 0;
        UniformUpdates = 0;
    }

    public void EstablishKnownTextureBaseline(int textureUnitCount)
    {
        if (textureUnitCount < 0)
            throw new ArgumentOutOfRangeException(nameof(textureUnitCount));

        for (int unit = 0; unit < textureUnitCount; unit++)
        {
            ActiveTexture(unit);
            BindSampler((uint)unit, 0);
            BindTexture(TextureTarget.Texture2D, 0);
            BindTexture(TextureTarget.Texture2DMultisample, 0);
            BindTexture(TextureTarget.TextureCubeMap, 0);
            BindTexture(TextureTarget.Texture3D, 0);
        }
        ActiveTexture(0);
    }

    public void UseProgram(uint program)
    {
        if (_program == program)
        {
            ElidedCalls++;
            return;
        }
        _gl.UseProgram(program);
        _program = program;
        SubmittedCalls++;
        ProgramChanges++;
    }

    public void BindVertexArray(uint vertexArray)
    {
        if (_vertexArray == vertexArray)
        {
            ElidedCalls++;
            return;
        }
        _gl.BindVertexArray(vertexArray);
        _vertexArray = vertexArray;
        SubmittedCalls++;
        VertexArrayChanges++;
    }

    public void ForgetVertexArrayBinding(uint vertexArray)
    {
        if (_vertexArray == vertexArray)
            _vertexArray = null;
    }

    public void BindArrayBuffer(uint buffer)
    {
        if (_arrayBuffer == buffer)
        {
            ElidedCalls++;
            return;
        }
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer);
        _arrayBuffer = buffer;
        SubmittedCalls++;
        BufferChanges++;
    }

    public void ForgetArrayBufferBinding(uint buffer)
    {
        if (_arrayBuffer == buffer)
            _arrayBuffer = null;
    }

    public void BindFramebuffer(FramebufferTarget target, uint framebuffer)
    {
        switch (target)
        {
            case FramebufferTarget.Framebuffer:
                if (_drawFramebuffer == framebuffer &&
                    _readFramebuffer == framebuffer)
                {
                    ElidedCalls++;
                    return;
                }
                _gl.BindFramebuffer(target, framebuffer);
                _drawFramebuffer = framebuffer;
                _readFramebuffer = framebuffer;
                break;
            case FramebufferTarget.DrawFramebuffer:
                if (_drawFramebuffer == framebuffer)
                {
                    ElidedCalls++;
                    return;
                }
                _gl.BindFramebuffer(target, framebuffer);
                _drawFramebuffer = framebuffer;
                break;
            case FramebufferTarget.ReadFramebuffer:
                if (_readFramebuffer == framebuffer)
                {
                    ElidedCalls++;
                    return;
                }
                _gl.BindFramebuffer(target, framebuffer);
                _readFramebuffer = framebuffer;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
        SubmittedCalls++;
        FramebufferChanges++;
    }

    public void ActiveTexture(int textureUnit)
    {
        if (textureUnit < 0)
            throw new ArgumentOutOfRangeException(nameof(textureUnit));
        if (_activeTextureUnit == textureUnit)
        {
            ElidedCalls++;
            return;
        }
        _gl.ActiveTexture(
            (TextureUnit)((int)TextureUnit.Texture0 + textureUnit));
        _activeTextureUnit = textureUnit;
        SubmittedCalls++;
    }

    public int GetActiveTextureUnit() =>
        _activeTextureUnit ?? throw new InvalidOperationException(
            "The renderer has not established a known active texture unit.");

    public void BindTexture(TextureTarget target, uint texture)
    {
        int unit = GetActiveTextureUnit();
        var key = new TextureBindingKey(unit, target);
        if (_textureBindings.TryGetValue(key, out uint current) &&
            current == texture)
        {
            ElidedCalls++;
            return;
        }
        _gl.BindTexture(target, texture);
        _textureBindings[key] = texture;
        SubmittedCalls++;
        TextureChanges++;
    }

    public uint GetTextureBinding(int textureUnit, TextureTarget target)
    {
        if (_textureBindings.TryGetValue(
                new TextureBindingKey(textureUnit, target),
                out uint texture))
        {
            return texture;
        }
        throw new InvalidOperationException(
            $"Texture unit {textureUnit} target {target} has no authoritative renderer state.");
    }

    public void ForgetTextureBinding(uint texture)
    {
        if (texture == 0)
            return;

        foreach (TextureBindingKey key in _textureBindings
                     .Where(binding => binding.Value == texture)
                     .Select(binding => binding.Key)
                     .ToArray())
        {
            _textureBindings.Remove(key);
        }
    }

    public void BindSampler(uint textureUnit, uint sampler)
    {
        if (_samplerBindings.TryGetValue(textureUnit, out uint current) &&
            current == sampler)
        {
            ElidedCalls++;
            return;
        }
        _gl.BindSampler(textureUnit, sampler);
        _samplerBindings[textureUnit] = sampler;
        SubmittedCalls++;
        SamplerChanges++;
    }

    public void ForgetSamplerBinding(uint sampler)
    {
        if (sampler == 0)
            return;

        foreach (uint textureUnit in _samplerBindings
                     .Where(binding => binding.Value == sampler)
                     .Select(binding => binding.Key)
                     .ToArray())
        {
            _samplerBindings.Remove(textureUnit);
        }
    }

    public void SetEnabled(EnableCap capability, bool enabled)
    {
        if (_enabledCaps.TryGetValue(capability, out bool current) &&
            current == enabled)
        {
            ElidedCalls++;
            return;
        }
        if (enabled)
            _gl.Enable(capability);
        else
            _gl.Disable(capability);
        _enabledCaps[capability] = enabled;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void DepthMask(bool enabled)
    {
        if (_depthMask == enabled)
        {
            ElidedCalls++;
            return;
        }
        _gl.DepthMask(enabled);
        _depthMask = enabled;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void DepthFunc(DepthFunction function)
    {
        if (_depthFunction == function)
        {
            ElidedCalls++;
            return;
        }
        _gl.DepthFunc(function);
        _depthFunction = function;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void FrontFace(FrontFaceDirection direction)
    {
        if (_frontFace == direction)
        {
            ElidedCalls++;
            return;
        }
        _gl.FrontFace(direction);
        _frontFace = direction;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void CullFace(TriangleFace face)
    {
        if (_cullFace == face)
        {
            ElidedCalls++;
            return;
        }
        _gl.CullFace(face);
        _cullFace = face;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void PolygonMode(PolygonMode mode)
    {
        if (_polygonMode == mode)
        {
            ElidedCalls++;
            return;
        }
        _gl.PolygonMode(TriangleFace.FrontAndBack, mode);
        _polygonMode = mode;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void ColorMask(bool red, bool green, bool blue, bool alpha)
    {
        var value = new ColorMaskBits(red, green, blue, alpha);
        if (_colorMask == value)
        {
            ElidedCalls++;
            return;
        }
        _gl.ColorMask(red, green, blue, alpha);
        _colorMask = value;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void BlendEquationSeparate(
        BlendEquationModeEXT rgb,
        BlendEquationModeEXT alpha)
    {
        var value = new BlendEquationBits(rgb, alpha);
        if (_blendEquation == value)
        {
            ElidedCalls++;
            return;
        }
        _gl.BlendEquationSeparate(rgb, alpha);
        _blendEquation = value;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void BlendFuncSeparate(
        BlendingFactor sourceRgb,
        BlendingFactor destinationRgb,
        BlendingFactor sourceAlpha,
        BlendingFactor destinationAlpha)
    {
        var value = new BlendFunctionBits(
            sourceRgb,
            destinationRgb,
            sourceAlpha,
            destinationAlpha);
        if (_blendFunction == value)
        {
            ElidedCalls++;
            return;
        }
        _gl.BlendFuncSeparate(
            sourceRgb,
            destinationRgb,
            sourceAlpha,
            destinationAlpha);
        _blendFunction = value;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void PolygonOffset(float factor, float units)
    {
        var value = new PolygonOffsetBits(ToBits(factor), ToBits(units));
        if (_polygonOffset == value)
        {
            ElidedCalls++;
            return;
        }
        _gl.PolygonOffset(factor, units);
        _polygonOffset = value;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void LineWidth(float width)
    {
        if (_lineWidth.HasValue && ToBits(_lineWidth.Value) == ToBits(width))
        {
            ElidedCalls++;
            return;
        }
        _gl.LineWidth(width);
        _lineWidth = width;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void Viewport(int x, int y, int width, int height)
    {
        var value = new ViewportBits(x, y, width, height);
        if (_viewport == value)
        {
            ElidedCalls++;
            return;
        }
        _gl.Viewport(x, y, checked((uint)width), checked((uint)height));
        _viewport = value;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void Scissor(int x, int y, int width, int height)
    {
        var value = new ScissorBits(x, y, width, height);
        if (_scissor == value)
        {
            ElidedCalls++;
            return;
        }
        _gl.Scissor(x, y, checked((uint)width), checked((uint)height));
        _scissor = value;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void StencilMask(uint mask)
    {
        if (_stencilMask == mask)
        {
            ElidedCalls++;
            return;
        }
        _gl.StencilMask(mask);
        _stencilMask = mask;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void SampleMask(uint wordIndex, uint mask)
    {
        var value = new SampleMaskBits(wordIndex, mask);
        if (_sampleMask == value)
        {
            ElidedCalls++;
            return;
        }
        _gl.SampleMask(wordIndex, mask);
        _sampleMask = value;
        SubmittedCalls++;
        RenderStateChanges++;
    }

    public void Uniform1(int location, int value)
    {
        if (location < 0)
            return;
        UniformKey key = CurrentUniformKey(location);
        if (_uniformInts.TryGetValue(key, out int current) && current == value)
        {
            ElidedCalls++;
            return;
        }
        _gl.Uniform1(location, value);
        _uniformInts[key] = value;
        SubmittedCalls++;
        UniformUpdates++;
    }

    public void Uniform1(int location, float value)
    {
        if (location < 0)
            return;
        UniformKey key = CurrentUniformKey(location);
        int bits = ToBits(value);
        if (_uniformFloats.TryGetValue(key, out int current) && current == bits)
        {
            ElidedCalls++;
            return;
        }
        _gl.Uniform1(location, value);
        _uniformFloats[key] = bits;
        SubmittedCalls++;
        UniformUpdates++;
    }

    public void Uniform3(int location, float x, float y, float z)
    {
        if (location < 0)
            return;
        UniformKey key = CurrentUniformKey(location);
        var value = new Vector3Bits(ToBits(x), ToBits(y), ToBits(z));
        if (_uniformVector3.TryGetValue(key, out Vector3Bits current) &&
            current == value)
        {
            ElidedCalls++;
            return;
        }
        _gl.Uniform3(location, x, y, z);
        _uniformVector3[key] = value;
        SubmittedCalls++;
        UniformUpdates++;
    }

    public void Uniform4(int location, float x, float y, float z, float w)
    {
        if (location < 0)
            return;
        UniformKey key = CurrentUniformKey(location);
        var value = new Vector4Bits(
            ToBits(x), ToBits(y), ToBits(z), ToBits(w));
        if (_uniformVector4.TryGetValue(key, out Vector4Bits current) &&
            current == value)
        {
            ElidedCalls++;
            return;
        }
        _gl.Uniform4(location, x, y, z, w);
        _uniformVector4[key] = value;
        SubmittedCalls++;
        UniformUpdates++;
    }

    public void UniformMatrix4(int location, in Matrix4x4 matrix)
    {
        if (location < 0)
            return;
        UniformKey key = CurrentUniformKey(location);
        Matrix4x4Bits value = Matrix4x4Bits.Create(matrix);
        if (_uniformMatrices.TryGetValue(key, out Matrix4x4Bits current) &&
            current == value)
        {
            ElidedCalls++;
            return;
        }
        Matrix4x4 local = matrix;
        _gl.UniformMatrix4(location, 1, false, (float*)&local);
        _uniformMatrices[key] = value;
        SubmittedCalls++;
        UniformUpdates++;
    }

    /// <summary>
    /// Records the exact state established by the default presenter without
    /// querying it back from the driver. Other texture units are unchanged by
    /// the presenter and remain authoritative.
    /// </summary>
    public void AdoptDefaultPresenterHandoff(
        int width,
        int height,
        uint hostFramebuffer = 0)
    {
        _program = 0;
        _vertexArray = 0;
        _arrayBuffer = null;
        _drawFramebuffer = hostFramebuffer;
        _readFramebuffer = hostFramebuffer;
        _activeTextureUnit = 0;
        _textureBindings[new TextureBindingKey(0, TextureTarget.Texture2D)] = 0;
        _textureBindings[new TextureBindingKey(
            0,
            TextureTarget.Texture2DMultisample)] = 0;
        _samplerBindings[0] = 0;
        _viewport = new ViewportBits(0, 0, width, height);

        // Fullscreen presentation writes these states directly. Mark fixed
        // state unknown so the next authored pass reestablishes it once.
        _enabledCaps.Clear();
        _depthMask = null;
        _depthFunction = null;
        _frontFace = null;
        _cullFace = null;
        _polygonMode = null;
        _colorMask = null;
        _blendEquation = null;
        _blendFunction = null;
        _polygonOffset = null;
        _lineWidth = null;
        _scissor = null;
        _stencilMask = null;
        _sampleMask = null;
    }

    public void InvalidateAll()
    {
        _program = null;
        _vertexArray = null;
        _arrayBuffer = null;
        _drawFramebuffer = null;
        _readFramebuffer = null;
        _activeTextureUnit = null;
        _enabledCaps.Clear();
        _textureBindings.Clear();
        _samplerBindings.Clear();
        _depthMask = null;
        _depthFunction = null;
        _frontFace = null;
        _cullFace = null;
        _polygonMode = null;
        _colorMask = null;
        _blendEquation = null;
        _blendFunction = null;
        _polygonOffset = null;
        _lineWidth = null;
        _viewport = null;
        _scissor = null;
        _stencilMask = null;
        _sampleMask = null;
        _uniformInts.Clear();
        _uniformFloats.Clear();
        _uniformVector3.Clear();
        _uniformVector4.Clear();
        _uniformMatrices.Clear();
    }

    private UniformKey CurrentUniformKey(int location) =>
        new(_program ?? throw new InvalidOperationException(
            "A uniform cannot be written without an authoritative current program."),
            location);

    private static int ToBits(float value) =>
        BitConverter.SingleToInt32Bits(value);

    private readonly record struct TextureBindingKey(
        int TextureUnit,
        TextureTarget Target);
    private readonly record struct UniformKey(uint Program, int Location);
    private readonly record struct Vector3Bits(int X, int Y, int Z);
    private readonly record struct Vector4Bits(int X, int Y, int Z, int W);
    private readonly record struct ColorMaskBits(
        bool Red,
        bool Green,
        bool Blue,
        bool Alpha);
    private readonly record struct BlendEquationBits(
        BlendEquationModeEXT Rgb,
        BlendEquationModeEXT Alpha);
    private readonly record struct BlendFunctionBits(
        BlendingFactor SourceRgb,
        BlendingFactor DestinationRgb,
        BlendingFactor SourceAlpha,
        BlendingFactor DestinationAlpha);
    private readonly record struct PolygonOffsetBits(int Factor, int Units);
    private readonly record struct ViewportBits(int X, int Y, int Width, int Height);
    private readonly record struct ScissorBits(int X, int Y, int Width, int Height);
    private readonly record struct SampleMaskBits(uint WordIndex, uint Mask);
    private readonly record struct Matrix4x4Bits(
        int M11, int M12, int M13, int M14,
        int M21, int M22, int M23, int M24,
        int M31, int M32, int M33, int M34,
        int M41, int M42, int M43, int M44)
    {
        public static Matrix4x4Bits Create(in Matrix4x4 value) => new(
            ToBits(value.M11), ToBits(value.M12), ToBits(value.M13), ToBits(value.M14),
            ToBits(value.M21), ToBits(value.M22), ToBits(value.M23), ToBits(value.M24),
            ToBits(value.M31), ToBits(value.M32), ToBits(value.M33), ToBits(value.M34),
            ToBits(value.M41), ToBits(value.M42), ToBits(value.M43), ToBits(value.M44));
    }
}
