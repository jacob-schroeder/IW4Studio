using IW4.Assets.Zone;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Zone;
using IW4.Render.Textures;
using IW4.Render.Shaders;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.Lifecycle.State;

namespace IW4.Render.Assets;

/// <summary>
/// Typed bridge from a runtime {kind, ordinal} destination to the current
/// runtime descriptor and its canonical image source.
/// </summary>
public sealed class MapRenderWorldTextureAssetBinding
{
    internal MapRenderWorldTextureAssetBinding(
        MapRenderWorldRuntimeTextureIdentity identity,
        MapRenderWorldTextureBindingStatus status,
        GfxTexture? descriptor = null,
        GfxWorldTextureSourceKind? sourceKind = null,
        XAssetPoolAddress? sourceImageAddress = null,
        GfxImageAsset? sourceImage = null,
        GfxImageAsset? descriptorImage = null,
        XAssetActiveProviderSnapshot? sourceImageProvider = null,
        MapRenderSelectedPassSamplerShape? shape = null,
        MapRenderSamplerState? decodedSamplerState = null,
        MapRenderDecodedTextureResourceSnapshot? resource = null,
        MapRenderWorldTextureResourceStatus resourceStatus =
            MapRenderWorldTextureResourceStatus.Unavailable)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (sourceKind is { } typedSourceKind && !Enum.IsDefined(typedSourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (sourceImageAddress is { AssetType: not XAssetType.Image })
            throw new ArgumentException("World texture provenance must reference an Image slot.", nameof(sourceImageAddress));
        if (!Enum.IsDefined(resourceStatus))
            throw new ArgumentOutOfRangeException(nameof(resourceStatus));
        if (shape is { } typedShape && !Enum.IsDefined(typedShape))
            throw new ArgumentOutOfRangeException(nameof(shape));
        bool hasCompleteRuntimeRow =
            descriptor is not null && sourceKind is not null && sourceImageAddress is not null;
        bool hasPartialRuntimeRow =
            descriptor is not null || sourceKind is not null || sourceImageAddress is not null;
        if (hasPartialRuntimeRow && !hasCompleteRuntimeRow)
            throw new ArgumentException("Runtime descriptor provenance must be complete.", nameof(descriptor));
        if (status == MapRenderWorldTextureBindingStatus.Ready &&
            (!hasCompleteRuntimeRow || sourceImage is null ||
             descriptorImage is null))
        {
            throw new ArgumentException(
                "A ready world texture binding requires descriptor, source, and decode view.",
                nameof(status));
        }
        MapRenderSelectedPassSamplerShape expectedShape = identity.Kind ==
            MapRenderWorldRuntimeTextureKind.ReflectionProbe
                ? MapRenderSelectedPassSamplerShape.Cube
                : MapRenderSelectedPassSamplerShape.TwoDimensional;
        MapRenderSamplerState expectedSamplerState =
            MapRenderWorldImplicitSamplerStateFactory.Create(identity.Kind);
        if (resourceStatus == MapRenderWorldTextureResourceStatus.Ready &&
            (sourceImageProvider is null || shape is null ||
             decodedSamplerState is null || resource is null ||
             sourceImageProvider.SlotAddress != sourceImageAddress ||
             sourceImageProvider.PoolRevision < 0 ||
             sourceImageProvider.IsReferencePlaceholder ||
             !sourceImageProvider.IsActiveCanonicalProvider ||
             sourceImageProvider.RuntimeAddress != sourceImage!.RuntimeAddress ||
             resource!.Shape != shape || shape != expectedShape ||
             decodedSamplerState != expectedSamplerState))
        {
            throw new ArgumentException(
                "A ready world binding requires one exact canonical image provider and decoded shape.",
                nameof(sourceImageProvider));
        }
        if (status == MapRenderWorldTextureBindingStatus.SourceImageUnavailable &&
            (!hasCompleteRuntimeRow || sourceImage is not null || descriptorImage is not null ||
             sourceImageProvider is not null || resource is not null ||
             decodedSamplerState is not null || shape is not null))
        {
            throw new ArgumentException(
                "A source-image failure must retain exactly one complete runtime descriptor row.",
                nameof(status));
        }
        bool isCapturedSourceFailure = resourceStatus is
            MapRenderWorldTextureResourceStatus.SourceProviderUnavailable or
            MapRenderWorldTextureResourceStatus.SamplerShapeUnavailable or
            MapRenderWorldTextureResourceStatus.ImageDecodeFailed;
        if (isCapturedSourceFailure &&
            (!hasCompleteRuntimeRow || sourceImage is null ||
             descriptorImage is null || resource is not null))
        {
            throw new ArgumentException(
                "A captured world-image failure must retain its runtime row and source views without decoded content.",
                nameof(status));
        }
        if (resourceStatus == MapRenderWorldTextureResourceStatus.SourceProviderUnavailable &&
            (sourceImageProvider is not null || shape is not null ||
             decodedSamplerState is not null))
        {
            throw new ArgumentException(
                "A provider failure cannot retain later shape or sampler state.",
                nameof(status));
        }
        if (resourceStatus == MapRenderWorldTextureResourceStatus.SamplerShapeUnavailable &&
            (sourceImageProvider is null || shape is not null ||
             decodedSamplerState is not null))
        {
            throw new ArgumentException(
                "A shape failure must retain only the canonical provider prefix.",
                nameof(status));
        }
        if (resourceStatus == MapRenderWorldTextureResourceStatus.ImageDecodeFailed &&
            (sourceImageProvider is null || shape is null ||
             decodedSamplerState is null))
        {
            throw new ArgumentException(
                "A decode failure must retain canonical provider, shape, and sampler state.",
                nameof(status));
        }
        if (status is not (
                MapRenderWorldTextureBindingStatus.Ready or
                MapRenderWorldTextureBindingStatus.SourceImageUnavailable) &&
            (hasPartialRuntimeRow || sourceImage is not null || descriptorImage is not null ||
             sourceImageProvider is not null || resource is not null ||
             decodedSamplerState is not null || shape is not null))
        {
            throw new ArgumentException(
                "A binding without a runtime row cannot retain descriptor or image state.",
                nameof(status));
        }

        Identity = identity;
        Status = status;
        Descriptor = descriptor;
        SourceKind = sourceKind;
        SourceImageAddress = sourceImageAddress;
        SourceImage = sourceImage;
        DescriptorImage = descriptorImage;
        SourceImageProvider = sourceImageProvider;
        Shape = shape;
        DecodedSamplerState = decodedSamplerState is null
            ? null
            : decodedSamplerState with { };
        Resource = resource;
        ResourceStatus = resourceStatus;
    }

    public MapRenderWorldRuntimeTextureIdentity Identity { get; }

    public MapRenderWorldTextureBindingStatus Status { get; }

    public GfxTexture? Descriptor { get; }

    public GfxWorldTextureSourceKind? SourceKind { get; }

    public XAssetPoolAddress? SourceImageAddress { get; }

    public GfxImageAsset? SourceImage { get; }

    public GfxImageAsset? DescriptorImage { get; }

    public XAssetActiveProviderSnapshot? SourceImageProvider { get; }

    public MapRenderSelectedPassSamplerShape? Shape { get; }

    public MapRenderSamplerState? DecodedSamplerState { get; }

    public MapRenderDecodedTextureResourceSnapshot? Resource { get; }

    public MapRenderWorldTextureResourceStatus ResourceStatus { get; }

    public long? AssetPoolRevision => SourceImageProvider?.PoolRevision;

    /// <summary>
    /// The runtime path can materialize its native RSX descriptor without a
    /// semantic source-image projection. Image availability remains a separate
    /// requirement for a managed render-resource snapshot.
    /// </summary>
    public bool IsDescriptorReady => Descriptor is not null;

    public bool IsReady => Status == MapRenderWorldTextureBindingStatus.Ready;

    public bool IsRenderResourceReady =>
        ResourceStatus == MapRenderWorldTextureResourceStatus.Ready;
}
