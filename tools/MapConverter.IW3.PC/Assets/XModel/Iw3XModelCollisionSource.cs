namespace MapConverter.IW3.PC.Assets.XModel;

internal sealed record Iw3XModelCollisionSurfaceSource(
    IReadOnlyList<float> Mins,
    IReadOnlyList<float> Maxs,
    int BoneIndex,
    int Contents,
    int SurfaceFlags);

internal sealed record Iw3XModelCollisionSource(
    string Name,
    int Contents,
    int CollisionLod,
    IReadOnlyList<Iw3XModelCollisionSurfaceSource> Surfaces);

internal sealed record Iw3XModelCollisionSourceData(
    string MapName,
    IReadOnlyDictionary<string, Iw3XModelCollisionSource> ByName);
