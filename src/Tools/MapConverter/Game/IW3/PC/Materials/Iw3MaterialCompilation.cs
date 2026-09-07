using IW4.Assets.Assets.Material;
using MapConverter.Game.IW3.PC.Images;

namespace MapConverter.Game.IW3.PC.Materials;

internal sealed record Iw3MaterialCompilation(
    MaterialAsset Material,
    string SourceTechniqueSetName);

internal sealed record Iw3MaterialSourceInspection(
    string SourceTechniqueSetName,
    IReadOnlyList<Iw3IwdImageRequest> ImageRequirements,
    IReadOnlyList<Iw3WaterImageRequest> WaterImageRequirements,
    IReadOnlyList<MaterialSamplerState> TextureSamplerStates,
    MaterialGameFlags GameFlags,
    byte TextureAtlasRowCount,
    byte TextureAtlasColumnCount);

internal sealed record Iw3WaterImageRequest(
    string ImageName,
    int Width,
    int Height);
