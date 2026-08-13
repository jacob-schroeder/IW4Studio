using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.Lifecycle.State;
using IW4.Render.Shaders;
using IW4.Render.Textures;

namespace IW4.Render.Assets;

public sealed partial class RenderAssetLookup
{
    public MapRenderWorldTextureAssetBinding ResolveWorldRuntimeTexture(
        GfxWorldAsset world,
        MapRenderWorldRuntimeTextureIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (_assetPool is null || _gfxWorldRuntimeState?.TextureState is not { } textureState)
        {
            return new MapRenderWorldTextureAssetBinding(
                identity,
                MapRenderWorldTextureBindingStatus.RuntimeStateUnavailable);
        }

        return ResolveWorldRuntimeTexture(
            world,
            identity,
            textureState,
            _assetPool.Revision);
    }

    public MapRenderWorldTextureBindingSnapshot CaptureWorldRuntimeTextureBindings(
        GfxWorldAsset world,
        GfxWorldTextureState textureState)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(textureState);
        if (_assetPool is null)
        {
            throw new InvalidOperationException(
                "A runtime texture binding snapshot requires the canonical XAssetPool.");
        }
        if (world.RuntimeAddress?.AssetPoolAddress is not { } worldAddress ||
            worldAddress != textureState.WorldAddress)
        {
            throw new InvalidOperationException(
                "The requested GfxWorld and texture-state revision do not share a canonical slot.");
        }

