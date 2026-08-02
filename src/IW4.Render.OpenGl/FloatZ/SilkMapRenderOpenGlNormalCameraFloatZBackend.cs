using System.Numerics;
using IW4.Render.Execution;
using IW4.Render.OpenGl.Presentation;
using IW4.Render.OpenGl.Programs;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.Lifecycle;
using IW4.Render.Shaders;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.FloatZ;

/// <summary>
/// Reusable OpenGL realization of the PS3 target-2 raw depth view followed by
/// the exact authored target-5 and target-8 FloatZ passes.
/// </summary>
internal sealed unsafe class
    SilkMapRenderOpenGlNormalCameraFloatZBackend : IDisposable
{
    // PS3 exposes the two-sample D24S8 allocation through a linear, doubled-
    // width A8R8G8B8 view. This host-only program reproduces that byte view;
    // it does not perform a depth reduction. Each GL sample is written into
    // its corresponding adjacent raw-view texel.
    private const string RawDepthViewVertexGlsl = """
        #version 330 core
        void main()
        {
            const vec2 positions[3] = vec2[3](
                vec2(-1.0, -1.0),
                vec2( 3.0, -1.0),
                vec2(-1.0,  3.0));
            gl_Position = vec4(positions[gl_VertexID], 0.0, 1.0);
        }
        """;

    private const string RawDepthViewFragmentGlsl = """
        #version 330 core
        uniform sampler2DMS rsxSampler0;
        layout(location = 0) out vec4 FragColor;
        void main()
        {
            ivec2 rawPixel = ivec2(gl_FragCoord.xy);
            ivec2 scenePixel = ivec2(rawPixel.x >> 1, rawPixel.y);
            int storedSample = rawPixel.x & 1;
            float depth = texelFetch(
                rsxSampler0,
                scenePixel,
                storedSample).r;
            uint z24 = uint(round(
                clamp(depth, 0.0, 1.0) * 16777215.0));

            // CELL_GCM_TEXTURE_A8R8G8B8 over D24S8 is consumed by the
            // authored $floatz shader as sample.wxy: high, middle, low.
            float highByte = float((z24 >> 16u) & 255u) / 255.0;
            float middleByte = float((z24 >> 8u) & 255u) / 255.0;
            float lowByte = float(z24 & 255u) / 255.0;
            FragColor = vec4(
                middleByte,
                lowByte,
                0.0,
                highByte);
        }
        """;

    private readonly GL _gl;
    private readonly SilkOpenGlStateShadow _state;
    private readonly MapRenderOpenGlNormalCameraFloatZProgramSources
        _sources;
    private readonly MapRenderOpenGlPresentationErrorMonitor _errorMonitor;
    private readonly MapRenderOpenGlProgramResource _rawDepthViewProgram;
    private readonly MapRenderOpenGlProgramResource _floatZProgram;
    private readonly MapRenderOpenGlProgramResource _processedFloatZProgram;
    private readonly int _ownerThreadId;
    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _indexBuffer;
    private uint _rawDepthViewTexture;
    private uint _floatZTexture;
    private uint _processedFloatZTexture;
    private uint _rawDepthViewFramebuffer;
    private uint _floatZFramebuffer;
    private uint _processedFloatZFramebuffer;
    private uint _pointClampSampler;
    private int _sceneWidth;
    private int _sceneHeight;
    private int _floatZWidth;
    private int _floatZHeight;
    private bool _disposed;

    internal SilkMapRenderOpenGlNormalCameraFloatZBackend(
        GL gl,
        SilkOpenGlStateShadow state,
        MapRenderWorldSceneSource source,
        MapRenderOpenGlProgramCache programs,
        MapRenderOpenGlPresentationErrorMonitor errorMonitor)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(programs);
        _errorMonitor = errorMonitor ??
            throw new ArgumentNullException(nameof(errorMonitor));
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _sources =
            MapRenderOpenGlNormalCameraFloatZProgramResolver.Resolve(source);
        if (_sources.AssetPoolRevision !=
                source.AssetPoolRevisionAtConstruction ||
            !source.AssetLookup.HasCanonicalAssetPoolRevision(
                _sources.AssetPoolRevision))
        {
            throw new ArgumentException(
                "FloatZ programs do not belong to the scene's canonical asset revision.",
                nameof(source));
        }

        _rawDepthViewProgram = programs.GetOrCompile(
            RawDepthViewVertexGlsl,
            RawDepthViewFragmentGlsl);
        _floatZProgram = programs.GetOrCompileAuthored(
            _sources.FloatZVertexGlsl,
            _sources.FloatZPixelSource);
        _processedFloatZProgram = programs.GetOrCompileAuthored(
            _sources.ProcessedFloatZVertexGlsl,
            _sources.ProcessedFloatZPixelSource);

        try
        {
            _errorMonitor.BeginStage("FloatZ resource initialization");
            CreateResources();
            InitializeProgramBindings();
            _errorMonitor.RequireNoErrors(
                "FloatZ resource initialization",
                MapRenderOpenGlPresentationErrorValidationPoint.Forced);
        }
        catch
        {
            DeleteResources();
            throw;
        }
    }

    internal void Resize(int sceneWidth, int sceneHeight)
    {
        EnsureUsableOnOwnerThread();
        if (sceneWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneWidth));
        if (sceneHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneHeight));
        if (_sceneWidth == sceneWidth && _sceneHeight == sceneHeight)
            return;

        MapRenderNormalCameraFloatZRecipe recipe =
            MapRenderNormalCameraFloatZRecipe.Current;
        MapRenderNormalCameraTargetExtent floatZExtent =
            recipe.FloatZTarget.ResolveExtent(sceneWidth, sceneHeight);
        MapRenderNormalCameraTargetExtent processedExtent =
            recipe.ProcessedFloatZTarget.ResolveExtent(
                sceneWidth,
                sceneHeight);
        if (floatZExtent != processedExtent ||
            floatZExtent.LogicalWidth <= 0 ||
            floatZExtent.LogicalHeight <= 0)
        {
            throw new InvalidOperationException(
                "FloatZ target 5 and target 8 no longer share one half-display extent.");
        }

        int rawWidth = checked(sceneWidth * 2);
        ResizeTexture(
            _rawDepthViewTexture,
            _rawDepthViewFramebuffer,
            InternalFormat.Rgba8,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            rawWidth,
            sceneHeight,
            "target-2 raw D24S8 view");
        ResizeTexture(
            _floatZTexture,
            _floatZFramebuffer,
            InternalFormat.R32f,
            PixelFormat.Red,
            PixelType.Float,
            floatZExtent.LogicalWidth,
            floatZExtent.LogicalHeight,
            "target 5 FloatZ");
        ResizeTexture(
            _processedFloatZTexture,
            _processedFloatZFramebuffer,
            InternalFormat.R32f,
            PixelFormat.Red,
            PixelType.Float,
            processedExtent.LogicalWidth,
            processedExtent.LogicalHeight,
            "target 8 ProcessedFloatZ");
        UploadFullscreenGeometry(
            floatZExtent.LogicalWidth,
            floatZExtent.LogicalHeight);
        InitializeAuthoredConstants(
            floatZExtent.LogicalWidth,
            floatZExtent.LogicalHeight);

        _sceneWidth = sceneWidth;
        _sceneHeight = sceneHeight;
        _floatZWidth = floatZExtent.LogicalWidth;
        _floatZHeight = floatZExtent.LogicalHeight;
    }

    internal MapRenderOpenGlProcessedFloatZFrame Execute(
        EditorPresentationFrame frame,
        float zNear)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.SceneTarget.Extent.LogicalWidth != _sceneWidth ||
            frame.SceneTarget.Extent.LogicalHeight != _sceneHeight ||
            _floatZWidth <= 0 ||
            _floatZHeight <= 0)
        {
            throw new InvalidOperationException(
                "FloatZ resources do not match the active target-2 extent.");
        }

        MapRenderShaderConstantValue zNearRow =
            FrameDirectCodeConstants.ProduceZNear(zNear).Value;
        if (!_processedFloatZProgram
                .TryGetCodePixelConstantUniformLocation(
                    FrameDirectCodeConstants.ZNearRowIndex,
                    out int zNearLocation))
        {
            throw new InvalidOperationException(
                "The exact $processed_floatz program has no direct ZNEAR row.");
        }

        _errorMonitor.BeginStage(
            "entry before target-2 raw FloatZ view");
        _errorMonitor.RequireNoErrors(
            "entry before target-2 raw FloatZ view",
            MapRenderOpenGlPresentationErrorValidationPoint.CapturedOnly);
        try
        {
            _errorMonitor.BeginStage(
                "target-2 D24S8 raw FloatZ view");
            ApplyHostAdapterState();
            _state.BindFramebuffer(
                FramebufferTarget.DrawFramebuffer,
                _rawDepthViewFramebuffer);
            _gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
            _state.Viewport(
                0,
                0,
                checked(_sceneWidth * 2),
                _sceneHeight);
            _state.UseProgram(_rawDepthViewProgram.Handle);
            _state.ActiveTexture(0);
            _state.BindSampler(0, 0);
            _state.BindTexture(
                TextureTarget.Texture2DMultisample,
                frame.SceneTarget.Binding.Resource
                    .DepthStencilTextureHandle);
            _state.BindVertexArray(_vertexArray);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

            _errorMonitor.BeginStage("authored $floatz target 5");
            ApplyAuthoredFullscreenState();
            _state.BindFramebuffer(
                FramebufferTarget.DrawFramebuffer,
                _floatZFramebuffer);
            _gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
            _state.Viewport(0, 0, _floatZWidth, _floatZHeight);
            _state.UseProgram(_floatZProgram.Handle);
            _state.ActiveTexture(0);
            _state.BindSampler(0, _pointClampSampler);
            _state.BindTexture(
                TextureTarget.Texture2D,
                _rawDepthViewTexture);
            _state.BindVertexArray(_vertexArray);
            _gl.DrawElements(
                PrimitiveType.Triangles,
                6,
                DrawElementsType.UnsignedShort,
                null);

            _errorMonitor.BeginStage(
                "authored $processed_floatz target 8");
            _state.BindFramebuffer(
                FramebufferTarget.DrawFramebuffer,
                _processedFloatZFramebuffer);
            _gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
            _state.UseProgram(_processedFloatZProgram.Handle);
            _state.Uniform4(
                zNearLocation,
                zNearRow.X,
                zNearRow.Y,
                zNearRow.Z,
                zNearRow.W);
            _state.ActiveTexture(0);
            _state.BindSampler(0, _pointClampSampler);
            _state.BindTexture(TextureTarget.Texture2D, _floatZTexture);
            _gl.DrawElements(
                PrimitiveType.Triangles,
                6,
                DrawElementsType.UnsignedShort,
                null);
        }
        finally
        {
            _errorMonitor.BeginStage(
                "scene target restore after FloatZ");
            RestoreSceneTarget(frame);
        }

        // Do not stamp the reusable target-8 allocation with this frame's
        // revision until every GL submission and the scene-target restoration
        // have been validated by the session's one diagnostic owner.
        _errorMonitor.RequireNoErrors(
            "FloatZ lifecycle completion",
            MapRenderOpenGlPresentationErrorValidationPoint.Forced);
        _errorMonitor.BeginStage(
            "outside default normal-camera presentation");
        return new MapRenderOpenGlProcessedFloatZFrame(
            frame.FrameRevision,
            _processedFloatZTexture,
            _pointClampSampler);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;
        _disposed = true;
        DeleteResources();
    }

    private void CreateResources()
    {
        _vertexArray = _gl.GenVertexArray();
        _vertexBuffer = _gl.GenBuffer();
        _indexBuffer = _gl.GenBuffer();
        _rawDepthViewTexture = _gl.GenTexture();
        _floatZTexture = _gl.GenTexture();
        _processedFloatZTexture = _gl.GenTexture();
        _rawDepthViewFramebuffer = _gl.GenFramebuffer();
        _floatZFramebuffer = _gl.GenFramebuffer();
        _processedFloatZFramebuffer = _gl.GenFramebuffer();
        _pointClampSampler = _gl.GenSampler();
        if (_vertexArray == 0 ||
            _vertexBuffer == 0 ||
            _indexBuffer == 0 ||
            _rawDepthViewTexture == 0 ||
            _floatZTexture == 0 ||
            _processedFloatZTexture == 0 ||
            _rawDepthViewFramebuffer == 0 ||
            _floatZFramebuffer == 0 ||
            _processedFloatZFramebuffer == 0 ||
            _pointClampSampler == 0)
        {
            throw new InvalidOperationException(
                "OpenGL did not allocate the FloatZ lifecycle resources.");
        }

        _state.BindVertexArray(_vertexArray);
        _state.BindArrayBuffer(_vertexBuffer);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
        ushort[] indices = [3, 0, 2, 2, 0, 1];
        fixed (ushort* indexPointer = indices)
        {
            _gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                checked((nuint)(indices.Length * sizeof(ushort))),
                indexPointer,
                BufferUsageARB.StaticDraw);
        }
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(
            0,
            4,
            VertexAttribPointerType.Float,
            false,
            checked((uint)sizeof(Vector4)),
            null);

        _gl.SamplerParameter(
            _pointClampSampler,
            GLEnum.TextureMinFilter,
            (int)TextureMinFilter.Nearest);
        _gl.SamplerParameter(
            _pointClampSampler,
            GLEnum.TextureMagFilter,
            (int)TextureMagFilter.Nearest);
        _gl.SamplerParameter(
            _pointClampSampler,
            GLEnum.TextureWrapS,
            (int)TextureWrapMode.ClampToEdge);
        _gl.SamplerParameter(
            _pointClampSampler,
            GLEnum.TextureWrapT,
            (int)TextureWrapMode.ClampToEdge);
    }

    private void InitializeProgramBindings()
    {
        BindSamplerZero(_rawDepthViewProgram, "raw target-2 adapter");
        BindSamplerZero(_floatZProgram, "$floatz");
        BindSamplerZero(_processedFloatZProgram, "$processed_floatz");
    }

    private void BindSamplerZero(
        MapRenderOpenGlProgramResource program,
        string identity)
    {
        if (!program.TryGetSamplerUniformLocation(0, out int location))
        {
            throw new InvalidOperationException(
                $"{identity} has no active sampler destination 0.");
        }
        _state.UseProgram(program.Handle);
        _state.Uniform1(location, 0);
    }

    private void InitializeAuthoredConstants(int width, int height)
    {
        MapRenderClipSpaceLookupCodeConstants lookup =
            FrameDirectCodeConstants.ProduceClipSpaceLookup(
                width,
                height,
                viewportX: 0,
                viewportY: 0,
                viewportWidth: width,
                viewportHeight: height);
        foreach (MapRenderOpenGlProgramResource program in
                 new[] { _floatZProgram, _processedFloatZProgram })
        {
            _state.UseProgram(program.Handle);
            SetVertexConstant(program, 0, 2f / width, 0f, 0f, 0f);
            SetVertexConstant(program, 1, 0f, -2f / height, 0f, 0f);
            SetVertexConstant(program, 2, 0f, 0f, 1f, 0f);
            SetVertexConstant(program, 3, -1f, 1f, 0f, 1f);
            SetVertexConstant(program, 17, lookup.Scale);
            SetVertexConstant(program, 18, lookup.Offset);
        }
    }

    private void SetVertexConstant(
        MapRenderOpenGlProgramResource program,
        int destination,
        float x,
        float y,
        float z,
        float w)
    {
        if (!program.TryGetVertexConstantUniformLocation(
                destination,
                out int location))
        {
            throw new InvalidOperationException(
                $"FloatZ program has no vertex constant c{destination}.");
        }
        _state.Uniform4(location, x, y, z, w);
    }

    private void SetVertexConstant(
        MapRenderOpenGlProgramResource program,
        int destination,
        MapRenderShaderConstantValue value) =>
        SetVertexConstant(
            program,
            destination,
            value.X,
            value.Y,
            value.Z,
            value.W);

    private void UploadFullscreenGeometry(int width, int height)
    {
        Vector4[] vertices =
        [
            new(0f, 0f, 0f, 1f),
            new(width, 0f, 0f, 1f),
            new(width, height, 0f, 1f),
            new(0f, height, 0f, 1f)
        ];
        _state.BindArrayBuffer(_vertexBuffer);
        fixed (Vector4* vertexPointer = vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                checked((nuint)(vertices.Length * sizeof(Vector4))),
                vertexPointer,
                BufferUsageARB.DynamicDraw);
        }
    }

    private void ResizeTexture(
        uint texture,
        uint framebuffer,
        InternalFormat internalFormat,
        PixelFormat pixelFormat,
        PixelType pixelType,
        int width,
        int height,
        string identity)
    {
        _state.ActiveTexture(0);
        _state.BindSampler(0, 0);
        _state.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            internalFormat,
            checked((uint)width),
            checked((uint)height),
            0,
            pixelFormat,
            pixelType,
            null);
        _state.BindFramebuffer(
            FramebufferTarget.DrawFramebuffer,
            framebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.DrawFramebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            texture,
            0);
        _gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
        GLEnum status = _gl.CheckFramebufferStatus(
            FramebufferTarget.DrawFramebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException(
                $"{identity} framebuffer is incomplete: {status}.");
        }
    }

    private void ApplyHostAdapterState()
    {
        _state.SetEnabled(EnableCap.FramebufferSrgb, false);
        _state.SetEnabled(EnableCap.Dither, false);
        _state.SetEnabled(EnableCap.ScissorTest, false);
        _state.SetEnabled(EnableCap.DepthTest, false);
        _state.DepthMask(false);
        _state.SetEnabled(EnableCap.StencilTest, false);
        _state.SetEnabled(EnableCap.Blend, false);
        _state.SetEnabled(EnableCap.CullFace, false);
        _state.SetEnabled(EnableCap.PolygonOffsetFill, false);
        _state.PolygonMode(PolygonMode.Fill);
        _state.ColorMask(true, true, true, true);
    }

    private void ApplyAuthoredFullscreenState()
    {
        ApplyHostAdapterState();
        _state.FrontFace(
            SilkMapRenderOpenGlNormalCameraDefaultPresenter
                .FullscreenHostFrontFace);
        _state.SetEnabled(EnableCap.CullFace, true);
        _state.CullFace(
            SilkMapRenderOpenGlNormalCameraDefaultPresenter
                .FullscreenHostCullFace);
    }

    private void RestoreSceneTarget(EditorPresentationFrame frame)
    {
        _state.BindFramebuffer(
            FramebufferTarget.DrawFramebuffer,
            frame.SceneTarget.CombinedFramebufferHandle);
        _gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
        _state.Viewport(
            frame.SceneTarget.ViewportX,
            frame.SceneTarget.ViewportY,
            frame.SceneTarget.ViewportWidth,
            frame.SceneTarget.ViewportHeight);
        _state.Scissor(
            frame.SceneTarget.HostEffectiveScissorX,
            frame.SceneTarget.HostEffectiveScissorY,
            frame.SceneTarget.HostEffectiveScissorWidth,
            frame.SceneTarget.HostEffectiveScissorHeight);
        _state.SetEnabled(
            EnableCap.ScissorTest,
            frame.SceneTarget.EnablesEffectiveSurfaceClipScissor);
        // The map renderer's established scene state follows OpenGL's
        // fixed-point framebuffer default. The raw byte-view adapter disables
        // dithering only while materializing the exact D24 byte channels.
        _state.SetEnabled(EnableCap.Dither, true);
    }

    private void DeleteResources()
    {
        if (_pointClampSampler != 0)
        {
            _state.ForgetSamplerBinding(_pointClampSampler);
            _gl.DeleteSampler(_pointClampSampler);
            _pointClampSampler = 0;
        }
        DeleteFramebuffer(ref _processedFloatZFramebuffer);
        DeleteFramebuffer(ref _floatZFramebuffer);
        DeleteFramebuffer(ref _rawDepthViewFramebuffer);
        DeleteTexture(ref _processedFloatZTexture);
        DeleteTexture(ref _floatZTexture);
        DeleteTexture(ref _rawDepthViewTexture);
        if (_indexBuffer != 0)
        {
            _gl.DeleteBuffer(_indexBuffer);
            _indexBuffer = 0;
        }
        if (_vertexBuffer != 0)
        {
            _state.ForgetArrayBufferBinding(_vertexBuffer);
            _gl.DeleteBuffer(_vertexBuffer);
            _vertexBuffer = 0;
        }
        if (_vertexArray != 0)
        {
            _state.ForgetVertexArrayBinding(_vertexArray);
            _gl.DeleteVertexArray(_vertexArray);
            _vertexArray = 0;
        }
    }

    private void DeleteTexture(ref uint handle)
    {
        if (handle == 0)
            return;
        _state.ForgetTextureBinding(handle);
        _gl.DeleteTexture(handle);
        handle = 0;
    }

    private void DeleteFramebuffer(ref uint handle)
    {
        if (handle == 0)
            return;
        _gl.DeleteFramebuffer(handle);
        handle = 0;
    }

    private void EnsureUsableOnOwnerThread()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "FloatZ resources may only be used and disposed on their owning render thread.");
        }
    }
}
