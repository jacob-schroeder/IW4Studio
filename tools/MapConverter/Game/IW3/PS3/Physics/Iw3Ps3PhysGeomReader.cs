using System.Buffers.Binary;
using IW4.Game.Assets.Physics;
using IW4.Game.Math;

namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        // COD4 PS3 0xc0550: PhysGeomList is 0x2c bytes. Its geometry table is
        // 0x44 bytes per row and is traversed by 0xc03b8.
        private PhysCollmapAsset? PhysGeomList(uint raw, string modelName, Region modelRoot)
        {
            Pointer? list = ReadArray(raw, 4, 0x2c, "model-phys-geom-list");
            if (list is null) return null;
            if (raw != uint.MaxValue)
            {
                if (!_converting) return null;
                return list.Value as PhysCollmapAsset ??
                    throw Unsupported($"model '{modelName}' has an unresolved physics geometry list");
            }

            ReadOnlySpan<byte> header = Bytes(list.Region).Span;
            int count = Count(Be32(header, 0));
            Pointer? table = ReadArray(Be32(header, 4), 4,
                Size(count, 0x44), "model-phys-geoms");
            var geoms = new List<PhysGeomInfo>(count);
            if (table is not null && Be32(header, 4) == uint.MaxValue)
            {
                for (int index = 0; index < count; index++)
                {
                    Region row = new(table.Region.Address.Add(index * 0x44),
                        table.Region.FileOffset + index * 0x44, 0x44);
                    PhysGeomInfo? geom = PhysGeom(row, modelName);
                    if (geom is not null) geoms.Add(geom);
                }
            }
            else if (_converting && count != 0)
                throw Unsupported($"model '{modelName}' has no inline physics geometry rows");

            if (!_converting) return null;
            if (geoms.Count != count)
                throw Invalid($"model '{modelName}' has an incomplete physics geometry table");
            ReadOnlySpan<byte> model = Bytes(modelRoot).Span;
            var asset = new PhysCollmapAsset
            {
                Name = _ownedNamePrefix + modelName,
                Count = count,
                Geoms = geoms,
                Mass = new PhysMass
                {
                    CenterOfMass = ReadVec3(header, 8),
                    MomentsOfInertia = ReadVec3(header, 0x14),
                    ProductsOfInertia = ReadVec3(header, 0x20)
                },
                Bounds = ReadMinMaxBounds(model, 0x9c, 0xa8)
            };
            Remember(list with { Value = asset });
            _definitions.Add(asset);
            return asset;
        }

        private PhysGeomInfo? PhysGeom(Region row, string modelName)
        {
            ReadOnlySpan<byte> bytes = Bytes(row).Span;
            BrushWrapper? brush = PhysBrush(Be32(bytes, 0), modelName);
            if (!_converting) return null;
            int sourceType = BinaryPrimitives.ReadInt32BigEndian(bytes[4..]);
            // A native IW4 PS3 PhysCollmap (vehicle_zodiac) has type 0 with
            // a populated BrushWrapper; its 0x44-byte geometry row matches
            // the source row and the wrapper is converted below.
            bool supported = sourceType switch
            {
                0 => brush is not null,
                1 => true,
                4 => brush is null,
                _ => false
            };
            if (!supported)
                throw Unsupported($"model '{modelName}' has physics geometry type {sourceType} without a proven IW4 mapping");
            Vec3 half = ReadVec3(bytes, 0x38);
            if (sourceType == 4)
            {
                // COD4 PS3 physics_cylinder parser 0x1f1fd0 writes type 4,
                // half-length at +0x38 and radius at +0x3c. IW4's native
                // cylinder (type 5) stores (radius, radius, half-length).
                if (half.Z != 0)
                    throw Unsupported($"model '{modelName}' physics cylinder has an unexpected third extent");
                half = new Vec3 { X = half.Y, Y = half.Y, Z = half.X };
            }
            return new PhysGeomInfo
            {
                BrushWrapper = brush,
                Type = sourceType == 4 ? 5 : sourceType,
                Orientation = [ReadVec3(bytes, 8), ReadVec3(bytes, 0x14),
                    ReadVec3(bytes, 0x20)],
                Bounds = new Bounds
                {
                    MidPoint = ReadVec3(bytes, 0x2c),
                    HalfSize = half
                }
            };
        }

        // COD4 PS3 0xc03b8 / 0xc0210 / 0xbff20. The source wrapper and
        // brush-side rows are 0x50 and 0x0c bytes; IW4 stores them as 0x44
        // and 0x08 byte rows with narrower material and adjacency fields.
        private BrushWrapper? PhysBrush(uint raw, string modelName)
        {
            Pointer? source = ReadArray(raw, 4, 0x50, "model-phys-brush");
            if (source is null) return null;
            if (raw != uint.MaxValue)
            {
                if (!_converting) return null;
                return source.Value as BrushWrapper ??
                    throw Unsupported($"model '{modelName}' has an unresolved physics brush");
            }

            ReadOnlySpan<byte> bytes = Bytes(source.Region).Span;
            int sideCount = Count(Be32(bytes, 0x1c));
            Pointer? sideRows = ReadArray(Be32(bytes, 0x20), 4,
                Size(sideCount, 0x0c), "model-phys-sides");
            var sides = new List<CBrushSide>(sideCount);
            if (sideRows is not null && Be32(bytes, 0x20) == uint.MaxValue)
            {
                for (int index = 0; index < sideCount; index++)
                {
                    ReadOnlySpan<byte> side = _data.AsSpan(
                        sideRows.Region.FileOffset + index * 0x0c, 0x0c);
                    CPlane? plane = PhysPlane(Be32(side, 0));
                    if (!_converting) continue;
                    uint material = Be32(side, 4);
                    ushort adjacent = Be16(side, 8);
                    if (material > ushort.MaxValue ||
                        adjacent > byte.MaxValue && adjacent != ushort.MaxValue)
                        throw Unsupported($"model '{modelName}' physics side exceeds IW4 field widths");
                    sides.Add(new CBrushSide
                    {
                        Plane = plane,
                        MaterialNum = (ushort)material,
                        FirstAdjacentSideOffset = adjacent == ushort.MaxValue
                            ? byte.MaxValue : (byte)adjacent,
                        EdgeCount = side[10]
                    });
                }
            }
            else if (_converting && sideCount != 0)
                throw Unsupported($"model '{modelName}' has no inline physics brush sides");

            int edgeCount = Count(Be32(bytes, 0x48));
            Pointer? edges = ReadArray(Be32(bytes, 0x30), 1,
                edgeCount, "model-phys-edges");
            CPlane[] planes = PhysPlanes(Be32(bytes, 0x4c), sideCount);
            if (!_converting) return null;
            if (sideCount > ushort.MaxValue)
                throw Unsupported($"model '{modelName}' has too many physics brush sides for IW4");
            var axial = new short[6];
            var adjacentOffsets = new byte[6];
            var edgeCounts = bytes[0x40..0x46].ToArray();
            for (int index = 0; index < 6; index++)
            {
                axial[index] = BinaryPrimitives.ReadInt16BigEndian(bytes[(0x24 + index * 2)..]);
                ushort offset = Be16(bytes, 0x34 + index * 2);
                if (offset > byte.MaxValue && offset != ushort.MaxValue)
                    throw Unsupported($"model '{modelName}' physics adjacency exceeds IW4 byte width");
                adjacentOffsets[index] = offset == ushort.MaxValue ? byte.MaxValue : (byte)offset;
            }
            byte[] adjacentBytes = edgeCount == 0 ? [] :
                edges is not null && edges.Region.Length == edgeCount
                    ? Bytes(edges.Region).ToArray()
                    : throw Unsupported($"model '{modelName}' has unresolved physics adjacent edges");
            var wrapper = new BrushWrapper
            {
                Bounds = ReadMinMaxBounds(bytes, 0, 0x10),
                Brush = new CBrush
                {
                    NumSides = (ushort)sideCount,
                    Sides = sides,
                    BaseAdjacentSide = adjacentBytes,
                    AxialMaterialNum = axial,
                    FirstAdjacentSideOffsets = adjacentOffsets,
                    EdgeCount = edgeCounts
                },
                TotalEdgeCount = edgeCount,
                Planes = planes
            };
            Remember(source with { Value = wrapper });
            return wrapper;
        }

        private CPlane[] PhysPlanes(uint raw, int count)
        {
            if (raw == 0) return [];
            if (raw == uint.MaxValue)
            {
                Pointer table = ReadArray(raw, 4, Size(count, 0x14),
                    "model-phys-planes") ?? throw Invalid("inline physics plane table was not retained");
                if (!_converting) return [];
                ReadOnlySpan<byte> bytes = Bytes(table.Region).Span;
                var values = new CPlane[count];
                for (int index = 0; index < count; index++)
                    values[index] = ReadPlane(bytes.Slice(index * 0x14, 0x14));
                return values;
            }
            if (!_converting) return [];
            Address address = Decode(raw);
            var planes = new CPlane[count];
            for (int index = 0; index < count; index++)
                planes[index] = PhysPlaneAt(address.Add(index * 0x14));
            return planes;
        }

        private CPlane? PhysPlane(uint raw)
        {
            if (raw == 0) return null;
            if (raw == uint.MaxValue)
            {
                Pointer plane = ReadArray(raw, 4, 0x14,
                    "model-phys-plane") ?? throw Invalid("inline physics plane was not retained");
                return _converting ? ReadPlane(Bytes(plane.Region).Span) : null;
            }
            return _converting ? PhysPlaneAt(Decode(raw)) : null;
        }

        private CPlane PhysPlaneAt(Address address)
        {
            foreach (Pointer pointer in _direct.Values)
            {
                if (pointer.Kind is not ("model-phys-plane" or "model-phys-planes" or "clip-planes"))
                    continue;
                Region region = pointer.Region;
                int offset = address.Offset - region.Address.Offset;
                if (address.Block != region.Address.Block || offset < 0 ||
                    offset % 0x14 != 0 || offset > region.Length - 0x14)
                    continue;
                return ReadPlane(_data.AsSpan(region.FileOffset + offset, 0x14));
            }
            throw Unsupported($"unresolved physics plane at block {address.Block}:0x{address.Offset:X}");
        }
    }
}