        long poolRevision = _assetPool.Revision;
        var bindings = new List<MapRenderWorldTextureAssetBinding>();
        CaptureBindings(
            world,
            textureState,
            MapRenderWorldRuntimeTextureKind.ReflectionProbe,
            GfxWorldTextureKind.ReflectionProbe,
            poolRevision,
            bindings);
        CaptureBindings(
            world,
            textureState,
            MapRenderWorldRuntimeTextureKind.SecondaryLightmap,
            GfxWorldTextureKind.SecondaryLightmap,
            poolRevision,
            bindings);
        CaptureBindings(
            world,
            textureState,
            MapRenderWorldRuntimeTextureKind.PrimaryLightmap,
            GfxWorldTextureKind.PrimaryLightmap,
            poolRevision,
            bindings);
        if (_assetPool.Revision != poolRevision)
        {
            throw new InvalidOperationException(
                $"The canonical provider revision changed during world texture capture: start={poolRevision};end={_assetPool.Revision}.");
        }
        return new MapRenderWorldTextureBindingSnapshot(
            worldAddress,
            textureState.Revision,
            poolRevision,
            bindings);
    }

    private void CaptureBindings(
        GfxWorldAsset world,
        GfxWorldTextureState textureState,
        MapRenderWorldRuntimeTextureKind renderKind,
        GfxWorldTextureKind runtimeKind,
        long poolRevision,
        ICollection<MapRenderWorldTextureAssetBinding> bindings)
    {
        IReadOnlyList<GfxWorldTextureRowState> rows = textureState.GetRows(runtimeKind);
        if (rows.Count > byte.MaxValue + 1)
        {
            throw new InvalidDataException(
                $"{renderKind} runtime texture rows exceed the byte-ordinal range.");
        }

        for (int ordinal = 0; ordinal < rows.Count; ordinal++)
        {
            var identity = new MapRenderWorldRuntimeTextureIdentity(
                renderKind,
                checked((byte)ordinal));
            bindings.Add(ResolveWorldRuntimeTexture(
                world,
                identity,
                textureState,
                poolRevision));
        }
    }

    private MapRenderWorldTextureAssetBinding ResolveWorldRuntimeTexture(
        GfxWorldAsset world,
        MapRenderWorldRuntimeTextureIdentity identity,
        GfxWorldTextureState textureState,
        long poolRevision)
    {
        if (_assetPool is null)
        {
            return new MapRenderWorldTextureAssetBinding(
                identity,
                MapRenderWorldTextureBindingStatus.RuntimeStateUnavailable);
        }
        if (world.RuntimeAddress?.AssetPoolAddress is not { } worldAddress ||
            worldAddress != textureState.WorldAddress)
        {
            return new MapRenderWorldTextureAssetBinding(
                identity,
                MapRenderWorldTextureBindingStatus.WorldIdentityMismatch);
        }

        GfxWorldTextureKind runtimeKind = identity.Kind switch
        {
            MapRenderWorldRuntimeTextureKind.ReflectionProbe =>
                GfxWorldTextureKind.ReflectionProbe,
            MapRenderWorldRuntimeTextureKind.SecondaryLightmap =>
                GfxWorldTextureKind.SecondaryLightmap,
            MapRenderWorldRuntimeTextureKind.PrimaryLightmap =>
                GfxWorldTextureKind.PrimaryLightmap,
            _ => throw new ArgumentOutOfRangeException(nameof(identity))
        };
        if (!textureState.TryGetRow(runtimeKind, identity.Ordinal, out GfxWorldTextureRowState? row) ||
            row is null)
        {
            return new MapRenderWorldTextureAssetBinding(
                identity,
                MapRenderWorldTextureBindingStatus.SlotOutOfRange);
        }
        if (!_assetPool.TryResolve<GfxImageAsset>(
                row.SourceImageAddress.RawValue,
                XAssetType.Image,
                out GfxImageAsset? image) ||
            image is null)
        {
            return new MapRenderWorldTextureAssetBinding(
                identity,
                MapRenderWorldTextureBindingStatus.SourceImageUnavailable,
                row.Descriptor,
                row.SourceKind,
                row.SourceImageAddress);
        }

        GfxImageAsset descriptorImage =
            MapRenderWorldTextureImageProjection.Create(image, row.Descriptor);

        if (!MapRenderAssetProviderSnapshotFactory.TryCapture(
                _assetPool,
                image,
                XAssetType.Image,
                poolRevision,
                out GfxImageAsset? canonicalImage,
                out XAssetActiveProviderSnapshot? imageProvider) ||
            !ReferenceEquals(canonicalImage, image))
        {
            return new MapRenderWorldTextureAssetBinding(
                identity,
                MapRenderWorldTextureBindingStatus.Ready,
                row.Descriptor,
                row.SourceKind,
                row.SourceImageAddress,
                image,
                descriptorImage,
                resourceStatus:
                    MapRenderWorldTextureResourceStatus.SourceProviderUnavailable);
        }

        TextureSamplerShape expectedShape = identity.Kind ==
            MapRenderWorldRuntimeTextureKind.ReflectionProbe
                ? TextureSamplerShape.Cube
                : TextureSamplerShape.TwoDimensional;
        TextureSamplerShape classifiedShape =
            TextureSamplerShapeClassifier.ClassifyMaterialImage(descriptorImage);
        if (classifiedShape == TextureSamplerShape.Unknown ||
            classifiedShape != expectedShape)
        {
            return new MapRenderWorldTextureAssetBinding(
                identity,
                MapRenderWorldTextureBindingStatus.Ready,
                row.Descriptor,
                row.SourceKind,
                row.SourceImageAddress,
                image,
                descriptorImage,
                sourceImageProvider: imageProvider,
                resourceStatus:
                    MapRenderWorldTextureResourceStatus.SamplerShapeUnavailable);
        }

        RsxSamplerState samplerState =
            MapRenderWorldImplicitSamplerStateFactory.Create(identity.Kind);
        if (!DecodedTextureResourceSnapshotFactory.TryDecode(
                descriptorImage,
                expectedShape,
                _imageStreams,
                out DecodedTextureResourceSnapshot? resource,
                out _))
        {
            return new MapRenderWorldTextureAssetBinding(
                identity,
                MapRenderWorldTextureBindingStatus.Ready,
                row.Descriptor,
                row.SourceKind,
                row.SourceImageAddress,
                image,
                descriptorImage,
                imageProvider,
                expectedShape,
                samplerState,
                resourceStatus:
                    MapRenderWorldTextureResourceStatus.ImageDecodeFailed);
        }

        return new MapRenderWorldTextureAssetBinding(
            identity,
            MapRenderWorldTextureBindingStatus.Ready,
            row.Descriptor,
            row.SourceKind,
            row.SourceImageAddress,
            image,
            descriptorImage,
            imageProvider,
            expectedShape,
            samplerState,
            resource,
            MapRenderWorldTextureResourceStatus.Ready);
    }
}
