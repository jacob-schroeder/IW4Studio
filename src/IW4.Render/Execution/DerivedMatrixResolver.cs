using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using IW4.Render.Shaders;
using IW4.Render.Transforms;

namespace IW4.Render.Execution;

internal static class DerivedMatrixResolver
{
    public const float Ps3ClipScale = RenderViewerMatrixMath.ClipScale;

    /// <summary>
    /// Creates the backend-neutral derived state from exact PS3-native camera
    /// matrices. Clip-space or framebuffer-origin conversion belongs to the
    /// backend and must be applied before calling this boundary.
    /// </summary>
    public static DerivedMatrixState CreateFromPs3NativeCamera(
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector3 eyeOffset) => Create(view, projection, eyeOffset);

    public static Matrix4x4 CreatePs3InfiniteProjection(
        float tanHalfFovX,
        float tanHalfFovY,
        float zNear)
    {
        return RenderViewerMatrixMath.CreateInfiniteProjection(
            tanHalfFovX,
            tanHalfFovY,
            zNear);
    }

    public static bool TryResolve(
        DerivedMatrixState state,
        CodeMatrixSemantic semantic,
        out Matrix4x4 matrix)
    {
        if (semantic == CodeMatrixSemantic.ShadowLookup)
        {
            if (state.ShadowLookup is { } shadowLookup)
            {
                matrix = shadowLookup;
                return true;
            }

            matrix = default;
            return false;
        }

        matrix = semantic switch
        {
            CodeMatrixSemantic.View => state.View,
            CodeMatrixSemantic.Projection => state.Projection,
            CodeMatrixSemantic.ViewProjection => state.ViewProjection,
            CodeMatrixSemantic.World0 => state.World0,
            CodeMatrixSemantic.WorldView0 => state.WorldView0,
            CodeMatrixSemantic.WorldViewProjection0 => state.WorldViewProjection0,
            _ => default
        };
        return Supports(semantic);
    }

    public static bool Supports(CodeMatrixSemantic semantic) =>
        semantic is CodeMatrixSemantic.View or
            CodeMatrixSemantic.Projection or
            CodeMatrixSemantic.ViewProjection or
            CodeMatrixSemantic.ShadowLookup or
            CodeMatrixSemantic.World0 or
            CodeMatrixSemantic.WorldView0 or
            CodeMatrixSemantic.WorldViewProjection0;

    public static bool TryResolveRow(
        DerivedMatrixState state,
        CodeMatrixSemantic semantic,
        CodeMatrixTransform transform,
        int rowIndex,
        out Vector4 row)
    {
        if (!TryResolve(state, semantic, out Matrix4x4 matrix))
        {
            row = default;
            return false;
        }
        return TryResolveExactRow(matrix, transform, rowIndex, out row);
    }

    /// <summary>
    /// Applies the PS3 matrix transform selector to an operational matrix
    /// value. Callers remain responsible for selecting its semantic source.
    /// </summary>
    public static bool TryResolveExactRow(
        Matrix4x4 matrix,
        CodeMatrixTransform transform,
        int rowIndex,
        out Vector4 row)
    {
        if (!Enum.IsDefined(transform))
        {
            row = default;
            return false;
        }
        if (transform is CodeMatrixTransform.Inverse or
            CodeMatrixTransform.InverseTranspose)
        {
            if (!Matrix4x4.Invert(matrix, out matrix))
            {
                row = default;
                return false;
            }
        }
        if (transform is CodeMatrixTransform.Transpose or
            CodeMatrixTransform.InverseTranspose)
        {
            matrix = Matrix4x4.Transpose(matrix);
        }

        row = rowIndex switch
        {
            0 => new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14),
            1 => new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24),
            2 => new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34),
            3 => new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44),
            _ => default
        };
        return rowIndex is >= 0 and <= 3;
    }

    /// <summary>
    /// Multiplies the row-major matrices with World0 first and ViewProjection
    /// second.
    /// </summary>
    public static Matrix4x4 MultiplyWorldViewProjection0(
        Matrix4x4 world0,
        Matrix4x4 viewProjection) => world0 * viewProjection;

    /// <summary>
    /// Starts World0 as identity and subtracts source-state EyeOffset
    /// (+0x1490) from its translation row.
    /// </summary>
    public static Matrix4x4 CreateWorld0(Vector3 eyeOffset)
    {
        if (!float.IsFinite(eyeOffset.X) ||
            !float.IsFinite(eyeOffset.Y) ||
            !float.IsFinite(eyeOffset.Z))
        {
            throw new ArgumentException(
                "EyeOffset values must be finite.",
                nameof(eyeOffset));
        }

        return Matrix4x4.CreateTranslation(-eyeOffset);
    }

    /// <summary>
    /// Copies the matrix from context +0x14A0, then replaces row three with
    /// <c>(EyeOffset.xyz, 1)</c> times that matrix. Accepting EyeOffset rather
    /// than an arbitrary Vector4 preserves the native homogeneous one at this
    /// API boundary.
    /// </summary>
    public static Matrix4x4 CreateShadowLookup(
        Matrix4x4 source,
        Vector3 eyeOffset)
    {
        ValidateFinite(source, nameof(source));
        ValidateFinite(eyeOffset, nameof(eyeOffset));

        Vector4 finalRow = Vector4.Transform(
            new Vector4(eyeOffset, 1.0f),
            source);
        source.M41 = finalRow.X;
        source.M42 = finalRow.Y;
        source.M43 = finalRow.Z;
        source.M44 = finalRow.W;
        return source;
    }

    /// <summary>
    /// Publishes one projection-owned shadow lookup into an existing derived
    /// matrix state. The state's normal-camera EyeOffset is used for the exact
    /// PS3 row-three replacement, so callers cannot accidentally combine the
    /// lookup with a different camera revision.
    /// </summary>
    public static DerivedMatrixState WithShadowLookupSource(
        DerivedMatrixState state,
        Matrix4x4 source) =>
        state with
        {
            ShadowLookup = CreateShadowLookup(source, state.EyeOffset)
        };

    private static DerivedMatrixState Create(
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector3 viewOrigin)
    {
        Matrix4x4 world0 = CreateWorld0(viewOrigin);
        Matrix4x4 viewProjection =
            RenderViewerMatrixMath.CreateViewProjection(
                view,
                projection);
        Matrix4x4 worldView0 = world0 * view;
        Matrix4x4 worldViewProjection0 = MultiplyWorldViewProjection0(
            world0,
            viewProjection);
        return new DerivedMatrixState(
            view,
            projection,
            viewProjection,
            world0,
            worldView0,
            worldViewProjection0,
            viewOrigin);
    }

    private static void ValidateFinite(
        Vector3 value,
        string parameterName)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z))
        {
            throw new ArgumentException(
                "Matrix source coordinates must be finite.",
                parameterName);
        }
    }

    private static void ValidateFinite(
        Vector4 value,
        string parameterName)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) ||
            !float.IsFinite(value.W))
        {
            throw new ArgumentException(
                "Matrix source coordinates must be finite.",
                parameterName);
        }
    }

    private static void ValidateFinite(
        Matrix4x4 value,
        string parameterName)
    {
        if (!RenderMatrixValidation.IsFinite(value))
        {
            throw new ArgumentException(
                "Matrix source values must be finite.",
                parameterName);
        }
    }
}
