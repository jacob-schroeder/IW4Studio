using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Render.Geometry;
using IW4.Render.Shaders;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder
{
    private static PreparedWorldSurfaceGeometry[] PrepareWorldSurfaceGeometries(
        GfxWorldAsset gfxMap,
        byte[] vertexBytes,
        IReadOnlyList<ushort> sourceIndices)
    {
        ArgumentNullException.ThrowIfNull(gfxMap);
        ArgumentNullException.ThrowIfNull(vertexBytes);
        ArgumentNullException.ThrowIfNull(sourceIndices);

        int surfaceCount = gfxMap.Dpvs.Surfaces.Count;
        var prepared = new PreparedWorldSurfaceGeometry[surfaceCount];
        var failures = new Exception?[surfaceCount];
        Parallel.For(
            0,
            surfaceCount,
            new ParallelOptions
            {
                // Position/topology preparation allocates one retained result
                // per surface. Bound concurrent allocation just as texture
                // decoding is bounded, rather than scaling it to every core.
                MaxDegreeOfParallelism = Math.Min(
                    Environment.ProcessorCount,
                    4)
            },
            surfaceIndex =>
            {
                try
                {
                    prepared[surfaceIndex] =
                        PreparedWorldSurfaceGeometryFactory.Create(
                            surfaceIndex,
                            gfxMap.Dpvs.Surfaces[surfaceIndex],
                            vertexBytes,
                            sourceIndices);
                }
                catch (Exception exception)
                    when (exception is not OutOfMemoryException)
                {
                    failures[surfaceIndex] = exception;
                }
            });

        // Parallel preparation writes directly to surface-indexed slots. Scan in
        // ascending order so both failure selection and every later merge remain
        // deterministic regardless of worker scheduling.
        for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
        {
            if (failures[surfaceIndex] is Exception failure)
            {
                throw new InvalidOperationException(
                    $"World surface {surfaceIndex} geometry preparation failed.",
                    failure);
            }
            if (prepared[surfaceIndex] is null)
            {
                throw new InvalidOperationException(
                    $"World surface {surfaceIndex} geometry preparation produced no result.");
            }
        }

        return prepared;
    }

    internal static int AddSolidSurface(
        GfxSurface surface,
        ReadOnlySpan<byte> vertexBytes,
        IReadOnlyList<ushort> sourceIndices,
        List<float> vertices,
        List<uint> indices,
        Vector3 color,
        bool includeInBounds,
        ref RenderBounds bounds,
        out int skippedTriangles,
        out int readFailureTriangles,
        out int skyboxTriangles)
    {
        PreparedWorldSurfaceGeometry prepared =
            PreparedWorldSurfaceGeometryFactory.Create(
                -1,
                surface,
                vertexBytes,
                sourceIndices);
        return AddSolidSurface(
            prepared,
            vertices,
            indices,
            color,
            includeInBounds,
            ref bounds,
            out skippedTriangles,
            out readFailureTriangles,
            out skyboxTriangles);
    }

    private static int AddSolidSurface(
        PreparedWorldSurfaceGeometry prepared,
        List<float> vertices,
        List<uint> indices,
        Vector3 color,
        bool includeInBounds,
        ref RenderBounds bounds,
        out int skippedTriangles,
        out int readFailureTriangles,
        out int skyboxTriangles)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        skippedTriangles = prepared.SolidSkippedTriangleCount;
        readFailureTriangles = prepared.SolidReadFailureTriangleCount;
        skyboxTriangles = prepared.SkyboxTriangleCount;
        if (prepared.SourceVertexCount <= 0)
            return 0;

        var destinationVertexIndices = new int[prepared.SourceVertexCount];
        Array.Fill(destinationVertexIndices, -1);

        foreach (PreparedWorldSurfaceTriangle triangle in prepared.Triangles)
        {
            Vector3 p0 = prepared.GetPosition(triangle.VertexSlot0);
            Vector3 p1 = prepared.GetPosition(triangle.VertexSlot1);
            Vector3 p2 = prepared.GetPosition(triangle.VertexSlot2);

            // Edge length is useful for classifying sky-scale geometry, but it
            // does not mean that an authored triangle is invalid. Keep the
            // triangle in the diagnostic geometry buffer; explicit sky
            // materials are excluded from camera framing by includeInBounds.

            indices.Add(GetOrAddSolidSurfaceVertex(triangle.VertexSlot0, p0));
            indices.Add(GetOrAddSolidSurfaceVertex(triangle.VertexSlot1, p1));
            indices.Add(GetOrAddSolidSurfaceVertex(triangle.VertexSlot2, p2));
        }

        if (includeInBounds)
            bounds = IncludeBounds(bounds, prepared.Bounds);
        return prepared.SolidTriangleCount;

        uint GetOrAddSolidSurfaceVertex(int sourceVertexSlot, Vector3 position)
        {
            int destinationVertexIndex = destinationVertexIndices[sourceVertexSlot];
            if (destinationVertexIndex >= 0)
                return checked((uint)destinationVertexIndex);

            uint result = checked((uint)(vertices.Count / MapRenderScene.VertexFloatCount));
            AddVertex(vertices, position, color);
            destinationVertexIndices[sourceVertexSlot] = checked((int)result);
            return result;
        }
    }

    private static bool TryBuildTexturedSurface(
        GfxSurface surface,
        PreparedWorldSurfaceGeometry preparedGeometry,
        ReadOnlySpan<byte> vertexBytes,
        IReadOnlyList<PreparedColorLayer> colorLayers,
        IReadOnlyList<ShaderVertexInputBinding> rsxInputBindings,
        IReadOnlyList<byte> vertexLayerBytes,
        bool allowUvValueSanitization,
        out List<float> vertices,
        out List<float> rsxVertexInputs,
        out bool rsxVertexInputsReady,
        out string rsxVertexInputBlocker,
        out List<uint> indices,
        out int triangleCount,
        out int skippedTriangles,
        out int readFailureTriangles,
        out int skyboxTriangles,
        out int uvFailedTriangles,
        out int degenerateUvTriangles,
        out bool lightmapUvReady,
        out RenderBounds bounds,
        bool useGenericFallback)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(preparedGeometry);
        if (!preparedGeometry.Matches(surface))
        {
            throw new ArgumentException(
                "Prepared world geometry does not match the requested surface.",
                nameof(preparedGeometry));
        }

        int sourceVertexCount = preparedGeometry.SourceVertexCount;
        vertices = new List<float>(sourceVertexCount * MapRenderScene.TexturedVertexFloatCount);
        bool materializeRsxVertexInputs = rsxInputBindings.Count > 0 ||
            useGenericFallback;
        rsxVertexInputs = materializeRsxVertexInputs
            ? new List<float>(checked(
                sourceVertexCount *
                RsxVertexInputCount *
                RsxVertexInputComponentCount))
            : [];
        rsxVertexInputsReady = materializeRsxVertexInputs;
        var rsxVertexInputFailures = new SortedSet<string>(StringComparer.Ordinal);
        rsxVertexInputBlocker = string.Empty;
        indices = new List<uint>(preparedGeometry.SourceTriangleCount * 3);
        triangleCount = 0;
        skippedTriangles = 0;
        readFailureTriangles = 0;
        skyboxTriangles = 0;
        uvFailedTriangles = 0;
        degenerateUvTriangles = 0;
        lightmapUvReady = colorLayers.Count > 0 &&
            colorLayers[0].Decoder.HasLightmapTexCoord;
        bounds = RenderBounds.Empty;
        if (colorLayers.Count == 0 || sourceVertexCount <= 0)
            return false;

        skippedTriangles = preparedGeometry.SolidSkippedTriangleCount;
        readFailureTriangles =
            preparedGeometry.SourceTopologyReadFailureTriangleCount;
        uvFailedTriangles = preparedGeometry.PositionReadFailureTriangleCount;

        // Position, routed UV layers, blend weights, and any authorized RSX
        // inputs are properties of a surface vertex, not of each triangle that
        // references it. Preserve the source index domain and materialize each
        // destination vertex at most once. Keep all decoded RSX inputs in one
        // pooled surface slab so a vertex never owns a separate 16-vector array.
        Vector4[]? preparedRsxVertexInputs = materializeRsxVertexInputs
            ? ArrayPool<Vector4>.Shared.Rent(checked(sourceVertexCount * RsxVertexInputCount))
            : null;
        try
        {
            var preparedVertices = new PreparedWorldSurfaceVertex[sourceVertexCount];
            var preparedVertexReady = new bool[sourceVertexCount];
            for (int sourceVertexSlot = 0; sourceVertexSlot < sourceVertexCount; sourceVertexSlot++)
            {
                if (!preparedGeometry.TryGetPosition(
                        sourceVertexSlot,
                        out Vector3 position))
                {
                    continue;
                }

                Span<Vector4> rsxInputDestination = preparedRsxVertexInputs is null
                    ? Span<Vector4>.Empty
                    : preparedRsxVertexInputs.AsSpan(
                        checked(sourceVertexSlot * RsxVertexInputCount),
                        RsxVertexInputCount);
                preparedVertexReady[sourceVertexSlot] = TryPrepareWorldSurfaceVertex(
                    surface,
                    preparedGeometry.GetSourceVertexIndex(sourceVertexSlot),
                    position,
                    vertexBytes,
                    vertexLayerBytes,
                    colorLayers,
                    rsxInputBindings,
                    allowUvValueSanitization,
                    useGenericFallback,
                    materializeRsxVertexInputs,
                    rsxInputDestination,
                    out preparedVertices[sourceVertexSlot]);
            }

            var destinationVertexIndices = new int[sourceVertexCount];
            Array.Fill(destinationVertexIndices, -1);

            foreach (PreparedWorldSurfaceTriangle triangle in preparedGeometry.Triangles)
            {
                int vertexSlot0 = triangle.VertexSlot0;
                int vertexSlot1 = triangle.VertexSlot1;
                int vertexSlot2 = triangle.VertexSlot2;

                if (!preparedVertexReady[vertexSlot0] ||
                    !preparedVertexReady[vertexSlot1] ||
                    !preparedVertexReady[vertexSlot2])
                {
                    uvFailedTriangles++;
                    skippedTriangles++;
                    continue;
                }

                ref readonly PreparedWorldSurfaceVertex vertex0 = ref preparedVertices[vertexSlot0];
                ref readonly PreparedWorldSurfaceVertex vertex1 = ref preparedVertices[vertexSlot1];
                ref readonly PreparedWorldSurfaceVertex vertex2 = ref preparedVertices[vertexSlot2];
                lightmapUvReady &=
                    vertex0.LightmapUvReady &&
                    vertex1.LightmapUvReady &&
                    vertex2.LightmapUvReady;
                if (triangle.IsSkyboxScale)
                    skyboxTriangles++;

                if (vertex0.UvSanitized || vertex1.UvSanitized || vertex2.UvSanitized)
                    uvFailedTriangles++;

                if (IsDegenerateTextureMapping(
                        vertex0.Position,
                        vertex1.Position,
                        vertex2.Position,
                        vertex0.PrimaryUv,
                        vertex1.PrimaryUv,
                        vertex2.PrimaryUv))
                {
                    degenerateUvTriangles++;
                }

                indices.Add(GetOrAddPreparedWorldSurfaceVertex(
                    vertexSlot0,
                    in vertex0,
                    preparedRsxVertexInputs,
                    destinationVertexIndices,
                    vertices,
                    rsxVertexInputs,
                    materializeRsxVertexInputs,
                    ref rsxVertexInputsReady,
                    rsxVertexInputFailures));
                indices.Add(GetOrAddPreparedWorldSurfaceVertex(
                    vertexSlot1,
                    in vertex1,
                    preparedRsxVertexInputs,
                    destinationVertexIndices,
                    vertices,
                    rsxVertexInputs,
                    materializeRsxVertexInputs,
                    ref rsxVertexInputsReady,
                    rsxVertexInputFailures));
                indices.Add(GetOrAddPreparedWorldSurfaceVertex(
                    vertexSlot2,
                    in vertex2,
                    preparedRsxVertexInputs,
                    destinationVertexIndices,
                    vertices,
                    rsxVertexInputs,
                    materializeRsxVertexInputs,
                    ref rsxVertexInputsReady,
                    rsxVertexInputFailures));
                bounds = bounds
                    .Include(vertex0.Position)
                    .Include(vertex1.Position)
                    .Include(vertex2.Position);
                triangleCount++;
            }

            if (!rsxVertexInputsReady ||
                rsxVertexInputs.Count != checked(
                    (vertices.Count / MapRenderScene.TexturedVertexFloatCount) *
                    RsxVertexInputCount *
                    RsxVertexInputComponentCount))
            {
                rsxVertexInputsReady = false;
                rsxVertexInputs.Clear();
            }
            rsxVertexInputBlocker = !materializeRsxVertexInputs
                ? "RSX_VERTEX_INPUT_PAYLOAD_NOT_AVAILABLE_FOR_GENERIC_FALLBACK"
                : rsxVertexInputsReady
                ? string.Empty
                : rsxVertexInputFailures.Count == 0
                    ? "RSX_VERTEX_INPUT_PAYLOAD_COUNT_MISMATCH"
                    : string.Join('|', rsxVertexInputFailures);

            return triangleCount > 0;
        }
        finally
        {
            if (preparedRsxVertexInputs is not null)
                ArrayPool<Vector4>.Shared.Return(preparedRsxVertexInputs, clearArray: false);
        }
    }

    private static bool TryPrepareWorldSurfaceVertex(
        GfxSurface surface,
        int surfaceVertexIndex,
        Vector3 position,
        ReadOnlySpan<byte> vertexBytes,
        IReadOnlyList<byte> vertexLayerBytes,
        IReadOnlyList<PreparedColorLayer> colorLayers,
        IReadOnlyList<ShaderVertexInputBinding> rsxInputBindings,
        bool allowUvValueSanitization,
        bool useGenericFallback,
        bool materializeRsxVertexInputs,
        Span<Vector4> rsxInputDestination,
        out PreparedWorldSurfaceVertex preparedVertex)
    {
        preparedVertex = default;
        Span<Vector2> layerUvs = stackalloc Vector2[MapRenderScene.MaxColorLayerCount];
        int layerCount = Math.Min(colorLayers.Count, MapRenderScene.MaxColorLayerCount);
        bool anySanitized = false;
        for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
        {
            if (!colorLayers[layerIndex].Decoder.TryReadTexCoord(surface, surfaceVertexIndex, out Vector2 raw) ||
                !TryPrepareTexCoord(raw, allowUvValueSanitization, out Vector2 value, out bool sanitized))
            {
                return false;
            }

            layerUvs[layerIndex] = value;
            anySanitized |= sanitized;
        }

        if (layerCount == 0)
            return false;

        colorLayers[0].Decoder.TryReadBlendWeights(surface, surfaceVertexIndex, out Vector4 blendWeights);
        bool lightmapUvReady = colorLayers[0].Decoder.TryReadLightmapTexCoord(
            surface,
            surfaceVertexIndex,
            out Vector2 lightmapUv);
        if (!lightmapUvReady)
            lightmapUv = Vector2.Zero;
        if (!colorLayers[0].Decoder.TryReadNormal(
                surface,
                surfaceVertexIndex,
                out Vector3 normal))
        {
            normal = Vector3.Zero;
        }
        bool rsxInputsReady = false;
        string rsxInputBlocker = string.Empty;
        if (materializeRsxVertexInputs)
        {
            if (useGenericFallback)
            {
                rsxInputsReady = TryBuildGenericRsxVertexInputs(
                    rsxInputDestination,
                    position,
                    layerUvs[0],
                    out rsxInputBlocker);
            }
            else
            {
                rsxInputsReady = TryReadRsxVertexInputs(
                    vertexBytes,
                    vertexLayerBytes,
                    surface,
                    surfaceVertexIndex,
                    rsxInputBindings,
                    rsxInputDestination,
                    out rsxInputBlocker);
            }
        }

        preparedVertex = new PreparedWorldSurfaceVertex(
            position,
            layerUvs[0],
            layerCount > 1 ? layerUvs[1] : layerUvs[0],
            layerCount > 2 ? layerUvs[2] : layerUvs[0],
            layerCount > 3 ? layerUvs[3] : layerUvs[0],
            layerCount > 4 ? layerUvs[4] : layerUvs[0],
            blendWeights,
            lightmapUv,
            lightmapUvReady,
            normal,
            anySanitized,
            rsxInputsReady,
            rsxInputBlocker);
        return true;
    }

    private static void AddPreparedWorldSurfaceVertex(
        List<float> vertices,
        in PreparedWorldSurfaceVertex vertex)
    {
        vertices.Add(vertex.Position.X);
        vertices.Add(vertex.Position.Y);
        vertices.Add(vertex.Position.Z);
        vertices.Add(vertex.Uv0.X);
        vertices.Add(vertex.Uv0.Y);
        vertices.Add(vertex.Uv1.X);
        vertices.Add(vertex.Uv1.Y);
        vertices.Add(vertex.Uv2.X);
        vertices.Add(vertex.Uv2.Y);
        vertices.Add(vertex.Uv3.X);
        vertices.Add(vertex.Uv3.Y);
        vertices.Add(vertex.Uv4.X);
        vertices.Add(vertex.Uv4.Y);
        vertices.Add(vertex.BlendWeights.X);
        vertices.Add(vertex.BlendWeights.Y);
        vertices.Add(vertex.BlendWeights.Z);
        vertices.Add(vertex.BlendWeights.W);
        vertices.Add(vertex.LightmapUv.X);
        vertices.Add(vertex.LightmapUv.Y);
        vertices.Add(vertex.Normal.X);
        vertices.Add(vertex.Normal.Y);
        vertices.Add(vertex.Normal.Z);
    }

    private static uint GetOrAddPreparedWorldSurfaceVertex(
        int sourceVertexSlot,
        in PreparedWorldSurfaceVertex preparedVertex,
        Vector4[]? preparedRsxVertexInputs,
        int[] destinationVertexIndices,
        List<float> vertices,
        List<float> rsxVertexInputs,
        bool materializeRsxVertexInputs,
        ref bool rsxVertexInputsReady,
        ISet<string> rsxVertexInputFailures)
    {
        int destinationVertexIndex = destinationVertexIndices[sourceVertexSlot];
        if (destinationVertexIndex >= 0)
            return checked((uint)destinationVertexIndex);

        uint result = checked((uint)(vertices.Count / MapRenderScene.TexturedVertexFloatCount));
        AddPreparedWorldSurfaceVertex(vertices, in preparedVertex);
        if (materializeRsxVertexInputs)
        {
            if (preparedVertex.RsxInputsReady && preparedRsxVertexInputs is not null)
            {
                AddRsxVertexInputs(
                    rsxVertexInputs,
                    preparedRsxVertexInputs.AsSpan(
                        checked(sourceVertexSlot * RsxVertexInputCount),
                        RsxVertexInputCount));
            }
            else
            {
                rsxVertexInputsReady = false;
                if (!string.IsNullOrEmpty(preparedVertex.RsxInputBlocker))
                    rsxVertexInputFailures.Add(preparedVertex.RsxInputBlocker);
            }
        }

        destinationVertexIndices[sourceVertexSlot] = checked((int)result);
        return result;
    }

    private static bool TryReadRsxVertexInputs(
        ReadOnlySpan<byte> stream0,
        IReadOnlyList<byte> stream1,
        GfxSurface surface,
        int surfaceVertexIndex,
        IReadOnlyList<ShaderVertexInputBinding> bindings,
        Span<Vector4> values,
        out string blocker)
    {
        if (values.Length != RsxVertexInputCount)
        {
            throw new ArgumentException(
                $"RSX vertex input destination must contain exactly {RsxVertexInputCount} values.",
                nameof(values));
        }

        values.Fill(DefaultRsxVertexInput);
        blocker = string.Empty;
        foreach (ShaderVertexInputBinding binding in bindings)
        {
            if (binding.Destination >= values.Length)
            {
                blocker = $"dest0x{binding.Destination:X2}:OUT_OF_RANGE";
                return false;
            }
            if (binding.IsDisabledDefaultAttribute)
            {
                values[binding.Destination] = DefaultRsxVertexInput;
                continue;
            }

            int streamBase = binding.StreamIndex switch
            {
                0 => checked(surface.Triangles.BaseVertex * VertexElementDecoder.WorldVertexStride),
                1 => surface.Triangles.VertexLayerData,
                _ => -1
            };
            if (streamBase < 0)
            {
                blocker = $"dest0x{binding.Destination:X2}:STREAM{binding.StreamIndex}_UNAVAILABLE";
                return false;
            }
            int offset = checked(streamBase + surfaceVertexIndex * binding.Stride + binding.Offset);
            if (!TryDecodeRsxVertexInput(
                    stream0,
                    stream1,
                    binding.StreamIndex,
                    offset,
                    binding.ComponentCount,
                    binding.RsxType,
                    out Vector4 value,
                    out string decodeBlocker))
            {
                blocker = $"dest0x{binding.Destination:X2}:{decodeBlocker}:offset0x{offset:X}";
                return false;
            }
            values[binding.Destination] = value;
        }
        return bindings.Count > 0;
    }

    private static bool TryDecodeRsxVertexInput(
        ReadOnlySpan<byte> stream0,
        IReadOnlyList<byte> stream1,
        byte streamIndex,
        int offset,
        byte componentCount,
        byte rsxType,
        out Vector4 value,
        out string blocker)
    {
        value = new Vector4(0f, 0f, 0f, 1f);
        blocker = string.Empty;
        int byteCount = rsxType switch
        {
            0x01 or 0x03 or 0x05 => componentCount * 2,
            0x02 => componentCount * 4,
            0x04 or 0x07 => componentCount,
            0x06 => 4,
            _ => 0
        };
        if (byteCount <= 0 || offset < 0)
        {
            blocker = $"TYPE0x{rsxType:X2}_OR_OFFSET_INVALID";
            return false;
        }

        Span<byte> bytes = byteCount <= 16 ? stackalloc byte[byteCount] : new byte[byteCount];
        if (streamIndex == 0)
        {
            if (offset + byteCount > stream0.Length)
            {
                blocker = $"STREAM0_RANGE_END0x{offset + byteCount:X}_SIZE0x{stream0.Length:X}";
                return false;
            }
            stream0.Slice(offset, byteCount).CopyTo(bytes);
        }
        else if (streamIndex == 1)
        {
            if (offset + byteCount > stream1.Count)
            {
                blocker = $"STREAM1_RANGE_END0x{offset + byteCount:X}_SIZE0x{stream1.Count:X}";
                return false;
            }
            for (int index = 0; index < byteCount; index++)
                bytes[index] = stream1[offset + index];
        }
        else
        {
            blocker = $"STREAM{streamIndex}_UNAVAILABLE";
            return false;
        }

        Span<float> decoded = stackalloc float[4];
        decoded[3] = 1f;
        if (rsxType == 0x06)
        {
            uint packed = BinaryPrimitives.ReadUInt32BigEndian(bytes);
            decoded[0] = (SignExtend((int)(packed & 0x7ff), 11) << 5) / 32767f;
            decoded[1] = (SignExtend((int)((packed >> 11) & 0x7ff), 11) << 5) / 32767f;
            decoded[2] = (SignExtend((int)((packed >> 22) & 0x3ff), 10) << 6) / 32767f;
        }
        else
        {
            for (int component = 0; component < componentCount && component < 4; component++)
            {
                decoded[component] = rsxType switch
                {
                    0x01 => (BinaryPrimitives.ReadInt16BigEndian(bytes[(component * 2)..]) + 0.5f) / 32767.5f,
                    0x02 => BinaryPrimitives.ReadSingleBigEndian(bytes[(component * 4)..]),
                    0x03 => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16BigEndian(bytes[(component * 2)..])),
                    0x04 => bytes[component] / 255f,
                    0x05 => BinaryPrimitives.ReadInt16BigEndian(bytes[(component * 2)..]),
                    0x07 => bytes[component],
                    _ => 0f
                };
            }
        }

        value = new Vector4(decoded[0], decoded[1], decoded[2], decoded[3]);
        bool finite = float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
        if (!finite)
            blocker = "NONFINITE_DECODE";
        return finite;
    }

    private static void AddRsxVertexInputs(
        List<float> destination,
        ReadOnlySpan<Vector4> inputs)
    {
        if (inputs.Length != RsxVertexInputCount)
        {
            throw new ArgumentException(
                $"RSX vertex input source must contain exactly {RsxVertexInputCount} values.",
                nameof(inputs));
        }

        for (int index = 0; index < RsxVertexInputCount; index++)
        {
            Vector4 value = inputs[index];
            destination.Add(value.X);
            destination.Add(value.Y);
            destination.Add(value.Z);
            destination.Add(value.W);
        }
    }

    private static bool IsDegenerateTextureMapping(
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector2 uv0,
        Vector2 uv1,
        Vector2 uv2)
    {
        float worldArea2 = Vector3.Cross(p1 - p0, p2 - p0).Length();
        if (worldArea2 <= MinTexturedWorldTriangleArea2)
            return false;

        float uvArea2 = MathF.Abs(
            (uv1.X - uv0.X) * (uv2.Y - uv0.Y) -
            (uv1.Y - uv0.Y) * (uv2.X - uv0.X));
        return uvArea2 <= MinTexturedUvTriangleArea2;
    }

}
