using System.Buffers.Binary;
using IW4.Formats.D3dbsp;
using MapConverter.Game.IW3.PC.Readers;

namespace MapConverter.Game.IW3.PC.World;

internal static partial class Iw3PcWorldGraphics
{
    internal static void AddLumps(Iw3PcZone zone, int gfx, int com, ushort[] materialIndices,
        int[] modelIndexBySource,
        List<(D3dbspLumpType Type, byte[] Data)> lumps, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(materialIndices);
        ArgumentNullException.ThrowIfNull(modelIndexBySource);
        ArgumentNullException.ThrowIfNull(lumps);
        var graphics = new Graphics(zone, gfx, com, token);
        Lighting lighting = graphics.EncodeLighting();
        Render render = graphics.EncodeRender(materialIndices, lighting.ProbeCount);
        (byte[] cells, byte[] spatial) = graphics.EncodeSpatial(modelIndexBySource, render, lighting.ProbeCount);
        var pending = new List<(D3dbspLumpType Type, byte[] Data)>
        {
            (D3dbspLumpType.LightGridEntries, lighting.GridEntries),
            (D3dbspLumpType.LightGridColors, lighting.GridColors),
            (D3dbspLumpType.Cells, cells),
            (D3dbspLumpType.RenderSpatial, spatial),
            (D3dbspLumpType.ReflectionProbes, lighting.Probes),
            (D3dbspLumpType.PrimaryLights, graphics.EncodePrimaryLights()),
            (D3dbspLumpType.LightGridHeader, lighting.GridHeader),
            (D3dbspLumpType.LightGridRows, lighting.GridRows),
            (D3dbspLumpType.UnlayeredTriangles, render.Triangles),
            (D3dbspLumpType.UnlayeredDrawVerts, render.Vertices),
            (D3dbspLumpType.UnlayeredDrawIndices, render.Indices)
        };
        if (lighting.HasRegions)
        {
            pending.Add((D3dbspLumpType.LightRegions, lighting.Regions));
            pending.Add((D3dbspLumpType.LightRegionHulls, lighting.RegionHulls));
            pending.Add((D3dbspLumpType.LightRegionAxes, lighting.RegionAxes));
        }
        lumps.AddRange(pending);
    }

    private sealed partial class Graphics
    {
        private readonly Iw3PcZone zone;
        private readonly int gfx;
        private readonly int com;
        private readonly CancellationToken token;

        internal Graphics(Iw3PcZone zone, int gfx, int com, CancellationToken token)
        {
            this.zone = zone;
            this.gfx = gfx;
            this.com = com;
            this.token = token;
        }

        internal int I32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(zone.Bytes(offset, 4));
        internal uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(zone.Bytes(offset, 4));
        internal int U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(zone.Bytes(offset, 2));
        internal float F32(int offset) => BinaryPrimitives.ReadSingleLittleEndian(zone.Bytes(offset, 4));
        internal int B(int offset) => zone.Bytes(offset, 1)[0];
        internal int P(int offset) => zone.Pointer(offset);
        internal int Required(int offset, string description) => P(offset) is var value && value != 0 ? value :
            throw new InvalidDataException($"IW3 PC {description} pointer is null.");
        internal int Count(int offset, string description)
        {
            int count = I32(offset);
            if (count < 0) throw new InvalidDataException($"IW3 PC {description} count is negative.");
            return count;
        }
        internal void Finite(float value, string description)
        {
            if (!float.IsFinite(value)) throw new InvalidDataException($"IW3 PC {description} is nonfinite.");
        }

