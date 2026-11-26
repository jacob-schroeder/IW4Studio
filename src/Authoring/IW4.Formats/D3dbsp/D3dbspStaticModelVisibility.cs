using IW4.Game.Math;

namespace IW4.Formats.D3dbsp;

/// <summary>World-space bounds and per-light caster IDs in authored misc_model order, before PS3 draw splitting.</summary>
public sealed record D3dbspStaticModelVisibility(
    IReadOnlyList<Bounds> PlacementBounds,
    IReadOnlyList<IReadOnlyList<ushort>>? ShadowCastersByLight);
