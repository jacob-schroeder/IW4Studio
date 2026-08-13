using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using System.Numerics;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;
using IW4.Assets.XModel.Export;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

/// <summary>Builds one semantic XModel definition and its generated XModelSurfs dependencies.</summary>
public static class XModelAssemblyCompiler
{
    // XMODEL_EXPORT writes six decimal places; allow only that round-trip
    // precision at retained root/bone containment boundaries.
    private const float ContainmentTolerance = 0.00001f;
    public static XModelAssemblyCompileResult Compile(XModelDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var issues = new List<AssetValidationIssue>(XModelLodAssemblyValidator.Validate(draft));
        XModelAsset baseline = draft.Model;
        var lods = new List<XModelLodInfo>(4);
        var providers = new Dictionary<AssetKey, BaseAsset>();
        var materials = new List<IW4.Assets.Assets.Material.MaterialAsset?>();
        var invHigh = new List<ushort>();
        int surfaceIndex = 0;
        for (int index = 0; index < 4; index++)
        {
            XModelLodDraft row = draft.LodAssembly[index];
            if (!row.IsOccupied)
            {
                lods.Add(new XModelLodInfo { Dist = 0f, NumSurfs = 0, SurfIndex = checked((ushort)surfaceIndex), PartBits = ZeroPartBits() });
                continue;
            }
            if (row.BaselineLod is { } retained)
            {
                if (retained.ModelSurfs is null || retained.PartBits.Count != 6) { issues.Add(Error(index, "Retained baseline LOD has no materialized XModelSurfs provider or PartBits.")); continue; }
                if (draft.CollisionLod == index)
                    ValidateRetainedCollisionTrees(retained, index, issues);
                lods.Add(new XModelLodInfo { Dist = row.Distance, NumSurfs = retained.NumSurfs, SurfIndex = checked((ushort)surfaceIndex), PartBits = retained.PartBits.ToArray(), ModelSurfs = retained.ModelSurfs });
                CopyBaselineMaterialRows(baseline, retained, materials, invHigh, index, issues);
                surfaceIndex += retained.NumSurfs;
                continue;
            }
            XModelExportDocument document = row.ImportedDocument!;
            XModelExportLodCompileResult compiled = XModelExportLodCompiler.Compile(
                document, baseline.NumBones, draft.CollisionLod == index);
            foreach (string blocker in compiled.Blockers) issues.Add(Error(index, blocker));
            if (!compiled.IsSuccess) { lods.Add(new XModelLodInfo { Dist = row.Distance, NumSurfs = 0, SurfIndex = checked((ushort)surfaceIndex), PartBits = ZeroPartBits() }); continue; }
            if (!draft.RebuildVisualBounds)
                ValidateBounds(baseline, document, index, issues);
            string name = GeneratedProviderName(baseline.Name, index, compiled.Surfaces);
            var provider = new XModelSurfsAsset { Name = name, NumSurfs = checked((ushort)compiled.Surfaces.Count), PartBits = compiled.PartBits.ToArray(), Surfaces = compiled.Surfaces };
            providers[AssetKey.FromDefinition(provider)] = provider;
            lods.Add(new XModelLodInfo { Dist = row.Distance, NumSurfs = provider.NumSurfs, SurfIndex = checked((ushort)surfaceIndex), PartBits = compiled.PartBits.ToArray(), ModelSurfs = provider });
            foreach (int importedMaterialIndex in compiled.ImportedMaterialIndices)
            {
                XModelMaterialMapping? mapping = importedMaterialIndex >= 0 && importedMaterialIndex < row.MaterialMappings.Count
                    ? row.MaterialMappings[importedMaterialIndex]
                    : null;
                if (mapping?.Material is null)
                    issues.Add(MaterialError(index, importedMaterialIndex,
                        "No compatible IW4 XModel render template with a proven inv-high value is available."));
                else
                {
                    MaterialAsset material = mapping.Material;
                    if (mapping.CreateOwnedMaterial)
                    {
                        XModelExportMaterial importedMaterial =
                            document.Materials[importedMaterialIndex];
                        if (importedMaterial.ImportMaterial is null)
                        {
                            issues.Add(MaterialError(index, importedMaterialIndex,
                                "The source has no GLB material facts to author."));
                        }
                        else if (TryCompileImportedMaterial(
                                     baseline.Name,
                                     importedMaterial,
                                     mapping.Material,
                                     out MaterialAsset? authoredMaterial,
                                     out GfxImageAsset? authoredImage,
                                     out string? blocker))
                        {
                            material = authoredMaterial!;
                            providers[AssetKey.FromDefinition(material)] = material;
                            providers[AssetKey.FromDefinition(authoredImage!)] = authoredImage!;
                            foreach (string warning in importedMaterial.ImportMaterial.Warnings)
                            {
                                issues.Add(new AssetValidationIssue(
                                    $"xmodel.lods[{index}].materials[{importedMaterialIndex}]",
                                    warning,
                                    AssetValidationSeverity.Warning));
                            }
                        }
                        else
                        {
                            issues.Add(MaterialError(index, importedMaterialIndex, blocker!));
                        }
                    }
                    materials.Add(material);
                    invHigh.Add(mapping.InvHighMipRadius);
                }
            }
            surfaceIndex += compiled.Surfaces.Count;
        }
        if (surfaceIndex > byte.MaxValue) issues.Add(new AssetValidationIssue("xmodel.numSurfs", "Compiled surface count exceeds the XModel byte limit.", AssetValidationSeverity.Error));
        int active = draft.LodAssembly.TakeWhile(lod => lod.IsOccupied).Count();
        if (active == 0) issues.Add(new AssetValidationIssue("xmodel.lods", "An XModel requires an active LOD.", AssetValidationSeverity.Error));
        // XModelLodGeometryCatalog begins at MaxLoadedLod.  Every active row
        // here has a retained or generated native provider, so the complete
        // contiguous assembly begins at LOD 0.
        byte maxLoaded = active == 0 ? (byte)0xFF : (byte)0;
        byte safeSurfaceCount = (byte)Math.Min(surfaceIndex, byte.MaxValue);
        byte safeActiveCount = (byte)Math.Min(active, 4);
        Bounds visualBounds = draft.RebuildVisualBounds
            ? BoundsFromImportedGeometry(draft.LodAssembly)
            : new Bounds { MidPoint = baseline.Bounds.MidPoint, HalfSize = baseline.Bounds.HalfSize };
        float radius = draft.RebuildVisualBounds
            ? RadiusFromImportedGeometry(draft.LodAssembly)
            : baseline.Radius;
        Bounds rootBoneBounds = draft.RebuildVisualBounds
            ? RootBoneBoundsFromImportedGeometry(draft.LodAssembly, baseline.BaseMat.ElementAtOrDefault(0))
            : visualBounds;
        float rootBoneRadius = draft.RebuildVisualBounds
            ? RootBoneRadiusFromImportedGeometry(draft.LodAssembly, baseline.BaseMat.ElementAtOrDefault(0))
            : radius;
        IReadOnlyList<XBoneInfo> boneInfo = draft.RebuildVisualBounds
            ? RebuildRootBoneInfo(baseline.BoneInfo, rootBoneBounds, rootBoneRadius)
            : baseline.BoneInfo;
        XModelAsset candidate = CopyWithAssembly(baseline, lods, materials, invHigh, safeSurfaceCount, safeActiveCount, maxLoaded, draft.CollisionLod, draft.CollisionSurfaces, draft.PhysPreset, draft.PhysCollmap, visualBounds, radius, boneInfo);
        return new XModelAssemblyCompileResult(
            candidate,
            Array.AsReadOnly(providers.Values.ToArray()),
            Array.AsReadOnly(issues.ToArray()));
    }

