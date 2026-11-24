using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Codecs.GfxMap;

namespace IW4.Formats.Codecs.D3dbsp;

internal static partial class D3dbspGfxCodec
{
    private static void SetStreamingWeights(
        GfxSurface[] surfaces, GfxSurfaceBounds[] bounds, byte[] positions, byte[] layers,
        int baseLayerSize, IReadOnlyList<ushort> indices,
        Func<string, MaterialAsset?> resolveMaterial, Func<string, GfxImageAsset?> resolveImage)
    {
        var materialMips = new Dictionary<MaterialAsset, (int Width, int Height)[][]>();
        for (int i = 0; i < surfaces.Length; i++)
        {
            GfxSurface surface = surfaces[i];
            MaterialAsset material = surface.Material ??
                throw new InvalidDataException($"Render surface {i} has no material.");
            if (!materialMips.TryGetValue(material, out var mips))
            {
                MaterialAsset definition = material.Info.Name is { } name
                    ? resolveMaterial(name) ?? material : material;
                if (definition.LayerCount > GfxSurfaceBounds.LayerSlotCount ||
                    definition.SubMaterialNames.Count != definition.LayerCount)
                    throw new InvalidDataException($"World material '{definition.Info.Name}' has an invalid streaming layer table.");
                mips = definition.LayerCount == 0
                    ? [GetMipDimensions(definition, resolveImage)]
                    : definition.SubMaterialNames.Select(layer => layer.Value is { Length: > 0 } layerName &&
                          resolveMaterial(layerName) is { } subMaterial
                        ? GetMipDimensions(subMaterial, resolveImage) : []).ToArray();
                materialMips.Add(material, mips);
            }

            // The base BSP stream carries UV0. Extended PS3 rows retain the
            // additional UV pairs declared by the material's world format.
            bool extended = surface.Triangles.VertexLayerData >= baseLayerSize;
            MaterialWorldVertexFormat format = extended
                ? material.TechniqueSet?.WorldVertexFormat ?? throw new InvalidDataException($"Render surface {i} has no PS3 vertex layout.")
                : MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_1_NRM_1;
            int row = WorldVertexLayout.ResolveGenericFallbackBackendRow(format);
            WorldVertexLayout.TryGetStreamStride(row, 1, out byte stride);
            var weights = new byte[GfxSurfaceBounds.LayerSlotCount];
            for (int slot = 0; slot < mips.Length; slot++)
            {
                MaterialStreamSource source = slot switch
                {
                    0 => MaterialStreamSource.TexCoord0,
                    1 or 2 => MaterialStreamSource.TexCoord1,
                    _ => MaterialStreamSource.TexCoord2
                };
                int component = slot is 2 or 4 ? 2 : 0;
                weights[slot] = WorldVertexLayout.TryGetSource(row, source, out var uv) &&
                    uv.StreamIndex == 1 && uv.ComponentCount >= component + 2
                    ? WorldSurfaceStreamingWeight.Calculate(positions, layers, stride,
                        uv.ByteOffset + component * sizeof(float), surface.Triangles, indices, mips[slot])
                    : byte.MaxValue;
            }
            GfxSurfaceBounds original = bounds[i];
            bounds[i] = new GfxSurfaceBounds
            {
                Bounds = original.Bounds, MipRadius = original.MipRadius,
                InvHighMipRadius = weights, TailPadding = original.TailPadding
            };
        }
    }

    private static (int Width, int Height)[] GetMipDimensions(
        MaterialAsset material, Func<string, GfxImageAsset?> resolveImage)
    {
        var streamed = new HashSet<(int, int)>();
        var resident = new HashSet<(int, int)>();
        foreach (MaterialTextureDef texture in material.Textures)
        {
            if (texture.Image is not { } image)
                continue;
            if (image.Name is { } name)
                image = resolveImage(name) ?? image;
            if (image.StreamData.FirstOrDefault() is { Width: > 0, Height: > 0 } mip)
                streamed.Add((mip.Width, mip.Height));
            else if (texture.Semantic == TextureSemantic.ColorMap && image.TextureSemantic == TextureSemantic.ColorMap &&
                image.Category == ImageCategory.LoadFromFile && image.MapType == MapType.TwoDimensional &&
                image.DimensionCount == GfxImageDimension.TwoDimensional && image.PayloadByteCount > 0)
                resident.Add((Math.Max(1, image.BaseWidth >> 2), Math.Max(1, image.BaseHeight >> 2)));
        }
        return (streamed.Count > 0 ? streamed : resident).ToArray();
    }
}
