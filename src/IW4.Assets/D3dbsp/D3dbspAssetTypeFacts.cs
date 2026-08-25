using IW4.FastFiles.Zone;

namespace IW4.Assets.D3dbsp;

/// <summary>
/// Multiplayer IW4 assets compiled together from one version 22 D3DBSP.
/// Every asset in a group uses the same owned <c>.d3dbsp</c> wire name.
/// </summary>
public static class D3dbspAssetTypeFacts
{
    private static readonly IReadOnlyList<XAssetType> MultiplayerTypesValue =
        Array.AsReadOnly(
        [
            XAssetType.ColMapMp,
            XAssetType.ComMap,
            XAssetType.GameMapMp,
            XAssetType.MapEnts,
            XAssetType.FxMap,
            XAssetType.GfxMap
        ]);

    public static IReadOnlyList<XAssetType> MultiplayerTypes =>
        MultiplayerTypesValue;

    public static bool IsMultiplayerType(XAssetType assetType) =>
        assetType is
            XAssetType.ColMapMp or
            XAssetType.ComMap or
            XAssetType.GameMapMp or
            XAssetType.MapEnts or
            XAssetType.FxMap or
            XAssetType.GfxMap;

    public static bool IsD3dbspName(string? name) =>
        name?.Contains(".d3dbsp", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsOwnedD3dbspGroupName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name[0] != ',' &&
        !name.Contains('\0') &&
        IsD3dbspName(name);

    public static bool IsOwnedD3dbspName(string? name) =>
        name is not null &&
        IsOwnedD3dbspGroupName(name) &&
        name.EndsWith(".d3dbsp", StringComparison.OrdinalIgnoreCase);
}
