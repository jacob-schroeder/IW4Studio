using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using IW4.Render.Geometry;
using IW4.Render.Transforms;

namespace IW4.Render.Shaders;

internal static class MapRenderDerivedMatrixResolver
{
    public const float Ps3ClipScale = MapRenderViewerMatrixMath.ClipScale;

    /// <summary>
    /// Creates the backend-neutral derived state from exact PS3-native camera
    /// matrices. Clip-space or framebuffer-origin conversion belongs to the
    /// backend and must be applied before calling this boundary.
    /// </summary>
    public static MapRenderDerivedMatrixState CreateFromPs3NativeCamera(
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector3 eyeOffset) => Create(view, projection, eyeOffset);

    public static Matrix4x4 CreatePs3InfiniteProjection(
        float tanHalfFovX,
        float tanHalfFovY,
        float zNear)
    {
        return MapRenderViewerMatrixMath.CreateInfiniteProjection(
            tanHalfFovX,
            tanHalfFovY,
            zNear);
    }

    public static bool TryResolve(
        MapRenderDerivedMatrixState state,
        MapRenderCodeMatrixSemantic semantic,
        out Matrix4x4 matrix)
    {
        if (semantic == MapRenderCodeMatrixSemantic.ShadowLookup)
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
            MapRenderCodeMatrixSemantic.View => state.View,
            MapRenderCodeMatrixSemantic.Projection => state.Projection,
            MapRenderCodeMatrixSemantic.ViewProjection => state.ViewProjection,
            MapRenderCodeMatrixSemantic.World0 => state.World0,
            MapRenderCodeMatrixSemantic.WorldView0 => state.WorldView0,
            MapRenderCodeMatrixSemantic.WorldViewProjection0 => state.WorldViewProjection0,
            _ => default
        };
        return Supports(semantic);
    }

    public static bool Supports(MapRenderCodeMatrixSemantic semantic) =>
        semantic is MapRenderCodeMatrixSemantic.View or
            MapRenderCodeMatrixSemantic.Projection or
            MapRenderCodeMatrixSemantic.ViewProjection or
            MapRenderCodeMatrixSemantic.ShadowLookup or
            MapRenderCodeMatrixSemantic.World0 or
            MapRenderCodeMatrixSemantic.WorldView0 or
            MapRenderCodeMatrixSemantic.WorldViewProjection0;

    public static bool TryResolveRow(
        MapRenderDerivedMatrixState state,
        MapRenderCodeMatrixSemantic semantic,
        MapRenderCodeMatrixTransform transform,
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
        MapRenderCodeMatrixTransform transform,
        int rowIndex,
        out Vector4 row)
    {
        if (!Enum.IsDefined(transform))
        {
            row = default;
            return false;
        }
        if (transform is MapRenderCodeMatrixTransform.Inverse or
            MapRenderCodeMatrixTransform.InverseTranspose)
        {
            if (!Matrix4x4.Invert(matrix, out matrix))
            {
                row = default;
                return false;
            }
        }
        if (transform is MapRenderCodeMatrixTransform.Transpose or
            MapRenderCodeMatrixTransform.InverseTranspose)
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
    /// Materializes the Event22 static-model world matrix for one isolated
    /// 0x2C-byte placement by expanding its packed scaled basis and
    /// subtracting the frame eye offset from the origin.
    /// </summary>
    public static MapRenderDerivedMatrixState WithStaticModelPlacement(
        MapRenderDerivedMatrixState frame,
        MapRenderStaticModelInstance instance)
    {
        ValidateFinite(instance.TransformRow0, nameof(instance));
        ValidateFinite(instance.TransformRow1, nameof(instance));
        ValidateFinite(instance.TransformRow2, nameof(instance));

        // Static instances are retained in host render coordinates for the
        // fixed-function instanced preview. Recover the native game placement
        // written by CreateStaticModelInstance before combining it with the
        // native rotation-only View and EyeOffset state used by translated RSX
        // execution.
        Vector3 axis0 = new(
            instance.TransformRow0.X,
            -instance.TransformRow2.X,
            instance.TransformRow1.X);
        Vector3 axis1 = new(
            instance.TransformRow0.Y,
            -instance.TransformRow2.Y,
            instance.TransformRow1.Y);
        Vector3 axis2 = new(
            instance.TransformRow0.Z,
            -instance.TransformRow2.Z,
            instance.TransformRow1.Z);
        Vector3 origin = new(
            instance.TransformRow0.W,
            -instance.TransformRow2.W,
            instance.TransformRow1.W);
        Vector3 eyeRelativeOrigin = origin - frame.EyeOffset;
        Matrix4x4 world0 = new(
            axis0.X, axis0.Y, axis0.Z, 0f,
            axis1.X, axis1.Y, axis1.Z, 0f,
            axis2.X, axis2.Y, axis2.Z, 0f,
            eyeRelativeOrigin.X,
            eyeRelativeOrigin.Y,
            eyeRelativeOrigin.Z,
            1f);
        Matrix4x4 worldView0 = world0 * frame.View;
        Matrix4x4 worldViewProjection0 =
            MultiplyWorldViewProjection0(world0, frame.ViewProjection);
        return frame with
        {
            World0 = world0,
            WorldView0 = worldView0,
            WorldViewProjection0 = worldViewProjection0
        };
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
    public static MapRenderDerivedMatrixState WithShadowLookupSource(
        MapRenderDerivedMatrixState state,
        Matrix4x4 source) =>
        state with
        {
            ShadowLookup = CreateShadowLookup(source, state.EyeOffset)
        };

    private static MapRenderDerivedMatrixState Create(
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector3 viewOrigin)
    {
        Matrix4x4 world0 = CreateWorld0(viewOrigin);
        Matrix4x4 viewProjection =
            MapRenderViewerMatrixMath.CreateViewProjection(
                view,
                projection);
        Matrix4x4 worldView0 = world0 * view;
        Matrix4x4 worldViewProjection0 = MultiplyWorldViewProjection0(
            world0,
            viewProjection);
        return new MapRenderDerivedMatrixState(
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
        if (!float.IsFinite(value.M11) ||
            !float.IsFinite(value.M12) ||
            !float.IsFinite(value.M13) ||
            !float.IsFinite(value.M14) ||
            !float.IsFinite(value.M21) ||
            !float.IsFinite(value.M22) ||
            !float.IsFinite(value.M23) ||
            !float.IsFinite(value.M24) ||
            !float.IsFinite(value.M31) ||
            !float.IsFinite(value.M32) ||
            !float.IsFinite(value.M33) ||
            !float.IsFinite(value.M34) ||
            !float.IsFinite(value.M41) ||
            !float.IsFinite(value.M42) ||
            !float.IsFinite(value.M43) ||
            !float.IsFinite(value.M44))
        {
            throw new ArgumentException(
                "Matrix source values must be finite.",
                parameterName);
        }
    }
}
