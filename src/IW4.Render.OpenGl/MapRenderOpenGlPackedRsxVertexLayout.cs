using System.Numerics;

using IW4.Render.Execution;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl;

/// <summary>
/// Compacts the backend-neutral 16-vec4 RSX input slab for OpenGL upload
/// without changing the shader-visible attribute locations. Disabled native
/// routes are already materialized as exact default vectors in the slab, so
/// a selected default is copied just like any authored value.
/// </summary>
internal readonly record struct MapRenderOpenGlPackedRsxVertexLayout
{
    internal const int SourceAttributeCount = 16;
    internal const int AttributeFloatCount = 4;
    internal const int SourceFloatStride =
        SourceAttributeCount * AttributeFloatCount;
    private const int ValidAttributeMask =
        (1 << SourceAttributeCount) - 1;

    internal MapRenderOpenGlPackedRsxVertexLayout(int attributeMask)
    {
        if ((attributeMask & ~ValidAttributeMask) != 0 ||
            attributeMask == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attributeMask),
                attributeMask,
                "A packed RSX layout requires at least one attribute in locations 0 through 15.");
        }

        AttributeMask = attributeMask;
    }

    internal int AttributeMask { get; }

    internal int AttributeCount =>
        BitOperations.PopCount(checked((uint)AttributeMask));

    internal int FloatStride =>
        checked(AttributeCount * AttributeFloatCount);

    internal static int ResolveAttributeMask(
        MapRenderShaderExecutionContract execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        int mask = 0;
        foreach (MapRenderShaderVertexInputBinding input in
                 execution.VertexInputs)
        {
            if (input.Destination >= SourceAttributeCount)
            {
                throw new InvalidOperationException(
                    $"Translated RSX vertex input destination {input.Destination} is outside the OpenGL attribute domain.");
            }

            mask |= 1 << input.Destination;
        }

        return mask;
    }

    internal bool ContainsAttribute(int attribute)
    {
        if ((uint)attribute >= SourceAttributeCount)
            return false;

        return (AttributeMask & (1 << attribute)) != 0;
    }

    internal int PackedFloatCount(int sourceFloatCount)
    {
        int vertexCount = ResolveSourceVertexCount(sourceFloatCount);
        return checked(vertexCount * FloatStride);
    }

    internal long PackedByteCount(int sourceFloatCount) =>
        checked((long)PackedFloatCount(sourceFloatCount) * sizeof(float));

    internal void Pack(
        ReadOnlySpan<float> source,
        Span<float> destination)
    {
        int vertexCount = ResolveSourceVertexCount(source.Length);
        int expectedDestinationCount =
            checked(vertexCount * FloatStride);
        if (destination.Length != expectedDestinationCount)
        {
            throw new ArgumentException(
                $"Packed RSX destination requires exactly {expectedDestinationCount} floats.",
                nameof(destination));
        }

        if (AttributeMask == ValidAttributeMask)
        {
            source.CopyTo(destination);
            return;
        }

        int destinationOffset = 0;
        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            int sourceVertexOffset = checked(
                vertex * SourceFloatStride);
            for (int attribute = 0;
                 attribute < SourceAttributeCount;
                 attribute++)
            {
                if (!ContainsAttribute(attribute))
                    continue;

                source.Slice(
                        sourceVertexOffset +
                        attribute * AttributeFloatCount,
                        AttributeFloatCount)
                    .CopyTo(destination.Slice(
                        destinationOffset,
                        AttributeFloatCount));
                destinationOffset += AttributeFloatCount;
            }
        }
    }

    private static int ResolveSourceVertexCount(int sourceFloatCount)
    {
        if (sourceFloatCount < 0 ||
            sourceFloatCount % SourceFloatStride != 0)
        {
            throw new ArgumentException(
                $"Translated RSX input payloads require a {SourceFloatStride}-float source stride.",
                nameof(sourceFloatCount));
        }

        return sourceFloatCount / SourceFloatStride;
    }
}
