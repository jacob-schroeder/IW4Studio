using Avalonia.Input;

namespace Iw4Radiant.Editing;

internal static class WeaponDrag
{
    private static readonly DataFormat<string> Format =
        DataFormat.CreateStringApplicationFormat("com.iw4studio.iw4radiant.weapon");

    internal static DataTransfer Create()
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(Format, GameplayEntityEditing.TurretClassName));
        return transfer;
    }

    internal static bool IsTurret(IDataTransfer transfer) =>
        transfer.TryGetValue(Format) == GameplayEntityEditing.TurretClassName;
}
