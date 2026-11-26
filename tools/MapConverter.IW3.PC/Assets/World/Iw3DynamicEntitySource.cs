namespace MapConverter.IW3.PC.Assets.World;

internal sealed record Iw3DynamicEntitySource(
    int Type,
    IReadOnlyList<float> Quat,
    IReadOnlyList<float> Origin,
    string? XModelName,
    ushort BrushModel,
    ushort PhysicsBrushModel,
    string? DestroyFxName,
    string? DestroyPiecesName,
    string? PhysPresetName,
    int Health,
    IReadOnlyList<float> CenterOfMass,
    IReadOnlyList<float> MomentsOfInertia,
    IReadOnlyList<float> ProductsOfInertia,
    int Contents);

internal sealed record Iw3DynamicEntitySourceData(
    string MapName,
    IReadOnlyList<IReadOnlyList<Iw3DynamicEntitySource>> Definitions)
{
    internal int Count => Definitions.Sum(list => list.Count);
}
