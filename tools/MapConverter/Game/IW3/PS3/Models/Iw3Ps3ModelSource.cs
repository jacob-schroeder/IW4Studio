namespace MapConverter.Game.IW3.PS3.Extraction;

internal sealed record Iw3Ps3ModelSource(
    string Name,
    ReadOnlyMemory<byte> Root,
    ReadOnlyMemory<byte> BoneNames,
    IReadOnlyList<string?> BoneNameValues,
    ReadOnlyMemory<byte> Parents,
    ReadOnlyMemory<byte> Quats,
    ReadOnlyMemory<byte> Translations,
    ReadOnlyMemory<byte> Classification,
    ReadOnlyMemory<byte> BaseMatrices,
    IReadOnlyList<Iw3Ps3ModelSurfaceSource> Surfaces,
    IReadOnlyList<string?> MaterialNames,
    ReadOnlyMemory<byte> Collision,
    ReadOnlyMemory<byte> BoneInfo,
    string? PresetName,
    bool HasPhysCollmap);

internal sealed record Iw3Ps3ModelSurfaceSource(
    ReadOnlyMemory<byte> Root,
    ReadOnlyMemory<byte> Blend,
    ReadOnlyMemory<byte> Verts0,
    ReadOnlyMemory<byte> Verts1,
    IReadOnlyList<Iw3Ps3ModelRigidSource> Rigids,
    ReadOnlyMemory<byte> Triangles,
    bool Verts1Physical);

internal sealed record Iw3Ps3ModelRigidSource(
    ReadOnlyMemory<byte> Root,
    Iw3Ps3ModelTreeSource? Tree);

internal sealed record Iw3Ps3ModelTreeSource(
    ReadOnlyMemory<byte> Root,
    ReadOnlyMemory<byte> Nodes,
    ReadOnlyMemory<byte> Leaves);
