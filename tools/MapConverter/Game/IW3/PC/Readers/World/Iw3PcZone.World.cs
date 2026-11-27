namespace MapConverter.Game.IW3.PC.Readers;

internal sealed partial class Iw3PcZone
{
    private sealed partial class Loader
    {
        // Offsets and traversal order follow OAT's generated IW3 PC
        // clipmap_t_iw3_load_db.cpp and gfxworld_iw3_load_db.cpp.
        private int WorldCount(int field)
        {
            int count = Int(field);
            if (count < 0)
                throw new InvalidDataException($"Negative IW3 PC world count at 0x{field:X}.");
            return count;
        }

        private bool WorldHasPointer(int field) => Int(field) != 0;

        private int WorldCountIfPresent(int pointerField, int countField) =>
            WorldHasPointer(pointerField) ? WorldCount(countField) : 0;

        private void ReadClipMap(int body)
        {
            Data(body + 12, WorldCountIfPresent(body + 12, body + 8), 20, shared: true);
            Data(body + 20, WorldCountIfPresent(body + 20, body + 16), 80,
                visit: row => Asset(row + 4, 3));
            Data(body + 28, WorldCountIfPresent(body + 28, body + 24), 72);
            Data(body + 36, WorldCountIfPresent(body + 36, body + 32), 12,
                visit: row => Data(row, 1, 20, shared: true), shared: true);
            Data(body + 44, WorldCountIfPresent(body + 44, body + 40), 1, align: 1);
            Data(body + 52, WorldCountIfPresent(body + 52, body + 48), 8,
                visit: row => Data(row, 1, 20, shared: true));
            Data(body + 60, WorldCountIfPresent(body + 60, body + 56), 44);
            Data(body + 76, WorldCountIfPresent(body + 76, body + 72), 2, align: 2);
            Data(body + 68, WorldCountIfPresent(body + 68, body + 64), 20, visit: ReadLeafBrushNode);
            Data(body + 84, WorldCountIfPresent(body + 84, body + 80), 4);
            Data(body + 92, WorldCountIfPresent(body + 92, body + 88), 12);
            Data(body + 100, checked(WorldCountIfPresent(body + 100, body + 96) * 3), 2, align: 2);
            Data(body + 104, checked((checked(WorldCountIfPresent(body + 104, body + 96) * 3) + 31) / 32 * 4), 1,
                align: 1);
            Data(body + 112, WorldCountIfPresent(body + 112, body + 108), 28);
            Data(body + 120, WorldCountIfPresent(body + 120, body + 116), 12,
                visit: row => Data(row + 8, 1, 28, shared: true));
            Data(body + 128, WorldCountIfPresent(body + 128, body + 124), 32);
            Data(body + 136, WorldCountIfPresent(body + 136, body + 132), 72);
            Data(body + 144, Short(body + 140), 80, align: 16,
                visit: ReadCollisionBrush, shared: true);
            Data(body + 156, WorldHasPointer(body + 156)
                ? checked(WorldCount(body + 148) * WorldCount(body + 152)) : 0, 1,
                align: 1);
            Asset(body + 164, 15);
            WorldDataOrReference(body + 168, 1, 80, 16, ReadCollisionBrush);
            for (int kind = 0; kind < 2; kind++)
            {
                int count = Short(body + 244 + kind * 2);
                Data(body + 248 + kind * 4, count, 96, visit: ReadDynamicEntity);
            }
            for (int kind = 0; kind < 2; kind++)
            {
                int count = Short(body + 244 + kind * 2);
                Data(body + 256 + kind * 4, count, 32, block: 1);
            }
            for (int kind = 0; kind < 2; kind++)
            {
                int count = Short(body + 244 + kind * 2);
                Data(body + 264 + kind * 4, count, 12, block: 1);
            }
            for (int kind = 0; kind < 2; kind++)
            {
                int count = Short(body + 244 + kind * 2);
                Data(body + 272 + kind * 4, count, 20, block: 1);
            }
        }

        private void ReadLeafBrushNode(int row)
        {
            int leafBrushCount = (short)Short(row + 2);
            if (leafBrushCount > 0)
                Data(row + 8, leafBrushCount, 2, align: 2, shared: true);
        }

        private void ReadCollisionBrush(int row)
        {
            WorldDataOrReference(row + 32, 1, 12, 4,
                side => Data(side, 1, 20, shared: true));
            Data(row + 48, 1, 1, align: 1, shared: true);
        }

