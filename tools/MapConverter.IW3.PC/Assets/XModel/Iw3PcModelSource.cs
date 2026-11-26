using System.Buffers.Binary;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.IW3.PC.Assets.XModel;

internal readonly record struct Iw3PcModelSource(Iw3PcZone Zone, int Offset)
{
    internal string Name => Zone.String(Offset);

    internal Iw3XModelCollisionSource ReadCollision()
    {
        int count = Zone.Int32(Offset + 156);
        int table = Zone.Pointer(Offset + 152);
        int lod = BinaryPrimitives.ReadInt16LittleEndian(Zone.Bytes(Offset + 198, 2));
        if (count is < 0 or > 1_000_000 || count != 0 && table == 0 || lod is < -1 or > 3)
            throw new InvalidDataException($"Invalid collision table for IW3 PC XModel '{Name}'.");
        int bones = Zone.Bytes(Offset + 4, 1)[0];
        var surfaces = new Iw3XModelCollisionSurfaceSource[count];
        for (int i = 0; i < count; i++)
        {
            int row = checked(table + i * 44);
            float[] mins = Zone.Floats(row + 8, 3), maxs = Zone.Floats(row + 20, 3);
            int bone = Zone.Int32(row + 32);
            if (bone < 0 || bone >= bones || mins.Where((value, axis) => value > maxs[axis]).Any())
                throw new InvalidDataException($"Invalid bounds or bone in IW3 PC XModel '{Name}' collision surface {i}.");
            surfaces[i] = new(mins, maxs, bone, Zone.Int32(row + 36), Zone.Int32(row + 40));
        }
        return new(Name, Zone.Int32(Offset + 160), lod, surfaces);
    }
}
