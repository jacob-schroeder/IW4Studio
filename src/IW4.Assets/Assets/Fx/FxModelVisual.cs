using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Fx;

public sealed class FxModelVisual : FxElemVisual
{
    public XPointer<XModelAsset> ModelPointer { get; init; }
    public XModelAsset? Model { get; init; }
    /// <summary>
    /// Serialized body consumed by this visual when the source pointer was
    /// inline/insert. This may differ from <see cref="Model"/> after
    /// DB_AddXAsset canonicalizes a duplicate identity.
    /// </summary>
    public XModelAsset? IncomingModel { get; init; }
}
