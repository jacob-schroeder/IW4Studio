using IW4.Render.OpenGl.Programs;
using IW4.Render.Resources;
using IW4.Render.Textures;
using Silk.NET.OpenGL;
using TextureTarget = Silk.NET.OpenGL.TextureTarget;

namespace IW4.Render.OpenGl.MaterialPreview;

/// <summary>
/// Context-owned retained OpenGL preview for one decoded material texture.
/// The caller must keep the creating context current for every operation.
/// </summary>
public sealed unsafe class SilkMaterialTexturePreviewRenderer : IDisposable
{
    private const int MaximumVolumeRaySteps = 256;
    private const string ProgramContextIdentity =
        "iw4-material-texture-preview/context-owned";
    private const string ProgramLinkProfileIdentity =
        "iw4-material-texture-preview/glsl330-core/v1";

    private readonly GL _gl;
    private readonly SilkMapRenderOpenGlProgramCompiler _programCompiler;
    private readonly SilkOpenGlTextureParameters _textureParameters;
    private readonly int _ownerThreadId;
    private readonly uint _vertexArray;
    private readonly uint _twoDimensionalProgram;
    private readonly int _twoDimensionalTextureLocation;
    private readonly int _twoDimensionalMipLocation;
    private readonly int _twoDimensionalViewportSizeLocation;
    private readonly int _twoDimensionalTextureSizeLocation;
    private readonly int _twoDimensionalZoomLocation;
    private readonly uint _cubeProgram;
    private readonly int _cubeTextureLocation;
    private readonly int _cubeMipLocation;
    private readonly int _cubeViewportSizeLocation;
    private readonly int _cubeYawLocation;
    private readonly int _cubePitchLocation;
    private readonly int _cubeZoomLocation;
    private readonly uint _volumeProgram;
    private readonly int _volumeTextureLocation;
    private readonly int _volumeMipLocation;
    private readonly int _volumeViewportSizeLocation;
    private readonly int _volumeHalfExtentLocation;
    private readonly int _volumeStepCountLocation;
    private readonly int _volumeYawLocation;
    private readonly int _volumePitchLocation;
    private readonly int _volumeZoomLocation;

    private RenderTextureDescriptor? _uploadedTexture;
    private TextureTarget _uploadedTarget;
    private uint _textureHandle;
    private bool _disposed;

