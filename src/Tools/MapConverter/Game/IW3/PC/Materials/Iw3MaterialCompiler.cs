using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using MapConverter.Game.IW3.PC.Images;
using MapConverter.Game.IW3.PC.Techniques;

namespace MapConverter.Game.IW3.PC.Materials;

/// <summary>
/// Converts the OpenAssetTools IW3 material-v1 representation into the
/// authored PS3 IW4 material representation consumed by IW4.Linker.
/// </summary>
internal static class Iw3MaterialCompiler
{
    internal static MaterialAsset CompileWorldMaterial(
        string targetName,
        MaterialAsset template,
        IReadOnlyDictionary<TextureSemantic, GfxImageAsset> replacementImages,
        GfxStateBits? sourceWorldState,
        MaterialAsset? sourceWaterMaterial) =>
        CompileNativeMaterial(targetName, template, replacementImages,
            sourceWorldState, sourceWaterMaterial, sourceModelInspection: null, isModel: false);

    internal static MaterialAsset CompileModelMaterial(
        string targetName,
        MaterialAsset template,
        IReadOnlyDictionary<TextureSemantic, GfxImageAsset> replacementImages,
        GfxStateBits? sourceState,
        Iw3MaterialSourceInspection? sourceInspection) =>
        CompileNativeMaterial(targetName, template, replacementImages,
            sourceState, sourceWaterMaterial: null, sourceModelInspection: sourceInspection, isModel: true);

    private static MaterialAsset CompileNativeMaterial(
        string targetName,
        MaterialAsset template,
        IReadOnlyDictionary<TextureSemantic, GfxImageAsset> replacementImages,
        GfxStateBits? sourceState,
        MaterialAsset? sourceWaterMaterial,
        Iw3MaterialSourceInspection? sourceModelInspection,
        bool isModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(replacementImages);
        string materialKind = isModel ? "model" : "world";
        if (targetName[0] == ',' || targetName.Contains('\0'))
            throw new ArgumentException($"A {materialKind} material requires an owned target name.", nameof(targetName));
        MaterialTechniqueSetAsset techniqueSet = (isModel
            ? GetModelTechniqueSet(template)
            : GetWorldTechniqueSet(template)) ??
            throw new InvalidDataException(
                isModel
                    ? "The model material template requires an owned native model technique set with its native format, complete constants and a valid main state."
                    : "The world material template requires an owned native lit, emissive, or unlit definition with the single-UV world vertex format and a valid main state.");
        if (!HasCompleteWorldTextures(template))
        {
            throw new InvalidDataException(
                $"The {materialKind} material template requires complete native image or water bindings.");
        }
        foreach ((TextureSemantic semantic, GfxImageAsset image) in replacementImages)
        {
            if (semantic is not (TextureSemantic.ColorMap or
                TextureSemantic.NormalMap or TextureSemantic.SpecularMap))
            {
                throw new InvalidDataException(
                    $"The {materialKind} material replacement semantic '{semantic}' is not a color, normal, or specular image.");
            }
            if (template.Textures.Count(texture => texture.Semantic == semantic) != 1)
            {
                throw new InvalidDataException(
                    $"The {materialKind} material template requires one unambiguous '{semantic}' binding for its replacement image.");
            }
            if (image is null || string.IsNullOrWhiteSpace(image.Name) ||
                image.Name[0] == ',' || image.Name.Contains('\0'))
            {
                throw new InvalidDataException(
                    $"A replacement {materialKind} '{semantic}' image must be an owned definition.");
            }
        }

        uint[][] stateWords = template.StateBits.Select(state => state.LoadBits.ToArray()).ToArray();
        MaterialStateFlags stateFlags = template.StateFlags;
        string? family = techniqueSet.Name is string techniqueName
            ? (isModel ? GetModelTechniqueFamily(techniqueName) : GetWorldTechniqueFamily(techniqueName))
            : null;
        MaterialWater? sourceWater = null;
        if (family == "water" || sourceWaterMaterial is not null)
        {
            if (family != "water" || sourceWaterMaterial is null || sourceState is null ||
                !HasCompleteWorldWaterContract(template) ||
                sourceWaterMaterial.Info.Name != targetName ||
                !HasCompleteWorldTextures(sourceWaterMaterial) ||
                sourceWaterMaterial.Textures.Count != template.Textures.Count ||
                template.Textures.Any(texture => sourceWaterMaterial.Textures.Count(sourceTexture =>
                    sourceTexture.Semantic == texture.Semantic && sourceTexture.NameHash == texture.NameHash) != 1) ||
                sourceWaterMaterial.Textures.Count(texture => texture.Semantic == TextureSemantic.WaterMap) != 1)
            {
                throw new InvalidDataException(
                    "A native world water material requires one complete source-owned simulation and compatible native bindings.");
            }
            MaterialTextureDef sourceTexture = sourceWaterMaterial.Textures.Single(
                texture => texture.Semantic == TextureSemantic.WaterMap);
            MaterialTextureDef nativeTexture = template.Textures.Single(
                texture => texture.Semantic == TextureSemantic.WaterMap);
            if (sourceTexture.NameHash != nativeTexture.NameHash ||
                sourceTexture.SamplerState != nativeTexture.SamplerState ||
                sourceTexture.Water is not { Image: { } sourceImage } water ||
                string.IsNullOrWhiteSpace(sourceImage.Name) || sourceImage.Name[0] == ',' ||
                sourceImage.Name.Contains('\0') ||
                sourceWaterMaterial.ConstantCount != sourceWaterMaterial.Constants.Count ||
                sourceWaterMaterial.Constants.Count != template.Constants.Count ||
                template.Constants.Any(constant => sourceWaterMaterial.Constants.Count(
                    sourceConstant => sourceConstant.NameHash == constant.NameHash) != 1))
            {
                throw new InvalidDataException(
                    "The source water simulation, sampler, image or constants do not match the native water bindings.");
            }
            sourceWater = water;
        }
        else if (template.Textures.Any(texture => texture.Semantic == TextureSemantic.WaterMap))
        {
            throw new InvalidDataException("Water simulation bindings require a compatible native water technique set.");
        }
        if (sourceState is not null && family != "sky")
        {
            GfxStateBits nativeState = GetWorldState(
                template.StateBitsEntries,
                template.StateBits,
                techniqueSet) ?? throw new InvalidDataException($"The native {materialKind} state is missing.");
            (uint Word0, uint Word1) source = NormalizeWorldState(sourceState);
            (uint Word0, uint Word1) native = NormalizeWorldState(nativeState);
            uint adaptationMask = GetWorldStateAdaptationMask(family, source.Word0, native.Word0, isModel);
            if (source.Word1 != native.Word1 ||
                (source.Word0 & ~adaptationMask) != (native.Word0 & ~adaptationMask))
            {
                throw new InvalidDataException($"The source {materialKind} state is incompatible with its native material template.");
            }
            if (adaptationMask != 0)
            {
                bool adaptCull = (adaptationMask & GfxStateBitsEncoding.CullFaceMask) != 0;
                (MaterialStateFlags Main, MaterialStateFlags Shadow) cullFlags =
                    (GfxCullFace)((source.Word0 & GfxStateBitsEncoding.CullFaceMask) >>
                        GfxStateBitsEncoding.CullFaceShift) switch
                    {
                        GfxCullFace.Back => (MaterialStateFlags.CullBack, MaterialStateFlags.CullBackShadow),
                        GfxCullFace.Front => (MaterialStateFlags.CullFront, MaterialStateFlags.CullFrontShadow),
                        _ => (MaterialStateFlags.None, MaterialStateFlags.None)
                    };
                foreach (MaterialTechniqueSlot slot in techniqueSet.TechniqueSlots)
                {
                    int firstState = template.StateBitsEntries[slot.Index].StateBitsIndex;
                    if (slot.Technique is null || firstState == byte.MaxValue)
                        continue;

                    for (int passIndex = 0; passIndex < slot.Technique.Passes.Count; passIndex++)
                    {
                        int stateIndex = firstState + passIndex;
                        if (stateIndex >= stateWords.Length || stateWords[stateIndex].Length < 2)
                            throw new InvalidDataException($"A native {materialKind} material pass has no valid state.");

                        // Multiply's second pass adds fog without the color pass's
                        // alpha contract. Only the main color row takes its alpha test.
                        uint passMask = adaptCull ? GfxStateBitsEncoding.CullFaceMask : 0;
                        if (passIndex == 0 && ReferenceEquals(template.StateBits[stateIndex], nativeState))
                            passMask |= adaptationMask;
                        stateWords[stateIndex][0] = (stateWords[stateIndex][0] & ~passMask) |
                            (source.Word0 & passMask);
                    }
                    if (adaptCull && slot.Type is MaterialTechniqueType.BuildShadowmapDepth or
                        MaterialTechniqueType.BuildShadowmapColor)
                    {
                        stateFlags &= ~(MaterialStateFlags.CullBackShadow | MaterialStateFlags.CullFrontShadow);
                        stateFlags |= cullFlags.Shadow;
                    }
                }
                if (adaptCull)
                {
                    stateFlags &= ~(MaterialStateFlags.CullBack | MaterialStateFlags.CullFront);
                    stateFlags |= cullFlags.Main;
                }
            }
        }

        // Keep the native shader, sampler, framebuffer and pass contracts while
        // replacing identity, images and the compatible source coverage state.
        // Imported storage pointers are detached.
        return new MaterialAsset
        {
            Info = new MaterialInfo
            {
                Name = targetName,
                GameFlags = template.Info.GameFlags,
                SortKey = template.Info.SortKey,
                TextureAtlasRowCount = sourceModelInspection?.TextureAtlasRowCount ?? template.Info.TextureAtlasRowCount,
                TextureAtlasColumnCount = sourceModelInspection?.TextureAtlasColumnCount ?? template.Info.TextureAtlasColumnCount,
                DrawSurf = template.Info.DrawSurf,
                SurfaceTypeBits = template.Info.SurfaceTypeBits,
                HashIndex = template.Info.HashIndex,
                Pad16 = template.Info.Pad16
            },
            StateBitsEntries = template.StateBitsEntries.ToArray(),
            TextureCount = template.TextureCount,
            ConstantCount = template.ConstantCount,
            StateBitsCount = template.StateBitsCount,
            StateFlags = stateFlags,
            CameraRegion = template.CameraRegion,
            XStringCount = template.XStringCount,
            Pad43 = template.Pad43,
            InlineTechniqueSlotStateBits = template.InlineTechniqueSlotStateBits.ToArray(),
            Pad8E = template.Pad8E,
            RuntimeTechniqueSlotStateBits = template.RuntimeTechniqueSlotStateBits.ToArray(),
            TechniqueSet = techniqueSet,
            Textures = template.Textures.Select(texture => new MaterialTextureDef
            {
                NameHash = texture.NameHash,
                NameStart = texture.NameStart,
                NameEnd = texture.NameEnd,
                SamplerState = texture.SamplerState,
                Semantic = texture.Semantic,
                Water = texture.Semantic == TextureSemantic.WaterMap ? sourceWater : null,
                Image = texture.Semantic == TextureSemantic.WaterMap
                    ? null
                    : replacementImages.TryGetValue(
                        texture.Semantic,
                        out GfxImageAsset? replacementImage)
                        ? replacementImage
                        : texture.Image
            }).ToArray(),
            Constants = template.Constants.Select(constant => new MaterialConstantDef
            {
                NameHash = constant.NameHash,
                NameBytes = constant.NameBytes.ToArray(),
                Literal = sourceWaterMaterial is null
                    ? constant.Literal
                    : sourceWaterMaterial.Constants.Single(sourceConstant =>
                        sourceConstant.NameHash == constant.NameHash).Literal
            }).ToArray(),
            StateBits = template.StateBits.Select((state, index) => new GfxStateBits
            {
                LoadBits = stateWords[index],
                CommandWordCount = state.CommandWordCount
            }).ToArray(),
            XStrings = template.XStrings.Select(value => new MaterialXStringEntry(
                value.Index,
                default,
                value.Value)).ToArray()
        };
    }

