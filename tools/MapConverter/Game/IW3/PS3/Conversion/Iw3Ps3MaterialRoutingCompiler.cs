using IW4.Game.Assets;
using IW4.Game.Assets.ComWorld;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Formats.SourceFormat.Material;
using IW4.Linker.Contracts;
using MapConverter.Game.IW3.PS3.Rules;

namespace MapConverter.Game.IW3.PS3.Conversion;

internal static class Iw3Ps3MaterialRoutingCompiler
{
    internal static void Apply(List<BaseAsset> definitions, string prefix, string bootstrapDirectory)
    {
        MaterialAsset? grass = FindMaterial("m/mtl_lightdrygrass");
        if (grass is not null)
        {
            byte[] nativePixel = File.ReadAllBytes(Path.Combine(bootstrapDirectory,
                "shader_bin_ps3", "pixel", "vertcol_simple_atest_nc.cg"));
            MaterialTechniqueSetAsset set = TechniqueRules.GrassShadow(prefix, grass, nativePixel);
            MaterialShaderAsset pixel = set.TechniqueSlots[(int)MaterialTechniqueType.BuildShadowmapDepth]
                .Technique?.Passes.Single().PixelShader ??
                throw new InvalidDataException("The grass shadow rule produced no pixel shader.");
            AddDefinition(pixel);
            ReplaceMaterial(grass, set);
        }

        MaterialAsset? canopy = FindMaterial("mc/mtl_red_pine_canopy");
        if (canopy is not null)
        {
            var world = definitions.OfType<GfxWorldAsset>().Single();
            var lights = definitions.OfType<ComWorldAsset>().Single();
            int sunIndex = world.SunPrimaryLightIndex;
            if (lights.PrimaryLightCount != lights.PrimaryLights.Count ||
                (uint)sunIndex >= (uint)lights.PrimaryLights.Count ||
                lights.PrimaryLights[sunIndex].Type != GfxLightType.Directional)
                throw new InvalidDataException("The canopy lighting rule requires the world's directional primary sun light.");
            ReplaceMaterial(canopy, TechniqueRules.CanopySun(prefix, canopy, lights.PrimaryLights[sunIndex].Color));
        }

        MaterialSourceCompiler? nativeRouting = null;
        HashSet<AssetKey> addedRoutingDefinitions = [];
        foreach (MaterialAsset material in definitions.OfType<MaterialAsset>()
                     .DistinctBy(AssetKey.FromDefinition).ToArray())
        {
            MaterialAsset current = material;
            string? localDonorName = TechniqueRules.NativeLocalLightDonor(prefix, current);
            if (localDonorName is not null)
            {
                nativeRouting ??= new MaterialSourceCompiler(bootstrapDirectory);
                MaterialTechniqueSetAsset donor = nativeRouting.LoadTechniqueSet(localDonorName);
                var converted = TechniqueRules.NativeLocalLight(prefix, current, donor,
                    nativeRouting.Assets.OfType<MaterialShaderAsset>().ToArray());
                foreach (BaseAsset definition in converted.Item3.Cast<BaseAsset>().Append(converted.Item1))
                    if (addedRoutingDefinitions.Add(AssetKey.FromDefinition(definition)))
                        AddDefinition(definition);
                current = MaterialRules.WithTechniqueSetAndStates(current, converted.Item1, converted.Item2);
            }

            string? sunDonorName = TechniqueRules.NativeSunShadowDonor(prefix, current);
            if (sunDonorName is not null)
            {
                nativeRouting ??= new MaterialSourceCompiler(bootstrapDirectory);
                MaterialTechniqueSetAsset donor = nativeRouting.LoadTechniqueSet(sunDonorName);
                var converted = TechniqueRules.NativeSunShadow(prefix, current, donor,
                    nativeRouting.Assets.OfType<MaterialShaderAsset>().ToArray());
                foreach (BaseAsset definition in converted.Item4.Cast<BaseAsset>().Append(converted.Item1))
                    if (addedRoutingDefinitions.Add(AssetKey.FromDefinition(definition)))
                        AddDefinition(definition);
                current = MaterialRules.WithTechniqueSetStateEntriesAndStates(
                    current, converted.Item1, converted.Item2, converted.Item3);
            }

            if (!ReferenceEquals(current, material))
                ReplaceDefinition(material, current);
        }

        MaterialAsset? FindMaterial(string name) => definitions.OfType<MaterialAsset>()
            .DistinctBy(AssetKey.FromDefinition).SingleOrDefault(material => material.Info.Name == prefix + name);

        void AddDefinition(BaseAsset asset)
        {
            AssetKey key = AssetKey.FromDefinition(asset);
            if (definitions.Any(existing => AssetKey.FromDefinition(existing) == key))
                throw new InvalidDataException($"Converted material routing conflicts with existing asset {key}.");
            definitions.Add(asset);
        }

        void ReplaceMaterial(MaterialAsset source, MaterialTechniqueSetAsset set)
        {
            AddDefinition(set);
            ReplaceDefinition(source, MaterialRules.WithTechniqueSet(source, set));
        }

        void ReplaceDefinition(MaterialAsset source, MaterialAsset replacement)
        {
            AssetKey key = AssetKey.FromDefinition(source);
            // Linker asset dependencies resolve by key. Replace the provider for
            // every reference while leaving the original shared technique set intact.
            for (int index = 0; index < definitions.Count; index++)
                if (AssetKey.FromDefinition(definitions[index]) == key)
                    definitions[index] = replacement;
        }
    }
}
