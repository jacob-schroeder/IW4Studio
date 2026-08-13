using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;

namespace IW4.Render.SceneBuilding;

internal sealed record SkySourceCandidate(
    int? WorldSkyIndex,
    MapRenderSkySource Source,
    IReadOnlyList<int> SkyStartSurfPositions,
    GfxImageAsset Image,
    MaterialSamplerState SamplerState);
