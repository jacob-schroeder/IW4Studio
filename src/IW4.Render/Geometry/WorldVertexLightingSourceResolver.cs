namespace IW4.Render.Geometry;

/// <summary>
/// Optional lighting channels carried by an Event20 world-vertex backend row.
/// These are kept separate from the material-selected base-UV and color/control
/// inputs so missing lighting data cannot silently alias another attribute.
/// </summary>
internal readonly record struct WorldVertexLightingSources(
    VertexSource? LightmapTexCoord,
    VertexSource? Normal);

internal static class WorldVertexLightingSourceResolver
{
    private const byte LightmapTexCoordSource = 0x02;
    private const byte NormalSource = 0x03;

    internal static WorldVertexLightingSources Resolve(
        WorldVertexLayoutSelection layout) =>
        layout.IsResolved
            ? Resolve(layout.BackendRow)
            : default;

    internal static WorldVertexLightingSources Resolve(int backendRow)
    {
        if (!WorldVertexLayout.TryGetEvent20StreamStrides(
                backendRow,
                out _,
                out _))
        {
            return default;
        }

        VertexSource? lightmapTexCoord = ResolveSource(
            backendRow,
            LightmapTexCoordSource,
            requiredComponentCount: 4,
            requiredRsxType: 0x02,
            componentA: 2,
            componentB: 3);
        VertexSource? normal = ResolveSource(
            backendRow,
            NormalSource,
            requiredComponentCount: 1,
            requiredRsxType: 0x06,
            componentA: 0,
            componentB: 0);
        return new WorldVertexLightingSources(lightmapTexCoord, normal);
    }

    private static VertexSource? ResolveSource(
        int backendRow,
        byte sourceIndex,
        byte requiredComponentCount,
        byte requiredRsxType,
        int componentA,
        int componentB)
    {
        if (!WorldVertexLayout.TryGetSource(
                backendRow,
                sourceIndex,
                out WorldVertexSource source) ||
            source.ComponentCount != requiredComponentCount ||
            source.RsxType != requiredRsxType ||
            !WorldVertexLayout.TryGetStreamStride(
                backendRow,
                source.StreamIndex,
                out byte stride))
        {
            return null;
        }

        return new VertexSource(
            StreamIndex: source.StreamIndex,
            Stride: stride,
            Offset: source.ByteOffset,
            FormatByte0: source.ComponentCount,
            FormatByte1: source.RsxType,
            ComponentA: componentA,
            ComponentB: componentB);
    }
}
