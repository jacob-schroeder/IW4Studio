using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

public sealed record MapRenderDecodedCubeTexture(
    string Name,
    string Format,
    bool HasTransparency,
    IReadOnlyList<IReadOnlyList<MapRenderTextureMip>> Faces);
