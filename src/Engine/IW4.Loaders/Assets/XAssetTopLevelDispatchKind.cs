using IW4.Game.Zone;
namespace IW4.Loaders.Assets;

/// <summary>
/// Describes the PS3 XAsset entry dispatch behavior, not semantic or emitter
/// completeness for the asset type.
/// </summary>
public enum XAssetTopLevelDispatchKind
{
    Unsupported,
    PointerWrapper,
    NativeNoOp
}
