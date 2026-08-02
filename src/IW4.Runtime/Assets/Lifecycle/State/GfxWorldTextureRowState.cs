using IW4.Assets.Zone;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets.Lifecycle.State;

public sealed class GfxWorldTextureRowState
{
    public GfxWorldTextureRowState(
        GfxWorldTextureKind kind,
        int ordinal,
        GfxTexture descriptor,
        GfxWorldTextureSourceKind sourceKind,
        XAssetPoolAddress sourceImageAddress)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (!Enum.IsDefined(sourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (sourceImageAddress.AssetType != XAssetType.Image)
        {
            throw new ArgumentException(
                "GfxWorld texture-row source must identify a canonical Image slot.",
                nameof(sourceImageAddress));
        }

        Kind = kind;
        Ordinal = ordinal;
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        SourceKind = sourceKind;
        SourceImageAddress = sourceImageAddress;
    }

    public GfxWorldTextureKind Kind { get; }

    public int Ordinal { get; }

    public GfxTexture Descriptor { get; }

    public GfxWorldTextureSourceKind SourceKind { get; }

    public XAssetPoolAddress SourceImageAddress { get; }
}