    internal static MaterialAsset? SelectWorldTemplate(
        Iw3MaterialSourceInspection source,
        GfxStateBits sourceState,
        IReadOnlyList<MaterialAsset> templates) =>
        SelectNativeTemplate(source, sourceState, templates, isModel: false);

    internal static MaterialAsset? SelectModelTemplate(
        Iw3MaterialSourceInspection source,
        GfxStateBits sourceState,
        IReadOnlyList<MaterialAsset> templates) =>
        SelectNativeTemplate(source, sourceState, templates, isModel: true);

    private static MaterialAsset? SelectNativeTemplate(
        Iw3MaterialSourceInspection source,
        GfxStateBits sourceState,
        IReadOnlyList<MaterialAsset> templates,
        bool isModel)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceTechniqueSetName);
        ArgumentNullException.ThrowIfNull(sourceState);
        ArgumentNullException.ThrowIfNull(templates);
        if (source.TextureSamplerStates.Count != source.ImageRequirements.Count)
            throw new InvalidDataException("The material source texture inspection is incomplete.");

        string? sourceFamily = isModel
            ? GetModelTechniqueFamily(source.SourceTechniqueSetName)
            : GetWorldTechniqueFamily(source.SourceTechniqueSetName);
        string? targetFamily = sourceFamily switch
        {
            "sky" when !isModel => "sky",
            "water" when !isModel => "water",
            "unlit_multiply" => "unlit_multiply_lin",
            "effect" when isModel => "effect_replace_lin",
            "effect_add" or "unlit_add" when isModel => "effect_add",
            // No native lit-add model profile is available. Keep the source
            // image and additive coverage using the native unlit effect.
            "l_sm_a0c0" when isModel => "effect_add",
            "l_sm_flag_t0c0n0s0" when isModel => sourceFamily,
            string family when family.StartsWith("l_sm_r0", StringComparison.Ordinal) ||
                family.StartsWith("l_sm_t0", StringComparison.Ordinal) ||
                family.StartsWith("l_sm_b0", StringComparison.Ordinal) => family,
            _ => null
        };
        if (targetFamily is null)
            return null;

        bool isSky = targetFamily == "sky";
        bool isWater = targetFamily == "water";
        if (isWater && source.WaterImageRequirements.Count != 1)
            throw new InvalidDataException("A source world water material requires one simulation texture.");
        (uint Word0, uint Word1) normalizedSource = isSky
            ? default
            : NormalizeWorldState(sourceState);
        string preferredTechniqueName = (isModel ? "mc_" : "wc_") + targetFamily;
        MaterialAsset? selected = null;
        int selectedRank = -1;
        foreach (MaterialAsset template in templates)
        {
            MaterialTechniqueSetAsset? techniqueSet = isModel
                ? GetModelTechniqueSet(template)
                : GetWorldTechniqueSet(template);
            if (techniqueSet?.Name is not string techniqueName ||
                (isModel ? GetModelTechniqueFamily(techniqueName) : GetWorldTechniqueFamily(techniqueName)) != targetFamily ||
                !HasCompleteWorldTextures(template) ||
                (isWater && !HasCompleteWorldWaterContract(template)))
            {
                continue;
            }

            if (!isSky && !HasMatchingWorldTextureAddressing(template, source))
                continue;

            GfxStateBits? nativeState = GetWorldState(
                template.StateBitsEntries,
                template.StateBits,
                techniqueSet);
            if (nativeState is null)
                continue;
            (uint Word0, uint Word1) normalizedNative = isSky
                ? default
                : NormalizeWorldState(nativeState);
            uint adaptationMask = GetWorldStateAdaptationMask(
                targetFamily,
                normalizedSource.Word0,
                normalizedNative.Word0,
                isModel);
            if (normalizedSource.Word1 != normalizedNative.Word1 ||
                (normalizedSource.Word0 & ~adaptationMask) != (normalizedNative.Word0 & ~adaptationMask))
            {
                continue;
            }

            bool hasExactState = normalizedNative == normalizedSource;
            bool hasPreferredTechnique = techniqueName == preferredTechniqueName;
            int rank = (hasExactState ? 4 : 0) | (hasPreferredTechnique ? 2 : 0) |
                (isWater && template.Info.GameFlags == source.GameFlags ? 1 : 0);
            if (selected is null || rank > selectedRank ||
                (rank == selectedRank && StringComparer.Ordinal.Compare(template.Info.Name, selected.Info.Name) < 0))
            {
                selected = template;
                selectedRank = rank;
            }
        }
        if (isWater && selected is null)
        {
            throw new InvalidDataException(
                "The source world water material has no compatible native simulation, shader, sampler and state contract.");
        }
        return selected;
    }

    private static uint GetWorldStateAdaptationMask(string? family, uint sourceWord0, uint nativeWord0, bool isModel)
    {
        const uint alphaTestMask = GfxStateBitsEncoding.AlphaTestMask |
            (uint)GfxStateBits0Flags.AlphaTestDisabled;
        const uint cutoutAlphaTest = (uint)GfxAlphaTest.GreaterThanOrEqualTo128 << GfxStateBitsEncoding.AlphaTestShift;
        const uint disabledAlphaTest = (uint)GfxStateBits0Flags.AlphaTestDisabled;
        uint sourceAlphaTest = sourceWord0 & alphaTestMask;
        uint nativeAlphaTest = nativeWord0 & alphaTestMask;
        uint mask = 0;
        if (family == "unlit_multiply_lin" &&
            sourceAlphaTest == cutoutAlphaTest &&
            nativeAlphaTest == disabledAlphaTest)
        {
            mask |= alphaTestMask;
        }
        bool isModelEffect = isModel && (family is "effect_add" or "effect_replace_lin");
        if (isModelEffect && sourceAlphaTest != nativeAlphaTest &&
            (sourceAlphaTest is cutoutAlphaTest or disabledAlphaTest) &&
            (nativeAlphaTest is cutoutAlphaTest or disabledAlphaTest))
        {
            mask |= alphaTestMask;
        }
        GfxCullFace sourceCull = (GfxCullFace)((sourceWord0 & GfxStateBitsEncoding.CullFaceMask) >>
            GfxStateBitsEncoding.CullFaceShift);
        GfxCullFace nativeCull = (GfxCullFace)((nativeWord0 & GfxStateBitsEncoding.CullFaceMask) >>
            GfxStateBitsEncoding.CullFaceShift);
        bool isLit = family is not null && family.StartsWith("l_sm_", StringComparison.Ordinal);
        if ((!isModel && isLit && sourceCull == GfxCullFace.None && nativeCull == GfxCullFace.Back) ||
            (isModel && (isLit || isModelEffect) && sourceCull != nativeCull &&
                (sourceCull is GfxCullFace.None or GfxCullFace.Back or GfxCullFace.Front) &&
                (nativeCull is GfxCullFace.None or GfxCullFace.Back or GfxCullFace.Front)))
        {
            mask |= GfxStateBitsEncoding.CullFaceMask;
        }
        return mask;
    }

    private static bool HasMatchingWorldTextureAddressing(
        MaterialAsset template,
        Iw3MaterialSourceInspection source)
    {
        for (int index = 0; index < source.ImageRequirements.Count; index++)
        {
            TextureSemantic semantic = source.ImageRequirements[index].Semantic;
            if (semantic is not (TextureSemantic.ColorMap or TextureSemantic.WaterMap or
                TextureSemantic.NormalMap or TextureSemantic.SpecularMap) ||
                template.Textures.Count(texture => texture.Semantic == semantic) != 1)
            {
                continue;
            }

            MaterialTextureDef nativeTexture = template.Textures.Single(
                texture => texture.Semantic == semantic);
            if (semantic == TextureSemantic.WaterMap &&
                nativeTexture.SamplerState != source.TextureSamplerStates[index])
            {
                return false;
            }
            // A replacement keeps the native filter and shader contract, but
            // its UV addressing must agree with the source texture's use.
            if ((nativeTexture.SamplerState & MaterialSamplerState.ClampMask) !=
                (source.TextureSamplerStates[index] & MaterialSamplerState.ClampMask))
            {
                return false;
            }
        }
        return true;
    }

    internal static GfxStateBits ReadWorldState(
        string assetName,
        Stream json)
    {
        ValidateInputs(assetName, json);

        using JsonDocument document = ParseDocument(assetName, json);
        JsonElement root = RequireKind(
            document.RootElement,
            JsonValueKind.Object,
            "root");
        ValidateHeader(root);
        GfxStateBits[] stateBits = ParseStateBits(root, assetName);
        MaterialStateBitsEntry[] stateEntries = ParseStateBitsEntries(
            root,
            stateBits.Length,
            assetName);
        return GetWorldState(stateEntries, stateBits) ??
            throw MaterialError(assetName, "has no valid lit, emissive, or unlit world state");
    }

    private static MaterialTechniqueSetAsset? GetWorldTechniqueSet(MaterialAsset template)
    {
        MaterialTechniqueSetAsset? techniqueSet = template.TechniqueSet;
        if (string.IsNullOrWhiteSpace(template.Info.Name) || template.Info.Name[0] == ',' ||
            techniqueSet is null || string.IsNullOrWhiteSpace(techniqueSet.Name) ||
            techniqueSet.Name[0] == ',' ||
            techniqueSet.WorldVertexFormat != MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_1_NRM_1 ||
            techniqueSet.TechniqueSlots.Count != MaterialAsset.TechniqueSlotCount ||
            GetWorldState(template.StateBitsEntries, template.StateBits, techniqueSet) is null)
        {
            return null;
        }
        return techniqueSet;
    }

    private static bool HasModelEffectContract(MaterialTechniqueSetAsset techniqueSet)
    {
        foreach (MaterialTechniqueType type in new[] { MaterialTechniqueType.Unlit, MaterialTechniqueType.Emissive })
        {
            MaterialTechniqueAsset? technique = techniqueSet.TechniqueSlots[(int)type].Technique;
            if (technique is null || technique.PassCount != 1 || technique.Passes.Count != 1)
                return false;
            MaterialPassAsset pass = technique.Passes[0];
            if (pass.PrecompiledVertexShader != MaterialPrecompiledVertexShader.ModelUnlit ||
                pass.VertexDeclaration is not { StreamCount: 3, HasOptionalSourceRaw: 1 } declaration ||
                declaration.Routing.Count != MaterialVertexDeclarationAsset.RoutingCount ||
                declaration.Routing[0] != new MaterialVertexStreamRouting(MaterialStreamSource.Position, MaterialStreamDestination.Position) ||
                declaration.Routing[1] != new MaterialVertexStreamRouting(MaterialStreamSource.Color, MaterialStreamDestination.Color0) ||
                declaration.Routing[2] != new MaterialVertexStreamRouting(MaterialStreamSource.TexCoord0, MaterialStreamDestination.TexCoord0) ||
                declaration.Routing.Skip(3).Any(route => route != default) ||
                pass.VertexShader is not { Name: "vertcol_simple_fog.hlsl", Data: { Length: > 0 } } ||
                pass.PixelShader is not { Name: "vertcol_simple_add_fog.hlsl", Data: { Length: > 0 } } ||
                pass.Args.Count != pass.PerPrimArgCount + pass.PerObjArgCount + pass.StableArgCount)
            {
                return false;
            }
        }
        return true;
    }

    private static MaterialTechniqueSetAsset? GetModelTechniqueSet(MaterialAsset template)
    {
        // Native PS3 model sets retain world format zero. Model streams use
        // the native declaration's packed/static-model rows independently.
        MaterialTechniqueSetAsset? techniqueSet = GetWorldTechniqueSet(template);
        if (techniqueSet?.Name is not string name || GetModelTechniqueFamily(name) is null ||
            template.ConstantCount != template.Constants.Count ||
            template.Textures.Any(texture => texture.Semantic == TextureSemantic.WaterMap))
        {
            return null;
        }
        if (name == "effect_add" && !HasModelEffectContract(techniqueSet))
            return null;
        return techniqueSet;
    }

    private static bool HasCompleteWorldTextures(MaterialAsset template) =>
        template.TextureCount == template.Textures.Count &&
        template.Textures.All(texture =>
            texture.Semantic == TextureSemantic.WaterMap
                ? texture.Image is null && texture.Water?.Image is not null
                : texture.Water is null && texture.Image is not null);

    private static bool HasCompleteWorldWaterContract(MaterialAsset template)
    {
        uint normalMapHash = Iw3MaterialPropertyName.Hash("normalMap");
        uint colorMapHash = Iw3MaterialPropertyName.Hash("colorMap");
        uint envMapHash = Iw3MaterialPropertyName.Hash("envMapParms");
        uint waterColorHash = Iw3MaterialPropertyName.Hash("waterColor");
        if (template.Info.SortKey != MaterialSortKey.TransparentWater ||
            template.CameraRegion != GfxCameraRegionType.LitTrans ||
            template.Textures.Count != 2 ||
            template.Textures.Count(texture => texture.Semantic == TextureSemantic.WaterMap &&
                texture.NameHash == normalMapHash) != 1 ||
            template.Textures.Count(texture => texture.Semantic == TextureSemantic.ColorMap &&
                texture.NameHash == colorMapHash) != 1 ||
            template.ConstantCount != template.Constants.Count || template.Constants.Count != 2 ||
            template.Constants.Count(constant => constant.NameHash == envMapHash) != 1 ||
            template.Constants.Count(constant => constant.NameHash == waterColorHash) != 1)
        {
            return false;
        }

        MaterialTechniqueAsset? lit = template.TechniqueSet?
            .TechniqueSlots[(int)MaterialTechniqueType.Lit].Technique;
        return lit is not null && lit.PassCount != 0 && lit.Passes.Count == lit.PassCount &&
            lit.Passes.All(pass =>
                pass.Args.Count == pass.PerPrimArgCount + pass.PerObjArgCount + pass.StableArgCount &&
                pass.Args.Any(argument => argument.Type == MaterialShaderArgumentType.MaterialPixelSampler &&
                    argument.MaterialNameHash == normalMapHash) &&
                pass.Args.Any(argument => argument.Type == MaterialShaderArgumentType.MaterialPixelConst &&
                    argument.MaterialNameHash == envMapHash) &&
                pass.Args.Any(argument => argument.Type == MaterialShaderArgumentType.MaterialPixelConst &&
                    argument.MaterialNameHash == waterColorHash));
    }

    internal static string? GetWorldTechniqueFamily(string name) =>
        name.StartsWith("wc_", StringComparison.Ordinal)
            ? name[3..]
            : name.StartsWith("w_", StringComparison.Ordinal)
                ? name[2..]
                : null;

    private static string? GetModelTechniqueFamily(string name) =>
        name == "effect_add"
            ? name
            : name.StartsWith("mc_", StringComparison.Ordinal)
            ? name[3..]
            : name.StartsWith("m_", StringComparison.Ordinal)
                ? name[2..]
                : null;

    private static GfxStateBits? GetWorldState(
        IReadOnlyList<MaterialStateBitsEntry> stateEntries,
        IReadOnlyList<GfxStateBits> stateBits,
        MaterialTechniqueSetAsset? techniqueSet = null)
    {
        if (stateEntries.Count != MaterialAsset.TechniqueSlotCount)
            return null;
        ReadOnlySpan<MaterialTechniqueType> mainTechniques =
        [
            MaterialTechniqueType.Lit,
            MaterialTechniqueType.Emissive,
            MaterialTechniqueType.Unlit
        ];
        foreach (MaterialTechniqueType technique in mainTechniques)
        {
            if (techniqueSet is not null &&
                techniqueSet.TechniqueSlots[(int)technique].Technique is null)
            {
                continue;
            }
            int stateIndex = stateEntries[(int)technique].StateBitsIndex;
            if (stateIndex != byte.MaxValue && stateIndex < stateBits.Count &&
                stateBits[stateIndex].LoadBits.Count >= 2)
                return stateBits[stateIndex];
        }
        return null;
    }

    private static (uint Word0, uint Word1) NormalizeWorldState(GfxStateBits state)
    {
        if (state.LoadBits.Count < 2)
            throw new InvalidDataException("A world material state requires two load words.");

        const uint rgbBlendMask = GfxStateBitsEncoding.SourceBlendRgbMask |
            GfxStateBitsEncoding.DestinationBlendRgbMask |
            GfxStateBitsEncoding.BlendOperationRgbMask;
        const uint alphaBlendMask = GfxStateBitsEncoding.SourceBlendAlphaMask |
            GfxStateBitsEncoding.DestinationBlendAlphaMask |
            GfxStateBitsEncoding.BlendOperationAlphaMask;
        // Framebuffer-alpha and gamma are native IW4 pipeline contracts.
        // Exclude them from cross-engine matching, not from the cloned state.
        uint word0 = state.LoadBits[0] & ~(alphaBlendMask |
            (uint)(GfxStateBits0Flags.GammaWrite | GfxStateBits0Flags.ColorWriteAlpha));
        uint word1 = state.LoadBits[1];
        if ((word0 & GfxStateBitsEncoding.BlendOperationRgbMask) == 0)
        {
            word0 &= ~rgbBlendMask;
        }
        else
        {
            word0 = NormalizeBlendFactor(word0,
                GfxStateBitsEncoding.SourceBlendRgbMask,
                GfxStateBitsEncoding.SourceBlendRgbShift);
            word0 = NormalizeBlendFactor(word0,
                GfxStateBitsEncoding.DestinationBlendRgbMask,
                GfxStateBitsEncoding.DestinationBlendRgbShift);
        }
        if ((word0 & (uint)GfxStateBits0Flags.AlphaTestDisabled) != 0)
            word0 &= ~GfxStateBitsEncoding.AlphaTestMask;
        if ((word1 & (uint)GfxStateBits1Flags.DepthTestDisabled) != 0)
        {
            word1 &= ~(GfxStateBitsEncoding.DepthTestMask |
                (uint)GfxStateBits1Flags.DepthWrite);
        }
        return (word0, word1);
    }

    private static uint NormalizeBlendFactor(uint word, uint mask, int shift) =>
        (word & mask) == 0 ? word | ((uint)GfxBlend.Zero << shift) : word;

    internal static Iw3MaterialSourceInspection InspectSource(
        string assetName,
        Stream json)
    {
        ValidateInputs(assetName, json);

        using JsonDocument document = ParseDocument(assetName, json);
        JsonElement root = RequireKind(
            document.RootElement,
            JsonValueKind.Object,
            "root");
        ValidateHeader(root);
        return ParseSourceInspection(root, assetName);
    }

    internal static Iw3MaterialCompilation Compile(
        string assetName,
        Stream json,
        IReadOnlyDictionary<string, GfxImageAsset> images,
        bool requiresRuntimeTechniqueState)
    {
        ValidateInputs(assetName, json);
        ArgumentNullException.ThrowIfNull(images);

        try
        {
            using JsonDocument document = ParseDocument(assetName, json);
            JsonElement root = RequireKind(
                document.RootElement,
                JsonValueKind.Object,
                "root");
            ValidateHeader(root);
            Iw3MaterialSourceInspection sourceInspection = ParseSourceInspection(
                root,
                assetName);

            GfxStateBits[] stateBits = ParseStateBits(root, assetName);
            MaterialStateBitsEntry[] stateEntries = ParseStateBitsEntries(
                root,
                stateBits.Length,
                assetName);
            MaterialTextureDef[] textures = ParseTextures(
                root,
                images,
                sourceInspection,
                assetName);
            MaterialConstantDef[] constants = ParseConstants(root, assetName);
            string techniqueSetName = sourceInspection.SourceTechniqueSetName;

            byte atlasRows = sourceInspection.TextureAtlasRowCount;
            byte atlasColumns = sourceInspection.TextureAtlasColumnCount;
            MaterialGameFlags gameFlags = sourceInspection.GameFlags;
            byte sortKey = RequireByte(root, "sortKey", "sortKey");
            if (sortKey >= 0x40)
            {
                throw MaterialError(
                    assetName,
                    $"sortKey {sortKey} does not fit the IW4 six-bit sort field");
            }

            uint surfaceTypeBits = RequireUInt32(
                root,
                "surfaceTypeBits",
                "surfaceTypeBits");
            byte stateFlags = RequireByte(root, "stateFlags", "stateFlags");
            GfxCameraRegionType cameraRegion = ParseCameraRegion(root, assetName);

            var material = new MaterialAsset
            {
                Info = new MaterialInfo
                {
                    Name = assetName,
                    GameFlags = gameFlags,
                    SortKey = (MaterialSortKey)sortKey,
                    TextureAtlasRowCount = atlasRows,
                    TextureAtlasColumnCount = atlasColumns,
                    SurfaceTypeBits = (MaterialSurfaceTypeBits)surfaceTypeBits
                },
                StateBitsEntries = stateEntries,
                TextureCount = checked((byte)textures.Length),
                ConstantCount = checked((byte)constants.Length),
                StateBitsCount = checked((byte)stateBits.Length),
                StateFlags = (MaterialStateFlags)stateFlags,
                CameraRegion = cameraRegion,
                TechniqueSet = new MaterialTechniqueSetAsset
                {
                    Name = "," + techniqueSetName
                },
                RuntimeTechniqueSlotStateBits = requiresRuntimeTechniqueState
                    ? new ushort[MaterialAsset.TechniqueSlotCount]
                    : [],
                Textures = textures,
                Constants = constants,
                StateBits = stateBits
            };

            return new Iw3MaterialCompilation(material, techniqueSetName);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"IW3 material '{assetName}' contains a count that exceeds the IW4 wire format.",
                exception);
        }
    }

    private static void ValidateInputs(string assetName, Stream json)
    {
        if (string.IsNullOrWhiteSpace(assetName))
            throw new ArgumentException("A material asset name is required.", nameof(assetName));
        ArgumentNullException.ThrowIfNull(json);
    }

    private static JsonDocument ParseDocument(string assetName, Stream json)
    {
        try
        {
            return JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64
                });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"IW3 material '{assetName}' is not valid material-v1 JSON: " +
                exception.Message,
                exception);
        }
    }

    private static Iw3MaterialSourceInspection ParseSourceInspection(
        JsonElement root,
        string assetName)
    {
        string techniqueSetName = RequireNonemptyString(
            root,
            "techniqueSet",
            "techniqueSet");
        if (techniqueSetName[0] == ',')
        {
            throw MaterialError(
                assetName,
                "techniqueSet cannot already be comma-prefixed");
        }

        JsonElement textures = RequireProperty(
            root,
            "textures",
            JsonValueKind.Array,
            "textures");
        var imageRequirements = new List<Iw3IwdImageRequest>();
        var waterImageRequirements = new List<Iw3WaterImageRequest>();
        var textureSamplerStates = new List<MaterialSamplerState>();
        int index = 0;
        foreach (JsonElement element in textures.EnumerateArray())
        {
            JsonElement texture = RequireKind(
                element,
                JsonValueKind.Object,
                $"textures[{index}]");
            string imageName = RequireNonemptyString(
                texture,
                "image",
                $"textures[{index}].image");
            TextureSemantic semantic = ParseTextureSemantic(
                texture,
                assetName,
                index);
            bool hasWater = texture.TryGetProperty("water", out JsonElement waterElement) &&
                waterElement.ValueKind != JsonValueKind.Null;
            if (semantic == TextureSemantic.WaterMap && !hasWater)
            {
                throw MaterialError(
                    assetName,
                    $"textures[{index}] uses waterMap semantic without water parameters");
            }
            if (semantic != TextureSemantic.WaterMap && hasWater)
            {
                throw MaterialError(
                    assetName,
                    $"textures[{index}] defines water parameters for a non-water semantic");
            }

            imageRequirements.Add(new Iw3IwdImageRequest(
                imageName,
                semantic,
                semantic is TextureSemantic.ColorMap or TextureSemantic.TwoDimensional));
            textureSamplerStates.Add(ParseSamplerState(texture, assetName, index));
            if (hasWater)
            {
                RequireKind(
                    waterElement,
                    JsonValueKind.Object,
                    $"textures[{index}].water");
                int width = RequireInt32(
                    waterElement,
                    "m",
                    $"textures[{index}].water.m");
                int height = RequireInt32(
                    waterElement,
                    "n",
                    $"textures[{index}].water.n");
                if (width <= 0 || height <= 0 ||
                    width > ushort.MaxValue || height > ushort.MaxValue)
                {
                    throw MaterialError(
                        assetName,
                        $"textures[{index}].water dimensions must be between 1 and {ushort.MaxValue}");
                }
                waterImageRequirements.Add(new Iw3WaterImageRequest(
                    imageName,
                    width,
                    height));
            }
            index++;
        }

        if (imageRequirements.Count > byte.MaxValue)
            throw MaterialError(assetName, "textures contains more than 255 rows");
        (byte atlasRows, byte atlasColumns) = ParseTextureAtlas(root, assetName);
        return new Iw3MaterialSourceInspection(
            techniqueSetName,
            Array.AsReadOnly(imageRequirements.ToArray()),
            Array.AsReadOnly(waterImageRequirements.ToArray()),
            Array.AsReadOnly(textureSamplerStates.ToArray()),
            (MaterialGameFlags)ParseGameFlags(root, assetName),
            atlasRows,
            atlasColumns);
    }

    private static void ValidateHeader(JsonElement root)
    {
        string type = RequireString(root, "_type", "_type");
        if (!string.Equals(type, "material", StringComparison.Ordinal))
            throw new InvalidDataException("Expected _type 'material'.");

        int version = RequireInt32(root, "_version", "_version");
        if (version != 1)
            throw new InvalidDataException($"Expected material version 1, found {version}.");

        string game = RequireString(root, "_game", "_game");
        if (!string.Equals(game, "iw3", StringComparison.Ordinal))
            throw new InvalidDataException($"Expected _game 'iw3', found '{game}'.");
    }

    private static byte ParseGameFlags(JsonElement root, string assetName)
    {
        JsonElement values = RequireProperty(
            root,
            "gameFlags",
            JsonValueKind.Array,
            "gameFlags");
        byte result = 0;
        int index = 0;
        foreach (JsonElement value in values.EnumerateArray())
        {
            uint flag = value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetUInt32(out uint number) => number,
                JsonValueKind.String => ParseGameFlagName(
                    value.GetString()!,
                    assetName,
                    index),
                _ => throw MaterialError(
                    assetName,
                    $"gameFlags[{index}] must be an unsigned number or string")
            };
            if (flag > byte.MaxValue)
            {
                throw MaterialError(
                    assetName,
                    $"gameFlags[{index}] value 0x{flag:X} does not fit the IW4 byte field");
            }

            result |= (byte)flag;
            index++;
        }
        return result;
    }

    private static uint ParseGameFlagName(
        string value,
        string assetName,
        int index)
    {
        if (string.Equals(value, "CASTS_SHADOW", StringComparison.Ordinal))
            return (uint)MaterialGameFlags.CastsShadow;

        ReadOnlySpan<char> digits = value.AsSpan();
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            digits = digits[2..];
        if (digits.Length != 0 &&
            uint.TryParse(
                digits,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out uint parsed))
        {
            return parsed;
        }

        throw MaterialError(
            assetName,
            $"gameFlags[{index}] has unsupported value '{value}'");
    }

    private static GfxCameraRegionType ParseCameraRegion(
        JsonElement root,
        string assetName) => RequireString(
            root,
            "cameraRegion",
            "cameraRegion") switch
        {
            "lit" => GfxCameraRegionType.LitOpaque,
            "decal" => GfxCameraRegionType.LitTrans,
            "emissive" => GfxCameraRegionType.Emissive,
            "none" => GfxCameraRegionType.None,
            string value => throw MaterialError(
                assetName,
                $"cameraRegion has unsupported value '{value}'")
        };

    private static (byte Rows, byte Columns) ParseTextureAtlas(
        JsonElement root,
        string assetName)
    {
        if (!root.TryGetProperty("textureAtlas", out JsonElement atlas) ||
            atlas.ValueKind == JsonValueKind.Null)
        {
            return (0, 0);
        }

        RequireKind(atlas, JsonValueKind.Object, "textureAtlas");
        byte rows = RequireByte(atlas, "rows", "textureAtlas.rows");
        byte columns = RequireByte(atlas, "columns", "textureAtlas.columns");
        if (rows == 0 || columns == 0)
        {
            throw MaterialError(
                assetName,
                "textureAtlas rows and columns must both be nonzero when present");
        }
        return (rows, columns);
    }

    private static MaterialStateBitsEntry[] ParseStateBitsEntries(
        JsonElement root,
        int stateBitsCount,
        string assetName)
    {
        JsonElement sourceElement = RequireProperty(
            root,
            "stateBitsEntry",
            JsonValueKind.Array,
            "stateBitsEntry");
        int[] source = sourceElement
            .EnumerateArray()
            .Select((value, index) => RequireInt32(
                value,
                $"stateBitsEntry[{index}]"))
            .ToArray();
        if (source.Length != Iw3TechniqueFormatParser.TechniqueSlotCount)
        {
            throw MaterialError(
                assetName,
                $"stateBitsEntry must contain exactly {Iw3TechniqueFormatParser.TechniqueSlotCount} IW3 slots");
        }

        for (int index = 0; index < source.Length; index++)
        {
            int stateIndex = source[index];
            if (stateIndex < -1 || stateIndex >= stateBitsCount)
            {
                throw MaterialError(
                    assetName,
                    $"stateBitsEntry[{index}] references invalid state row {stateIndex}");
            }
        }

        return Iw3TechniqueSlotMapping.Iw4Slots
            .Select(sourceSlot => new MaterialStateBitsEntry(
                sourceSlot is null || source[(int)sourceSlot.Value] < 0
                    ? byte.MaxValue
                    : checked((byte)source[(int)sourceSlot.Value])))
            .ToArray();
    }

    private static GfxStateBits[] ParseStateBits(
        JsonElement root,
        string assetName)
    {
        JsonElement array = RequireProperty(
            root,
            "stateBits",
            JsonValueKind.Array,
            "stateBits");
        var result = new List<GfxStateBits>();
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            JsonElement state = RequireKind(
                element,
                JsonValueKind.Object,
                $"stateBits[{index}]");
            uint word0 = 0;
            uint word1 = 0;

            word0 |= Encode(
                ParseBlend(state, "srcBlendRgb", assetName, index),
                GfxStateBitsEncoding.SourceBlendRgbShift);
            word0 |= Encode(
                ParseBlend(state, "dstBlendRgb", assetName, index),
                GfxStateBitsEncoding.DestinationBlendRgbShift);
            word0 |= Encode(
                ParseBlendOperation(state, "blendOpRgb", assetName, index),
                GfxStateBitsEncoding.BlendOperationRgbShift);
            word0 |= EncodeAlphaTest(state, assetName, index);
            word0 |= Encode(
                ParseCullFace(state, assetName, index),
                GfxStateBitsEncoding.CullFaceShift);
            word0 |= Encode(
                ParseBlend(state, "srcBlendAlpha", assetName, index),
                GfxStateBitsEncoding.SourceBlendAlphaShift);
            word0 |= Encode(
                ParseBlend(state, "dstBlendAlpha", assetName, index),
                GfxStateBitsEncoding.DestinationBlendAlphaShift);
            word0 |= Encode(
                ParseBlendOperation(state, "blendOpAlpha", assetName, index),
                GfxStateBitsEncoding.BlendOperationAlphaShift);
            if (RequireBoolean(state, "colorWriteRgb", StatePath(index, "colorWriteRgb")))
                word0 |= (uint)GfxStateBits0Flags.ColorWriteRgb;
            if (RequireBoolean(state, "colorWriteAlpha", StatePath(index, "colorWriteAlpha")))
                word0 |= (uint)GfxStateBits0Flags.ColorWriteAlpha;
            if (RequireBoolean(state, "polymodeLine", StatePath(index, "polymodeLine")))
                word0 |= (uint)GfxStateBits0Flags.PolygonModeLine;

            if (RequireBoolean(state, "depthWrite", StatePath(index, "depthWrite")))
                word1 |= (uint)GfxStateBits1Flags.DepthWrite;
            word1 |= EncodeDepthTest(state, assetName, index);
            word1 |= Encode(
                ParsePolygonOffset(state, assetName, index),
                GfxStateBitsEncoding.PolygonOffsetShift);
            word1 |= EncodeStencil(state, "stencilFront", back: false, assetName, index);
            word1 |= EncodeStencil(state, "stencilBack", back: true, assetName, index);

            result.Add(new GfxStateBits
            {
                LoadBits = new uint[] { word0, word1 }
            });
            index++;
        }

        if (result.Count > byte.MaxValue)
            throw MaterialError(assetName, "stateBits contains more than 255 rows");
        return result.ToArray();
    }

    private static uint EncodeAlphaTest(
        JsonElement state,
        string assetName,
        int index) => RequireString(
            state,
            "alphaTest",
            StatePath(index, "alphaTest")) switch
        {
            "disabled" => (uint)GfxStateBits0Flags.AlphaTestDisabled,
            "gt0" => Encode(
                GfxAlphaTest.GreaterThanZero,
                GfxStateBitsEncoding.AlphaTestShift),
            "lt128" => Encode(
                GfxAlphaTest.LessThan128,
                GfxStateBitsEncoding.AlphaTestShift),
            "ge128" => Encode(
                GfxAlphaTest.GreaterThanOrEqualTo128,
                GfxStateBitsEncoding.AlphaTestShift),
            string value => throw UnsupportedStateValue(
                assetName,
                index,
                "alphaTest",
                value)
        };

    private static GfxBlend ParseBlend(
        JsonElement state,
        string property,
        string assetName,
        int index) => RequireString(
            state,
            property,
            StatePath(index, property)) switch
        {
            "disabled" => GfxBlend.Disabled,
            "zero" => GfxBlend.Zero,
            "one" => GfxBlend.One,
            "srccolor" => GfxBlend.SourceColor,
            "invsrccolor" => GfxBlend.InverseSourceColor,
            "srcalpha" => GfxBlend.SourceAlpha,
            "invsrcalpha" => GfxBlend.InverseSourceAlpha,
            "destalpha" => GfxBlend.DestinationAlpha,
            "invdestalpha" => GfxBlend.InverseDestinationAlpha,
            "destcolor" => GfxBlend.DestinationColor,
            "invdestcolor" => GfxBlend.InverseDestinationColor,
            string value => throw UnsupportedStateValue(
                assetName,
                index,
                property,
                value)
        };

    private static GfxBlendOperation ParseBlendOperation(
        JsonElement state,
        string property,
        string assetName,
        int index) => RequireString(
            state,
            property,
            StatePath(index, property)) switch
        {
            "disabled" => GfxBlendOperation.Disabled,
            "add" => GfxBlendOperation.Add,
            "subtract" => GfxBlendOperation.Subtract,
            "revsubtract" => GfxBlendOperation.ReverseSubtract,
            "min" => GfxBlendOperation.Minimum,
            "max" => GfxBlendOperation.Maximum,
            string value => throw UnsupportedStateValue(
                assetName,
                index,
                property,
                value)
        };

    private static GfxCullFace ParseCullFace(
        JsonElement state,
        string assetName,
        int index) => RequireString(
            state,
            "cullFace",
            StatePath(index, "cullFace")) switch
        {
            "none" => GfxCullFace.None,
            "back" => GfxCullFace.Back,
            "front" => GfxCullFace.Front,
            string value => throw UnsupportedStateValue(
                assetName,
                index,
                "cullFace",
                value)
        };

    private static uint EncodeDepthTest(
        JsonElement state,
        string assetName,
        int index) => RequireString(
            state,
            "depthTest",
            StatePath(index, "depthTest")) switch
        {
            "disabled" => (uint)GfxStateBits1Flags.DepthTestDisabled,
            "always" => Encode(GfxDepthTest.Always, GfxStateBitsEncoding.DepthTestShift),
            "less" => Encode(GfxDepthTest.Less, GfxStateBitsEncoding.DepthTestShift),
            "equal" => Encode(GfxDepthTest.Equal, GfxStateBitsEncoding.DepthTestShift),
            "less_equal" => Encode(
                GfxDepthTest.LessThanOrEqual,
                GfxStateBitsEncoding.DepthTestShift),
            string value => throw UnsupportedStateValue(
                assetName,
                index,
                "depthTest",
                value)
        };

    private static GfxPolygonOffset ParsePolygonOffset(
        JsonElement state,
        string assetName,
        int index) => RequireString(
            state,
            "polygonOffset",
            StatePath(index, "polygonOffset")) switch
        {
            "offset0" => GfxPolygonOffset.Disabled,
            "offset1" => GfxPolygonOffset.Offset1,
            "offset2" => GfxPolygonOffset.Offset2,
            // IW3's shadow-map-selected offset occupies the same encoded value
            // as IW4's inherit selector; both defer the concrete bias choice.
            "offsetShadowmap" => GfxPolygonOffset.Inherit,
            string value => throw UnsupportedStateValue(
                assetName,
                index,
                "polygonOffset",
                value)
        };

    private static uint EncodeStencil(
        JsonElement state,
        string property,
        bool back,
        string assetName,
        int index)
    {
        if (!state.TryGetProperty(property, out JsonElement stencil) ||
            stencil.ValueKind == JsonValueKind.Null)
        {
            return 0;
        }
        RequireKind(stencil, JsonValueKind.Object, StatePath(index, property));

        uint result = back
            ? (uint)GfxStateBits1Flags.StencilBackFaceIndependent
            : (uint)GfxStateBits1Flags.StencilEnabled;
        result |= Encode(
            ParseStencilOperation(stencil, "pass", assetName, index, property),
            back
                ? GfxStateBitsEncoding.StencilBackPassShift
                : GfxStateBitsEncoding.StencilFrontPassShift);
        result |= Encode(
            ParseStencilOperation(stencil, "fail", assetName, index, property),
            back
                ? GfxStateBitsEncoding.StencilBackFailShift
                : GfxStateBitsEncoding.StencilFrontFailShift);
        result |= Encode(
            ParseStencilOperation(stencil, "zfail", assetName, index, property),
            back
                ? GfxStateBitsEncoding.StencilBackDepthFailShift
                : GfxStateBitsEncoding.StencilFrontDepthFailShift);
        result |= Encode(
            ParseStencilFunction(stencil, assetName, index, property),
            back
                ? GfxStateBitsEncoding.StencilBackFunctionShift
                : GfxStateBitsEncoding.StencilFrontFunctionShift);
        return result;
    }

    private static GfxStencilOperation ParseStencilOperation(
        JsonElement stencil,
        string field,
        string assetName,
        int index,
        string stencilName) => RequireString(
            stencil,
            field,
            $"{StatePath(index, stencilName)}.{field}") switch
        {
            "keep" => GfxStencilOperation.Keep,
            "zero" => GfxStencilOperation.Zero,
            "replace" => GfxStencilOperation.Replace,
            "incrsat" => GfxStencilOperation.IncrementSaturate,
            "decrsat" => GfxStencilOperation.DecrementSaturate,
            "invert" => GfxStencilOperation.Invert,
            "incr" => GfxStencilOperation.IncrementWrap,
            "decr" => GfxStencilOperation.DecrementWrap,
            string value => throw UnsupportedStateValue(
                assetName,
                index,
                $"{stencilName}.{field}",
                value)
        };

    private static GfxStencilFunction ParseStencilFunction(
        JsonElement stencil,
        string assetName,
        int index,
        string stencilName) => RequireString(
            stencil,
            "func",
            $"{StatePath(index, stencilName)}.func") switch
        {
            "never" => GfxStencilFunction.Never,
            "less" => GfxStencilFunction.Less,
            "equal" => GfxStencilFunction.Equal,
            "lessequal" => GfxStencilFunction.LessThanOrEqual,
            "greater" => GfxStencilFunction.Greater,
            "notequal" => GfxStencilFunction.NotEqual,
            "greaterequal" => GfxStencilFunction.GreaterThanOrEqual,
            "always" => GfxStencilFunction.Always,
            string value => throw UnsupportedStateValue(
                assetName,
                index,
                $"{stencilName}.func",
                value)
        };

    private static MaterialTextureDef[] ParseTextures(
        JsonElement root,
        IReadOnlyDictionary<string, GfxImageAsset> images,
        Iw3MaterialSourceInspection sourceInspection,
        string assetName)
    {
        ArgumentNullException.ThrowIfNull(sourceInspection);
        IReadOnlyList<Iw3IwdImageRequest> imageRequirements = sourceInspection.ImageRequirements;
        JsonElement array = RequireProperty(
            root,
            "textures",
            JsonValueKind.Array,
            "textures");
        var result = new List<MaterialTextureDef>();
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            JsonElement texture = RequireKind(
                element,
                JsonValueKind.Object,
                $"textures[{index}]");
            if (index >= imageRequirements.Count)
                throw new InvalidOperationException("The material source inspection is incomplete.");
            Iw3IwdImageRequest imageRequirement = imageRequirements[index];
            string imageName = imageRequirement.ImageName;
            if (!images.TryGetValue(imageName, out GfxImageAsset? image) || image is null)
            {
                throw MaterialError(
                    assetName,
                    $"textures[{index}] references unavailable image '{imageName}'");
            }

            (uint nameHash, byte nameStart, byte nameEnd) = ParseTextureName(
                texture,
                assetName,
                index);
            MaterialSamplerState samplerState = sourceInspection.TextureSamplerStates[index];
            TextureSemantic semantic = imageRequirement.Semantic;
            bool hasWater = texture.TryGetProperty("water", out JsonElement waterElement) &&
                waterElement.ValueKind != JsonValueKind.Null;

            result.Add(new MaterialTextureDef
            {
                NameHash = nameHash,
                NameStart = nameStart,
                NameEnd = nameEnd,
                SamplerState = samplerState,
                Semantic = semantic,
                Image = hasWater ? null : image,
                Water = hasWater
                    ? ParseWater(waterElement, image, assetName, index)
                    : null
            });
            index++;
        }

        if (index != imageRequirements.Count)
            throw new InvalidOperationException("The material source inspection has extra texture rows.");
        return result.ToArray();
    }

    private static (uint Hash, byte Start, byte End) ParseTextureName(
        JsonElement texture,
        string assetName,
        int index)
    {
        if (texture.TryGetProperty("name", out JsonElement nameElement) &&
            nameElement.ValueKind != JsonValueKind.Null)
        {
            string name = RequireNonemptyString(nameElement, $"textures[{index}].name");
            RequireLatin1(name, assetName, $"textures[{index}].name");
            return (
                Iw3MaterialPropertyName.Hash(name),
                checked((byte)name[0]),
                checked((byte)name[^1]));
        }

        uint hash = RequireUInt32(texture, "nameHash", $"textures[{index}].nameHash");
        string start = RequireString(texture, "nameStart", $"textures[{index}].nameStart");
        string end = RequireString(texture, "nameEnd", $"textures[{index}].nameEnd");
        if (start.Length != 1 || end.Length != 1)
        {
            throw MaterialError(
                assetName,
                $"textures[{index}] nameStart and nameEnd must each contain one character");
        }
        RequireLatin1(start, assetName, $"textures[{index}].nameStart");
        RequireLatin1(end, assetName, $"textures[{index}].nameEnd");
        return (hash, checked((byte)start[0]), checked((byte)end[0]));
    }

    private static MaterialSamplerState ParseSamplerState(
        JsonElement texture,
        string assetName,
        int index)
    {
        JsonElement sampler = RequireProperty(
            texture,
            "samplerState",
            JsonValueKind.Object,
            $"textures[{index}].samplerState");
        MaterialSamplerState result = RequireString(
            sampler,
            "filter",
            $"textures[{index}].samplerState.filter") switch
        {
            "disabled" => MaterialSamplerState.FilterDisabled,
            "nearest" => MaterialSamplerState.FilterNearest,
            "linear" => MaterialSamplerState.FilterLinear,
            "aniso2x" => MaterialSamplerState.FilterAnisotropic2X,
            "aniso4x" => MaterialSamplerState.FilterAnisotropic4X,
            string value => throw MaterialError(
                assetName,
                $"textures[{index}] has unsupported sampler filter '{value}'")
        };
        result |= RequireString(
            sampler,
            "mipMap",
            $"textures[{index}].samplerState.mipMap") switch
        {
            "disabled" => MaterialSamplerState.MipMapDisabled,
            "nearest" => MaterialSamplerState.MipMapNearest,
            "linear" => MaterialSamplerState.MipMapLinear,
            string value => throw MaterialError(
                assetName,
                $"textures[{index}] has unsupported sampler mipMap '{value}'")
        };
        if (RequireBoolean(sampler, "clampU", $"textures[{index}].samplerState.clampU"))
            result |= MaterialSamplerState.ClampU;
        if (RequireBoolean(sampler, "clampV", $"textures[{index}].samplerState.clampV"))
            result |= MaterialSamplerState.ClampV;
        if (RequireBoolean(sampler, "clampW", $"textures[{index}].samplerState.clampW"))
            result |= MaterialSamplerState.ClampW;
        return result;
    }

    private static TextureSemantic ParseTextureSemantic(
        JsonElement texture,
        string assetName,
        int index) => RequireString(
            texture,
            "semantic",
            $"textures[{index}].semantic") switch
        {
            "2D" => TextureSemantic.TwoDimensional,
            "function" => TextureSemantic.Function,
            "colorMap" => TextureSemantic.ColorMap,
            "unused1" => TextureSemantic.DetailMap,
            "detailMap" => TextureSemantic.DetailMap,
            "unused2" => TextureSemantic.Unused2,
            "normalMap" => TextureSemantic.NormalMap,
            "unused3" => TextureSemantic.Unused3,
            "unused4" => TextureSemantic.Unused4,
            "specularMap" => TextureSemantic.SpecularMap,
            "unused5" => TextureSemantic.Unused5,
            "unused6" => TextureSemantic.Unused6,
            "waterMap" => TextureSemantic.WaterMap,
            string value => throw MaterialError(
                assetName,
                $"textures[{index}] has unsupported semantic '{value}'")
        };

    private static MaterialWater ParseWater(
        JsonElement water,
        GfxImageAsset image,
        string assetName,
        int textureIndex)
    {
        string path = $"textures[{textureIndex}].water";
        RequireKind(water, JsonValueKind.Object, path);
        int m = RequireInt32(water, "m", path + ".m");
        int n = RequireInt32(water, "n", path + ".n");
        if (m <= 0 || n <= 0)
            throw MaterialError(assetName, $"{path} dimensions must be positive");
        int elementCount;
        try
        {
            elementCount = checked(m * n);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"IW3 material '{assetName}' {path} dimensions are too large.",
                exception);
        }

        float floatTime = RequireFiniteSingle(water, "floatTime", path + ".floatTime");
        MaterialVec4 codeConstant = ParseVec4(water, "codeConstant", path, assetName);
        MaterialVec2 windDirection = ParseVec2(water, "winddir", path, assetName);
        (float[] h0x, float[] h0y) = DecodeComplexValues(
            RequireString(water, "h0", path + ".h0"),
            elementCount,
            assetName,
            path + ".h0");
        float[] wTerm = DecodeFloatValues(
            RequireString(water, "wTerm", path + ".wTerm"),
            elementCount,
            assetName,
            path + ".wTerm");

        return new MaterialWater
        {
            Writable = new MaterialWaterWritable(
                unchecked((uint)BitConverter.SingleToInt32Bits(floatTime))),
            M = m,
            N = n,
            Lx = RequireFiniteSingle(water, "lx", path + ".lx"),
            Lz = RequireFiniteSingle(water, "lz", path + ".lz"),
            Gravity = RequireFiniteSingle(water, "gravity", path + ".gravity"),
            WindVelocity = RequireFiniteSingle(water, "windvel", path + ".windvel"),
            WindDirection = windDirection,
            Amplitude = RequireFiniteSingle(water, "amplitude", path + ".amplitude"),
            CodeConstant = codeConstant,
            H0X = h0x,
            H0Y = h0y,
            WTerm = wTerm,
            Image = image
        };
    }

    private static MaterialConstantDef[] ParseConstants(
        JsonElement root,
        string assetName)
    {
        JsonElement array = RequireProperty(
            root,
            "constants",
            JsonValueKind.Array,
            "constants");
        var result = new List<MaterialConstantDef>();
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            JsonElement constant = RequireKind(
                element,
                JsonValueKind.Object,
                $"constants[{index}]");
            string serializedName;
            uint hash;
            if (constant.TryGetProperty("name", out JsonElement nameElement) &&
                nameElement.ValueKind != JsonValueKind.Null)
            {
                serializedName = RequireNonemptyString(
                    nameElement,
                    $"constants[{index}].name");
                RequireLatin1(serializedName, assetName, $"constants[{index}].name");
                hash = Iw3MaterialPropertyName.Hash(serializedName);
            }
            else
            {
                serializedName = RequireString(
                    constant,
                    "nameFragment",
                    $"constants[{index}].nameFragment");
                RequireLatin1(
                    serializedName,
                    assetName,
                    $"constants[{index}].nameFragment");
                hash = RequireUInt32(
                    constant,
                    "nameHash",
                    $"constants[{index}].nameHash");
            }

            result.Add(new MaterialConstantDef
            {
                NameHash = hash,
                NameBytes = EncodeConstantName(serializedName),
                Literal = ParseVec4(
                    constant,
                    "literal",
                    $"constants[{index}]",
                    assetName)
            });
            index++;
        }

        if (result.Count > byte.MaxValue)
            throw MaterialError(assetName, "constants contains more than 255 rows");
        return result.ToArray();
    }

    private static MaterialVec4 ParseVec4(
        JsonElement owner,
        string property,
        string ownerPath,
        string assetName)
    {
        JsonElement array = RequireProperty(
            owner,
            property,
            JsonValueKind.Array,
            ownerPath + "." + property);
        float[] values = array
            .EnumerateArray()
            .Select((value, index) => RequireFiniteSingle(
                value,
                $"{ownerPath}.{property}[{index}]"))
            .ToArray();
        if (values.Length != 4)
        {
            throw MaterialError(
                assetName,
                $"{ownerPath}.{property} must contain exactly four numbers");
        }
        return new MaterialVec4(values[0], values[1], values[2], values[3]);
    }

    private static MaterialVec2 ParseVec2(
        JsonElement owner,
        string property,
        string ownerPath,
        string assetName)
    {
        JsonElement array = RequireProperty(
            owner,
            property,
            JsonValueKind.Array,
            ownerPath + "." + property);
        float[] values = array
            .EnumerateArray()
            .Select((value, index) => RequireFiniteSingle(
                value,
                $"{ownerPath}.{property}[{index}]"))
            .ToArray();
        if (values.Length != 2)
        {
            throw MaterialError(
                assetName,
                $"{ownerPath}.{property} must contain exactly two numbers");
        }
        return new MaterialVec2(values[0], values[1]);
    }

    private static (float[] Real, float[] Imaginary) DecodeComplexValues(
        string encoded,
        int count,
        string assetName,
        string path)
    {
        byte[] bytes = DecodeBase64(encoded, assetName, path);
        int expectedLength = checked(count * 2 * sizeof(float));
        if (bytes.Length != expectedLength)
        {
            throw MaterialError(
                assetName,
                $"{path} decodes to {bytes.Length} bytes; expected {expectedLength}");
        }

        var real = new float[count];
        var imaginary = new float[count];
        for (int index = 0; index < count; index++)
        {
            real[index] = ReadFiniteSingle(bytes.AsSpan(index * 8, 4), assetName, path);
            imaginary[index] = ReadFiniteSingle(
                bytes.AsSpan(index * 8 + 4, 4),
                assetName,
                path);
        }
        return (real, imaginary);
    }

    private static float[] DecodeFloatValues(
        string encoded,
        int count,
        string assetName,
        string path)
    {
        byte[] bytes = DecodeBase64(encoded, assetName, path);
        int expectedLength = checked(count * sizeof(float));
        if (bytes.Length != expectedLength)
        {
            throw MaterialError(
                assetName,
                $"{path} decodes to {bytes.Length} bytes; expected {expectedLength}");
        }

        var result = new float[count];
        for (int index = 0; index < count; index++)
        {
            result[index] = ReadFiniteSingle(
                bytes.AsSpan(index * sizeof(float), sizeof(float)),
                assetName,
                path);
        }
        return result;
    }

    private static byte[] DecodeBase64(
        string value,
        string assetName,
        string path)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                $"IW3 material '{assetName}' {path} is not valid base64.",
                exception);
        }
    }

    private static float ReadFiniteSingle(
        ReadOnlySpan<byte> source,
        string assetName,
        string path)
    {
        float value = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(source));
        if (!float.IsFinite(value))
            throw MaterialError(assetName, $"{path} contains a non-finite float");
        return value;
    }

    private static byte[] EncodeConstantName(string value)
    {
        byte[] result = new byte[12];
        int byteCount = Math.Min(value.Length, result.Length);
        Encoding.Latin1.GetBytes(value.AsSpan(0, byteCount), result);
        return result;
    }

    private static void RequireLatin1(
        string value,
        string assetName,
        string path)
    {
        if (value.Any(character => character > byte.MaxValue))
            throw MaterialError(assetName, $"{path} contains a non-Latin-1 character");
    }

    private static uint Encode<T>(T value, int shift)
        where T : struct, Enum => Convert.ToUInt32(value, CultureInfo.InvariantCulture) << shift;

    private static string StatePath(int index, string field) =>
        $"stateBits[{index}].{field}";

    private static InvalidDataException UnsupportedStateValue(
        string assetName,
        int index,
        string field,
        string value) => MaterialError(
            assetName,
            $"stateBits[{index}].{field} has unsupported value '{value}'");

    private static InvalidDataException MaterialError(
        string assetName,
        string message) => new($"IW3 material '{assetName}' {message}.");

    private static JsonElement RequireProperty(
        JsonElement owner,
        string property,
        JsonValueKind kind,
        string path)
    {
        if (!owner.TryGetProperty(property, out JsonElement value))
            throw new InvalidDataException($"Missing required material property '{path}'.");
        return RequireKind(value, kind, path);
    }

    private static JsonElement RequireKind(
        JsonElement value,
        JsonValueKind kind,
        string path)
    {
        if (value.ValueKind != kind)
        {
            throw new InvalidDataException(
                $"Material property '{path}' must be {kind}, found {value.ValueKind}.");
        }
        return value;
    }

    private static string RequireString(
        JsonElement owner,
        string property,
        string path) => RequireString(
            RequireProperty(owner, property, JsonValueKind.String, path),
            path);

    private static string RequireString(JsonElement value, string path)
    {
        RequireKind(value, JsonValueKind.String, path);
        return value.GetString()!;
    }

    private static string RequireNonemptyString(
        JsonElement owner,
        string property,
        string path) => RequireNonemptyString(
            RequireProperty(owner, property, JsonValueKind.String, path),
            path);

    private static string RequireNonemptyString(JsonElement value, string path)
    {
        string result = RequireString(value, path);
        if (result.Length == 0)
            throw new InvalidDataException($"Material property '{path}' cannot be empty.");
        return result;
    }

    private static bool RequireBoolean(
        JsonElement owner,
        string property,
        string path)
    {
        if (!owner.TryGetProperty(property, out JsonElement value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                $"Material property '{path}' must be a boolean.");
        }
        return value.GetBoolean();
    }

    private static byte RequireByte(
        JsonElement owner,
        string property,
        string path)
    {
        uint value = RequireUInt32(owner, property, path);
        if (value > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"Material property '{path}' value {value} does not fit a byte.");
        }
        return (byte)value;
    }

    private static uint RequireUInt32(
        JsonElement owner,
        string property,
        string path)
    {
        JsonElement value = RequireProperty(owner, property, JsonValueKind.Number, path);
        if (!value.TryGetUInt32(out uint result))
        {
            throw new InvalidDataException(
                $"Material property '{path}' must be an unsigned 32-bit integer.");
        }
        return result;
    }

    private static int RequireInt32(
        JsonElement owner,
        string property,
        string path) => RequireInt32(
            RequireProperty(owner, property, JsonValueKind.Number, path),
            path);

    private static int RequireInt32(JsonElement value, string path)
    {
        RequireKind(value, JsonValueKind.Number, path);
        if (!value.TryGetInt32(out int result))
        {
            throw new InvalidDataException(
                $"Material property '{path}' must be a signed 32-bit integer.");
        }
        return result;
    }

    private static float RequireFiniteSingle(
        JsonElement owner,
        string property,
        string path) => RequireFiniteSingle(
            RequireProperty(owner, property, JsonValueKind.Number, path),
            path);

    private static float RequireFiniteSingle(JsonElement value, string path)
    {
        RequireKind(value, JsonValueKind.Number, path);
        if (!value.TryGetSingle(out float result) || !float.IsFinite(result))
        {
            throw new InvalidDataException(
                $"Material property '{path}' must be a finite single-precision number.");
        }
        return result;
    }
}
