using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;
using IW4.FastFiles.Zone;

namespace IW4.AssetExchange.XModel;

public enum WeaponCamoStyle
{
    Static,
    Animated
}

public sealed record WeaponCamoCompileRequest(
    XModelAsset SourceModel,
    MaterialAsset SourceMaterial,
    XModelImportImage ColorImage,
    WeaponCamoStyle Style,
    float LoopSeconds,
    MaterialTechniqueSetAsset? AnimatedTechniqueSet,
    string ScopeIdentity);

public sealed record WeaponCamoCompileResult(
    XModelAsset Model,
    MaterialAsset Material,
    GfxImageAsset Image,
    IReadOnlyList<BaseAsset> Providers);

/// <summary>
/// Compiles one isolated weapon-camo XModel dependency closure from a stock
/// XModel material and decoded RGBA pixels.
/// </summary>
public static class WeaponCamoCompiler
{
    public const string AnimatedTechniqueSetName = "weapon_camo_animated";
    public const string AnimatedTechniqueSetTemplateName = "m_l_sm_ua_b0c0n0sf0";
    public const uint UvAnimParmsHash = 0x70EBAF95;

    private static readonly byte[] UvAnimParmsName =
        Encoding.ASCII.GetBytes("uvAnimParms\0");

    public static bool TryCompile(
        WeaponCamoCompileRequest request,
        out WeaponCamoCompileResult? result,
        out string? blocker)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceModel);
        ArgumentNullException.ThrowIfNull(request.SourceMaterial);
        ArgumentNullException.ThrowIfNull(request.ColorImage);
        result = null;
        blocker = null;

        if (!Enum.IsDefined(request.Style))
        {
            blocker = $"Unsupported weapon camo style {request.Style}.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.ScopeIdentity))
        {
            blocker = "Weapon camo scope identity is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.SourceModel.Name) ||
            request.SourceModel.Name.StartsWith(','))
        {
            blocker = "The selected weapon model has no full asset identity.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.SourceMaterial.Info.Name) ||
            request.SourceMaterial.Info.Name.StartsWith(','))
        {
            blocker = "The selected weapon material has no full asset identity.";
            return false;
        }
        if (request.SourceModel.NumSurfs != request.SourceModel.Materials.Count ||
            request.SourceModel.NumSurfs != request.SourceModel.InvHighMipRadius.Count)
        {
            blocker = "The selected weapon model has inconsistent material rows.";
            return false;
        }
        if (request.SourceModel.Lods.Count != 4 || request.SourceModel.NumLods > 4)
        {
            blocker = "The selected weapon model does not retain four native LOD rows.";
            return false;
        }
        for (int index = 0; index < request.SourceModel.NumLods; index++)
        {
            XModelLodInfo lod = request.SourceModel.Lods[index];
            if (lod.ModelSurfs is not { } modelSurfs ||
                string.IsNullOrWhiteSpace(modelSurfs.Name) ||
                modelSurfs.Name.StartsWith(',') ||
                lod.NumSurfs != modelSurfs.NumSurfs ||
                modelSurfs.NumSurfs != modelSurfs.Surfaces.Count ||
                modelSurfs.PartBits.Count != 6)
            {
                blocker = $"The selected weapon model LOD {index} has no full XModelSurfs body.";
                return false;
            }
        }
        if (request.SourceMaterial.Textures.Count(row =>
                row.Semantic == TextureSemantic.ColorMap) != 1)
        {
            blocker = "The selected material must have exactly one ColorMap texture row.";
            return false;
        }
        if (request.SourceMaterial.Textures.Any(row =>
                row.Semantic == TextureSemantic.WaterMap || row.Water is not null))
        {
            blocker = "Water materials cannot be used as weapon camo templates.";
            return false;
        }
        if (!XModelImportedMaterialCompiler.TryResolveTemplateAlphaMode(
                request.SourceMaterial,
                out XModelImportAlphaMode alphaMode,
                out float? alphaCutoff))
        {
            blocker = "The selected material has no single proven camera-color alpha behavior.";
            return false;
        }

        MaterialTechniqueSetAsset? techniqueSetTemplate = request.Style switch
        {
            WeaponCamoStyle.Static => request.SourceMaterial.TechniqueSet,
            WeaponCamoStyle.Animated => request.AnimatedTechniqueSet,
            _ => null
        };
        if (!ValidateTechniqueSet(
                techniqueSetTemplate,
                request.Style == WeaponCamoStyle.Animated,
                out blocker))
        {
            return false;
        }
        MaterialTechniqueSetAsset techniqueSet =
            request.Style == WeaponCamoStyle.Animated
                ? BootstrapAnimatedTechniqueSet(techniqueSetTemplate)
                : techniqueSetTemplate;
        if (request.Style == WeaponCamoStyle.Animated &&
            (!float.IsFinite(request.LoopSeconds) ||
             request.LoopSeconds < 1f ||
             request.LoopSeconds > 50f))
        {
            blocker = "Animated camo loop duration must be between 1 and 50 seconds.";
            return false;
        }

        MaterialConstantDef[] constants;
        if (!TryCreateConstants(request, out constants, out blocker))
            return false;
        if (!MaterialSatisfiesTechnique(
                request.SourceMaterial,
                constants,
                techniqueSet,
                out blocker))
        {
            return false;
        }

        try
        {
            string digest = ComputeDigest(request, techniqueSet);
            string modelPart = SafeNamePart(request.SourceModel.Name, "weapon", 44);
            string materialPart = SafeNamePart(
                request.SourceMaterial.Info.Name,
                "material",
                36);
            string modelName = $"{modelPart}_studio_camo_{digest}";
            string materialName = $"{modelPart}_studio_{materialPart}_{digest}";
            string imageName = $"{modelPart}_studio_camo_{digest}_col";

            var importedMaterial = new XModelImportMaterial(
                Vector4.One,
                request.ColorImage,
                NormalImage: null,
                NormalScale: 1f,
                DoubleSided: false,
                alphaMode,
                alphaCutoff ?? 0.5f,
                Warnings: []);
            GfxImageAsset image = XModelImportedMaterialCompiler.CreateColorImage(
                imageName,
                importedMaterial,
                useWeaponCamoStorage: true);
            MaterialTextureDef colorRow = request.SourceMaterial.Textures.Single(row =>
                row.Semantic == TextureSemantic.ColorMap);
            MaterialAsset material = XModelImportedMaterialCompiler.CloneMaterialTemplate(
                request.SourceMaterial,
                materialName,
                colorRow,
                image,
                normalRow: null,
                normalImage: null,
                doubleSided: false,
                techniqueSet,
                constants);
            XModelAsset model = CloneModel(
                request.SourceModel,
                request.SourceMaterial,
                material,
                modelName,
                out int replacementCount);
            if (replacementCount == 0)
            {
                blocker = "The selected material is not used by the selected weapon model.";
                return false;
            }

            IReadOnlyList<BaseAsset> providers = CreateProviderClosure(
                model,
                material,
                image,
                techniqueSet!);
            result = new WeaponCamoCompileResult(
                model,
                material,
                image,
                providers);
            return true;
        }
        catch (Exception exception) when (exception is
            InvalidDataException or OverflowException or ArgumentException)
        {
            blocker = exception.Message;
            result = null;
            return false;
        }
    }

    private static bool TryCreateConstants(
        WeaponCamoCompileRequest request,
        out MaterialConstantDef[] constants,
        out string? blocker)
    {
        blocker = null;
        constants = request.SourceMaterial.Constants.Select(CloneConstant).ToArray();
        if (request.Style == WeaponCamoStyle.Static)
        {
            if (string.Equals(
                    CanonicalName(request.SourceMaterial.TechniqueSet?.Name ?? string.Empty),
                    AnimatedTechniqueSetName,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    CanonicalName(request.SourceMaterial.TechniqueSet?.Name ?? string.Empty),
                    AnimatedTechniqueSetTemplateName,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (constants.Count(row => row.NameHash == UvAnimParmsHash) > 1)
                {
                    blocker = "The selected material contains duplicate uvAnimParms constants.";
                    return false;
                }
                int uvAnimIndex = Array.FindIndex(constants, row =>
                    row.NameHash == UvAnimParmsHash);
                if (uvAnimIndex >= 0)
                {
                    constants[uvAnimIndex] = new MaterialConstantDef
                    {
                        NameHash = UvAnimParmsHash,
                        NameBytes = UvAnimParmsName.ToArray(),
                        Literal = new MaterialVec4(0f, 0f, 0f, 0f)
                    };
                }
            }
            return true;
        }
        if (constants.Count(row => row.NameHash == UvAnimParmsHash) > 1)
        {
            blocker = "The selected material contains duplicate uvAnimParms constants.";
            return false;
        }

        float uvRate = -1f / request.LoopSeconds;
        if (!float.IsFinite(uvRate))
        {
            blocker = "Animated camo loop duration produces a non-finite UV rate.";
            return false;
        }
        var uvAnim = new MaterialConstantDef
        {
            NameHash = UvAnimParmsHash,
            NameBytes = UvAnimParmsName.ToArray(),
            Literal = new MaterialVec4(0f, uvRate, 0f, 0f)
        };
        int existing = Array.FindIndex(constants, row =>
            row.NameHash == UvAnimParmsHash);
        if (existing >= 0)
        {
            constants[existing] = uvAnim;
            return true;
        }
        if (constants.Length == byte.MaxValue)
        {
            blocker = "The selected material cannot accept another material constant.";
            return false;
        }
        if (!constants.Zip(constants.Skip(1)).All(pair =>
                pair.First.NameHash < pair.Second.NameHash))
        {
            blocker = "The selected material constants are not strictly ordered by name hash.";
            return false;
        }

        int insertionIndex = constants.TakeWhile(row =>
            row.NameHash < UvAnimParmsHash).Count();
        constants = [
            .. constants.Take(insertionIndex),
            uvAnim,
            .. constants.Skip(insertionIndex)
        ];
        return true;
    }

    private static bool ValidateTechniqueSet(
        [NotNullWhen(true)]
        MaterialTechniqueSetAsset? techniqueSet,
        bool requireAnimatedUa,
        out string? blocker)
    {
        blocker = null;
        if (techniqueSet is null || string.IsNullOrWhiteSpace(techniqueSet.Name))
        {
            blocker = requireAnimatedUa
                ? $"The full {AnimatedTechniqueSetTemplateName} TechniqueSet template is not loaded."
                : "The selected material has no full TechniqueSet.";
            return false;
        }
        if (techniqueSet.Name.StartsWith(','))
        {
            blocker = $"TechniqueSet '{CanonicalName(techniqueSet.Name)}' is only a reference; its full body is required.";
            return false;
        }
        if (requireAnimatedUa &&
            !string.Equals(
                CanonicalName(techniqueSet.Name),
                AnimatedTechniqueSetTemplateName,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                CanonicalName(techniqueSet.Name),
                AnimatedTechniqueSetName,
                StringComparison.OrdinalIgnoreCase))
        {
            blocker = $"Animated camo bootstrapping requires TechniqueSet template " +
                $"'{AnimatedTechniqueSetTemplateName}'.";
            return false;
        }
        if (techniqueSet.TechniqueSlots.Count != MaterialAsset.TechniqueSlotCount)
        {
            blocker = $"TechniqueSet '{techniqueSet.Name}' does not retain all native technique slots.";
            return false;
        }

        bool hasTechnique = false;
        bool animatedLit = false;
        for (int index = 0; index < techniqueSet.TechniqueSlots.Count; index++)
        {
            MaterialTechniqueSlot slot = techniqueSet.TechniqueSlots[index];
            if (slot.Index != index)
            {
                blocker = $"TechniqueSet '{techniqueSet.Name}' has an out-of-order slot {index}.";
                return false;
            }
            if (slot.Technique is not { } technique)
                continue;
            hasTechnique = true;
            if (technique.PassCount != technique.Passes.Count ||
                technique.Passes.Count == 0)
            {
                blocker = $"TechniqueSet '{techniqueSet.Name}' slot {index} has inconsistent passes.";
                return false;
            }
            bool hasUvAnim = false;
            bool hasGameTime = false;
            foreach (MaterialPassAsset pass in technique.Passes)
            {
                if (!ValidateShader(pass.VertexShader, MaterialShaderKind.Vertex, out blocker) ||
                    !ValidateShader(pass.PixelShader, MaterialShaderKind.Pixel, out blocker))
                {
                    return false;
                }
                if (pass.Args.Count != checked(
                        pass.PerPrimArgCount + pass.PerObjArgCount + pass.StableArgCount))
                {
                    blocker = $"TechniqueSet '{techniqueSet.Name}' slot {index} has inconsistent shader arguments.";
                    return false;
                }
                hasUvAnim |= pass.Args.Any(IsUvAnimArgument);
                hasGameTime |= pass.Args.Any(IsGameTimeArgument);
            }
            if (index == (int)MaterialTechniqueType.Lit)
                animatedLit = hasUvAnim && hasGameTime;
        }
        if (!hasTechnique)
        {
            blocker = $"TechniqueSet '{techniqueSet.Name}' contains no techniques.";
            return false;
        }
        if (requireAnimatedUa && !animatedLit)
        {
            blocker = $"TechniqueSet '{techniqueSet.Name}' does not expose the proven lit uvAnimParms/GameTime contract.";
            return false;
        }
        return true;
    }

    private static MaterialTechniqueSetAsset BootstrapAnimatedTechniqueSet(
        MaterialTechniqueSetAsset template) => new()
    {
        Name = AnimatedTechniqueSetName,
        WorldVertexFormat = template.WorldVertexFormat,
        TechniqueSlots = template.TechniqueSlots.ToArray()
    };

    private static bool ValidateShader(
        MaterialShaderAsset? shader,
        MaterialShaderKind expectedKind,
        out string? blocker)
    {
        blocker = null;
        if (shader is null || string.IsNullOrWhiteSpace(shader.Name))
        {
            blocker = $"A TechniqueSet pass has no {expectedKind.ToString().ToLowerInvariant()} shader identity.";
            return false;
        }
        if (shader.Kind != expectedKind)
        {
            blocker = $"Shader '{shader.Name}' has kind {shader.Kind}, expected {expectedKind}.";
            return false;
        }
        return true;
    }

    private static bool MaterialSatisfiesTechnique(
        MaterialAsset material,
        IReadOnlyList<MaterialConstantDef> constants,
        MaterialTechniqueSetAsset techniqueSet,
        out string? blocker)
    {
        blocker = null;
        if (material.StateBitsEntries.Count != MaterialAsset.TechniqueSlotCount ||
            material.TextureCount != material.Textures.Count ||
            material.ConstantCount != material.Constants.Count ||
            material.StateBitsCount != material.StateBits.Count)
        {
            blocker = "The selected material has inconsistent native texture or state tables.";
            return false;
        }
        var constantHashes = constants.Select(row => row.NameHash).ToHashSet();
        var textureHashes = material.Textures.Select(row => row.NameHash).ToHashSet();
        foreach (MaterialTechniqueSlot slot in techniqueSet.TechniqueSlots.Where(row =>
                     row.Technique is not null))
        {
            MaterialTechniqueAsset technique = slot.Technique!;
            int stateIndex = material.StateBitsEntries[slot.Index].StateBitsIndex;
            if (stateIndex < 0 || stateIndex + technique.Passes.Count > material.StateBits.Count)
            {
                blocker = $"The selected material has no state-bit span for technique slot {slot.Index}.";
                return false;
            }
            foreach (MaterialShaderArgumentAsset argument in technique.Passes.SelectMany(pass =>
                         pass.Args))
            {
                if ((argument.Type is MaterialShaderArgumentType.MaterialVertexConst or
                    MaterialShaderArgumentType.MaterialPixelConst) &&
                    !constantHashes.Contains(argument.MaterialNameHash))
                {
                    blocker = $"The selected material lacks constant 0x{argument.MaterialNameHash:X8} required by technique slot {slot.Index}.";
                    return false;
                }
                if (argument.Type == MaterialShaderArgumentType.MaterialPixelSampler &&
                    !textureHashes.Contains(argument.MaterialNameHash))
                {
                    blocker = $"The selected material lacks sampler 0x{argument.MaterialNameHash:X8} required by technique slot {slot.Index}.";
                    return false;
                }
            }
        }
        return true;
    }

    private static XModelAsset CloneModel(
        XModelAsset source,
        MaterialAsset sourceMaterial,
        MaterialAsset replacement,
        string name,
        out int replacementCount)
    {
        string sourceName = NormalizedName(sourceMaterial.Info.Name!);
        MaterialAsset?[] materials = source.Materials.Select(material =>
        {
            if (material is null || !string.Equals(
                    NormalizedName(material.Info.Name ?? string.Empty),
                    sourceName,
                    StringComparison.Ordinal))
            {
                return material;
            }
            return replacement;
        }).ToArray();
        replacementCount = materials.Count(material =>
            ReferenceEquals(material, replacement));
        var clonedSurfs = new Dictionary<string, XModelSurfsAsset>(
            StringComparer.Ordinal);
        XModelLodInfo[] lods = source.Lods.Select((lod, index) =>
            CloneLod(lod, index, name, clonedSurfs)).ToArray();
        return new XModelAsset
        {
            Name = name,
            NumBones = source.NumBones,
            NumRootBones = source.NumRootBones,
            NumSurfs = source.NumSurfs,
            LodRampType = source.LodRampType,
            Scale = source.Scale,
            NoScalePartBits = source.NoScalePartBits.ToArray(),
            BoneNames = source.BoneNames.ToArray(),
            ParentList = source.ParentList.ToArray(),
            Quats = source.Quats.ToArray(),
            Trans = source.Trans.ToArray(),
            PartClassification = source.PartClassification.ToArray(),
            BaseMat = source.BaseMat.ToArray(),
            Materials = materials,
            Lods = lods,
            MaxLoadedLod = source.MaxLoadedLod,
            NumLods = source.NumLods,
            CollLod = source.CollLod,
            Flags = source.Flags,
            NumCollSurfs = source.NumCollSurfs,
            Contents = source.Contents,
            CollSurfs = source.CollSurfs.Select(CloneCollisionSurface).ToArray(),
            BoneInfo = source.BoneInfo.Select(CloneBoneInfo).ToArray(),
            Radius = source.Radius,
            Bounds = CloneBounds(source.Bounds),
            InvHighMipRadius = source.InvHighMipRadius.ToArray(),
            MemUsage = source.MemUsage,
            PhysPreset = source.PhysPreset,
            PhysCollmap = source.PhysCollmap
        };
    }

    private static XModelLodInfo CloneLod(
        XModelLodInfo source,
        int index,
        string modelName,
        IDictionary<string, XModelSurfsAsset> clonedSurfs)
    {
        XModelSurfsAsset? modelSurfs = null;
        if (source.ModelSurfs is { } sourceSurfs)
        {
            string sourceName = NormalizedName(sourceSurfs.Name ?? string.Empty);
            if (!clonedSurfs.TryGetValue(sourceName, out modelSurfs))
            {
                modelSurfs = new XModelSurfsAsset
                {
                    Name = $"{modelName}_lod{index}_surfs",
                    NumSurfs = sourceSurfs.NumSurfs,
                    Pad0A = sourceSurfs.Pad0A,
                    PartBits = sourceSurfs.PartBits.ToArray(),
                    Surfaces = sourceSurfs.Surfaces.ToArray()
                };
                clonedSurfs.Add(sourceName, modelSurfs);
            }
        }
        return new XModelLodInfo
        {
            Dist = source.Dist,
            NumSurfs = source.NumSurfs,
            SurfIndex = source.SurfIndex,
            PartBits = source.PartBits.ToArray(),
            ModelSurfs = modelSurfs
        };
    }

    private static XModelCollSurf CloneCollisionSurface(XModelCollSurf source) =>
        new(CloneBounds(source.Bounds), source.BoneIndex, source.Contents, source.SurfaceFlags);

    private static XBoneInfo CloneBoneInfo(XBoneInfo source) =>
        new(CloneBounds(source.Bounds), source.RadiusSquared);

    private static Bounds CloneBounds(Bounds source) => new()
    {
        MidPoint = source.MidPoint,
        HalfSize = source.HalfSize
    };

    private static IReadOnlyList<BaseAsset> CreateProviderClosure(
        XModelAsset model,
        MaterialAsset material,
        GfxImageAsset image,
        MaterialTechniqueSetAsset techniqueSet)
    {
        var providers = new Dictionary<(XAssetType Type, string Name), BaseAsset>();
        AddFull(model);
        AddFull(material);
        AddFull(image);
        AddFull(techniqueSet);

        foreach (MaterialPassAsset pass in techniqueSet.TechniqueSlots
                     .Where(slot => slot.Technique is not null)
                     .SelectMany(slot => slot.Technique!.Passes))
        {
            AddReference(CreateShaderReference(pass.VertexShader!));
            AddReference(CreateShaderReference(pass.PixelShader!));
        }
        foreach (XModelSurfsAsset modelSurfs in model.Lods
                     .Select(lod => lod.ModelSurfs)
                     .OfType<XModelSurfsAsset>()
                     .Distinct())
        {
            AddFull(modelSurfs);
        }
        foreach (MaterialAsset other in model.Materials
                     .OfType<MaterialAsset>()
                     .Where(candidate => !SameIdentity(candidate, material)))
        {
            AddReference(new MaterialAsset
            {
                Info = new MaterialInfo
                    { Name = "," + CanonicalRequiredName(other) }
            });
        }
        if (model.PhysPreset is { } physPreset)
        {
            AddReference(new PhysPresetAsset
                { Name = "," + CanonicalRequiredName(physPreset) });
        }
        if (model.PhysCollmap is { } physCollmap)
        {
            AddReference(new PhysCollmapAsset
                { Name = "," + CanonicalRequiredName(physCollmap) });
        }
        foreach (GfxImageAsset other in material.Textures
                     .Select(texture => texture.Image)
                     .OfType<GfxImageAsset>()
                     .Where(candidate => !SameIdentity(candidate, image)))
        {
            AddReference(new GfxImageAsset
                { Name = "," + CanonicalRequiredName(other) });
        }

        return Array.AsReadOnly(providers.Values.ToArray());

        void AddFull(BaseAsset provider) => Add(provider, isReference: false);
        void AddReference(BaseAsset provider) => Add(provider, isReference: true);
        void Add(BaseAsset provider, bool isReference)
        {
            string name = provider.SerializedAssetName ?? throw new InvalidDataException(
                $"{provider.SerializedAssetType} provider has no asset name.");
            var key = (provider.SerializedAssetType, NormalizedName(name));
            if (!providers.TryGetValue(key, out BaseAsset? existing) ||
                existing.SerializedAssetName!.StartsWith(',') && !isReference)
            {
                providers[key] = provider;
            }
        }
    }

    private static MaterialShaderAsset CreateShaderReference(
        MaterialShaderAsset source) => new()
    {
        Kind = source.Kind,
        Name = "," + CanonicalRequiredName(source),
        ProgramBytes = new byte[MaterialShaderAsset.GetProgramByteCount(source.Kind)]
    };

    private static bool SameIdentity(BaseAsset left, BaseAsset right) =>
        left.SerializedAssetType == right.SerializedAssetType &&
        string.Equals(
            NormalizedName(left.SerializedAssetName ?? string.Empty),
            NormalizedName(right.SerializedAssetName ?? string.Empty),
            StringComparison.Ordinal);

    private static string CanonicalRequiredName(BaseAsset asset)
    {
        string? name = asset.SerializedAssetName;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"{asset.SerializedAssetType} dependency has no asset name.");
        return CanonicalName(name);
    }

    private static bool IsUvAnimArgument(MaterialShaderArgumentAsset argument) =>
        argument.Type == MaterialShaderArgumentType.MaterialVertexConst &&
        argument.Dest == 20 &&
        argument.MaterialNameHash == UvAnimParmsHash;

    private static bool IsGameTimeArgument(MaterialShaderArgumentAsset argument)
    {
        if (argument.Type != MaterialShaderArgumentType.CodeVertexConst ||
            argument.Dest != 22 || argument.ArgumentRaw != 0x00070001)
        {
            return false;
        }
        MaterialCodeConstantArgument code =
            MaterialCodeConstantArgument.FromRaw(argument.ArgumentRaw);
        return code.Source == MaterialConstantSource.GameTime &&
            code.FirstRow == 0 && code.RowCount == 1;
    }

    private static MaterialConstantDef CloneConstant(MaterialConstantDef source) => new()
    {
        NameHash = source.NameHash,
        NameBytes = source.NameBytes.ToArray(),
        Literal = source.Literal
    };

    private static string ComputeDigest(
        WeaponCamoCompileRequest request,
        MaterialTechniqueSetAsset techniqueSet)
    {
        var payload = new List<byte>();
        WriteString(payload, CanonicalName(request.SourceModel.Name!));
        WriteString(payload, CanonicalName(request.SourceMaterial.Info.Name!));
        WriteString(payload, CanonicalName(techniqueSet.Name!));
        WriteString(payload, request.ScopeIdentity);
        payload.Add((byte)request.Style);
        WriteSingle(payload, request.Style == WeaponCamoStyle.Animated
            ? request.LoopSeconds
            : 0f);
        WriteUInt32(payload, checked((uint)request.ColorImage.Width));
        WriteUInt32(payload, checked((uint)request.ColorImage.Height));
        payload.AddRange(request.ColorImage.RgbaBytes);
        return Convert.ToHexString(SHA256.HashData(payload.ToArray()))
            .ToLowerInvariant()[..16];
    }

    private static string SafeNamePart(
        string? value,
        string fallback,
        int maximumLength)
    {
        string result = new((value ?? string.Empty)
            .Select(character => character is >= 'a' and <= 'z' or
                >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'
                    ? char.ToLowerInvariant(character)
                    : '_')
            .ToArray());
        result = result.Trim('_');
        if (result.Length == 0)
            result = fallback;
        return result.Length <= maximumLength
            ? result
            : result[..maximumLength];
    }

    private static string CanonicalName(string wireName) =>
        wireName.StartsWith(',') ? wireName[1..] : wireName;

    private static string NormalizedName(string wireName) =>
        CanonicalName(wireName).Replace('\\', '/').ToLowerInvariant();

    private static void WriteString(List<byte> values, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(values, checked((uint)bytes.Length));
        values.AddRange(bytes);
    }

    private static void WriteSingle(List<byte> values, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(
            bytes,
            BitConverter.SingleToInt32Bits(value));
        values.AddRange(bytes.ToArray());
    }

    private static void WriteUInt32(List<byte> values, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        values.AddRange(bytes.ToArray());
    }
}
