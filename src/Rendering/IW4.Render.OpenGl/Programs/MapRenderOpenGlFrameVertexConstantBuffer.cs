using System.Numerics;
using IW4.Render.Execution;
using IW4.Render.Shaders;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Three dynamic std140 snapshots for the normal-camera values shared by all
/// rewritten translated map vertex programs in one frame.
/// </summary>
internal sealed unsafe class MapRenderOpenGlFrameVertexConstantBuffer
{
    private const int BufferCount = 3;
    private const int MatrixRowCount = 96;
    private const int GameTimeRow = MatrixRowCount;
    private const int ClipScaleRow = GameTimeRow + 1;
    private const int ClipOffsetRow = ClipScaleRow + 1;
    private const int ZNearRow = ClipOffsetRow + 1;
    private const int EyeOffsetRow = ZNearRow + 1;
    private const int VegetationTimeRow = EyeOffsetRow + 1;
    private const int RowCount = VegetationTimeRow + 1;
    internal const int ExpectedByteSize = RowCount * 16;

    private static readonly CodeMatrixSemantic[] MatrixSemantics =
    [
        CodeMatrixSemantic.View,
        CodeMatrixSemantic.Projection,
        CodeMatrixSemantic.ViewProjection,
        CodeMatrixSemantic.World0,
        CodeMatrixSemantic.WorldView0,
        CodeMatrixSemantic.WorldViewProjection0
    ];
    private static readonly CodeMatrixTransform[] MatrixTransforms =
    [
        CodeMatrixTransform.None,
        CodeMatrixTransform.Inverse,
        CodeMatrixTransform.Transpose,
        CodeMatrixTransform.InverseTranspose
    ];

    private readonly GL _gl;
    private readonly SilkOpenGlStateShadow _state;
    private readonly uint[] _buffers = new uint[BufferCount];
    private readonly Vector4[] _rows = new Vector4[RowCount];

    public MapRenderOpenGlFrameVertexConstantBuffer(
        GL gl,
        SilkOpenGlStateShadow state)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public string? ValidateAndBindProgram(uint program)
    {
        uint blockIndex = _gl.GetUniformBlockIndex(
            program,
            MapRenderOpenGlFrameVertexConstantComposer.BlockName);
        if (blockIndex == uint.MaxValue)
        {
            return "Translated map program lost its frame-constant uniform block.";
        }

        int dataSize = 0;
        _gl.GetActiveUniformBlock(
            program,
            blockIndex,
            UniformBlockPName.DataSize,
            &dataSize);
        if (dataSize != ExpectedByteSize)
        {
            return $"Translated map program frame-constant block is {dataSize} bytes; expected {ExpectedByteSize}.";
        }

        int referencedByVertexShader = 0;
        _gl.GetActiveUniformBlock(
            program,
            blockIndex,
            UniformBlockPName.ReferencedByVertexShader,
            &referencedByVertexShader);
        if (referencedByVertexShader != 1)
        {
            return "Translated map program frame-constant block is not referenced by the vertex shader.";
        }

        _gl.UniformBlockBinding(
            program,
            blockIndex,
            MapRenderOpenGlFrameVertexConstantComposer.BindingPoint);
        return null;
    }

    public void Upload(
        long frameIndex,
        in DerivedMatrixState matrices,
        float animationTimeSeconds,
        ShaderConstantValue clipSpaceLookupScale,
        ShaderConstantValue clipSpaceLookupOffset,
        ShaderConstantValue zNear)
    {
        if (frameIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        if (!float.IsFinite(animationTimeSeconds))
            throw new ArgumentOutOfRangeException(nameof(animationTimeSeconds));

        foreach (CodeMatrixSemantic semantic in MatrixSemantics)
        {
            if (!DerivedMatrixResolver.TryResolve(
                    matrices,
                    semantic,
                    out Matrix4x4 matrix))
            {
                throw new InvalidOperationException(
                    $"Map frame matrix {semantic} is unavailable.");
            }

            if (!Matrix4x4.Invert(matrix, out Matrix4x4 inverse))
            {
                throw new InvalidOperationException(
                    $"Map frame matrix {semantic}:Inverse is unavailable.");
            }

            Matrix4x4 transpose = Matrix4x4.Transpose(matrix);
            Matrix4x4 inverseTranspose = Matrix4x4.Transpose(inverse);
            foreach (CodeMatrixTransform transform in MatrixTransforms)
            {
                for (int row = 0; row < 4; row++)
                {
                    Matrix4x4 transformed = transform switch
                    {
                        CodeMatrixTransform.None => matrix,
                        CodeMatrixTransform.Inverse => inverse,
                        CodeMatrixTransform.Transpose => transpose,
                        CodeMatrixTransform.InverseTranspose => inverseTranspose,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(transform))
                    };

                    _rows[MapRenderOpenGlFrameVertexConstantComposer
                        .MatrixRowIndex(semantic, transform, row)] =
                        ResolveRow(transformed, row);
                }
            }
        }

        _rows[GameTimeRow] = ToVector4(
            FrameDirectCodeConstants.ProduceGameTimeValue(
                animationTimeSeconds));
        _rows[ClipScaleRow] = ToVector4(clipSpaceLookupScale);
        _rows[ClipOffsetRow] = ToVector4(clipSpaceLookupOffset);
        _rows[ZNearRow] = ToVector4(zNear);
        _rows[EyeOffsetRow] = new Vector4(matrices.EyeOffset, 0f);
        _rows[VegetationTimeRow] =
            new Vector4(animationTimeSeconds, 0f, 0f, 0f);

        EnsureBuffers();
        uint buffer = _buffers[checked((int)(frameIndex % BufferCount))];
        _gl.BindBuffer(BufferTargetARB.UniformBuffer, buffer);
        fixed (Vector4* rows = _rows)
        {
            _gl.BufferSubData(
                BufferTargetARB.UniformBuffer,
                0,
                checked((nuint)(RowCount * sizeof(Vector4))),
                rows);
        }
        _state.BindUniformBufferBase(
            MapRenderOpenGlFrameVertexConstantComposer.BindingPoint,
            buffer);
    }

    public void Dispose()
    {
        foreach (uint buffer in _buffers)
        {
            if (buffer == 0)
                continue;
            _state.ForgetUniformBufferBinding(buffer);
            _gl.DeleteBuffer(buffer);
        }
        Array.Clear(_buffers);
    }

    public void AbandonContext() => Array.Clear(_buffers);

    private void EnsureBuffers()
    {
        if (_buffers[0] != 0)
            return;

        try
        {
            for (int index = 0; index < _buffers.Length; index++)
            {
                uint buffer = _gl.GenBuffer();
                if (buffer == 0)
                    throw new InvalidOperationException(
                        "OpenGL could not allocate a map frame constant buffer.");
                _gl.BindBuffer(BufferTargetARB.UniformBuffer, buffer);
                _gl.BufferData(
                    BufferTargetARB.UniformBuffer,
                    checked((nuint)(RowCount * sizeof(Vector4))),
                    null,
                    BufferUsageARB.DynamicDraw);
                _buffers[index] = buffer;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static Vector4 ToVector4(ShaderConstantValue value) =>
        new(value.X, value.Y, value.Z, value.W);

    private static Vector4 ResolveRow(Matrix4x4 matrix, int row) => row switch
    {
        0 => new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14),
        1 => new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24),
        2 => new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34),
        3 => new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44),
        _ => throw new ArgumentOutOfRangeException(nameof(row))
    };
}
