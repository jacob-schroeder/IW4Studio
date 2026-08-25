using System.Buffers.Binary;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;

namespace IW4.AssetExchange.SourceFormat.Material;

/// <summary>
/// Writes PS3 IW4 materials in the OpenAssetTools material-v1 JSON source
/// format. State bits are decoded from the proven console load-bit words.
/// </summary>
public sealed class MaterialExchange
{
    private const uint UnrepresentedStateBits0Mask = 0x20000000;
    private const uint StencilFrontFieldsMask =
        GfxStateBitsEncoding.StencilFrontPassMask |
        GfxStateBitsEncoding.StencilFrontFailMask |
        GfxStateBitsEncoding.StencilFrontDepthFailMask |
        GfxStateBitsEncoding.StencilFrontFunctionMask;
    private const uint StencilBackFieldsMask =
        GfxStateBitsEncoding.StencilBackPassMask |
        GfxStateBitsEncoding.StencilBackFailMask |
        GfxStateBitsEncoding.StencilBackDepthFailMask |
        GfxStateBitsEncoding.StencilBackFunctionMask;

    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 4
    };

    private static readonly IReadOnlyDictionary<uint, string> KnownConstantNames =
        CreateKnownNameLookup(new string[]
        {
            "worldViewProjectionMatrix",
            "worldViewMatrix2",
            "worldViewMatrix1",
            "worldViewMatrix",
            "worldOutdoorLookupMatrix",
            "worldMatrix",
            "waterColor",
            "viewportDimensions",
            "viewProjectionMatrix",
            "uvScale",
            "uvAnimParms",
            "thermalColorOffset",
            "sunShadowmapPixelAdjust",
            "ssaoParms",
            "spotShadowmapPixelAdjust",
            "shadowmapSwitchPartition",
            "shadowmapScale",
            "shadowmapPolygonOffset",
            "shadowLookupMatrix",
            "renderTargetSize",
            "renderSourceSize",
            "projectionMatrix",
            "playlistPopulationParams",
            "pixelCostFracs",
            "pixelCostDecode",
            "particleCloudSparkColor2",
            "particleCloudSparkColor1",
            "particleCloudSparkColor0",
            "particleCloudMatrix2",
            "particleCloudMatrix1",
            "particleCloudMatrix",
            "particleCloudColor",
            "outdoorFeatherParms",
            "oceanUVAnimParmPaintedFoam",
            "oceanUVAnimParmOctave2",
            "oceanUVAnimParmOctave1",
            "oceanUVAnimParmOctave0",
            "oceanUVAnimParmFoam",
            "oceanUVAnimParmDetail1",
            "oceanUVAnimParmDetail0",
            "oceanScrollParms",
            "oceanMiscParms",
            "oceanFoamParms",
            "oceanAmplitude",
            "materialColor",
            "lightprobeAmbient",
            "lightingLookupScale",
            "lightSpotFactors",
            "lightSpotDir",
            "lightSpecular",
            "lightPosition",
            "lightFalloffPlacement",
            "lightDiffuse",
            "inverseWorldViewMatrix",
            "inverseViewProjectionMatrix",
            "inverseTransposeWorldViewMatrix",
            "heatMapDetail",
            "glowSetup",
            "glowApply",
            "gameTime",
            "fullscreenDistortion",
            "fogSunDir",
            "fogSunConsts",
            "fogSunColorLinear",
            "fogSunColorGamma",
            "fogConsts",
            "fogColorLinear",
            "fogColorGamma",
            "flagParms",
            "filterTap",
            "featherParms",
            "falloffParms",
            "falloffEndColor",
            "falloffBeginColor",
            "fadeEffect",
            "eyeOffsetParms",
            "eyeOffset",
            "envMapParms",
            "dustTint",
            "dustParms",
            "dustEyeParms",
            "dofRowDelta",
            "dofLerpScale",
            "dofLerpBias",
            "dofEquationViewModelAndFarBlur",
            "dofEquationScene",
            "distortionScale",
            "detailScale",
            "depthFromClip",
            "debugBumpmap",
            "colorTintQuadraticDelta",
            "colorTintDelta",
            "colorTintBase",
            "colorSaturationR",
            "colorSaturationG",
            "colorSaturationB",
            "colorObjMin",
            "colorObjMax",
            "colorMatrixR",
            "colorMatrixG",
            "colorMatrixB",
            "colorBias",
            "codeMeshArg",
            "clipSpaceLookupScale",
            "clipSpaceLookupOffset",
            "baseLightingCoords"
        });

    private static readonly IReadOnlyDictionary<uint, string> KnownTextureNames =
        CreateKnownNameLookup(new string[]
        {
            "attenuation",
            "attenuationSampler",
            "cinematicA",
            "cinematicASampler",
            "cinematicCb",
            "cinematicCbSampler",
            "cinematicCr",
            "cinematicCrSampler",
            "cinematicY",
            "cinematicYSampler",
            "colorMap",
            "colorMap1",
            "colorMap2",
            "colorMapPostSun",
            "colorMapPostSunSampler",
            "colorMapSampler",
            "colorMapSampler1",
            "colorMapSampler2",
            "cucoloris",
            "cucolorisSampler",
            "detailMap",
            "detailMapSampler",
            "dust",
            "dustSampler",
            "fadeMap",
            "fadeMapSampler",
            "floatZ",
            "floatZSampler",
            "grainMap",
            "grainMapSampler",
            "halfParticleColor",
            "halfParticleColorSampler",
            "halfParticleDepth",
            "halfParticleDepthSampler",
            "heatmap",
            "heatmapSampler",
            "lightmapPrimary",
            "lightmapSamplerPrimary",
            "lightmapSamplerSecondary",
            "lightmapSecondary",
            "lookupMap",
            "lookupMapSampler",
            "modelLighting",
            "modelLightingSampler",
            "normalMap",
            "normalMapSampler",
            "oceanColorRamp",
            "oceanColorRampSampler",
            "oceanDetailNormal",
            "oceanDetailNormalSampler",
            "oceanDisplacement",
            "oceanDisplacementSampler",
            "oceanEnv",
            "oceanEnvSampler",
            "oceanFoam",
            "oceanFoamSampler",
            "oceanHeightNormal",
            "oceanHeightNormalSampler",
            "oceanPaintedFoam",
            "oceanPaintedFoamSampler",
            "outdoorMap",
            "outdoorMapSampler",
            "population",
            "populationSampler",
            "reflectionProbe",
            "reflectionProbeSampler",
            "shadowmapSamplerSpot",
            "shadowmapSamplerSun",
            "shadowmapSpot",
            "shadowmapSun",
            "skyMap",
            "skyMapSampler",
            "specularMap",
            "specularMapSampler",
            "ssao",
            "ssaoSampler",
            "worldMap",
            "worldMapSampler"
        });

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        MaterialAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Info.Name,
            "Material");
        Validate(asset, assetName);
        string relativePath = GetSourcePath(assetName);

        return new SourceOutput(sourceDirectory).WriteBinaryBatch([
            (relativePath, stream => WriteJson(stream, asset, assetName))
        ]);
    }

    private static void Validate(MaterialAsset asset, string assetName)
    {
        if (asset.StateBitsEntries.Count != MaterialAsset.TechniqueSlotCount)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' has {asset.StateBitsEntries.Count} state-bit entries; expected {MaterialAsset.TechniqueSlotCount}.");
        }
        if (asset.TextureCount != asset.Textures.Count ||
            asset.ConstantCount != asset.Constants.Count ||
            asset.StateBitsCount != asset.StateBits.Count)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' table counts do not match their materialized rows.");
        }

        _ = CameraRegionName(asset.CameraRegion, assetName);
        if (asset.TechniqueSet is null)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' has no materialized technique set for its required source dependency.");
        }
        _ = SourceOutput.NormalizeReferencedAssetName(
            asset.TechniqueSet.Name,
            $"Material '{assetName}' technique set");

        for (int index = 0; index < asset.StateBitsEntries.Count; index++)
        {
            byte stateBitsIndex = asset.StateBitsEntries[index].StateBitsIndex;
            if (stateBitsIndex != byte.MaxValue && stateBitsIndex >= asset.StateBitsCount)
            {
                throw new InvalidDataException(
                    $"Material '{assetName}' state-bit entry {index} references row {stateBitsIndex}, outside its {asset.StateBitsCount} rows.");
            }
        }
        for (int index = 0; index < asset.StateBits.Count; index++)
        {
            GfxStateBits state = asset.StateBits[index] ??
                throw new InvalidDataException(
                    $"Material '{assetName}' state-bit row {index} is null.");
            if (state.LoadBits.Count != 2)
            {
                throw new InvalidDataException(
                    $"Material '{assetName}' state-bit row {index} has {state.LoadBits.Count} load words; expected 2.");
            }
            ValidateStateBits(state.LoadBits[0], state.LoadBits[1], assetName, index);
        }

        for (int index = 0; index < asset.Textures.Count; index++)
            ValidateTexture(asset.Textures[index], assetName, index);
        for (int index = 0; index < asset.Constants.Count; index++)
        {
            MaterialConstantDef constant = asset.Constants[index] ??
                throw new InvalidDataException(
                    $"Material '{assetName}' constant row {index} is null.");
            if (constant.NameBytes.Count != 12)
            {
                throw new InvalidDataException(
                    $"Material '{assetName}' constant row {index} has {constant.NameBytes.Count} name bytes; expected 12.");
            }
            ValidateConstantName(constant, assetName, index);
            RequireFinite(constant.Literal.X, assetName, $"constant {index} literal X");
            RequireFinite(constant.Literal.Y, assetName, $"constant {index} literal Y");
            RequireFinite(constant.Literal.Z, assetName, $"constant {index} literal Z");
            RequireFinite(constant.Literal.W, assetName, $"constant {index} literal W");
        }
    }

    private static void ValidateTexture(
        MaterialTextureDef texture,
        string assetName,
        int index)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _ = TextureSemanticName(texture.Semantic, assetName, index);
        _ = FilterName(texture.SamplerState, assetName, index);
        _ = MipMapName(texture.SamplerState, assetName, index);
        if (texture.NameStart > 0x7f || texture.NameEnd > 0x7f)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' texture row {index} has a name boundary byte that OAT's UTF-8 JSON form cannot represent as one character.");
        }

        if (texture.Semantic == TextureSemantic.WaterMap)
        {
            if (texture.Water is null)
            {
                throw new InvalidDataException(
                    $"Material '{assetName}' water texture row {index} has no materialized water parameters.");
            }
            if (texture.Image is not null)
            {
                throw new InvalidDataException(
                    $"Material '{assetName}' water texture row {index} retains an image outside its water union arm.");
            }
            ValidateWater(texture.Water, assetName, index);
            return;
        }

        if (texture.Water is not null)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' texture row {index} is not water semantic and cannot retain water parameters.");
        }
        if (texture.Image is null)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' image texture row {index} has no materialized image dependency.");
        }
    }

    private static void ValidateWater(
        MaterialWater water,
        string assetName,
        int textureIndex)
    {
        if (water.M < 0 || water.N < 0)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' water texture row {textureIndex} has negative dimensions {water.M}x{water.N}.");
        }

        int count;
        try
        {
            count = checked(water.M * water.N);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' water texture row {textureIndex} dimensions overflow.",
                exception);
        }

        if (water.H0X.Count != count || water.H0Y.Count != count)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' water texture row {textureIndex} requires {count} values in each H0 component array.");
        }
        if (water.WTerm.Count != count)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' water texture row {textureIndex} requires {count} WTerm values.");
        }
        if (water.Image is null)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' water texture row {textureIndex} has no materialized image dependency.");
        }

        RequireFinite(water.Writable.FloatTime, assetName, $"water texture {textureIndex} floatTime");
        RequireFinite(water.Lx, assetName, $"water texture {textureIndex} lx");
        RequireFinite(water.Lz, assetName, $"water texture {textureIndex} lz");
        RequireFinite(water.Gravity, assetName, $"water texture {textureIndex} gravity");
        RequireFinite(water.WindVelocity, assetName, $"water texture {textureIndex} wind velocity");
        RequireFinite(water.WindDirection.X, assetName, $"water texture {textureIndex} wind direction X");
        RequireFinite(water.WindDirection.Y, assetName, $"water texture {textureIndex} wind direction Y");
        RequireFinite(water.Amplitude, assetName, $"water texture {textureIndex} amplitude");
        RequireFinite(water.CodeConstant.X, assetName, $"water texture {textureIndex} code constant X");
        RequireFinite(water.CodeConstant.Y, assetName, $"water texture {textureIndex} code constant Y");
        RequireFinite(water.CodeConstant.Z, assetName, $"water texture {textureIndex} code constant Z");
        RequireFinite(water.CodeConstant.W, assetName, $"water texture {textureIndex} code constant W");
        foreach (float value in water.H0X.Concat(water.H0Y).Concat(water.WTerm))
            RequireFinite(value, assetName, $"water texture {textureIndex} spectrum");
    }

    private static void ValidateConstantName(
        MaterialConstantDef constant,
        string assetName,
        int index)
    {
        byte[] bytes = constant.NameBytes.ToArray();
        int nullIndex = Array.IndexOf(bytes, (byte)0);
        int fragmentLength = nullIndex < 0 ? bytes.Length : nullIndex;
        if (bytes.Take(fragmentLength).Any(value => value > 0x7f))
        {
            throw new InvalidDataException(
                $"Material '{assetName}' constant row {index} has name bytes that OAT's UTF-8 JSON form cannot preserve.");
        }
        if (nullIndex >= 0 && bytes.Skip(nullIndex).Any(value => value != 0))
        {
            throw new InvalidDataException(
                $"Material '{assetName}' constant row {index} has nonzero bytes after its name terminator that material-v1 cannot preserve.");
        }
    }

    private static void WriteJson(
        Stream stream,
        MaterialAsset asset,
        string assetName)
    {
        using (var writer = new Utf8JsonWriter(stream, JsonOptions))
        {
            writer.WriteStartObject();
            writer.WriteString(
                "$schema",
                "http://openassettools.dev/schema/material.v1.json");
            writer.WriteString("_game", "iw4");
            writer.WriteString("_type", "material");
            writer.WriteNumber("_version", 1);

            writer.WriteString(
                "cameraRegion",
                CameraRegionName(asset.CameraRegion, assetName));

            writer.WriteStartArray("constants");
            foreach (MaterialConstantDef constant in asset.Constants)
                WriteConstant(writer, constant);
            writer.WriteEndArray();

            writer.WriteStartArray("gameFlags");
            byte gameFlags = (byte)asset.Info.GameFlags;
            for (int bit = 0; bit < 8; bit++)
            {
                int value = 1 << bit;
                if ((gameFlags & value) != 0)
                    writer.WriteStringValue(value.ToString("X"));
            }
            writer.WriteEndArray();
            writer.WriteNumber("sortKey", (byte)asset.Info.SortKey);

            writer.WriteStartArray("stateBits");
            for (int index = 0; index < asset.StateBits.Count; index++)
            {
                GfxStateBits state = asset.StateBits[index];
                WriteStateBits(
                    writer,
                    state.LoadBits[0],
                    state.LoadBits[1],
                    assetName,
                    index);
            }
            writer.WriteEndArray();

            writer.WriteStartArray("stateBitsEntry");
            foreach (MaterialStateBitsEntry entry in asset.StateBitsEntries)
                writer.WriteNumberValue(unchecked((sbyte)entry.StateBitsIndex));
            writer.WriteEndArray();
            writer.WriteNumber("stateFlags", (byte)asset.StateFlags);
            writer.WriteNumber("surfaceTypeBits", (uint)asset.Info.SurfaceTypeBits);

            string techniqueSet = SourceOutput.NormalizeReferencedAssetName(
                asset.TechniqueSet!.Name,
                $"Material '{assetName}' technique set");
            writer.WriteString("techniqueSet", techniqueSet);

            writer.WriteStartObject("textureAtlas");
            writer.WriteNumber("columns", asset.Info.TextureAtlasColumnCount);
            writer.WriteNumber("rows", asset.Info.TextureAtlasRowCount);
            writer.WriteEndObject();

            writer.WriteStartArray("textures");
            for (int index = 0; index < asset.Textures.Count; index++)
                WriteTexture(writer, asset.Textures[index], assetName, index);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        stream.WriteByte((byte)'\n');
    }

    private static void WriteTexture(
        Utf8JsonWriter writer,
        MaterialTextureDef texture,
        string assetName,
        int index)
    {
        string imageName = texture.Semantic == TextureSemantic.WaterMap
            ? ReferencedImageName(texture.Water!.Image, assetName, index)
            : ReferencedImageName(texture.Image, assetName, index);
        bool hasKnownName =
            KnownTextureNames.TryGetValue(texture.NameHash, out string? knownName) &&
            knownName is not null &&
            knownName.Length != 0 &&
            knownName[0] == texture.NameStart &&
            knownName[^1] == texture.NameEnd;

        writer.WriteStartObject();
        writer.WriteString("image", imageName);
        if (hasKnownName)
        {
            writer.WriteString("name", knownName);
        }
        else
        {
            writer.WriteString("nameEnd", ((char)texture.NameEnd).ToString());
            writer.WriteNumber("nameHash", texture.NameHash);
            writer.WriteString("nameStart", ((char)texture.NameStart).ToString());
        }
        writer.WriteStartObject("samplerState");
        writer.WriteBoolean("clampU", (texture.SamplerState & MaterialSamplerState.ClampU) != 0);
        writer.WriteBoolean("clampV", (texture.SamplerState & MaterialSamplerState.ClampV) != 0);
        writer.WriteBoolean("clampW", (texture.SamplerState & MaterialSamplerState.ClampW) != 0);
        writer.WriteString("filter", FilterName(texture.SamplerState, assetName, index));
        writer.WriteString("mipMap", MipMapName(texture.SamplerState, assetName, index));
        writer.WriteEndObject();
        writer.WriteString(
            "semantic",
            TextureSemanticName(texture.Semantic, assetName, index));
        if (texture.Semantic == TextureSemantic.WaterMap)
            WriteWater(writer, texture.Water!);
        writer.WriteEndObject();
    }

    private static string ReferencedImageName(
        GfxImageAsset? image,
        string assetName,
        int textureIndex) => image is null
        ? string.Empty
        : SourceOutput.NormalizeReferencedAssetName(
            image.Name,
            $"Material '{assetName}' texture row {textureIndex} image");

    private static void WriteWater(Utf8JsonWriter writer, MaterialWater water)
    {
        writer.WriteStartObject("water");
        writer.WriteNumber("amplitude", water.Amplitude);
        writer.WriteStartArray("codeConstant");
        writer.WriteNumberValue(water.CodeConstant.X);
        writer.WriteNumberValue(water.CodeConstant.Y);
        writer.WriteNumberValue(water.CodeConstant.Z);
        writer.WriteNumberValue(water.CodeConstant.W);
        writer.WriteEndArray();
        writer.WriteNumber("floatTime", water.Writable.FloatTime);
        writer.WriteNumber("gravity", water.Gravity);
        writer.WriteString("h0", EncodeH0(water));
        writer.WriteNumber("lx", water.Lx);
        writer.WriteNumber("lz", water.Lz);
        writer.WriteNumber("m", water.M);
        writer.WriteNumber("n", water.N);
        writer.WriteString("wTerm", EncodeFloats(water.WTerm));
        writer.WriteStartArray("winddir");
        writer.WriteNumberValue(water.WindDirection.X);
        writer.WriteNumberValue(water.WindDirection.Y);
        writer.WriteEndArray();
        writer.WriteNumber("windvel", water.WindVelocity);
        writer.WriteEndObject();
    }

    private static void WriteConstant(
        Utf8JsonWriter writer,
        MaterialConstantDef constant)
    {
        byte[] nameBytes = constant.NameBytes.ToArray();
        int nullIndex = Array.IndexOf(nameBytes, (byte)0);
        int fragmentLength = nullIndex < 0 ? nameBytes.Length : nullIndex;
        string fragment = Encoding.Latin1.GetString(nameBytes, 0, fragmentLength);
        string? resolvedName = null;
        if (HashString(fragment) == constant.NameHash)
        {
            resolvedName = fragment;
        }
        else if (KnownConstantNames.TryGetValue(
                     constant.NameHash,
                     out string? knownName) &&
                 knownName is not null &&
                 MatchesSerializedConstantName(knownName, nameBytes))
        {
            resolvedName = knownName;
        }

        writer.WriteStartObject();
        writer.WriteStartArray("literal");
        writer.WriteNumberValue(constant.Literal.X);
        writer.WriteNumberValue(constant.Literal.Y);
        writer.WriteNumberValue(constant.Literal.Z);
        writer.WriteNumberValue(constant.Literal.W);
        writer.WriteEndArray();
        if (resolvedName is not null)
        {
            writer.WriteString("name", resolvedName);
        }
        else
        {
            writer.WriteString("nameFragment", fragment);
            writer.WriteNumber("nameHash", constant.NameHash);
        }
        writer.WriteEndObject();
    }

    private static void WriteStateBits(
        Utf8JsonWriter writer,
        uint word0,
        uint word1,
        string assetName,
        int index)
    {
        writer.WriteStartObject();
        writer.WriteString("alphaTest", AlphaTestName(word0, assetName, index));
        writer.WriteString("blendOpAlpha", BlendOperationName(Field(word0,
            GfxStateBitsEncoding.BlendOperationAlphaMask,
            GfxStateBitsEncoding.BlendOperationAlphaShift), assetName, index));
        writer.WriteString("blendOpRgb", BlendOperationName(Field(word0,
            GfxStateBitsEncoding.BlendOperationRgbMask,
            GfxStateBitsEncoding.BlendOperationRgbShift), assetName, index));
        writer.WriteBoolean("colorWriteAlpha", HasFlag(word0, GfxStateBits0Flags.ColorWriteAlpha));
        writer.WriteBoolean("colorWriteRgb", HasFlag(word0, GfxStateBits0Flags.ColorWriteRgb));
        writer.WriteString("cullFace", CullFaceName(Field(word0,
            GfxStateBitsEncoding.CullFaceMask,
            GfxStateBitsEncoding.CullFaceShift), assetName, index));
        writer.WriteString("depthTest", DepthTestName(word1, assetName, index));
        writer.WriteBoolean("depthWrite", HasFlag(word1, GfxStateBits1Flags.DepthWrite));
        writer.WriteString("dstBlendAlpha", BlendName(Field(word0,
            GfxStateBitsEncoding.DestinationBlendAlphaMask,
            GfxStateBitsEncoding.DestinationBlendAlphaShift), assetName, index));
        writer.WriteString("dstBlendRgb", BlendName(Field(word0,
            GfxStateBitsEncoding.DestinationBlendRgbMask,
            GfxStateBitsEncoding.DestinationBlendRgbShift), assetName, index));
        writer.WriteBoolean("gammaWrite", HasFlag(word0, GfxStateBits0Flags.GammaWrite));
        writer.WriteString("polygonOffset", PolygonOffsetName(Field(word1,
            GfxStateBitsEncoding.PolygonOffsetMask,
            GfxStateBitsEncoding.PolygonOffsetShift), assetName, index));
        writer.WriteBoolean("polymodeLine", HasFlag(word0, GfxStateBits0Flags.PolygonModeLine));
        writer.WriteString("srcBlendAlpha", BlendName(Field(word0,
            GfxStateBitsEncoding.SourceBlendAlphaMask,
            GfxStateBitsEncoding.SourceBlendAlphaShift), assetName, index));
        writer.WriteString("srcBlendRgb", BlendName(Field(word0,
            GfxStateBitsEncoding.SourceBlendRgbMask,
            GfxStateBitsEncoding.SourceBlendRgbShift), assetName, index));
        if (HasFlag(word1, GfxStateBits1Flags.StencilBackFaceIndependent))
            WriteStencil(writer, "stencilBack", word1, back: true);
        if (HasFlag(word1, GfxStateBits1Flags.StencilEnabled))
            WriteStencil(writer, "stencilFront", word1, back: false);
        writer.WriteEndObject();
    }

    private static void WriteStencil(
        Utf8JsonWriter writer,
        string propertyName,
        uint word,
        bool back)
    {
        int passShift = back
            ? GfxStateBitsEncoding.StencilBackPassShift
            : GfxStateBitsEncoding.StencilFrontPassShift;
        int failShift = back
            ? GfxStateBitsEncoding.StencilBackFailShift
            : GfxStateBitsEncoding.StencilFrontFailShift;
        int zFailShift = back
            ? GfxStateBitsEncoding.StencilBackDepthFailShift
            : GfxStateBitsEncoding.StencilFrontDepthFailShift;
        int functionShift = back
            ? GfxStateBitsEncoding.StencilBackFunctionShift
            : GfxStateBitsEncoding.StencilFrontFunctionShift;

        writer.WriteStartObject(propertyName);
        writer.WriteString("fail", StencilOperationName((word >> failShift) & 7));
        writer.WriteString("func", StencilFunctionName((word >> functionShift) & 7));
        writer.WriteString("pass", StencilOperationName((word >> passShift) & 7));
        writer.WriteString("zfail", StencilOperationName((word >> zFailShift) & 7));
        writer.WriteEndObject();
    }

    private static void ValidateStateBits(
        uint word0,
        uint word1,
        string assetName,
        int index)
    {
        if ((word0 & UnrepresentedStateBits0Mask) != 0)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' state-bit row {index} sets word-0 bit 29, which material-v1 cannot represent.");
        }
        if (HasFlag(word0, GfxStateBits0Flags.AlphaTestDisabled) &&
            (word0 & GfxStateBitsEncoding.AlphaTestMask) != 0)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' state-bit row {index} retains alpha-test bits while alpha testing is disabled.");
        }
        if (HasFlag(word1, GfxStateBits1Flags.DepthTestDisabled) &&
            (word1 & GfxStateBitsEncoding.DepthTestMask) != 0)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' state-bit row {index} retains depth-test bits while depth testing is disabled.");
        }
        if (!HasFlag(word1, GfxStateBits1Flags.StencilEnabled) &&
            (word1 & StencilFrontFieldsMask) != 0)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' state-bit row {index} retains front-stencil fields while front stencil is disabled.");
        }
        if (!HasFlag(word1, GfxStateBits1Flags.StencilBackFaceIndependent) &&
            (word1 & StencilBackFieldsMask) != 0)
        {
            throw new InvalidDataException(
                $"Material '{assetName}' state-bit row {index} retains back-stencil fields while back stencil is disabled.");
        }

        _ = BlendName(Field(word0, GfxStateBitsEncoding.SourceBlendRgbMask,
            GfxStateBitsEncoding.SourceBlendRgbShift), assetName, index);
        _ = BlendName(Field(word0, GfxStateBitsEncoding.DestinationBlendRgbMask,
            GfxStateBitsEncoding.DestinationBlendRgbShift), assetName, index);
        _ = BlendOperationName(Field(word0, GfxStateBitsEncoding.BlendOperationRgbMask,
            GfxStateBitsEncoding.BlendOperationRgbShift), assetName, index);
        _ = AlphaTestName(word0, assetName, index);
        _ = CullFaceName(Field(word0, GfxStateBitsEncoding.CullFaceMask,
            GfxStateBitsEncoding.CullFaceShift), assetName, index);
        _ = BlendName(Field(word0, GfxStateBitsEncoding.SourceBlendAlphaMask,
            GfxStateBitsEncoding.SourceBlendAlphaShift), assetName, index);
        _ = BlendName(Field(word0, GfxStateBitsEncoding.DestinationBlendAlphaMask,
            GfxStateBitsEncoding.DestinationBlendAlphaShift), assetName, index);
        _ = BlendOperationName(Field(word0, GfxStateBitsEncoding.BlendOperationAlphaMask,
            GfxStateBitsEncoding.BlendOperationAlphaShift), assetName, index);
        _ = DepthTestName(word1, assetName, index);
        _ = PolygonOffsetName(Field(word1,
            GfxStateBitsEncoding.PolygonOffsetMask,
            GfxStateBitsEncoding.PolygonOffsetShift), assetName, index);
    }

    private static uint Field(uint word, uint mask, int shift) =>
        (word & mask) >> shift;

    private static bool HasFlag(uint word, GfxStateBits0Flags flag) =>
        (word & (uint)flag) != 0;

    private static bool HasFlag(uint word, GfxStateBits1Flags flag) =>
        (word & (uint)flag) != 0;

    private static string BlendName(uint value, string assetName, int index) =>
        value switch
        {
            0 => "disabled",
            1 => "zero",
            2 => "one",
            3 => "srccolor",
            4 => "invsrccolor",
            5 => "srcalpha",
            6 => "invsrcalpha",
            7 => "destalpha",
            8 => "invdestalpha",
            9 => "destcolor",
            10 => "invdestcolor",
            _ => throw StateValueError(assetName, index, "blend", value)
        };

    private static string BlendOperationName(
        uint value,
        string assetName,
        int index) => value switch
        {
            0 => "disabled",
            1 => "add",
            2 => "subtract",
            3 => "revsubtract",
            4 => "min",
            5 => "max",
            _ => throw StateValueError(assetName, index, "blend operation", value)
        };

    private static string AlphaTestName(uint word, string assetName, int index)
    {
        if (HasFlag(word, GfxStateBits0Flags.AlphaTestDisabled))
            return "disabled";
        return Field(word, GfxStateBitsEncoding.AlphaTestMask,
            GfxStateBitsEncoding.AlphaTestShift) switch
        {
            1 => "gt0",
            2 => "lt128",
            3 => "ge128",
            uint value => throw StateValueError(assetName, index, "alpha test", value)
        };
    }

    private static string CullFaceName(
        uint value,
        string assetName,
        int index) => value switch
        {
            1 => "none",
            2 => "back",
            3 => "front",
            _ => throw StateValueError(assetName, index, "cull face", value)
        };

    private static string DepthTestName(uint word, string assetName, int index)
    {
        if (HasFlag(word, GfxStateBits1Flags.DepthTestDisabled))
            return "disabled";
        return Field(word, GfxStateBitsEncoding.DepthTestMask,
            GfxStateBitsEncoding.DepthTestShift) switch
        {
            0 => "always",
            1 => "less",
            2 => "equal",
            3 => "less_equal",
            uint value => throw StateValueError(assetName, index, "depth test", value)
        };
    }

    private static string PolygonOffsetName(
        uint value,
        string assetName,
        int index) => value switch
        {
            0 => "offset0",
            1 => "offset1",
            2 => "offset2",
            3 => throw new NotSupportedException(
                $"Material '{assetName}' state-bit row {index} uses PS3 polygon-offset inherit, which OAT IW4 material-v1 names as the different PC shadowmap mode."),
            _ => throw StateValueError(
                assetName,
                index,
                "polygon offset",
                value)
        };

    private static string StencilOperationName(uint value) => value switch
    {
        0 => "keep",
        1 => "zero",
        2 => "replace",
        3 => "incrsat",
        4 => "decrsat",
        5 => "invert",
        6 => "incr",
        _ => "decr"
    };

    private static string StencilFunctionName(uint value) => value switch
    {
        0 => "never",
        1 => "less",
        2 => "equal",
        3 => "lessequal",
        4 => "greater",
        5 => "notequal",
        6 => "greaterequal",
        _ => "always"
    };

    private static string FilterName(
        MaterialSamplerState state,
        string assetName,
        int index) => (state & MaterialSamplerState.FilterMask) switch
        {
            MaterialSamplerState.FilterDisabled => "disabled",
            MaterialSamplerState.FilterNearest => "nearest",
            MaterialSamplerState.FilterLinear => "linear",
            MaterialSamplerState.FilterAnisotropic2X => "aniso2x",
            MaterialSamplerState.FilterAnisotropic4X => "aniso4x",
            MaterialSamplerState value => throw new InvalidDataException(
                $"Material '{assetName}' texture row {index} has unsupported filter bits 0x{(byte)value:X2}.")
        };

    private static string MipMapName(
        MaterialSamplerState state,
        string assetName,
        int index) => (state & MaterialSamplerState.MipMapMask) switch
        {
            MaterialSamplerState.MipMapDisabled => "disabled",
            MaterialSamplerState.MipMapNearest => "nearest",
            MaterialSamplerState.MipMapLinear => "linear",
            MaterialSamplerState value => throw new InvalidDataException(
                $"Material '{assetName}' texture row {index} has unsupported mip-map bits 0x{(byte)value:X2}.")
        };

    private static string TextureSemanticName(
        TextureSemantic semantic,
        string assetName,
        int index) => semantic switch
        {
            TextureSemantic.TwoDimensional => "2D",
            TextureSemantic.Function => "function",
            TextureSemantic.ColorMap => "colorMap",
            TextureSemantic.DetailMap => "detailMap",
            TextureSemantic.Unused2 => "unused2",
            TextureSemantic.NormalMap => "normalMap",
            TextureSemantic.Unused3 => "unused3",
            TextureSemantic.Unused4 => "unused4",
            TextureSemantic.SpecularMap => "specularMap",
            TextureSemantic.Unused5 => "unused5",
            TextureSemantic.Unused6 => "unused6",
            TextureSemantic.WaterMap => "waterMap",
            _ => throw new InvalidDataException(
                $"Material '{assetName}' texture row {index} has unsupported semantic {semantic}.")
        };

    private static string CameraRegionName(
        GfxCameraRegionType cameraRegion,
        string assetName) => cameraRegion switch
        {
            GfxCameraRegionType.LitOpaque => "litOpaque",
            GfxCameraRegionType.LitTrans => "litTrans",
            GfxCameraRegionType.Emissive => "emissive",
            GfxCameraRegionType.DepthHack => "depthHack",
            GfxCameraRegionType.None => "none",
            _ => throw new NotSupportedException(
                $"Material '{assetName}' camera region {cameraRegion} has no IW4 OpenAssetTools source value.")
        };

    private static InvalidDataException StateValueError(
        string assetName,
        int index,
        string field,
        uint value) => new(
            $"Material '{assetName}' state-bit row {index} has unsupported {field} value {value}.");

    private static string EncodeH0(MaterialWater water)
    {
        if (water.H0X.Count == 0 && water.H0Y.Count == 0)
            return string.Empty;

        byte[] bytes = new byte[checked(water.H0X.Count * 2 * sizeof(float))];
        for (int index = 0; index < water.H0X.Count; index++)
        {
            WriteFloat(bytes.AsSpan(index * 8, 4), water.H0X[index]);
            WriteFloat(bytes.AsSpan(index * 8 + 4, 4), water.H0Y[index]);
        }
        return Convert.ToBase64String(bytes);
    }

    private static string EncodeFloats(IReadOnlyList<float> values)
    {
        if (values.Count == 0)
            return string.Empty;

        byte[] bytes = new byte[checked(values.Count * sizeof(float))];
        for (int index = 0; index < values.Count; index++)
            WriteFloat(bytes.AsSpan(index * 4, 4), values[index]);
        return Convert.ToBase64String(bytes);
    }

    private static void WriteFloat(Span<byte> destination, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            destination,
            BitConverter.SingleToInt32Bits(value));

    private static bool MatchesSerializedConstantName(
        string name,
        IReadOnlyList<byte> serialized)
    {
        for (int index = 0; index < serialized.Count; index++)
        {
            byte expected = index < name.Length
                ? checked((byte)name[index])
                : (byte)0;
            if (serialized[index] != expected)
                return false;
        }
        return true;
    }

    private static IReadOnlyDictionary<uint, string> CreateKnownNameLookup(
        IEnumerable<string> names)
    {
        var lookup = new Dictionary<uint, string>();
        foreach (string name in names)
            lookup.TryAdd(HashString(name), name);
        return lookup;
    }

    internal static bool TryGetKnownSourcePropertyName(
        uint hash,
        out string name)
    {
        if (KnownConstantNames.TryGetValue(hash, out string? constantName))
        {
            name = constantName;
            return true;
        }
        if (KnownTextureNames.TryGetValue(hash, out string? textureName))
        {
            name = textureName;
            return true;
        }

        name = string.Empty;
        return false;
    }

    internal static uint HashSourcePropertyName(string value) =>
        HashString(value);

    private static uint HashString(string value)
    {
        uint hash = 0;
        foreach (char character in value)
            hash = unchecked(hash * 33u ^ (byte)(character | 0x20));
        return hash;
    }

    private static void RequireFinite(
        float value,
        string assetName,
        string field)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                $"Material '{assetName}' {field} is not finite.");
        }
    }

    private static string GetSourcePath(string assetName)
    {
        string fileName = assetName;
        if (fileName[0] == '*')
        {
            fileName = fileName.Replace('*', '_');
            int parenthesis = fileName.IndexOf('(');
            if (parenthesis >= 0)
                fileName = fileName[..parenthesis];
            fileName = $"generated/{fileName}";
        }
        return $"materials/{fileName}.json";
    }
}