    public static string ImportedMaterialName(
        string? modelName,
        XModelExportMaterial source,
        MaterialAsset template)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(template);
        XModelImportMaterial imported = source.ImportMaterial ??
            throw new ArgumentException("The source has no GLB material facts.", nameof(source));
        using SHA256 hash = SHA256.Create();
        var payload = new List<byte>();
        WriteString(payload, modelName);
        WriteString(payload, source.Name);
        WriteString(payload, template.Info.Name);
        foreach (float value in new[]
                 {
                     imported.BaseColorFactor.X,
                     imported.BaseColorFactor.Y,
                     imported.BaseColorFactor.Z,
                     imported.BaseColorFactor.W
                 })
        {
            WriteSingle(payload, value);
        }
        payload.Add((byte)imported.AlphaMode);
        if (imported.BaseColorImage is { } image)
        {
            WriteUInt32(payload, checked((uint)image.Width));
            WriteUInt32(payload, checked((uint)image.Height));
            payload.AddRange(image.RgbaBytes);
        }
        string digest = Convert.ToHexString(hash.ComputeHash(payload.ToArray()))
            .ToLowerInvariant()[..16];
        string model = SafeNamePart(modelName, "xmodel", 40);
        string material = SafeNamePart(source.Name, "material", 40);
        return $"{model}_studio_{material}_{digest}";
    }

    public static bool IsCompatibleImportTemplate(
        XModelExportMaterial source,
        MaterialAsset template,
        out string? blocker)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(template);
        blocker = null;
        if (source.ImportMaterial is not { } imported)
        {
            blocker = "The source has no GLB material facts.";
            return false;
        }
        if (template.Textures.Count(row => row.Semantic == TextureSemantic.ColorMap) != 1)
        {
            blocker = "The IW4 template must have exactly one ColorMap texture row.";
            return false;
        }
        if (template.Textures.Any(row => row.Semantic == TextureSemantic.WaterMap))
        {
            blocker = "Water-material templates cannot be used for imported XModel materials.";
            return false;
        }
        if (!TryResolveTemplateAlphaMode(
                template,
                out XModelImportAlphaMode templateAlpha,
                out float? templateAlphaCutoff))
        {
            blocker = "The IW4 template has no single proven camera-color alpha behavior.";
            return false;
        }
        if (templateAlpha != imported.AlphaMode)
        {
            blocker = $"GLB alpha mode {imported.AlphaMode.ToString().ToUpperInvariant()} is incompatible with the template's {templateAlpha.ToString().ToUpperInvariant()} state.";
            return false;
        }
        if (imported.AlphaMode == XModelImportAlphaMode.Mask &&
            (templateAlphaCutoff is not float cutoff ||
             MathF.Abs(cutoff - imported.AlphaCutoff) > 0.000001f))
        {
            blocker = $"GLB alpha cutoff {imported.AlphaCutoff:G9} is incompatible with the template's proven alpha-test threshold.";
            return false;
        }
        return true;
    }

    private static bool TryCompileImportedMaterial(
        string? modelName,
        XModelExportMaterial source,
        MaterialAsset template,
        out MaterialAsset? material,
        out GfxImageAsset? image,
        out string? blocker)
    {
        material = null;
        image = null;
        blocker = null;
        XModelImportMaterial imported = source.ImportMaterial!;
        if (!IsCompatibleImportTemplate(source, template, out blocker))
            return false;
        MaterialTextureDef colorRow = template.Textures.Single(row =>
            row.Semantic == TextureSemantic.ColorMap);

        try
        {
            string materialName = ImportedMaterialName(modelName, source, template);
            image = CreateColorImage(materialName + "_color", imported);
            material = CloneMaterialTemplate(template, materialName, colorRow, image);
            return true;
        }
        catch (Exception exception) when (exception is
            InvalidDataException or OverflowException or ArgumentException)
        {
            blocker = exception.Message;
            material = null;
            image = null;
            return false;
        }
    }

    private static bool TryResolveTemplateAlphaMode(
        MaterialAsset template,
        out XModelImportAlphaMode alphaMode,
        out float? alphaCutoff)
    {
        alphaMode = default;
        alphaCutoff = null;
        if (template.StateBitsEntries.Count != MaterialAsset.TechniqueSlotCount ||
            template.TechniqueSet?.TechniqueSlots.Count != MaterialAsset.TechniqueSlotCount)
        {
            return false;
        }
        var modes = new HashSet<XModelImportAlphaMode>();
        var alphaCutoffs = new HashSet<float>();
        MaterialTechniqueSlot[] populated = template.TechniqueSet.TechniqueSlots
            .Where(slot => slot.Technique is not null)
            .OrderBy(slot => slot.Index)
            .ToArray();
        MaterialTechniqueSlot? selected = populated.FirstOrDefault(slot =>
                slot.Index == (int)MaterialTechniqueType.Lit) ??
            populated.FirstOrDefault(slot =>
                slot.Index == (int)MaterialTechniqueType.Emissive) ??
            populated.FirstOrDefault();
        if (selected?.Technique is not { } technique ||
            (uint)selected.Index >= MaterialAsset.TechniqueSlotCount ||
            technique.Passes.Count == 0 ||
            technique.PassCount != technique.Passes.Count)
        {
            return false;
        }
        int firstState = template.StateBitsEntries[selected.Index].StateBitsIndex;
        for (int pass = 0; pass < technique.Passes.Count; pass++)
        {
            int stateIndex = firstState + pass;
            if ((uint)stateIndex >= (uint)template.StateBits.Count ||
                template.StateBits[stateIndex].LoadBits.Count != 2)
            {
                return false;
            }
            uint word = template.StateBits[stateIndex].LoadBits[0];
            bool blend = (word & GfxStateBitsEncoding.BlendOperationRgbMask) != 0;
            bool alphaTest = (word & (uint)GfxStateBits0Flags.AlphaTestDisabled) == 0;
            if (!blend && alphaTest)
            {
                var test = (GfxAlphaTest)((word & GfxStateBitsEncoding.AlphaTestMask) >>
                    GfxStateBitsEncoding.AlphaTestShift);
                if (test == GfxAlphaTest.GreaterThanZero)
                    alphaCutoffs.Add(0f);
                else if (test == GfxAlphaTest.GreaterThanOrEqualTo128)
                    alphaCutoffs.Add(0.5f);
                else
                    return false;
            }
            modes.Add(blend
                ? XModelImportAlphaMode.Blend
                : alphaTest
                    ? XModelImportAlphaMode.Mask
                    : XModelImportAlphaMode.Opaque);
        }
        if (modes.Count != 1)
            return false;
        alphaMode = modes.Single();
        if (alphaMode == XModelImportAlphaMode.Mask)
        {
            if (alphaCutoffs.Count != 1)
                return false;
            alphaCutoff = alphaCutoffs.Single();
        }
        return true;
    }

    private static MaterialAsset CloneMaterialTemplate(
        MaterialAsset template,
        string name,
        MaterialTextureDef colorRow,
        GfxImageAsset image) => new()
    {
        Info = new MaterialInfo
        {
            Name = name,
            GameFlags = template.Info.GameFlags,
            SortKey = template.Info.SortKey,
            TextureAtlasRowCount = template.Info.TextureAtlasRowCount,
            TextureAtlasColumnCount = template.Info.TextureAtlasColumnCount,
            DrawSurf = template.Info.DrawSurf,
            SurfaceTypeBits = template.Info.SurfaceTypeBits,
            HashIndex = template.Info.HashIndex,
            Pad16 = template.Info.Pad16
        },
        StateBitsEntries = template.StateBitsEntries.ToArray(),
        TextureCount = template.TextureCount,
        ConstantCount = template.ConstantCount,
        StateBitsCount = template.StateBitsCount,
        StateFlags = template.StateFlags,
        CameraRegion = template.CameraRegion,
        XStringCount = template.XStringCount,
        Pad43 = template.Pad43,
        InlineTechniqueSlotStateBits = template.InlineTechniqueSlotStateBits.ToArray(),
        Pad8E = template.Pad8E,
        RuntimeTechniqueSlotStateBits = template.RuntimeTechniqueSlotStateBits.ToArray(),
        TechniqueSet = template.TechniqueSet,
        Textures = template.Textures.Select(row => new MaterialTextureDef
        {
            NameHash = row.NameHash,
            NameStart = row.NameStart,
            NameEnd = row.NameEnd,
            SamplerState = row.SamplerState,
            Semantic = row.Semantic,
            Image = ReferenceEquals(row, colorRow) ? image : row.Image,
            Water = row.Water
        }).ToArray(),
        Constants = template.Constants.Select(row => new MaterialConstantDef
        {
            NameHash = row.NameHash,
            NameBytes = row.NameBytes.ToArray(),
            Literal = row.Literal
        }).ToArray(),
        StateBits = template.StateBits.Select(row => new GfxStateBits
        {
            LoadBits = row.LoadBits.ToArray(),
            CommandWordCount = row.CommandWordCount
        }).ToArray(),
        XStrings = template.XStrings.Select(row => new MaterialXStringEntry(
            row.Index,
            default,
            row.Value)).ToArray()
    };

    private static GfxImageAsset CreateColorImage(
        string name,
        XModelImportMaterial material)
    {
        XModelImportImage? source = material.BaseColorImage;
        int width = source?.Width ?? 4;
        int height = source?.Height ?? 4;
        if (width is <= 0 or > ushort.MaxValue || height is <= 0 or > ushort.MaxValue)
            throw new InvalidDataException("Imported base-color image dimensions exceed IW4 limits.");
        int pixelBytes = checked(width * height * 4);
        if (source is not null && source.RgbaBytes.Count != pixelBytes)
            throw new InvalidDataException("Imported base-color image pixels do not match its dimensions.");
        int payloadBytes = checked((pixelBytes + 0x7f) & ~0x7f);
        var payload = new byte[payloadBytes];
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            int sourceOffset = pixel * 4;
            float red = source is null ? 1f : SrgbToLinear(source.RgbaBytes[sourceOffset] / 255f);
            float green = source is null ? 1f : SrgbToLinear(source.RgbaBytes[sourceOffset + 1] / 255f);
            float blue = source is null ? 1f : SrgbToLinear(source.RgbaBytes[sourceOffset + 2] / 255f);
            float alpha = material.AlphaMode == XModelImportAlphaMode.Opaque
                ? 1f
                : (source is null ? 1f : source.RgbaBytes[sourceOffset + 3] / 255f) *
                  material.BaseColorFactor.W;
            int destination = sourceOffset;
            payload[destination] = ToByte(alpha);
            payload[destination + 1] = ToByte(LinearToSrgb(red * material.BaseColorFactor.X));
            payload[destination + 2] = ToByte(LinearToSrgb(green * material.BaseColorFactor.Y));
            payload[destination + 3] = ToByte(LinearToSrgb(blue * material.BaseColorFactor.Z));
        }
        return new GfxImageAsset
        {
            Format = (byte)((byte)GfxImageBaseFormat.A8R8G8B8 | (byte)GfxImageFormatFlags.Linear),
            LevelCount = 1,
            DimensionCount = GfxImageDimension.TwoDimensional,
            TextureControl1 = 0x0001aae4,
            Width = checked((ushort)width),
            Height = checked((ushort)height),
            Depth = 1,
            MemoryLocation = GfxImageMemoryLocation.Local,
            MapType = MapType.TwoDimensional,
            TextureSemantic = TextureSemantic.ColorMap,
            Category = ImageCategory.LoadFromFile,
            UseSrgbReads = 1,
            CardMemory = checked((uint)payload.Length),
            BaseWidth = checked((ushort)width),
            BaseHeight = checked((ushort)height),
            BaseDepth = 1,
            BaseLevelCount = 1,
            Cached = GfxImageCached.Auto,
            PayloadByteCount = payload.Length,
            PayloadBytes = payload,
            Name = name
        };
    }

    private static float SrgbToLinear(float value) => value <= 0.04045f
        ? value / 12.92f
        : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static float LinearToSrgb(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value <= 0.0031308f
            ? value * 12.92f
            : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
    }

    private static byte ToByte(float value) =>
        (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * byte.MaxValue);

    private static string SafeNamePart(string? value, string fallback, int maximumLength)
    {
        string result = new((value ?? string.Empty)
            .Select(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'
                ? char.ToLowerInvariant(character)
                : '_')
            .ToArray());
        result = result.Trim('_');
        if (result.Length == 0)
            result = fallback;
        return result.Length <= maximumLength ? result : result[..maximumLength];
    }

    private static void WriteString(List<byte> values, string? value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteUInt32(values, checked((uint)bytes.Length));
        values.AddRange(bytes);
    }

    private static void CopyBaselineMaterialRows(XModelAsset model, XModelLodInfo lod, List<IW4.Assets.Assets.Material.MaterialAsset?> materials, List<ushort> invHigh, int lodIndex, List<AssetValidationIssue> issues)
    {
        for (int i = 0; i < lod.NumSurfs; i++)
        {
            int source = lod.SurfIndex + i;
            if (source < 0 || source >= model.Materials.Count || source >= model.InvHighMipRadius.Count) { issues.Add(Error(lodIndex, $"Retained surface {i} has no matching source material/inv-high row.")); continue; }
            materials.Add(model.Materials[source]); invHigh.Add(model.InvHighMipRadius[source]);
        }
    }
    private static void ValidateBounds(XModelAsset model, XModelExportDocument document, int lod, List<AssetValidationIssue> issues)
    {
        foreach ((XModelExportVertex vertex, int vertexIndex) in document.Vertices.Select((value, index) => (value, index)))
        {
            float rootRadius = model.Radius + ContainmentTolerance;
            if (!float.IsFinite(model.Radius) || model.Radius < 0f ||
                !InBounds(vertex.Position, model.Bounds) ||
                vertex.Position.LengthSquared() > rootRadius * rootRadius) { issues.Add(Error(lod, $"vertex {vertexIndex}: lies outside preserved XModel root bounds/radius.")); continue; }
            foreach (XModelExportBoneWeight influence in vertex.Weights)
            {
                if (influence.BoneIndex < 0 || influence.BoneIndex >= model.BaseMat.Count || influence.BoneIndex >= model.BoneInfo.Count) { issues.Add(Error(lod, $"vertex {vertexIndex}: bone-bound relationship cannot be established.")); continue; }
                DObjAnimMat mat = model.BaseMat[influence.BoneIndex];
                Quaternion q = new(mat.Quat.X, mat.Quat.Y, mat.Quat.Z, mat.Quat.W);
                if (!float.IsFinite(q.LengthSquared()) || q.LengthSquared() <= 0f) { issues.Add(Error(lod, $"vertex {vertexIndex}: bone {influence.BoneIndex} bind rotation is invalid.")); continue; }
                Vector3 local = Vector3.Transform(vertex.Position - new Vector3(mat.Trans.X, mat.Trans.Y, mat.Trans.Z), Quaternion.Inverse(Quaternion.Normalize(q)));
                XBoneInfo info = model.BoneInfo[influence.BoneIndex];
                if (!float.IsFinite(info.RadiusSquared) || info.RadiusSquared < 0f)
                {
                    issues.Add(Error(lod, $"vertex {vertexIndex}: bone {influence.BoneIndex} has an invalid preserved radius."));
                    continue;
                }
                float boneRadius = MathF.Sqrt(info.RadiusSquared) + ContainmentTolerance;
                if (!InBounds(local, info.Bounds) || local.LengthSquared() > boneRadius * boneRadius) issues.Add(Error(lod, $"vertex {vertexIndex}: lies outside preserved bone {influence.BoneIndex} bound/radius."));
            }
        }
    }
    private static bool InBounds(Vector3 point, Bounds bounds) =>
        MathF.Abs(point.X - bounds.MidPoint.X) <= bounds.HalfSize.X + ContainmentTolerance &&
        MathF.Abs(point.Y - bounds.MidPoint.Y) <= bounds.HalfSize.Y + ContainmentTolerance &&
        MathF.Abs(point.Z - bounds.MidPoint.Z) <= bounds.HalfSize.Z + ContainmentTolerance;
    private static string GeneratedProviderName(string? modelName, int lod, IReadOnlyList<XSurface> surfaces)
    {
        string root = string.IsNullOrWhiteSpace(modelName) ? "xmodel" : modelName;
        if (root.Any(c => c == '\0' || c > byte.MaxValue)) throw new InvalidDataException("Hosted XModel name is not a valid Latin-1 provider prefix.");
        using SHA256 hash = SHA256.Create();
        var payload = new List<byte>();
        WriteUInt16(payload, checked((ushort)surfaces.Count));
        foreach (XSurface surface in surfaces)
        {
            payload.Add((byte)surface.TileMode); payload.Add(surface.DeformedRaw); payload.Add((byte)surface.StreamFlags); payload.Add(surface.Pad03);
            WriteUInt16(payload, surface.VertCount); WriteUInt16(payload, surface.TriCount);
            WriteUInt16(payload, surface.VertexInfo.Blend0); WriteUInt16(payload, surface.VertexInfo.Blend1); WriteUInt16(payload, surface.VertexInfo.Blend2); WriteUInt16(payload, surface.VertexInfo.Blend3);
            foreach (ushort value in surface.VertexInfo.VertsBlend) WriteUInt16(payload, value);
            payload.AddRange(surface.Verts0); payload.AddRange(surface.Verts1);
            foreach (ushort value in surface.TriIndices) WriteUInt16(payload, value);
            WriteUInt16(payload, checked((ushort)surface.VertListCount));
            foreach (XRigidVertList rigid in surface.VertList) { WriteUInt16(payload, rigid.BoneOffset); WriteUInt16(payload, rigid.VertCount); WriteUInt16(payload, rigid.TriOffset); WriteUInt16(payload, rigid.TriCount); }
            foreach (XRigidVertList rigid in surface.VertList)
                WriteCollisionTree(payload, rigid.CollisionTree);
            foreach (uint value in surface.PartBits) WriteUInt32(payload, value);
        }
        string digest = Convert.ToHexString(hash.ComputeHash(payload.ToArray())).ToLowerInvariant()[..16];
        return $"{root}_lod{lod}_studio_{digest}";
    }
    private static void WriteCollisionTree(List<byte> payload, XSurfaceCollisionTree? tree)
    {
        payload.Add(tree is null ? (byte)0 : (byte)1);
        if (tree is null) return;
        WriteSingle(payload, tree.Trans.X); WriteSingle(payload, tree.Trans.Y); WriteSingle(payload, tree.Trans.Z);
        WriteSingle(payload, tree.Scale.X); WriteSingle(payload, tree.Scale.Y); WriteSingle(payload, tree.Scale.Z);
        WriteUInt32(payload, checked((uint)tree.NodeCount));
        foreach (XSurfaceCollisionNode node in tree.Nodes)
        {
            WriteUInt16(payload, node.Aabb.MinsX); WriteUInt16(payload, node.Aabb.MinsY); WriteUInt16(payload, node.Aabb.MinsZ);
            WriteUInt16(payload, node.Aabb.MaxsX); WriteUInt16(payload, node.Aabb.MaxsY); WriteUInt16(payload, node.Aabb.MaxsZ);
            WriteUInt16(payload, node.ChildBeginIndex); WriteUInt16(payload, node.ChildCount);
        }
        WriteUInt32(payload, checked((uint)tree.LeafCount));
        foreach (XSurfaceCollisionLeaf leaf in tree.Leafs) WriteUInt16(payload, leaf.TriangleBeginIndex);
    }
    private static void WriteSingle(List<byte> values, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, BitConverter.SingleToInt32Bits(value));
        values.AddRange(bytes.ToArray());
    }
    private static void ValidateRetainedCollisionTrees(XModelLodInfo lod, int lodIndex, List<AssetValidationIssue> issues)
    {
        if (lod.ModelSurfs is null) { issues.Add(Error(lodIndex, "Retained collision LOD has no XModelSurfs provider.")); return; }
        foreach ((XSurface surface, int surfaceIndex) in lod.ModelSurfs.Surfaces.Select((surface, index) => (surface, index)))
        {
            if (surface.VertListCount == 0 || surface.VertList.Count != surface.VertListCount)
            {
                issues.Add(Error(lodIndex, $"Retained collision surface {surfaceIndex} has no complete rigid-list rows."));
                continue;
            }
            int coveredVertices = 0;
            var coveredTriangles = new bool[surface.TriCount];
            foreach ((XRigidVertList rigid, int rigidIndex) in surface.VertList.Select((rigid, index) => (rigid, index)))
            {
                coveredVertices = checked(coveredVertices + rigid.VertCount);
                if ((int)rigid.TriOffset + rigid.TriCount > surface.TriCount)
                {
                    issues.Add(Error(lodIndex, $"Retained collision surface {surfaceIndex} rigid row {rigidIndex} has an out-of-range triangle span."));
                    continue;
                }
                for (int triangle = rigid.TriOffset; triangle < rigid.TriOffset + rigid.TriCount; triangle++)
                {
                    if (coveredTriangles[triangle])
                        issues.Add(Error(lodIndex, $"Retained collision surface {surfaceIndex} rigid triangle spans overlap at triangle {triangle}."));
                    coveredTriangles[triangle] = true;
                }
                string fieldPath = $"Retained collision surface {surfaceIndex} rigid row {rigidIndex}";
                XSurfaceCollisionTree? tree = rigid.CollisionTree;
                if (tree is null)
                {
                    issues.Add(Error(lodIndex, $"{fieldPath} has no collision tree."));
                }
                else if (!XModelCollisionTreeCompiler.TryValidate(
                             tree,
                             rigid,
                             surface,
                             fieldPath,
                             out string? blocker))
                {
                    issues.Add(Error(lodIndex, blocker!));
                }
            }
            if (coveredVertices != surface.VertCount || coveredTriangles.Any(covered => !covered))
                issues.Add(Error(lodIndex, $"Retained collision surface {surfaceIndex} rigid rows do not exactly cover its vertices and triangles."));
        }
    }
    private static void WriteUInt16(List<byte> values, ushort value) { Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes, value); values.AddRange(bytes.ToArray()); }
    private static void WriteUInt32(List<byte> values, uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); values.AddRange(bytes.ToArray()); }
    private static uint[] ZeroPartBits() => [0, 0, 0, 0, 0, 0];
    private static AssetValidationIssue Error(int lod, string message) => new($"xmodel.lods[{lod}]", message, AssetValidationSeverity.Error);
    private static AssetValidationIssue MaterialError(int lod, int material, string message) =>
        new($"xmodel.lods[{lod}].materials[{material}]", message, AssetValidationSeverity.Error);
    private static XModelAsset CopyWithAssembly(XModelAsset value, IReadOnlyList<XModelLodInfo> lods, IReadOnlyList<IW4.Assets.Assets.Material.MaterialAsset?> materials, IReadOnlyList<ushort> invHigh, byte numSurfs, byte numLods, byte maxLoaded, byte collLod, IReadOnlyList<XModelCollSurf> collisionSurfaces, IW4.Assets.Assets.Physics.PhysPresetAsset? physPreset, IW4.Assets.Assets.Physics.PhysCollmapAsset? physCollmap, Bounds bounds, float radius, IReadOnlyList<XBoneInfo> boneInfo) => new()
    {
        Offset = value.Offset, RuntimeAddress = value.RuntimeAddress, NamePointer = default, Name = value.Name, NumBones = value.NumBones, NumRootBones = value.NumRootBones, NumSurfs = numSurfs, LodRampType = value.LodRampType, Scale = value.Scale, NoScalePartBits = value.NoScalePartBits.ToArray(), BoneNames = value.BoneNames.ToArray(), ParentList = value.ParentList.ToArray(), Quats = value.Quats.ToArray(), Trans = value.Trans.ToArray(), PartClassification = value.PartClassification.ToArray(), BaseMat = value.BaseMat.ToArray(), Materials = materials.ToArray(), Lods = lods.ToArray(), MaxLoadedLod = maxLoaded, NumLods = numLods, CollLod = collLod, Flags = value.Flags, NumCollSurfs = checked((byte)collisionSurfaces.Count), Contents = collisionSurfaces.Aggregate(0, (contents, row) => contents | row.Contents), CollSurfs = collisionSurfaces.Select(row => new XModelCollSurf(new Bounds { MidPoint = row.Bounds.MidPoint, HalfSize = row.Bounds.HalfSize }, row.BoneIndex, row.Contents, row.SurfaceFlags)).ToArray(), BoneInfo = boneInfo.ToArray(), Radius = radius, Bounds = bounds, InvHighMipRadius = invHigh.ToArray(), MemUsage = value.MemUsage, PhysPreset = physPreset, PhysCollmap = physCollmap
    };

    private static Bounds BoundsFromImportedGeometry(IReadOnlyList<XModelLodDraft> lods)
    {
        XModelExportVertex[] vertices = lods.Where(lod => lod.ImportedDocument is not null)
            .SelectMany(lod => lod.ImportedDocument!.Vertices)
            .ToArray();
        if (vertices.Length == 0)
            throw new InvalidOperationException("Rebuilding XModel bounds requires imported geometry.");
        Vector3 minimum = vertices[0].Position;
        Vector3 maximum = vertices[0].Position;
        foreach (XModelExportVertex vertex in vertices.Skip(1))
        {
            minimum = Vector3.Min(minimum, vertex.Position);
            maximum = Vector3.Max(maximum, vertex.Position);
        }
        Vector3 midpoint = (minimum + maximum) * 0.5f;
        Vector3 halfSize = (maximum - minimum) * 0.5f;
        return new Bounds
        {
            MidPoint = new Vec3 { X = midpoint.X, Y = midpoint.Y, Z = midpoint.Z },
            HalfSize = new Vec3 { X = halfSize.X, Y = halfSize.Y, Z = halfSize.Z }
        };
    }

    private static float RadiusFromImportedGeometry(IReadOnlyList<XModelLodDraft> lods) =>
        MathF.Sqrt(lods.Where(lod => lod.ImportedDocument is not null)
            .SelectMany(lod => lod.ImportedDocument!.Vertices)
            .Max(vertex => vertex.Position.LengthSquared()));

    private static Bounds RootBoneBoundsFromImportedGeometry(
        IReadOnlyList<XModelLodDraft> lods,
        DObjAnimMat? bindPose)
    {
        Vector3[] positions = lods.Where(lod => lod.ImportedDocument is not null)
            .SelectMany(lod => lod.ImportedDocument!.Vertices)
            .Select(vertex => ToBoneLocal(vertex.Position, bindPose))
            .ToArray();
        Vector3 minimum = positions[0];
        Vector3 maximum = positions[0];
        foreach (Vector3 position in positions.Skip(1))
        {
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
        }
        Vector3 midpoint = (minimum + maximum) * 0.5f;
        Vector3 halfSize = (maximum - minimum) * 0.5f;
        return new Bounds
        {
            MidPoint = new Vec3 { X = midpoint.X, Y = midpoint.Y, Z = midpoint.Z },
            HalfSize = new Vec3 { X = halfSize.X, Y = halfSize.Y, Z = halfSize.Z }
        };
    }

    private static float RootBoneRadiusFromImportedGeometry(
        IReadOnlyList<XModelLodDraft> lods,
        DObjAnimMat? bindPose) =>
        MathF.Sqrt(lods.Where(lod => lod.ImportedDocument is not null)
            .SelectMany(lod => lod.ImportedDocument!.Vertices)
            .Max(vertex => ToBoneLocal(vertex.Position, bindPose).LengthSquared()));

    private static Vector3 ToBoneLocal(Vector3 position, DObjAnimMat? bindPose)
    {
        if (bindPose is null)
            return position;
        Quaternion rotation = new(
            bindPose.Quat.X,
            bindPose.Quat.Y,
            bindPose.Quat.Z,
            bindPose.Quat.W);
        Vector3 translation = new(bindPose.Trans.X, bindPose.Trans.Y, bindPose.Trans.Z);
        return Vector3.Transform(
            position - translation,
            Quaternion.Inverse(Quaternion.Normalize(rotation)));
    }

    private static IReadOnlyList<XBoneInfo> RebuildRootBoneInfo(
        IReadOnlyList<XBoneInfo> source,
        Bounds bounds,
        float radius)
    {
        XBoneInfo[] result = source.ToArray();
        if (result.Length != 0)
            result[0] = new XBoneInfo(
                new Bounds { MidPoint = bounds.MidPoint, HalfSize = bounds.HalfSize },
                radius * radius);
        return result;
    }
}

public sealed record XModelAssemblyCompileResult(
    XModelAsset Definition,
    IReadOnlyList<BaseAsset> Providers,
    IReadOnlyList<AssetValidationIssue> Issues)
{
    public bool IsSuccess => !Issues.Any(issue => issue.Severity == AssetValidationSeverity.Error);
}