    public SilkMaterialTexturePreviewRenderer(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _textureParameters = new SilkOpenGlTextureParameters(gl);
        _programCompiler = new SilkMapRenderOpenGlProgramCompiler(
            gl,
            ProgramContextIdentity,
            ProgramLinkProfileIdentity);

        uint twoDimensionalProgram = 0;
        uint cubeProgram = 0;
        uint volumeProgram = 0;
        uint vertexArray = 0;
        try
        {
            twoDimensionalProgram = CompilePreviewProgram(
                "two-dimensional",
                TwoDimensionalFragmentShaderSource);
            _twoDimensionalTextureLocation = RequireUniform(
                twoDimensionalProgram,
                "uTexture");
            _twoDimensionalMipLocation = RequireUniform(
                twoDimensionalProgram,
                "uMip");
            _twoDimensionalViewportSizeLocation = RequireUniform(
                twoDimensionalProgram,
                "uViewportSize");
            _twoDimensionalTextureSizeLocation = RequireUniform(
                twoDimensionalProgram,
                "uTextureSize");
            _twoDimensionalZoomLocation = RequireUniform(
                twoDimensionalProgram,
                "uZoom");

            cubeProgram = CompilePreviewProgram(
                "cube",
                CubeFragmentShaderSource);
            _cubeTextureLocation = RequireUniform(cubeProgram, "uTexture");
            _cubeMipLocation = RequireUniform(cubeProgram, "uMip");
            _cubeViewportSizeLocation = RequireUniform(
                cubeProgram,
                "uViewportSize");
            _cubeYawLocation = RequireUniform(cubeProgram, "uYaw");
            _cubePitchLocation = RequireUniform(cubeProgram, "uPitch");
            _cubeZoomLocation = RequireUniform(cubeProgram, "uZoom");

            volumeProgram = CompilePreviewProgram(
                "volume",
                VolumeFragmentShaderSource);
            _volumeTextureLocation = RequireUniform(
                volumeProgram,
                "uTexture");
            _volumeMipLocation = RequireUniform(volumeProgram, "uMip");
            _volumeViewportSizeLocation = RequireUniform(
                volumeProgram,
                "uViewportSize");
            _volumeHalfExtentLocation = RequireUniform(
                volumeProgram,
                "uHalfExtent");
            _volumeStepCountLocation = RequireUniform(
                volumeProgram,
                "uStepCount");
            _volumeYawLocation = RequireUniform(volumeProgram, "uYaw");
            _volumePitchLocation = RequireUniform(volumeProgram, "uPitch");
            _volumeZoomLocation = RequireUniform(volumeProgram, "uZoom");

            vertexArray = _gl.GenVertexArray();
            if (vertexArray == 0)
            {
                throw new InvalidOperationException(
                    "OpenGL did not allocate the material texture preview vertex array.");
            }
        }
        catch
        {
            if (vertexArray != 0)
                _gl.DeleteVertexArray(vertexArray);
            if (volumeProgram != 0)
                _programCompiler.DeleteProgram(volumeProgram);
            if (cubeProgram != 0)
                _programCompiler.DeleteProgram(cubeProgram);
            if (twoDimensionalProgram != 0)
                _programCompiler.DeleteProgram(twoDimensionalProgram);
            throw;
        }

        _twoDimensionalProgram = twoDimensionalProgram;
        _cubeProgram = cubeProgram;
        _volumeProgram = volumeProgram;
        _vertexArray = vertexArray;
    }

    /// <summary>
    /// Replaces the retained preview with every declared decoded RGBA8 mip.
    /// Cube subresources are consumed in OpenGL/DDS face order (+X, -X, +Y,
    /// -Y, +Z, -Z); 3D subresources contain tightly packed depth slices.
    /// </summary>
    public void Upload(
        RenderTextureDescriptor texture,
        bool useSrgbReads)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(texture);
        ValidatePreviewTopology(texture);
        ValidateDecodedPayloads(texture);

        TextureTarget target = ToOpenGlTarget(texture.Dimension);
        uint replacement = _gl.GenTexture();
        if (replacement == 0)
        {
            throw new InvalidOperationException(
                $"OpenGL did not allocate material preview texture '{texture.Name}'.");
        }

