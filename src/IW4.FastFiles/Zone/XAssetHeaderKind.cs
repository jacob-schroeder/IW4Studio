namespace IW4.FastFiles.Zone;

/// <summary>
/// Describes whether the second word of a serialized XAsset row participates
/// in a pointer-wrapper path for that type.
/// </summary>
public enum XAssetHeaderKind
{
    Pointer,
    Opaque
}
