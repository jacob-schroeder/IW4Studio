using IW4.Render.Techniques;

namespace IW4.Render.Materials;

public sealed record MaterialPassIdentity(
    string MaterialName,
    TechniquePassIdentity TechniquePass);
