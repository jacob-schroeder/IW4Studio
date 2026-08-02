using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Fx;

public sealed class FxMaterialVisual : FxElemVisual
{
    public XPointer<MaterialAsset> MaterialPointer { get; init; }
    public MaterialAsset? Material { get; init; }
    /// <summary>
    /// Serialized body consumed by this visual when the source pointer was
    /// inline/insert. This may differ from <see cref="Material"/> after
    /// DB_AddXAsset canonicalizes a duplicate identity.
    /// </summary>
    public MaterialAsset? IncomingMaterial { get; init; }
}
