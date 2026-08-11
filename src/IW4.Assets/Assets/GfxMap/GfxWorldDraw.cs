using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxWorldDraw
{
    public const int SerializedSize = 0x54;

    private IReadOnlyList<GfxTexture> _reflectionProbeTextures = [];
    private IReadOnlyList<GfxTexture> _lightmapPrimaryTextures = [];
    private IReadOnlyList<GfxTexture> _lightmapSecondaryTextures = [];

    // GfxWorld staging root + 0x50. Runtime identity remains the GfxMap
    // XAssetPool slot rather than this TEMP address.
    public XBlockAddress? StagingAddress { get; init; }

    public uint ReflectionProbeCount { get; init; }
    public XPointer<GfxImageAsset[]> ReflectionProbeImagesPointer { get; init; }
    // Serialized pointer cells parallel to ReflectionProbeImages.
    public IReadOnlyList<XPointer<GfxImageAsset>> ReflectionProbeImagePointers { get; init; } = [];
    public IReadOnlyList<GfxImageAsset?> ReflectionProbeImages { get; init; } = [];
    public XPointer<GfxReflectionProbe[]> ReflectionProbeOriginsPointer { get; init; }
    public IReadOnlyList<GfxReflectionProbe> ReflectionProbeOrigins { get; init; } = [];
    public XPointer<GfxTexture[]> ReflectionProbeTexturesPointer { get; init; }
    public XBlockAddress? ReflectionProbeTexturesAddress { get; init; }
    // Runtime row i is initialized from ReflectionProbeImages[i] bytes
    // 0x00..0x17 and retains no GfxImage pointer.
    public IReadOnlyList<GfxTexture> ReflectionProbeTextures
    {
        get => _reflectionProbeTextures;
        init => _reflectionProbeTextures = Snapshot(value);
    }
    public int LightmapCount { get; init; }
    public XPointer<GfxLightmapArray[]> LightmapsPointer { get; init; }
    public IReadOnlyList<GfxLightmapArray> Lightmaps { get; init; } = [];
    public XPointer<GfxTexture[]> LightmapPrimaryTexturesPointer { get; init; }
    public XBlockAddress? LightmapPrimaryTexturesAddress { get; init; }
    // Normal-path row i comes from Lightmaps[i].Primary and may be replaced
    // from descriptor-only override storage.
    public IReadOnlyList<GfxTexture> LightmapPrimaryTextures
    {
        get => _lightmapPrimaryTextures;
        init => _lightmapPrimaryTextures = Snapshot(value);
    }
    public XPointer<GfxTexture[]> LightmapSecondaryTexturesPointer { get; init; }
    public XBlockAddress? LightmapSecondaryTexturesAddress { get; init; }
    // Normal-path row i comes from Lightmaps[i].Secondary and may be replaced
    // from descriptor-only override storage.
    public IReadOnlyList<GfxTexture> LightmapSecondaryTextures
    {
        get => _lightmapSecondaryTextures;
        init => _lightmapSecondaryTextures = Snapshot(value);
    }
    // +0x20/+0x24: serialized GfxImagePtr values. Runtime world loading zeros
    // both cells before reusing them as non-owning cached override identities.
    public XPointer<GfxImageAsset> LightmapOverridePrimaryPointer { get; init; }
    public GfxImageAsset? LightmapOverridePrimary { get; init; }
    public XPointer<GfxImageAsset> LightmapOverrideSecondaryPointer { get; init; }
    public GfxImageAsset? LightmapOverrideSecondary { get; init; }
    public uint VertexCount { get; init; }
    public GfxWorldVertexData VertexData { get; init; } = new();
    public uint VertexLayerDataSize { get; init; }
    public GfxWorldVertexLayerData VertexLayerData { get; init; } = new();
    public int IndexCount { get; init; }
    public XPointer<ushort[]> IndicesPointer { get; init; }
    public IReadOnlyList<ushort> Indices { get; init; } = [];
    public int IndexBufferRaw { get; init; }

    internal void ApplyRuntimeTextures(
        IReadOnlyList<GfxTexture> reflectionProbeTextures,
        IReadOnlyList<GfxTexture> lightmapPrimaryTextures,
        IReadOnlyList<GfxTexture> lightmapSecondaryTextures)
    {
        _reflectionProbeTextures = Snapshot(reflectionProbeTextures);
        _lightmapPrimaryTextures = Snapshot(lightmapPrimaryTextures);
        _lightmapSecondaryTextures = Snapshot(lightmapSecondaryTextures);
    }

    private static IReadOnlyList<GfxTexture> Snapshot(
        IReadOnlyList<GfxTexture>? textures)
    {
        ArgumentNullException.ThrowIfNull(textures);
        GfxTexture[] snapshot = textures.ToArray();
        if (snapshot.Any(texture => texture is null))
            throw new ArgumentException("Runtime GfxTexture collections cannot contain null rows.", nameof(textures));

        return Array.AsReadOnly(snapshot);
    }
}
