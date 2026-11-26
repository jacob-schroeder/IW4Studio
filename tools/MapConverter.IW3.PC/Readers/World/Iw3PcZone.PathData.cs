namespace MapConverter.IW3.PC.Readers;

public sealed partial class Iw3PcZone
{
    private sealed partial class Loader
    {
        private void ReadPathData(int path)
        {
            int count = Int(path);
            Data(path + 4, count, 128, visit: node => Data(node + 64, Short(node + 62), 12));
            Data(path + 8, count, 16, 16, block: 1);
            Data(path + 16, count, 2, 2);
            Data(path + 20, count, 2, 2);
            Data(path + 28, Int(path + 24), 1, 1);
            Data(path + 36, Int(path + 32), 16, visit: ReadPathTree);
        }

        private void ReadPathTree(int tree)
        {
            if (Int(tree) < 0) Data(tree + 12, Int(tree + 8), 2, 2);
            else
            {
                Data(tree + 8, 1, 16, visit: ReadPathTree, shared: true);
                Data(tree + 12, 1, 16, visit: ReadPathTree, shared: true);
            }
        }
    }
}
