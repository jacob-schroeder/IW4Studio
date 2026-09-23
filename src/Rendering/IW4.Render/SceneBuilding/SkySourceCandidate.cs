using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;

namespace IW4.Render.SceneBuilding;

internal sealed record SkySourceCandidate(
    int? WorldSkyIndex,
    MapRenderSkySource Source,
    IReadOnlyList<int> SkyStartSurfPositions,
    GfxImageAsset Image,
    MaterialSamplerState SamplerState);