        private void ReadDynamicEntity(int row)
        {
            Asset(row + 32, 3);
            Asset(row + 40, 25);
            Data(row + 44, 1, 12, visit: pieces =>
            {
                XString(pieces);
                Data(pieces + 8, WorldCount(pieces + 4), 16,
                    visit: piece => Asset(piece, 3));
            }, shared: true);
            Asset(row + 48, 1);
        }

        private void ReadGfxWorld(int body)
        {
            XString(body + 4);
            Data(body + 20, WorldCountIfPresent(body + 20, body + 16), 2, align: 2);
            Data(body + 36, WorldCountIfPresent(body + 36, body + 32), 4);
            Asset(body + 40, 6);
            Data(body + 200, 1, 64, visit: light => Asset(light + 60, 17), shared: true);
            int probeCount = WorldCountIfPresent(body + 232, body + 228);
            Data(body + 232, probeCount, 16,
                visit: probe => Asset(probe + 12, 6));
            Data(body + 236, WorldCountIfPresent(body + 236, body + 228), 4, block: 1);

            int planes = body + 240;
            int cellCount = WorldCount(planes);
            Data(planes + 4, WorldCountIfPresent(planes + 4, body + 8), 20, shared: true);
            Data(planes + 8, WorldCountIfPresent(planes + 8, body + 12), 2, align: 2);
            Data(planes + 12, WorldHasPointer(planes + 12) ? checked(cellCount * 256) : 0, 4, block: 1);
            Data(body + 260, cellCount, 56, visit: ReadGfxCell, shared: true);
            int lightmapCount = WorldCountIfPresent(body + 268, body + 264);
            Data(body + 268, lightmapCount, 8, visit: row =>
            {
                Asset(row, 6);
                Asset(row + 4, 6);
            });
            ReadWorldLightGrid(body + 272);
            Data(body + 328, WorldCountIfPresent(body + 328, body + 264), 4, block: 1);
            Data(body + 332, WorldCountIfPresent(body + 332, body + 264), 4, block: 1);
            Data(body + 340, WorldCountIfPresent(body + 340, body + 336), 56);
            Data(body + 376, WorldCountIfPresent(body + 376, body + 372), 8,
                visit: row => Asset(row, 4));
            Data(body + 52, WorldCountIfPresent(body + 52, body + 48), 44);
            Data(body + 64, WorldCountIfPresent(body + 64, body + 60), 1, align: 1);
            Asset(body + 384, 4);
            Asset(body + 388, 4);
            Asset(body + 540, 6);

            Data(body + 544, WorldHasPointer(body + 544)
                ? checked(cellCount * ((checked(cellCount + 31)) / 32)) : 0, 4, block: 1);
            int dpvsDynamic = body + 684;
            int modelDynCount = WorldCount(dpvsDynamic + 8);
            int brushDynCount = WorldCount(dpvsDynamic + 12);
            Data(body + 548, modelDynCount, 6, block: 1);
            Data(body + 552, brushDynCount, 4, block: 1);
            int nonsunLightCount = 0;
            if (WorldHasPointer(body + 556) || WorldHasPointer(body + 560) || WorldHasPointer(body + 564))
            {
                nonsunLightCount = checked(WorldCount(body + 220) - WorldCount(body + 216) - 1);
                if (nonsunLightCount < 0)
                    throw new InvalidDataException("IW3 PC GfxWorld sun index exceeds the light count.");
            }
            Data(body + 556, WorldHasPointer(body + 556) ? checked(nonsunLightCount * 4096) : 0, 4, block: 1);
            Data(body + 560, WorldHasPointer(body + 560) ? checked(modelDynCount * nonsunLightCount) : 0, 4, block: 1);
            Data(body + 564, WorldHasPointer(body + 564) ? checked(brushDynCount * nonsunLightCount) : 0, 4, block: 1);
            Data(body + 568, modelDynCount, 1, align: 1, block: 1);
            int lightCount = WorldHasPointer(body + 572) || WorldHasPointer(body + 576)
                ? WorldCount(body + 220) : 0;
            Data(body + 572, lightCount, 12, visit: row =>
            {
                Data(row + 4, Short(row), 2, align: 2);
                Data(row + 8, Short(row + 2), 2, align: 2);
            });
            Data(body + 576, lightCount, 8, visit: ReadWorldLightRegion);
            ReadWorldDpvsStatic(body + 580, body);
            ReadWorldDpvsDynamic(dpvsDynamic, cellCount);
        }

