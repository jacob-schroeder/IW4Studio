namespace MapConverter.Game.IW3.PC.Readers;

internal sealed partial class Iw3PcZone
{
    private sealed partial class Loader
    {
        private void ReadModel(int model)
        {
            int bones = Byte(model + 4), children = bones - Byte(model + 5), surfaces = Byte(model + 6);
            Data(model + 8, bones, 2, 2, shared: true);
            Data(model + 12, children, 1, 1, shared: true);
            Data(model + 16, children, 8, 2, shared: true);
            Data(model + 20, children, 16, shared: true);
            Data(model + 24, bones, 1, 1, shared: true);
            Data(model + 28, bones, 32, shared: true);
            Data(model + 32, surfaces, 56, visit: ReadSurface);
            Data(model + 36, surfaces, 4, visit: material => Asset(material, 4));
            Data(model + 152, Int(model + 156), 44, visit: surface => Data(surface, Int(surface + 4), 48));
            Data(model + 164, bones, 40);
            Asset(model + 212, 1);
            Data(model + 216, 1, 44, visit: list =>
                Data(list + 4, Int(list), 68, visit: geom => Data(geom, 1, 80, visit: ReadBrushWrapper), shared: true),
                shared: true);
        }

        private void ReadSurface(int surface)
        {
            int blends = Short(surface + 16) + 3 * Short(surface + 18) +
                5 * Short(surface + 20) + 7 * Short(surface + 22);
            Data(surface + 24, blends, 2, 2, shared: true);
            Data(surface + 28, Short(surface + 2), 32, 16, shared: true, block: 7);
            Data(surface + 36, Int(surface + 32), 12, visit: rigid =>
                Data(rigid + 8, 1, 40, visit: tree =>
                {
                    Data(tree + 28, Int(tree + 24), 16, 16);
                    Data(tree + 36, Int(tree + 32), 2, 2);
                }, shared: true), shared: true);
            Data(surface + 12, Short(surface + 4), 6, 16, shared: true, block: 8);
        }

        private void ReadBrushWrapper(int brush)
        {
            int sides = Int(brush + 28);
            Data(brush + 32, sides, 12, visit: side => Data(side, 1, 20, shared: true));
            Data(brush + 48, Int(brush + 72), 1, 1);
            Data(brush + 76, sides, 20, shared: true);
        }

        private void ReadAnimation(int anim)
        {
            int indexSize = Short(anim + 14) < 256 ? 1 : 2;
            Data(anim + 48, Byte(anim + 27), 2, 2);
            Data(anim + 80, Byte(anim + 28), 8);
            Data(anim + 84, 1, 8, visit: delta =>
            {
                Data(delta, 1, 4, visit: trans =>
                {
                    int count = Short(trans);
                    if (count == 0) Take(Virtual, 1, 12);
                    else
                    {
                        Take(Virtual, 1, 28 + (count + 1) * indexSize);
                        bool small = Byte(trans + 2) != 0;
                        Data(trans + 28, count + 1, small ? 3 : 6, small ? 1 : 4);
                    }
                });
                Data(delta + 4, 1, 4, visit: quat =>
                {
                    int count = Short(quat);
                    Take(Virtual, 1, 4 + (count == 0 ? 0 : (count + 1) * indexSize));
                    if (count > 0) Data(quat + 4, count + 1, 4);
                });
            });
            Data(anim + 52, Short(anim + 4), 1, 1);
            Data(anim + 56, Short(anim + 6), 2, 2);
            Data(anim + 60, Short(anim + 8), 4);
            Data(anim + 64, Int(anim + 32), 2, 2);
            Data(anim + 68, Short(anim + 10), 1, 1);
            Data(anim + 72, Short(anim + 12), 4);
            Data(anim + 76, Int(anim + 36), indexSize, indexSize);
        }
    }
}
