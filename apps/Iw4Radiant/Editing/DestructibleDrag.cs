using Avalonia.Input;

namespace Iw4Radiant.Editing;

internal static class DestructibleDrag
{
    private static readonly DataFormat<string> Format =
        DataFormat.CreateStringApplicationFormat("com.iw4studio.iw4radiant.destructible");

    internal static DataTransfer Create(DestructiblePreset preset)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(Format, preset.ModelName));
        return transfer;
    }

    internal static DestructiblePreset? Read(IDataTransfer transfer)
    {
        string? modelName = transfer.TryGetValue(Format);
        return DestructiblePresets.All.FirstOrDefault(preset => preset.ModelName == modelName);
    }
}
