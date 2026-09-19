using System.Globalization;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Desktop.Workbench.Tools.ZoneDetails;

public sealed class ZoneBlockStreamViewModel
{
    public ZoneBlockStreamViewModel(XFileBlockType type, uint size)
    {
        TypeName = type.ToString();
        SizeText = $"{size.ToString("N0", CultureInfo.InvariantCulture)} bytes";
        HexSizeText = $"0x{size:X8}";
    }

    public string TypeName { get; }

    public string SizeText { get; }

    public string HexSizeText { get; }
}
