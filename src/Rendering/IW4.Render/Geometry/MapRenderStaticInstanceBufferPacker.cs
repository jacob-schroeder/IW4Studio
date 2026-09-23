using System.Numerics;

namespace IW4.Render.Geometry;

/// <summary>
/// Identifies the semantic value carried by static-instance attribute 12.
/// Row 0x39 and row 0x3A share one physical lane but are not interchangeable.
/// </summary>
internal enum MapRenderStaticInstanceLightingPayload
{
    None,
    BaseLightingCoords,
    LightProbeAmbient
}

/// <summary>
/// One canonical CPU packing path for initial and dynamically compacted
/// static-instance buffers. When present, the selected per-instance lighting
/// row is first, followed by the three viewer-placement rows.
/// </summary>
internal static class MapRenderStaticInstanceBufferPacker
{
    internal const int PlacementOnlyFloatStride = 12;
    internal const int LightingAwareFloatStride = 16;

    internal static int FloatStride(
        MapRenderStaticInstanceLightingPayload lightingPayload) =>
        lightingPayload != MapRenderStaticInstanceLightingPayload.None
            ? LightingAwareFloatStride
            : PlacementOnlyFloatStride;

    internal static void PackAll(
        IReadOnlyList<MapRenderStaticModelInstance> instances,
        MapRenderStaticInstanceLightingPayload lightingPayload,
        Span<float> destination,
        Vector4[]? baseLightingCoordinatesByObject = null)
    {
        ArgumentNullException.ThrowIfNull(instances);
        int stride = FloatStride(lightingPayload);
        if (destination.Length < checked(instances.Count * stride))
            throw new ArgumentException(
                "Static instance destination is too small.",
                nameof(destination));

        for (int index = 0; index < instances.Count; index++)
        {
            WriteInstance(
                instances[index],
                lightingPayload,
                destination.Slice(index * stride, stride),
                baseLightingCoordinatesByObject);
        }
    }

    internal static void PackSelected(
        IReadOnlyList<MapRenderStaticModelInstance> instances,
        ReadOnlySpan<int> selectedSourceIndices,
        MapRenderStaticInstanceLightingPayload lightingPayload,
        Span<float> destination,
        Vector4[]? baseLightingCoordinatesByObject = null)
    {
        ArgumentNullException.ThrowIfNull(instances);
        int stride = FloatStride(lightingPayload);
        if (destination.Length <
            checked(selectedSourceIndices.Length * stride))
        {
            throw new ArgumentException(
                "Static instance destination is too small.",
                nameof(destination));
        }

        for (int selectedIndex = 0;
             selectedIndex < selectedSourceIndices.Length;
             selectedIndex++)
        {
            int sourceIndex = selectedSourceIndices[selectedIndex];
            if ((uint)sourceIndex >= (uint)instances.Count)
                throw new ArgumentOutOfRangeException(
                    nameof(selectedSourceIndices));
            WriteInstance(
                instances[sourceIndex],
                lightingPayload,
                destination.Slice(selectedIndex * stride, stride),
                baseLightingCoordinatesByObject);
        }
    }

    private static void WriteInstance(
        MapRenderStaticModelInstance instance,
        MapRenderStaticInstanceLightingPayload lightingPayload,
        Span<float> destination,
        Vector4[]? baseLightingCoordinatesByObject)
    {
        int placementOffset =
            lightingPayload == MapRenderStaticInstanceLightingPayload.None
                ? 0
                : 4;
        switch (lightingPayload)
        {
            case MapRenderStaticInstanceLightingPayload.None:
                break;
            case MapRenderStaticInstanceLightingPayload.BaseLightingCoords:
                WriteVector4(
                    destination,
                    0,
                    ResolveBaseLightingCoordinates(
                        instance,
                        baseLightingCoordinatesByObject));
                break;
            case MapRenderStaticInstanceLightingPayload.LightProbeAmbient:
                WriteVector4(destination, 0, instance.LightProbeAmbient);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(lightingPayload),
                    lightingPayload,
                    "Unknown static-instance lighting payload.");
        }
        WriteVector4(destination, placementOffset, instance.TransformRow0);
        WriteVector4(
            destination,
            placementOffset + 4,
            instance.TransformRow1);
        WriteVector4(
            destination,
            placementOffset + 8,
            instance.TransformRow2);
    }

    private static Vector4 ResolveBaseLightingCoordinates(
        MapRenderStaticModelInstance instance,
        Vector4[]? baseLightingCoordinatesByObject)
    {
        if (baseLightingCoordinatesByObject is null)
            return instance.BaseLightingCoords;
        if ((uint)instance.ObjectIndex >=
            (uint)baseLightingCoordinatesByObject.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseLightingCoordinatesByObject),
                instance.ObjectIndex,
                "A static instance has no working-set lighting-coordinate row.");
        }
        return baseLightingCoordinatesByObject[instance.ObjectIndex];
    }

    private static void WriteVector4(
        Span<float> destination,
        int offset,
        System.Numerics.Vector4 value)
    {
        destination[offset] = value.X;
        destination[offset + 1] = value.Y;
        destination[offset + 2] = value.Z;
        destination[offset + 3] = value.W;
    }
}
