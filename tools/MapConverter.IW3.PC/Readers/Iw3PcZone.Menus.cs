namespace MapConverter.IW3.PC.Readers;

public sealed partial class Iw3PcZone
{
    private sealed partial class Loader
    {
        private void ReadMenu(int menu)
        {
            ReadWindowAfterName(menu);
            XString(menu + 156);
            XString(menu + 196);
            XString(menu + 200);
            XString(menu + 204);
            ReadKeyHandlers(menu + 208);
            ReadExpression(menu + 212);
            XString(menu + 220);
            XString(menu + 224);
            ReadExpression(menu + 264);
            ReadExpression(menu + 272);
            Data(menu + 280, Int(menu + 164), 4, visit: field => Data(field, 1, 372, visit: ReadMenuItem));
        }

        private void ReadWindowAfterName(int window)
        {
            XString(window + 52);
            Asset(window + 152, 4);
        }

        private void ReadKeyHandlers(int field)
        {
            while (Int(field) != 0)
            {
                token.ThrowIfCancellationRequested();
                Data(field, 1, 12);
                int handler = zone.Pointer(field);
                XString(handler + 4);
                field = handler + 8;
            }
        }

        private void ReadExpression(int expression) =>
            Data(expression + 4, Int(expression), 4, visit: entry =>
                Data(entry, 1, 12, visit: value =>
                {
                    if (Int(value) == 1 && Int(value + 4) == 2) XString(value + 8);
                }));

        private void ReadMenuItem(int item)
        {
            XString(item);
            ReadWindowAfterName(item);
            XString(item + 224);
            for (int offset = 236; offset <= 272; offset += 4) XString(item + offset);
            ReadKeyHandlers(item + 276);
            XString(item + 280);
            Asset(item + 288, 7);
            int options = item + 300;
            switch (Int(item + 180))
            {
                case 6:
                    Data(options, 1, 340, visit: list =>
                    {
                        XString(list + 288);
                        Asset(list + 336, 4);
                    });
                    break;
                case 0 or 4 or 9 or 10 or 11 or 14 or 16 or 17 or 18: Data(options, 1, 32); break;
                case 12:
                    Data(options, 1, 392, visit: multi =>
                    {
                        for (int i = 0; i < 64; ++i) XString(multi + i * 4);
                    });
                    break;
                case 13: XString(options); break;
            }
            for (int offset = 308; offset <= 364; offset += 8) ReadExpression(item + offset);
        }
    }
}