        private void ReadGfxCell(int row)
        {
            Data(row + 28, WorldCountIfPresent(row + 28, row + 24), 44,
                visit: tree => Data(tree + 36, Short(tree + 34), 2, align: 2, shared: true));
            Data(row + 36, WorldCountIfPresent(row + 36, row + 32), 68, visit: portal =>
            {
                WorldDataOrReference(portal + 32, 1, 56, 4, ReadGfxCell);
                Data(portal + 36, Byte(portal + 40), 12);
            });
            Data(row + 44, WorldCountIfPresent(row + 44, row + 40), 4);
            Data(row + 52, Byte(row + 48), 1, align: 1);
        }

        private void ReadWorldLightGrid(int grid)
        {
            int axis = WorldHasPointer(grid + 28) ? WorldCount(grid + 20) : 0;
            if (axis > 2)
                throw new InvalidDataException("IW3 PC light-grid row axis is invalid.");
            int rowCount = WorldHasPointer(grid + 28)
                ? Short(grid + 14 + axis * 2) - Short(grid + 8 + axis * 2) + 1 : 0;
            if (WorldHasPointer(grid + 28) && rowCount < 1)
                throw new InvalidDataException("IW3 PC light-grid row bounds are invalid.");
            Data(grid + 28, rowCount, 2, align: 2);
            Data(grid + 36, WorldCountIfPresent(grid + 36, grid + 32), 1, align: 1);
            Data(grid + 44, WorldCountIfPresent(grid + 44, grid + 40), 4);
            Data(grid + 52, WorldCountIfPresent(grid + 52, grid + 48), 168);
        }

        private void ReadWorldLightRegion(int row) =>
            Data(row + 4, WorldCountIfPresent(row + 4, row), 80, visit: hull =>
                Data(hull + 76, WorldCountIfPresent(hull + 76, hull + 72), 20));

        private void ReadWorldDpvsStatic(int dpvs, int world)
        {
            int modelCount = WorldCount(dpvs);
            int surfaceCount = WorldCount(dpvs + 4);
            for (int index = 0; index < 3; index++)
                Data(dpvs + 44 + index * 4, modelCount, 1, align: 1, block: 1);
            for (int index = 0; index < 3; index++)
                Data(dpvs + 56 + index * 4, surfaceCount, 1, align: 1, block: 1);
            Data(dpvs + 68, WorldHasPointer(dpvs + 68)
                ? checked(WorldCount(dpvs + 36) * 2) : 0, 4, align: 128, block: 1);
            Data(dpvs + 72, WorldHasPointer(dpvs + 72)
                ? checked(surfaceCount + WorldCount(dpvs + 8)) : 0, 2, align: 2);
            Data(dpvs + 76, modelCount, 28);
            Data(dpvs + 80, WorldCountIfPresent(dpvs + 80, world + 24), 48,
                visit: row => Asset(row + 16, 4));
            Data(dpvs + 84, WorldCountIfPresent(dpvs + 84, world + 224), 32);
            Data(dpvs + 88, modelCount, 76,
                visit: row => Asset(row + 56, 3));
            Data(dpvs + 92, surfaceCount, 8, block: 1);
            Data(dpvs + 96, WorldCountIfPresent(dpvs + 96, dpvs + 40), 4, align: 128, block: 1);
        }

        private void ReadWorldDpvsDynamic(int dpvs, int cellCount)
        {
            for (int kind = 0; kind < 2; kind++)
            {
                int words = WorldCountIfPresent(dpvs + 16 + kind * 4, dpvs + kind * 4);
                Data(dpvs + 16 + kind * 4, checked(words * cellCount), 4, block: 1);
            }
            for (int kind = 0; kind < 2; kind++)
                for (int slice = 0; slice < 3; slice++)
                {
                    int field = dpvs + 24 + kind * 12 + slice * 4;
                    Data(field, checked(32 * WorldCountIfPresent(field, dpvs + kind * 4)),
                        1, align: 16, block: 1);
                }
        }

        private void WorldDataOrReference(int field, int count, int stride, int align,
            Action<int> visit)
        {
            uint raw = unchecked((uint)Int(field));
            if (raw == 0) return;
            if (raw == Following)
                Data(field, count, stride, align, visit, shared: true);
            else
                zone.pointers[field] = Resolve(raw, false);
        }
    }
}
