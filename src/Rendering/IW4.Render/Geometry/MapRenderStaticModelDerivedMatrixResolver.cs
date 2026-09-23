using System.Numerics;

using IW4.Render.Execution;

namespace IW4.Render.Geometry;

internal static class MapRenderStaticModelDerivedMatrixResolver
{
    internal static DerivedMatrixState WithPlacement(
        DerivedMatrixState frame,
        MapRenderStaticModelInstance instance)
    {
        ValidateFinite(instance.TransformRow0, nameof(instance));
        ValidateFinite(instance.TransformRow1, nameof(instance));
        ValidateFinite(instance.TransformRow2, nameof(instance));
        Vector3 axis0 = new(instance.TransformRow0.X, -instance.TransformRow2.X, instance.TransformRow1.X);
        Vector3 axis1 = new(instance.TransformRow0.Y, -instance.TransformRow2.Y, instance.TransformRow1.Y);
        Vector3 axis2 = new(instance.TransformRow0.Z, -instance.TransformRow2.Z, instance.TransformRow1.Z);
        Vector3 origin = new(instance.TransformRow0.W, -instance.TransformRow2.W, instance.TransformRow1.W);
        Vector3 eyeRelativeOrigin = origin - frame.EyeOffset;
        Matrix4x4 world0 = new(axis0.X, axis0.Y, axis0.Z, 0f, axis1.X, axis1.Y, axis1.Z, 0f, axis2.X, axis2.Y, axis2.Z, 0f, eyeRelativeOrigin.X, eyeRelativeOrigin.Y, eyeRelativeOrigin.Z, 1f);
        Matrix4x4 worldView0 = world0 * frame.View;
        Matrix4x4 worldViewProjection0 = DerivedMatrixResolver.MultiplyWorldViewProjection0(world0, frame.ViewProjection);
        return frame with { World0 = world0, WorldView0 = worldView0, WorldViewProjection0 = worldViewProjection0 };
    }

    private static void ValidateFinite(Vector4 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) || !float.IsFinite(value.W))
        {
            throw new ArgumentException("Matrix source coordinates must be finite.", parameterName);
        }
    }
}
