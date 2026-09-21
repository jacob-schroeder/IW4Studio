using Avalonia.Input;

namespace Iw4Radiant.Materials;

internal static class XModelDrag
{
    private static readonly DataFormat<string> Format =
        DataFormat.CreateStringApplicationFormat("com.iw4studio.iw4radiant.xmodel");

    internal static DataTransfer Create(XModelSource model, bool alignToSurface)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(Format, $"{(alignToSurface ? '1' : '0')}\n{model.Name}"));
        return transfer;
    }

    internal static bool TryRead(IDataTransfer transfer, out string name, out bool alignToSurface)
    {
        string? value = transfer.TryGetValue(Format);
        alignToSurface = value is { Length: > 2 } && value[0] == '1' && value[1] == '\n';
        name = value is { Length: > 2 } && value[1] == '\n' ? value[2..] : "";
        return name.Length > 0;
    }
}
