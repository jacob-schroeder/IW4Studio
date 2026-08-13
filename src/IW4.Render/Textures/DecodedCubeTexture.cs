using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

public sealed record DecodedCubeTexture(
    string Name,
    string Format,
    bool HasTransparency,
    IReadOnlyList<IReadOnlyList<TextureMip>> Faces);