        internal byte[] EncodePrimaryLights()
        {
            int count = Count(com + 8, "primary light");
            int table = Required(com + 12, "primary light table");
            if (count <= 1 || B(table) != 0)
                throw new InvalidDataException("IW3 PC primary lights need a none sentinel and map light.");
            int lastSun = 0;
            while (lastSun + 1 < count && B(table + (lastSun + 1) * 68) == 1) lastSun++;
            if (Count(gfx + 220, "GfxWorld primary light") != count ||
                Count(gfx + 216, "sun primary light index") != lastSun)
                throw new InvalidDataException("GfxWorld primary lights disagree with ComWorld.");
            int sun = P(gfx + 200);
            float[] sunColor = new float[3];
            if (lastSun != 0)
            {
                if (sun == 0 || B(sun) != 1)
                    throw new InvalidDataException("GfxWorld selected sun is not directional.");
                int light = table + lastSun * 68;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (BitConverter.SingleToInt32Bits(F32(sun + 16 + axis * 4)) !=
                        BitConverter.SingleToInt32Bits(F32(light + 16 + axis * 4)))
                        throw new InvalidDataException("GfxWorld sun direction disagrees with ComWorld.");
                    float linear = F32(sun + 4 + axis * 4);
                    Finite(linear, "sun color");
                    const float threshold = 0.0392800010740757f, slope = 0.07739938050508499f;
                    const float offset = 0.054999999701976776f, multiplier = 0.9478673338890076f;
                    const float exponent = 2.4000000953674316f;
                    sunColor[axis] = (float)(linear <= threshold * slope ? linear / (double)slope :
                        Math.Pow(linear, 1.0 / exponent) / multiplier - offset);
                    Finite(sunColor[axis], "encoded sun color");
                }
            }
            var data = new byte[checked(count * 128)];
            bool nonDirectional = false;
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                int source = table + index * 68, row = index * 128;
                int type = B(source);
                if (index > 0)
                {
                    if (type != 1) nonDirectional = true;
                    else if (nonDirectional)
                        throw new InvalidDataException("Directional lights are not a contiguous prefix.");
                }
                data[row] = (byte)type;
                data[row + 1] = (byte)B(source + 1);
                for (int axis = 0; axis < 3; axis++)
                {
                    WF(data, row + 4 + axis * 4, index != 0 && index == lastSun ? sunColor[axis] : F32(source + 4 + axis * 4));
                    WF(data, row + 16 + axis * 4, F32(source + 16 + axis * 4));
                    WF(data, row + 28 + axis * 4, F32(source + 28 + axis * 4));
                }
                WF(data, row + 40, F32(source + 40));
                WF(data, row + 44, F32(source + 44));
                WF(data, row + 48, F32(source + 48));
                W32(data, row + 52, B(source + 2));
                WF(data, row + 56, F32(source + 56));
                WF(data, row + 60, F32(source + 60));
                if (type is not (0 or 1) && P(source + 64) != 0)
                {
                    byte[] name = System.Text.Encoding.Latin1.GetBytes(zone.String(source + 64));
                    name.AsSpan(0, Math.Min(name.Length, 63)).CopyTo(data.AsSpan(row + 64));
                }
            }
            return data;
        }

        internal Render EncodeRender(ushort[] materialIndices, int probeCount)
        {
            int surfaceCount = Count(gfx + 24, "world surface");
            int vertexCount = Count(gfx + 48, "world vertex");
            int indexCount = Count(gfx + 16, "world index");
            int lightmapCount = Count(gfx + 264, "world lightmap");
            if (surfaceCount <= 0 || surfaceCount > ushort.MaxValue || vertexCount <= 0 || indexCount <= 0 ||
                materialIndices.Length != surfaceCount || lightmapCount > 31 ||
                lightmapCount != 0 && P(gfx + 268) == 0)
                throw new InvalidDataException("IW3 PC render table counts are invalid.");
            int surfaces = Required(gfx + 660, "world surface table");
            int vertices = Required(gfx + 52, "world vertex table");
            int indices = Required(gfx + 20, "world index table");
            byte[] indexBytes = zone.Bytes(indices, checked(indexCount * 2)).ToArray();
            var drawVerts = new byte[checked(vertexCount * 68)];
            var triangles = new byte[checked(surfaceCount * 24)];
            for (int index = 0; index < vertexCount; index++)
            {
                token.ThrowIfCancellationRequested();
                int source = vertices + index * 44, row = index * 68;
                float[] normal = UnitVector(source + 36), tangent = UnitVector(source + 40);
                float sign = F32(source + 12) < 0 ? -1f : 1f;
                for (int axis = 0; axis < 3; axis++)
                {
                    float position = F32(source + axis * 4);
                    Finite(position, "render position");
                    WF(drawVerts, row + axis * 4, position);
                    WF(drawVerts, row + 12 + axis * 4, normal[axis]);
                    WF(drawVerts, row + 44 + axis * 4, tangent[axis]);
                }
                zone.Bytes(source + 16, 4).CopyTo(drawVerts.AsSpan(row + 24));
                for (int component = 0; component < 4; component++)
                {
                    float coordinate = F32(source + 20 + component * 4);
                    if (component >= 2) Finite(coordinate, "render lightmap coordinate");
                    WF(drawVerts, row + 28 + component * 4, coordinate);
                }
                float[] binormal =
                [
                    (normal[1] * tangent[2] - normal[2] * tangent[1]) * sign,
                    (normal[2] * tangent[0] - normal[0] * tangent[2]) * sign,
                    (normal[0] * tangent[1] - normal[1] * tangent[0]) * sign
                ];
                for (int axis = 0; axis < 3; axis++) WF(drawVerts, row + 56 + axis * 4, binormal[axis]);
            }
            for (int index = 0; index < surfaceCount; index++)
            {
                token.ThrowIfCancellationRequested();
                int surface = surfaces + index * 48, row = index * 24;
                int firstVertex = I32(surface + 4), baseIndex = I32(surface + 12);
                int localVerts = U16(surface + 8), localIndices = checked(U16(surface + 10) * 3);
                int lightmap = B(surface + 20), reflection = B(surface + 21);
                if (localVerts <= 0 || localIndices <= 0 || localIndices > ushort.MaxValue ||
                    firstVertex < 0 || baseIndex < 0 || firstVertex > vertexCount - localVerts ||
                    baseIndex > indexCount - localIndices || lightmap != 31 && lightmap >= lightmapCount ||
                    reflection >= probeCount)
                    throw new InvalidDataException($"IW3 PC render surface {index} has invalid ranges or light indices.");
                W16(triangles, row, materialIndices[index]);
                triangles[row + 2] = (byte)lightmap;
                triangles[row + 3] = (byte)reflection;
                int primary = B(surface + 22);
                triangles[row + 4] = (byte)(primary < Count(gfx + 220, "primary light") ? primary : 0);
                triangles[row + 5] = (byte)(B(surface + 23) & 1);
                W32(triangles, row + 12, firstVertex);
                W16(triangles, row + 16, localVerts);
                W16(triangles, row + 18, localIndices);
                W32(triangles, row + 20, baseIndex);
                for (int local = 0; local < localIndices; local++)
                    if (BinaryPrimitives.ReadUInt16LittleEndian(indexBytes.AsSpan((baseIndex + local) * 2)) >= localVerts)
                        throw new InvalidDataException($"IW3 PC render surface {index} has a nonlocal index.");
            }
            return new Render(triangles, drawVerts, indexBytes);
        }

        private float[] UnitVector(int source)
        {
            float scale = (B(source + 3) + 192f) / 32385f;
            float[] decoded = Enumerable.Range(0, 3).Select(i => (B(source + i) - 127f) * scale).ToArray();
            foreach (float value in decoded) Finite(value, "render basis");
            return decoded;
        }
    }

    private sealed record Render(byte[] Triangles, byte[] Vertices, byte[] Indices);
    private static void W16(byte[] data, int offset, int value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), checked((ushort)value));
    private static void W32(byte[] data, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
    private static void WF(byte[] data, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset), value);
}