        try
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindSampler(0, 0);
            _gl.BindTexture(target, replacement);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            InternalFormat internalFormat = useSrgbReads
                ? InternalFormat.Srgb8Alpha8
                : InternalFormat.Rgba8;
            switch (texture.Dimension)
            {
                case RenderTextureDimension.Texture2D:
                    UploadTwoDimensional(texture, internalFormat);
                    break;
                case RenderTextureDimension.TextureCube:
                    UploadCube(texture, internalFormat);
                    break;
                case RenderTextureDimension.Texture3D:
                    UploadVolume(texture, internalFormat);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(texture),
                        texture.Dimension,
                        "Unknown material preview texture dimension.");
            }
            _textureParameters.ApplySwizzle(
                RsxTextureSwizzleDecoder.Decode(
                    new RsxTextureCommandState(
                        texture.Source.TexOffsetPayload,
                        texture.Source.TexFormatPayload,
                        texture.Source.TexNpotSizePayload,
                        texture.Source.TexSize1Payload,
                        texture.Source.TexSwizzlePayload)),
                target);
            ApplySamplerParameters(target, texture.MipCount);
        }
        catch
        {
            _gl.BindTexture(target, 0);
            _gl.DeleteTexture(replacement);
            throw;
        }
        finally
        {
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            _gl.BindTexture(target, 0);
        }

        uint previous = _textureHandle;
        _textureHandle = replacement;
        _uploadedTarget = target;
        _uploadedTexture = texture;
        if (previous != 0)
            _gl.DeleteTexture(previous);
    }

    /// <summary>
    /// Draws the retained texture into the supplied framebuffer. Angles are
    /// radians and <paramref name="zoom"/> is a positive magnification.
    /// The selected mip is sampled explicitly with GLSL textureLod.
    /// </summary>
    public void Render(
        int framebuffer,
        int width,
        int height,
        int selectedMip,
        float yaw,
        float pitch,
        float zoom)
    {
        ThrowIfUnavailable();
        if (framebuffer < 0)
            throw new ArgumentOutOfRangeException(nameof(framebuffer));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (!float.IsFinite(yaw))
            throw new ArgumentOutOfRangeException(nameof(yaw));
        if (!float.IsFinite(pitch))
            throw new ArgumentOutOfRangeException(nameof(pitch));
        if (!float.IsFinite(zoom) || zoom <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(zoom),
                "Material texture preview zoom must be finite and positive.");
        }
        RenderTextureDescriptor texture = _uploadedTexture ??
            throw new InvalidOperationException(
                "A material texture must be uploaded before rendering its preview.");
        if ((uint)selectedMip >= (uint)texture.MipCount)
            throw new ArgumentOutOfRangeException(nameof(selectedMip));

        EstablishFrameState(framebuffer, width, height);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindSampler(0, 0);
        _gl.BindTexture(_uploadedTarget, _textureHandle);
        _gl.BindVertexArray(_vertexArray);
        switch (texture.Dimension)
        {
            case RenderTextureDimension.Texture2D:
                RenderTwoDimensional(
                    texture,
                    width,
                    height,
                    selectedMip,
                    zoom);
                break;
            case RenderTextureDimension.TextureCube:
                RenderCube(width, height, selectedMip, yaw, pitch, zoom);
                break;
            case RenderTextureDimension.Texture3D:
                RenderVolume(
                    texture,
                    width,
                    height,
                    selectedMip,
                    yaw,
                    pitch,
                    zoom);
                break;
            default:
                throw new InvalidOperationException(
                    $"Uploaded texture dimension '{texture.Dimension}' is no longer supported.");
        }
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
        _gl.BindTexture(_uploadedTarget, 0);
        _gl.UseProgram(0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        RequireOwnerThread();

        if (_textureHandle != 0)
            _gl.DeleteTexture(_textureHandle);
        _gl.DeleteVertexArray(_vertexArray);
        _programCompiler.DeleteProgram(_volumeProgram);
        _programCompiler.DeleteProgram(_cubeProgram);
        _programCompiler.DeleteProgram(_twoDimensionalProgram);
        _textureHandle = 0;
        _uploadedTexture = null;
        _disposed = true;
    }

    private void UploadTwoDimensional(
        RenderTextureDescriptor texture,
        InternalFormat internalFormat)
    {
        for (int mip = 0; mip < texture.MipCount; mip++)
        {
            RenderTextureSubresourceDescriptor subresource =
                texture.RequireSubresource(mip, 0);
            RenderTexturePayloadDescriptor payload =
                RequireDecodedPayload(texture, subresource);
            fixed (byte* pixels = payload.Payload.AsSpan())
            {
                _gl.TexImage2D(
                    TextureTarget.Texture2D,
                    mip,
                    internalFormat,
                    checked((uint)subresource.Width),
                    checked((uint)subresource.Height),
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixels);
            }
        }
    }

    private void UploadCube(
        RenderTextureDescriptor texture,
        InternalFormat internalFormat)
    {
        for (int face = 0; face < texture.FaceCount; face++)
        {
            TextureTarget faceTarget = (TextureTarget)(
                (int)TextureTarget.TextureCubeMapPositiveX + face);
            for (int mip = 0; mip < texture.MipCount; mip++)
            {
                RenderTextureSubresourceDescriptor subresource =
                    texture.RequireSubresource(mip, layer: 0, face);
                RenderTexturePayloadDescriptor payload =
                    RequireDecodedPayload(texture, subresource);
                fixed (byte* pixels = payload.Payload.AsSpan())
                {
                    _gl.TexImage2D(
                        faceTarget,
                        mip,
                        internalFormat,
                        checked((uint)subresource.Width),
                        checked((uint)subresource.Height),
                        0,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
                        pixels);
                }
            }
        }
    }

    private void UploadVolume(
        RenderTextureDescriptor texture,
        InternalFormat internalFormat)
    {
        for (int mip = 0; mip < texture.MipCount; mip++)
        {
            RenderTextureSubresourceDescriptor subresource =
                texture.RequireSubresource(mip, 0);
            RenderTexturePayloadDescriptor payload =
                RequireDecodedPayload(texture, subresource);
            fixed (byte* pixels = payload.Payload.AsSpan())
            {
                _gl.TexImage3D(
                    TextureTarget.Texture3D,
                    mip,
                    internalFormat,
                    checked((uint)subresource.Width),
                    checked((uint)subresource.Height),
                    checked((uint)subresource.Depth),
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixels);
            }
        }
    }

    private void RenderTwoDimensional(
        RenderTextureDescriptor texture,
        int width,
        int height,
        int selectedMip,
        float zoom)
    {
        RenderTextureSubresourceDescriptor subresource =
            texture.RequireSubresource(selectedMip, 0);
        _gl.UseProgram(_twoDimensionalProgram);
        _gl.Uniform1(_twoDimensionalTextureLocation, 0);
        _gl.Uniform1(_twoDimensionalMipLocation, (float)selectedMip);
        _gl.Uniform2(_twoDimensionalViewportSizeLocation, (float)width, height);
        _gl.Uniform2(
            _twoDimensionalTextureSizeLocation,
            (float)subresource.Width,
            subresource.Height);
        _gl.Uniform1(_twoDimensionalZoomLocation, zoom);
    }

    private void RenderCube(
        int width,
        int height,
        int selectedMip,
        float yaw,
        float pitch,
        float zoom)
    {
        _gl.UseProgram(_cubeProgram);
        _gl.Uniform1(_cubeTextureLocation, 0);
        _gl.Uniform1(_cubeMipLocation, (float)selectedMip);
        _gl.Uniform2(_cubeViewportSizeLocation, (float)width, height);
        _gl.Uniform1(_cubeYawLocation, yaw);
        _gl.Uniform1(_cubePitchLocation, pitch);
        _gl.Uniform1(_cubeZoomLocation, zoom);
    }

    private void RenderVolume(
        RenderTextureDescriptor texture,
        int width,
        int height,
        int selectedMip,
        float yaw,
        float pitch,
        float zoom)
    {
        RenderTextureSubresourceDescriptor subresource =
            texture.RequireSubresource(selectedMip, 0);
        float maximumExtent = Math.Max(
            Math.Max(subresource.Width, subresource.Height),
            subresource.Depth);
        int stepCount = Math.Clamp(
            (int)MathF.Ceiling(maximumExtent),
            24,
            MaximumVolumeRaySteps);

        _gl.UseProgram(_volumeProgram);
        _gl.Uniform1(_volumeTextureLocation, 0);
        _gl.Uniform1(_volumeMipLocation, (float)selectedMip);
        _gl.Uniform2(_volumeViewportSizeLocation, (float)width, height);
        _gl.Uniform3(
            _volumeHalfExtentLocation,
            0.5f * subresource.Width / maximumExtent,
            0.5f * subresource.Height / maximumExtent,
            0.5f * subresource.Depth / maximumExtent);
        _gl.Uniform1(_volumeStepCountLocation, stepCount);
        _gl.Uniform1(_volumeYawLocation, yaw);
        _gl.Uniform1(_volumePitchLocation, pitch);
        _gl.Uniform1(_volumeZoomLocation, zoom);
    }

    private void EstablishFrameState(int framebuffer, int width, int height)
    {
        _gl.BindFramebuffer(
            FramebufferTarget.Framebuffer,
            checked((uint)framebuffer));
        _gl.DrawBuffer(framebuffer == 0
            ? DrawBufferMode.Back
            : DrawBufferMode.ColorAttachment0);
        _gl.Viewport(0, 0, checked((uint)width), checked((uint)height));
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Disable(EnableCap.FramebufferSrgb);
        _gl.Enable(EnableCap.TextureCubeMapSeamless);
        _gl.DepthMask(false);
        _gl.ColorMask(true, true, true, true);
        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        _gl.ClearColor(0.52f, 0.52f, 0.52f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    private void ApplySamplerParameters(TextureTarget target, int mipCount)
    {
        _gl.TexParameter(
            target,
            TextureParameterName.TextureMinFilter,
            (int)(mipCount > 1
                ? TextureMinFilter.LinearMipmapLinear
                : TextureMinFilter.Linear));
        _gl.TexParameter(
            target,
            TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        _gl.TexParameter(
            target,
            TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(
            target,
            TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.ClampToEdge);
        if (target is TextureTarget.TextureCubeMap or TextureTarget.Texture3D)
        {
            _gl.TexParameter(
                target,
                TextureParameterName.TextureWrapR,
                (int)TextureWrapMode.ClampToEdge);
        }
        _gl.TexParameter(
            target,
            TextureParameterName.TextureBaseLevel,
            0);
        _gl.TexParameter(
            target,
            TextureParameterName.TextureMaxLevel,
            checked(mipCount - 1));
    }

    private static void ValidatePreviewTopology(RenderTextureDescriptor texture)
    {
        switch (texture.Dimension)
        {
            case RenderTextureDimension.Texture2D
                when texture.ArrayLayerCount != 1:
                throw new NotSupportedException(
                    "The material texture preview does not support 2D texture arrays.");
            case RenderTextureDimension.TextureCube
                when texture.LayerCount != 1:
                throw new NotSupportedException(
                    "The material texture preview does not support cube-map arrays.");
            case RenderTextureDimension.Texture2D:
            case RenderTextureDimension.TextureCube:
            case RenderTextureDimension.Texture3D:
                return;
            default:
                throw new NotSupportedException(
                    $"Texture dimension '{texture.Dimension}' cannot be previewed.");
        }
    }

    private static void ValidateDecodedPayloads(
        RenderTextureDescriptor texture)
    {
        foreach (RenderTextureSubresourceDescriptor subresource in
                 texture.Subresources)
        {
            RenderTexturePayloadDescriptor payload =
                RequireDecodedPayload(texture, subresource);
            long expectedByteCount = checked(
                (long)subresource.Width *
                subresource.Height *
                subresource.Depth * 4L);
            if (payload.TotalPayloadBytes != expectedByteCount)
            {
                throw new InvalidOperationException(
                    $"Texture '{texture.Name}' mip {subresource.MipLevel}, " +
                    $"layer {subresource.ArrayLayer} has " +
                    $"{payload.TotalPayloadBytes} decoded RGBA8 bytes; " +
                    $"expected {expectedByteCount}.");
            }
        }
    }

    private static RenderTexturePayloadDescriptor RequireDecodedPayload(
        RenderTextureDescriptor texture,
        RenderTextureSubresourceDescriptor subresource)
    {
        foreach (RenderTexturePayloadDescriptor payload in subresource.Payloads)
        {
            if (payload.Kind == RenderTexturePayloadKind.DecodedRgba8)
                return payload;
        }
        throw new InvalidOperationException(
            $"Texture '{texture.Name}' mip {subresource.MipLevel}, layer " +
            $"{subresource.ArrayLayer} has no decoded RGBA8 payload.");
    }

    private static TextureTarget ToOpenGlTarget(
        RenderTextureDimension dimension) => dimension switch
    {
        RenderTextureDimension.Texture2D => TextureTarget.Texture2D,
        RenderTextureDimension.TextureCube => TextureTarget.TextureCubeMap,
        RenderTextureDimension.Texture3D => TextureTarget.Texture3D,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };

    private uint CompilePreviewProgram(
        string previewShape,
        string fragmentSource)
    {
        OpenGlProgramKey key = OpenGlProgramKey.Create(
            FullscreenVertexShaderSource,
            fragmentSource,
            ProgramLinkProfileIdentity);
        try
        {
            return _programCompiler.Compile(
                key,
                FullscreenVertexShaderSource,
                fragmentSource).Handle;
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                $"Material texture preview {previewShape} OpenGL program " +
                $"compilation failed: {error.Message}",
                error);
        }
    }

    private int RequireUniform(uint program, string name)
    {
        int location = _gl.GetUniformLocation(program, name);
        if (location >= 0)
            return location;
        throw new InvalidOperationException(
            $"Material texture preview program omitted required uniform '{name}'.");
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RequireOwnerThread();
    }

    private void RequireOwnerThread()
    {
        if (Environment.CurrentManagedThreadId == _ownerThreadId)
            return;
        throw new InvalidOperationException(
            "The material texture preview renderer may only be used and disposed on its creating OpenGL thread.");
    }

    private const string FullscreenVertexShaderSource = """
        #version 330 core
        out vec2 vUv;
        const vec2 positions[3] = vec2[](
            vec2(-1.0, -1.0),
            vec2( 3.0, -1.0),
            vec2(-1.0,  3.0));
        void main()
        {
            vec2 position = positions[gl_VertexID];
            vUv = position * 0.5 + 0.5;
            gl_Position = vec4(position, 0.0, 1.0);
        }
        """;

    private const string TwoDimensionalFragmentShaderSource = """
        #version 330 core
        in vec2 vUv;
        layout (location = 0) out vec4 FragColor;
        uniform sampler2D uTexture;
        uniform float uMip;
        uniform vec2 uViewportSize;
        uniform vec2 uTextureSize;
        uniform float uZoom;

        vec3 checkerboard()
        {
            vec2 cell = floor(gl_FragCoord.xy / 16.0);
            float alternate = mod(cell.x + cell.y, 2.0);
            return mix(vec3(0.40), vec3(0.56), alternate);
        }

        void main()
        {
            vec3 background = checkerboard();
            float fit = min(
                uViewportSize.x / uTextureSize.x,
                uViewportSize.y / uTextureSize.y);
            vec2 displaySize = max(uTextureSize * fit * uZoom, vec2(1.0));
            vec2 pixelOffset = (vUv - vec2(0.5)) * uViewportSize;
            vec2 textureCoordinates =
                vec2(pixelOffset.x, -pixelOffset.y) / displaySize + vec2(0.5);
            bvec2 below = lessThan(textureCoordinates, vec2(0.0));
            bvec2 above = greaterThan(textureCoordinates, vec2(1.0));
            if (any(below) || any(above))
            {
                FragColor = vec4(background, 1.0);
                return;
            }

            vec4 sampleColor = textureLod(uTexture, textureCoordinates, uMip);
            FragColor = vec4(
                mix(background, sampleColor.rgb, sampleColor.a),
                1.0);
        }
        """;

    private const string CubeFragmentShaderSource = """
        #version 330 core
        in vec2 vUv;
        layout (location = 0) out vec4 FragColor;
        uniform samplerCube uTexture;
        uniform float uMip;
        uniform vec2 uViewportSize;
        uniform float uYaw;
        uniform float uPitch;
        uniform float uZoom;

        vec3 rotateX(vec3 value, float angle)
        {
            float sine = sin(angle);
            float cosine = cos(angle);
            return vec3(
                value.x,
                cosine * value.y - sine * value.z,
                sine * value.y + cosine * value.z);
        }

        vec3 rotateY(vec3 value, float angle)
        {
            float sine = sin(angle);
            float cosine = cos(angle);
            return vec3(
                cosine * value.x + sine * value.z,
                value.y,
                -sine * value.x + cosine * value.z);
        }

        void main()
        {
            vec2 screen = vUv * 2.0 - vec2(1.0);
            float aspect = uViewportSize.x / uViewportSize.y;
            vec3 ray = normalize(vec3(
                screen.x * aspect,
                -screen.y,
                1.35 * uZoom));
            ray = rotateY(rotateX(ray, uPitch), uYaw);
            vec4 sampleColor = textureLod(uTexture, ray, uMip);
            vec2 cell = floor(gl_FragCoord.xy / 16.0);
            float alternate = mod(cell.x + cell.y, 2.0);
            vec3 background = mix(vec3(0.40), vec3(0.56), alternate);
            FragColor = vec4(
                mix(background, sampleColor.rgb, sampleColor.a),
                1.0);
        }
        """;

    private const string VolumeFragmentShaderSource = """
        #version 330 core
        in vec2 vUv;
        layout (location = 0) out vec4 FragColor;
        uniform sampler3D uTexture;
        uniform float uMip;
        uniform vec2 uViewportSize;
        uniform vec3 uHalfExtent;
        uniform int uStepCount;
        uniform float uYaw;
        uniform float uPitch;
        uniform float uZoom;

        vec3 checkerboard()
        {
            vec2 cell = floor(gl_FragCoord.xy / 16.0);
            float alternate = mod(cell.x + cell.y, 2.0);
            return mix(vec3(0.40), vec3(0.56), alternate);
        }

        vec3 rotateX(vec3 value, float angle)
        {
            float sine = sin(angle);
            float cosine = cos(angle);
            return vec3(
                value.x,
                cosine * value.y - sine * value.z,
                sine * value.y + cosine * value.z);
        }

        vec3 rotateY(vec3 value, float angle)
        {
            float sine = sin(angle);
            float cosine = cos(angle);
            return vec3(
                cosine * value.x + sine * value.z,
                value.y,
                -sine * value.x + cosine * value.z);
        }

        float inverseComponent(float value)
        {
            if (abs(value) >= 0.000001)
                return 1.0 / value;
            return value < 0.0 ? -1000000.0 : 1000000.0;
        }

        void main()
        {
            vec3 background = checkerboard();
            vec2 screen = vUv * 2.0 - vec2(1.0);
            float aspect = uViewportSize.x / uViewportSize.y;
            float cameraDistance = 0.65 + 1.6 / uZoom;
            vec3 rayOrigin = rotateY(
                rotateX(vec3(0.0, 0.0, -cameraDistance), uPitch),
                uYaw);
            vec3 rayDirection = normalize(vec3(
                screen.x * aspect,
                -screen.y,
                1.6));
            rayDirection = rotateY(
                rotateX(rayDirection, uPitch),
                uYaw);

            vec3 inverseRay = vec3(
                inverseComponent(rayDirection.x),
                inverseComponent(rayDirection.y),
                inverseComponent(rayDirection.z));
            vec3 firstPlane = (-uHalfExtent - rayOrigin) * inverseRay;
            vec3 secondPlane = (uHalfExtent - rayOrigin) * inverseRay;
            vec3 nearPlane = min(firstPlane, secondPlane);
            vec3 farPlane = max(firstPlane, secondPlane);
            float entry = max(max(nearPlane.x, nearPlane.y), nearPlane.z);
            float exit = min(min(farPlane.x, farPlane.y), farPlane.z);
            entry = max(entry, 0.0);
            if (exit <= entry)
            {
                FragColor = vec4(background, 1.0);
                return;
            }

            float interval = (exit - entry) / float(uStepCount);
            vec4 accumulated = vec4(0.0);
            for (int step = 0; step < 256; step++)
            {
                if (step >= uStepCount || accumulated.a >= 0.995)
                    break;
                float distance = entry + (float(step) + 0.5) * interval;
                vec3 position = rayOrigin + rayDirection * distance;
                vec3 coordinates = position / (uHalfExtent * 2.0) + vec3(0.5);
                vec4 sampleColor = textureLod(uTexture, coordinates, uMip);
                float opacity = 1.0 - exp(
                    -clamp(sampleColor.a, 0.0, 1.0) *
                    6.0 / float(uStepCount));
                accumulated.rgb +=
                    (1.0 - accumulated.a) * sampleColor.rgb * opacity;
                accumulated.a += (1.0 - accumulated.a) * opacity;
            }
            FragColor = vec4(
                accumulated.rgb + background * (1.0 - accumulated.a),
                1.0);
        }
        """;
}
