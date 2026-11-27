namespace MapConverter.Game.IW3.PC.Readers;

internal sealed partial class Iw3PcZone
{
    private sealed partial class Loader
    {
        private void ReadWeapon(int body)
        {
            XString(body + 4);
            XString(body + 8);
            // The sixteen gun models are followed by the hand model and 33 animation names.
            for (int offset = 12; offset <= 76; offset += 4) Asset(body + offset, 3);
            for (int offset = 80; offset <= 212; offset += 4) XString(body + offset);
            Asset(body + 332, 25);
            Asset(body + 336, 25);
            void SoundName(int field) => Data(field, 1, 4, visit: XString, shared: true);
            for (int offset = 340; offset < 520; offset += 4) SoundName(body + offset);
            Data(body + 520, 29, 4, visit: SoundName, shared: true);
            for (int offset = 524; offset <= 536; offset += 4) Asset(body + offset, 25);
            Asset(body + 540, 4);
            Asset(body + 544, 4);
            for (int offset = 700; offset <= 776; offset += 4) Asset(body + offset, 3);
            Asset(body + 780, 4);
            Asset(body + 788, 4);
            XString(body + 804);
            XString(body + 812);
            XString(body + 832);
            Asset(body + 1072, 4);
            Asset(body + 1076, 4);
            Asset(body + 1304, 4);
            Asset(body + 1316, 4);
            XString(body + 1340);
            Asset(body + 1412, 3);
            Asset(body + 1420, 25);
            Asset(body + 1428, 25);
            SoundName(body + 1432);
            SoundName(body + 1436);
            Asset(body + 1704, 25);
            Asset(body + 1732, 25);
            SoundName(body + 1736);
            for (int graph = 0; graph < 2; ++graph)
            {
                int offset = graph * 4;
                XString(body + 1900 + offset);
                int knots = Int(body + 1924 + offset);
                Data(body + 1908 + offset, knots, 8, shared: true);
                Data(body + 1916 + offset, knots, 8, shared: true);
            }
            XString(body + 2012);
            XString(body + 2016);
            XString(body + 2036);
            XString(body + 2152);
            XString(body + 2156);
        }
    }
}
