using System.Globalization;
using System.Numerics;
using System.Text.Json;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;
using IW4.AssetExchange.XModel;
using IW4.FastFiles.Strings;

namespace MapConverter.Game.IW3.PC.Models;

internal sealed record Iw3XModelMaterialMapping(
    MaterialAsset Material,
    ushort InvHighMipRadius);

internal sealed record Iw3XModelExportImportResult(
    IReadOnlyList<XModelAsset> Models,
    IReadOnlyList<XModelSurfsAsset> ModelSurfs);

/// <summary>
/// Converts an explicit set of IW3 OpenAssetTools XModel exports to
/// authored IW4 model definitions. Geometry and skeleton facts come from the
/// XMODEL_EXPORT files; callers supply the authored IW4 material and selected
/// inverse-high-mip value for every imported material name.
/// </summary>
internal static class Iw3XModelExportImporter
{
    private const string ExpectedGame = "iw3";
    private const string ExpectedAssetType = "xmodel";
    private const string RigidModelType = "rigid";
    private const string AnimatedModelType = "animated";
    private const int ExpectedMetadataVersion = 2;
    private const float SkeletonTolerance = 0.0005f;
    private const float WeightTolerance = 0.00001f;

    internal static Iw3XModelExportImportResult Import(
        string exportRoot,
        IReadOnlyList<string> modelNames,
        IReadOnlyDictionary<string, Iw3XModelMaterialMapping>
            materialMappingsByImportedName,
        IReadOnlyDictionary<string, Iw3XModelCollisionSource>
            collisionSourcesByModelName,
        IReadOnlySet<string> staticModelNames)
    {
        ArgumentNullException.ThrowIfNull(materialMappingsByImportedName);
        ArgumentNullException.ThrowIfNull(collisionSourcesByModelName);
        ArgumentNullException.ThrowIfNull(staticModelNames);
        IReadOnlyList<RequestedXModel> requestedModels = ReadRequestedModels(
            exportRoot,
            modelNames,
            out string fullExportRoot);

        Dictionary<string, Iw3XModelMaterialMapping> exactMaterialMappings =
            FreezeMaterialMappings(materialMappingsByImportedName);
        var importedModels = new List<XModelAsset>(requestedModels.Count);
        var importedModelSurfs = new List<XModelSurfsAsset>();
        var seenModelSurfsNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (RequestedXModel requestedModel in requestedModels)
        {
            importedModels.Add(ImportModel(
                fullExportRoot,
                requestedModel.Name,
                requestedModel.Metadata,
                exactMaterialMappings,
                collisionSourcesByModelName.GetValueOrDefault(requestedModel.Name),
                seenModelSurfsNames,
                importedModelSurfs,
                staticModelNames.Contains(requestedModel.Name)));
        }

        return new Iw3XModelExportImportResult(
            Array.AsReadOnly(importedModels.ToArray()),
            Array.AsReadOnly(importedModelSurfs.ToArray()));
    }

    internal static IReadOnlyList<string> ReadReferencedMaterialNames(
        string exportRoot,
        IReadOnlyList<string> modelNames)
    {
        IReadOnlyList<RequestedXModel> requestedModels = ReadRequestedModels(
            exportRoot,
            modelNames,
            out string fullExportRoot);
        var materialNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (RequestedXModel requestedModel in requestedModels)
        {
            foreach (XModelExportLodSource lod in ReadActiveLods(
                         fullExportRoot,
                         requestedModel.Name,
                         requestedModel.Metadata))
            {
                foreach ((int _, string materialName) in
                         ReferencedMaterials(
                             lod.Document,
                             requestedModel.Name,
                             lod.Index))
                {
                    materialNames.Add(materialName);
                }
            }
        }
        return Array.AsReadOnly(materialNames.ToArray());
    }

    private static XModelAsset ImportModel(
        string exportRoot,
        string modelName,
        XModelMetadata metadata,
        IReadOnlyDictionary<string, Iw3XModelMaterialMapping> materialMappings,
        Iw3XModelCollisionSource? collisionSource,
        ISet<string> seenModelSurfsNames,
        ICollection<XModelSurfsAsset> modelSurfsDestination,
        bool forStaticWorld)
    {
        var lods = new List<XModelLodInfo>(4);
        var materials = new List<MaterialAsset?>();
        var invHighMipRadius = new List<ushort>();
        var modelBounds = new GeometryBoundsAccumulator();
        XModelSkeleton? skeleton = null;
        GeometryBoundsAccumulator[]? boneBounds = null;
        IReadOnlyList<XModelCollSurf> collisionSurfaces = [];
        int surfaceIndex = 0;

        if (collisionSource is not null)
        {
            if (!string.Equals(
                    collisionSource.Name,
                    modelName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"IW3 XModel collision source '{collisionSource.Name}' was " +
                    $"supplied for '{modelName}'.");
            }
            if (collisionSource.CollisionLod != metadata.CollisionLod)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' collision LOD disagrees between " +
                    $"metadata ({metadata.CollisionLod}) and native source " +
                    $"({collisionSource.CollisionLod}).");
            }
            if (collisionSource.CollisionLod >= metadata.Lods.Count)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' collision LOD " +
                    $"{collisionSource.CollisionLod} does not select an active LOD.");
            }
        }

        foreach (XModelExportLodSource lod in ReadActiveLods(
                     exportRoot,
                     modelName,
                     metadata))
        {
            int lodIndex = lod.Index;
            XModelMetadataLod metadataLod = lod.Metadata;
            XModelExportDocument document = lod.Document;
            if (skeleton is null)
            {
                skeleton = CreateSkeleton(document.Bones, modelName);
                boneBounds = Enumerable.Range(0, skeleton.Bones.Count)
                    .Select(_ => new GeometryBoundsAccumulator())
                    .ToArray();
            }
            else if (!SameSkeleton(skeleton.Bones, document.Bones))
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} does not have the " +
                    "same skeleton and bind pose as LOD 0.");
            }

            ValidateMaterialMappings(
                document,
                modelName,
                lodIndex,
                materialMappings);
            AccumulateGeometryBounds(
                document,
                skeleton.Bones,
                modelBounds,
                boneBounds!);

            // Static marks and collision-enabled scene DObj marks traverse LOD 0,
            // independently of the LOD selected for collision traces.
            bool compileCollisionTrees =
                (lodIndex == 0 &&
                 (forStaticWorld || collisionSource is { CollisionLod: >= 0 })) ||
                (collisionSource is not null &&
                 collisionSource.CollisionLod == lodIndex);
            XModelExportLodCompileResult compiled = metadata.IsAnimated
                ? XModelExportLodCompiler.Compile(
                    document,
                    skeleton.Bones.Count,
                    compileCollisionTrees)
                : Iw3RigidSurfaceCompiler.Compile(
                    lod.TangentSource,
                    document,
                    skeleton.Bones.Count,
                    compileCollisionTrees);
            if (!compiled.IsSuccess)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} could not be " +
                    $"compiled: {string.Join("; ", compiled.Blockers)}");
            }
            ValidateCompiledSurfaces(
                compiled,
                skeleton.Bones.Count,
                modelName,
                lodIndex,
                allowDeformed: metadata.IsAnimated);
            if (forStaticWorld && !metadata.IsAnimated &&
                compiled.Surfaces.Count > GfxStaticModelDrawInst.MaxLodSurfaceCount)
            {
                compiled = Iw3RigidSurfaceCompiler.CoalesceStaticSurfaces(compiled);
                if (!compiled.IsSuccess)
                {
                    throw new InvalidDataException(
                        $"IW3 static XModel '{modelName}' LOD {lodIndex} could not " +
                        $"be coalesced: {string.Join("; ", compiled.Blockers)}");
                }
                ValidateCompiledSurfaces(
                    compiled,
                    skeleton.Bones.Count,
                    modelName,
                    lodIndex,
                    allowDeformed: false);
            }

            int nextSurfaceIndex = checked(surfaceIndex + compiled.Surfaces.Count);
            if (nextSurfaceIndex > byte.MaxValue)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' has {nextSurfaceIndex} surfaces " +
                    $"across its active LODs; IW4 supports at most {byte.MaxValue}.");
            }

            if (collisionSource is not null && collisionSource.CollisionLod == lodIndex)
            {
                collisionSurfaces = CompileCollisionSurfaces(
                    modelName, lod.TangentSource, compiled, collisionSource);
            }

            string modelSurfsName = $"{modelName}_lod{lodIndex}";
            if (!seenModelSurfsNames.Add(modelSurfsName))
            {
                throw new InvalidDataException(
                    $"Generated XModelSurfs name '{modelSurfsName}' is not unique.");
            }
            var modelSurfs = new XModelSurfsAsset
            {
                Name = modelSurfsName,
                NumSurfs = checked((ushort)compiled.Surfaces.Count),
                PartBits = compiled.PartBits.ToArray(),
                Surfaces = compileCollisionTrees
                    ? compiled.Surfaces.Select(WithCpuReadStreamsInLarge).ToArray()
                    : compiled.Surfaces.ToArray()
            };
            modelSurfsDestination.Add(modelSurfs);
            lods.Add(new XModelLodInfo
            {
                Dist = metadataLod.Distance,
                NumSurfs = modelSurfs.NumSurfs,
                SurfIndex = checked((ushort)surfaceIndex),
                PartBits = compiled.PartBits.ToArray(),
                ModelSurfs = modelSurfs
            });

            foreach (int importedMaterialIndex in compiled.ImportedMaterialIndices)
            {
                if (importedMaterialIndex < 0 ||
                    importedMaterialIndex >= document.Materials.Count)
                {
                    throw new InvalidDataException(
                        $"IW3 XModel '{modelName}' LOD {lodIndex} compiled an " +
                        $"out-of-range material row {importedMaterialIndex}.");
                }
                string importedMaterialName =
                    document.Materials[importedMaterialIndex].Name;
                Iw3XModelMaterialMapping mapping =
                    materialMappings[importedMaterialName];
                materials.Add(mapping.Material);
                invHighMipRadius.Add(mapping.InvHighMipRadius);
            }
            surfaceIndex = nextSurfaceIndex;
        }

        if (skeleton is null || boneBounds is null ||
            !modelBounds.HasGeometry)
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' has no imported visual geometry.");
        }
        if (materials.Count != surfaceIndex ||
            invHighMipRadius.Count != surfaceIndex)
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' did not produce one material and " +
                "inv-high row per surface.");
        }

        while (lods.Count < 4)
        {
            lods.Add(new XModelLodInfo
            {
                NumSurfs = 0,
                SurfIndex = checked((ushort)surfaceIndex),
                PartBits = ZeroPartBits()
            });
        }

        return new XModelAsset
        {
            Name = modelName,
            NumBones = checked((byte)skeleton.Bones.Count),
            NumRootBones = checked((byte)skeleton.RootBoneCount),
            NumSurfs = checked((byte)surfaceIndex),
            LodRampType = metadata.IsAnimated
                ? XModelLodRampType.Skinned
                : XModelLodRampType.Rigid,
            Scale = 1.0f,
            NoScalePartBits = ZeroPartBits(),
            BoneNames = skeleton.BoneNames,
            ParentList = skeleton.ParentList,
            Quats = skeleton.Quats,
            Trans = skeleton.Trans,
            PartClassification = skeleton.PartClassification,
            BaseMat = skeleton.BaseMat,
            Materials = materials.ToArray(),
            Lods = lods.ToArray(),
            MaxLoadedLod = 0,
            NumLods = checked((byte)metadata.Lods.Count),
            CollLod = collisionSource is null || collisionSource.CollisionLod < 0
                ? byte.MaxValue : checked((byte)collisionSource.CollisionLod),
            Flags = metadata.Flags,
            NumCollSurfs = collisionSurfaces.Count,
            Contents = collisionSource?.Contents ?? 0,
            CollSurfs = collisionSurfaces,
            BoneInfo = CreateBoneInfo(boneBounds),
            Radius = MathF.Sqrt(modelBounds.MaximumRadiusSquared),
            Bounds = modelBounds.ToBounds(),
            InvHighMipRadius = invHighMipRadius.ToArray(),
            MemUsage = 0,
            // A metadata physics-preset name does not contain its definition.
            PhysPreset = null,
            PhysCollmap = null
        };
    }

    private static XSurface WithCpuReadStreamsInLarge(XSurface surface)
    {
        // Collision traces and LOD 0 marks read positions and indices on
        // the PPU. Keep those streams in main memory; Verts1 keeps its GPU placement.
        return new XSurface
        {
            TileMode = surface.TileMode,
            DeformedRaw = surface.DeformedRaw,
            StreamFlags = surface.StreamFlags |
                XSurfaceStreamFlags.Verts0InLarge |
                XSurfaceStreamFlags.TriIndicesInLarge,
            Pad03 = surface.Pad03,
            VertCount = surface.VertCount,
            TriCount = surface.TriCount,
            TriIndicesPointer = surface.TriIndicesPointer,
            TriIndices = surface.TriIndices,
            VertexInfo = surface.VertexInfo,
            Verts0Pointer = surface.Verts0Pointer,
            Verts0 = surface.Verts0,
            Vb0 = surface.Vb0,
            Verts1Pointer = surface.Verts1Pointer,
            Verts1 = surface.Verts1,
            Vb1 = surface.Vb1,
            VertListCount = surface.VertListCount,
            VertListPointer = surface.VertListPointer,
            VertList = surface.VertList,
            IndexBuffer = surface.IndexBuffer,
            PartBits = surface.PartBits
        };
    }

    private static IReadOnlyList<XModelCollSurf> CompileCollisionSurfaces(
        string modelName,
        XModelExportDocument document,
        XModelExportLodCompileResult compiled,
        Iw3XModelCollisionSource source)
    {
        if (source.Surfaces.Count == 0)
            return [];

        // IW3 keeps independent collision triangles. Match their authored bounds
        // to the original exported rigid groups before tangent/size partitioning
        // changes surface order. The exporter rounds positions and native bounds
        // include approximately 0.001 units of padding.
        const float boundsTolerance = 0.002f;
        var metadata = new Dictionary<(int Material, int Bone), (int Contents, int Flags)>();
        var matchedSources = new HashSet<int>();
        foreach (var group in document.Triangles.GroupBy(triangle => (
                     triangle.ObjectIndex, triangle.MaterialIndex,
                     Bone: RigidBoneIndex(document, triangle.First))))
        {
            var bounds = new GeometryBoundsAccumulator();
            foreach (XModelExportTriangle triangle in group)
            {
                if (RigidBoneIndex(document, triangle.Second) != group.Key.Bone ||
                    RigidBoneIndex(document, triangle.Third) != group.Key.Bone)
                    throw new InvalidDataException($"IW3 XModel '{modelName}' collision geometry is not rigid.");
                bounds.Add(document.Vertices[triangle.First.VertexIndex].Position);
                bounds.Add(document.Vertices[triangle.Second.VertexIndex].Position);
                bounds.Add(document.Vertices[triangle.Third.VertexIndex].Position);
            }
            Bounds exported = bounds.ToBounds();
            var matches = source.Surfaces.Select((surface, index) => (surface, index))
                .Where(value => value.surface.BoneIndex == group.Key.Bone &&
                    MathF.Abs(value.surface.Mins[0] - (exported.MidPoint.X - exported.HalfSize.X)) <= boundsTolerance &&
                    MathF.Abs(value.surface.Mins[1] - (exported.MidPoint.Y - exported.HalfSize.Y)) <= boundsTolerance &&
                    MathF.Abs(value.surface.Mins[2] - (exported.MidPoint.Z - exported.HalfSize.Z)) <= boundsTolerance &&
                    MathF.Abs(value.surface.Maxs[0] - (exported.MidPoint.X + exported.HalfSize.X)) <= boundsTolerance &&
                    MathF.Abs(value.surface.Maxs[1] - (exported.MidPoint.Y + exported.HalfSize.Y)) <= boundsTolerance &&
                    MathF.Abs(value.surface.Maxs[2] - (exported.MidPoint.Z + exported.HalfSize.Z)) <= boundsTolerance)
                .ToArray();
            if (matches.Length == 0 || matches.Select(value =>
                    (value.surface.Contents, value.surface.SurfaceFlags)).Distinct().Count() != 1)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' object {group.Key.ObjectIndex} bone {group.Key.Bone} " +
                    "does not have unambiguous source collision metadata.");
            }
            var key = (group.Key.MaterialIndex, group.Key.Bone);
            var value = (matches[0].surface.Contents, matches[0].surface.SurfaceFlags);
            if (metadata.TryGetValue(key, out var previous) && previous != value)
                throw new InvalidDataException($"IW3 XModel '{modelName}' material/bone collision metadata conflicts.");
            metadata[key] = value;
            foreach (var match in matches) matchedSources.Add(match.index);
        }
        if (matchedSources.Count != source.Surfaces.Count)
            throw new InvalidDataException($"IW3 XModel '{modelName}' has unmatched source collision surfaces.");

        // PS3 XModelTraceLine consumes one CollSurf per rigid list, flattened in
        // final XSurface order. Splits duplicate metadata; coalescing concatenates
        // rigid lists. Bounds must enclose that list's model-space triangles.
        var result = new List<XModelCollSurf>();
        for (int surfaceIndex = 0; surfaceIndex < compiled.Surfaces.Count; surfaceIndex++)
        {
            XSurface surface = compiled.Surfaces[surfaceIndex];
            foreach (XRigidVertList rigid in surface.VertList)
            {
                int bone = rigid.BoneOffset / 0x40;
                if (!metadata.TryGetValue((compiled.ImportedMaterialIndices[surfaceIndex], bone), out var value))
                    throw new InvalidDataException($"IW3 XModel '{modelName}' emitted rigid group has no collision metadata.");
                var bounds = new GeometryBoundsAccumulator();
                int end = checked((rigid.TriOffset + rigid.TriCount) * 3);
                for (int index = rigid.TriOffset * 3; index < end; index++)
                {
                    if (!XSurfaceVertexCodec.TryReadPosition(surface.Verts0, surface.TriIndices[index], out Vector3 position))
                        throw new InvalidDataException($"IW3 XModel '{modelName}' emitted collision vertex is invalid.");
                    bounds.Add(position);
                }
                Bounds emitted = bounds.ToBounds();
                emitted.HalfSize = new Vec3
                {
                    X = emitted.HalfSize.X + boundsTolerance,
                    Y = emitted.HalfSize.Y + boundsTolerance,
                    Z = emitted.HalfSize.Z + boundsTolerance
                };
                result.Add(new XModelCollSurf(emitted, bone, value.Contents, value.Flags));
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static Dictionary<string, Iw3XModelMaterialMapping>
        FreezeMaterialMappings(
            IReadOnlyDictionary<string, Iw3XModelMaterialMapping> source)
    {
        var result = new Dictionary<string, Iw3XModelMaterialMapping>(
            source.Count,
            StringComparer.Ordinal);
        foreach ((string importedName, Iw3XModelMaterialMapping mapping) in source)
        {
            ValidateLatin1Name(importedName, "Imported material mapping");
            if (mapping is null || mapping.Material is null)
            {
                throw new InvalidDataException(
                    $"Imported material mapping '{importedName}' has no IW4 material.");
            }
            if (string.IsNullOrWhiteSpace(mapping.Material.Info.Name))
            {
                throw new InvalidDataException(
                    $"Imported material mapping '{importedName}' resolves to an " +
                    "unnamed IW4 material.");
            }
            if (!result.TryAdd(importedName, mapping))
            {
                throw new InvalidDataException(
                    $"Imported material mapping '{importedName}' is duplicated.");
            }
        }
        return result;
    }

    private static IReadOnlyList<RequestedXModel> ReadRequestedModels(
        string exportRoot,
        IReadOnlyList<string> modelNames,
        out string fullExportRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportRoot);
        ArgumentNullException.ThrowIfNull(modelNames);
        if (modelNames.Count == 0)
        {
            throw new ArgumentException(
                "At least one explicit IW3 XModel name is required.",
                nameof(modelNames));
        }

        fullExportRoot = Path.GetFullPath(exportRoot);
        if (!Directory.Exists(fullExportRoot))
        {
            throw new DirectoryNotFoundException(
                $"The IW3 export root '{fullExportRoot}' does not exist.");
        }

        var result = new List<RequestedXModel>(modelNames.Count);
        var seenModelNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (string requestedModelName in modelNames)
        {
            string modelName = NormalizeOwnedAssetName(
                requestedModelName,
                "IW3 XModel");
            if (!seenModelNames.Add(modelName))
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' was requested more than once.");
            }

            string metadataPath = ResolveContainedFile(
                fullExportRoot,
                $"xmodel/{modelName}.json",
                $"IW3 XModel '{modelName}' metadata");
            result.Add(new RequestedXModel(
                modelName,
                ReadMetadata(metadataPath, modelName)));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static IEnumerable<XModelExportLodSource> ReadActiveLods(
        string exportRoot,
        string modelName,
        XModelMetadata metadata)
    {
        var seenLodFiles = new HashSet<string>(FilePathComparer());
        for (int lodIndex = 0; lodIndex < metadata.Lods.Count; lodIndex++)
        {
            XModelMetadataLod metadataLod = metadata.Lods[lodIndex];
            string lodPath = ResolveContainedFile(
                exportRoot,
                metadataLod.File,
                $"IW3 XModel '{modelName}' LOD {lodIndex}");
            if (!string.Equals(
                    Path.GetExtension(lodPath),
                    ".xmodel_export",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} does not name an " +
                    "XMODEL_EXPORT file.");
            }
            if (!seenLodFiles.Add(lodPath))
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' reuses XMODEL_EXPORT file " +
                    $"'{metadataLod.File}' across active LODs.");
            }

            XModelExportDocument document = ReadXModelExport(
                lodPath,
                modelName,
                lodIndex);
            ValidateDocument(
                document,
                modelName,
                lodIndex,
                allowBlended: metadata.IsAnimated);
            document = NormalizeIw3CornerNormals(
                document,
                modelName,
                lodIndex);
            XModelExportDocument tangentSource = document;
            if (!metadata.IsAnimated)
            {
                document = SplitRigidBoneSurfacePartitions(
                    document,
                    modelName,
                    lodIndex);
            }
            yield return new XModelExportLodSource(
                lodIndex,
                metadataLod,
                tangentSource,
                document);
        }
    }

    private static void ValidateMaterialMappings(
        XModelExportDocument document,
        string modelName,
        int lodIndex,
        IReadOnlyDictionary<string, Iw3XModelMaterialMapping> mappings)
    {
        foreach ((int _, string importedName) in
                 ReferencedMaterials(document, modelName, lodIndex))
        {
            if (!mappings.TryGetValue(
                    importedName,
                    out Iw3XModelMaterialMapping? mapping) ||
                mapping?.Material is null)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} material " +
                    $"'{importedName}' has no IW4 material and inv-high mapping.");
            }
        }
    }

    private static IEnumerable<(int Index, string Name)> ReferencedMaterials(
        XModelExportDocument document,
        string modelName,
        int lodIndex)
    {
        foreach (int materialIndex in document.Triangles
                     .Select(triangle => triangle.MaterialIndex)
                     .Distinct()
                     .OrderBy(index => index))
        {
            if (materialIndex < 0 || materialIndex >= document.Materials.Count)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} references " +
                    $"out-of-range material row {materialIndex}.");
            }
            string importedName = document.Materials[materialIndex].Name;
            ValidateLatin1Name(
                importedName,
                $"IW3 XModel '{modelName}' LOD {lodIndex} material {materialIndex}");
            yield return (materialIndex, importedName);
        }
    }

    private static void ValidateDocument(
        XModelExportDocument document,
        string modelName,
        int lodIndex,
        bool allowBlended)
    {
        if (document.Bones.Count is < 1 or > 192)
        {
            throw new NotSupportedException(
                $"IW3 XModel '{modelName}' LOD {lodIndex} has " +
                $"{document.Bones.Count} bones; IW4 supports between one and " +
                "192 bones in its six-word part-bit representation.");
        }

        var boneNames = new HashSet<string>(StringComparer.Ordinal);
        int rootBoneCount = 0;
        bool sawChildBone = false;
        for (int boneIndex = 0; boneIndex < document.Bones.Count; boneIndex++)
        {
            XModelExportBone bone = document.Bones[boneIndex];
            if (bone.Name.Length != 0)
            {
                ValidateLatin1Name(
                    bone.Name,
                    $"IW3 XModel '{modelName}' LOD {lodIndex} bone {boneIndex}");
            }
            if (!boneNames.Add(bone.Name))
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} has more than " +
                    $"one bone named '{bone.Name}'.");
            }
            if (bone.ParentIndex == -1)
            {
                if (sawChildBone)
                {
                    throw new InvalidDataException(
                        $"IW3 XModel '{modelName}' LOD {lodIndex} root bones " +
                        "must precede all child bones.");
                }
                rootBoneCount++;
            }
            else
            {
                sawChildBone = true;
                if (bone.ParentIndex < 0 || bone.ParentIndex >= boneIndex)
                {
                    throw new InvalidDataException(
                        $"IW3 XModel '{modelName}' LOD {lodIndex} bone " +
                        $"{boneIndex} has invalid parent {bone.ParentIndex}.");
                }
                if (boneIndex - bone.ParentIndex > byte.MaxValue)
                {
                    throw new NotSupportedException(
                        $"IW3 XModel '{modelName}' LOD {lodIndex} bone " +
                        $"{boneIndex} has a parent delta that IW4 cannot " +
                        "represent.");
                }
            }
            if (!IsFinite(bone.GlobalOffset) ||
                !IsFiniteNonzero(bone.GlobalRotation))
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} bone " +
                    $"{boneIndex} has a non-finite or zero bind transform.");
            }
        }
        if (rootBoneCount == 0)
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' LOD {lodIndex} has no root bone.");
        }
        if (document.Vertices.Count == 0 ||
            document.Triangles.Count == 0 ||
            document.Objects.Count == 0)
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' LOD {lodIndex} has no complete " +
                "visual geometry.");
        }

        for (int vertexIndex = 0;
             vertexIndex < document.Vertices.Count;
             vertexIndex++)
        {
            IReadOnlyList<XModelExportBoneWeight> weights =
                document.Vertices[vertexIndex].Weights;
            bool invalidCount = allowBlended
                ? weights.Count is < 1 or > 4
                : weights.Count != 1;
            bool invalidWeight = weights.Any(weight =>
                weight.BoneIndex < 0 ||
                weight.BoneIndex >= document.Bones.Count ||
                !float.IsFinite(weight.Weight) ||
                weight.Weight <= 0f);
            bool duplicateBone = weights
                .Select(weight => weight.BoneIndex)
                .Distinct()
                .Count() != weights.Count;
            float weightTotal = weights.Sum(weight => weight.Weight);
            if (invalidCount || invalidWeight || duplicateBone ||
                !float.IsFinite(weightTotal) ||
                MathF.Abs(weightTotal - 1.0f) > WeightTolerance)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} vertex " +
                    $"{vertexIndex} does not have " +
                    (allowBlended
                        ? "one to four normalized model-bone weights."
                        : "exactly one normalized model-bone weight."));
            }
        }
    }

    private static XModelExportDocument NormalizeIw3CornerNormals(
        XModelExportDocument document,
        string modelName,
        int lodIndex)
    {
        XModelExportTriangle[] triangles = document.Triangles
            .Select((triangle, triangleIndex) => new XModelExportTriangle(
                triangle.ObjectIndex,
                triangle.MaterialIndex,
                NormalizeCorner(
                    triangle.First,
                    modelName,
                    lodIndex,
                    triangleIndex,
                    0),
                NormalizeCorner(
                    triangle.Second,
                    modelName,
                    lodIndex,
                    triangleIndex,
                    1),
                NormalizeCorner(
                    triangle.Third,
                    modelName,
                    lodIndex,
                    triangleIndex,
                    2)))
            .ToArray();
        return new XModelExportDocument(
            document.Bones,
            document.Vertices,
            Array.AsReadOnly(triangles),
            document.Objects,
            document.Materials);
    }

    private static XModelExportDocument SplitRigidBoneSurfacePartitions(
        XModelExportDocument document,
        string modelName,
        int lodIndex)
    {
        var objects = new List<XModelExportObject>();
        var objectIndexBySourceAndBone =
            new Dictionary<(int ObjectIndex, int BoneIndex), int>();
        var triangles = new XModelExportTriangle[document.Triangles.Count];
        bool changed = false;
        for (int triangleIndex = 0;
             triangleIndex < document.Triangles.Count;
             triangleIndex++)
        {
            XModelExportTriangle triangle = document.Triangles[triangleIndex];
            int firstBone = RigidBoneIndex(document, triangle.First);
            int secondBone = RigidBoneIndex(document, triangle.Second);
            int thirdBone = RigidBoneIndex(document, triangle.Third);
            if (firstBone != secondBone || firstBone != thirdBone)
            {
                throw new NotSupportedException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} triangle " +
                    $"{triangleIndex} spans multiple bones and requires " +
                    "deformed XSurface geometry.");
            }

            var key = (triangle.ObjectIndex, firstBone);
            if (!objectIndexBySourceAndBone.TryGetValue(key, out int objectIndex))
            {
                objectIndex = objects.Count;
                objectIndexBySourceAndBone.Add(key, objectIndex);
                string sourceIdentity =
                    document.Objects[triangle.ObjectIndex].SurfaceIdentity;
                objects.Add(new XModelExportObject(
                    $"{sourceIdentity}_bone{firstBone}"));
            }
            changed |= objectIndex != triangle.ObjectIndex;
            triangles[triangleIndex] = triangle with { ObjectIndex = objectIndex };
        }

        if (!changed && objects.Count == document.Objects.Count)
            return document;
        return new XModelExportDocument(
            document.Bones,
            document.Vertices,
            Array.AsReadOnly(triangles),
            Array.AsReadOnly(objects.ToArray()),
            document.Materials);
    }

    private static int RigidBoneIndex(
        XModelExportDocument document,
        XModelExportCorner corner) =>
        document.Vertices[corner.VertexIndex].Weights[0].BoneIndex;

    private static XModelExportCorner NormalizeCorner(
        XModelExportCorner corner,
        string modelName,
        int lodIndex,
        int triangleIndex,
        int cornerIndex)
    {
        float lengthSquared = corner.Normal.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 0f)
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' LOD {lodIndex} triangle " +
                $"{triangleIndex} corner {cornerIndex} has no finite normal " +
                "direction.");
        }
        return corner with { Normal = Vector3.Normalize(corner.Normal) };
    }

    private static void ValidateCompiledSurfaces(
        XModelExportLodCompileResult compiled,
        int boneCount,
        string modelName,
        int lodIndex,
        bool allowDeformed)
    {
        if (compiled.Surfaces.Count == 0 ||
            compiled.Surfaces.Count != compiled.ImportedMaterialIndices.Count ||
            compiled.PartBits.Count != 6)
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' LOD {lodIndex} produced an " +
                "incomplete native surface graph.");
        }
        foreach ((XSurface surface, int surfaceIndex) in
                 compiled.Surfaces.Select((value, index) => (value, index)))
        {
            // The projector validates every rigid list's bone and cumulative
            // vertex coverage, including coalesced static surfaces.
            if (!XModelSurfaceSkinningProjector.TryProject(
                    surface,
                    boneCount,
                    out _,
                    out string blocker))
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} surface " +
                    $"{surfaceIndex} has invalid native skinning: {blocker}");
            }
            bool hasBlendData =
                surface.VertexInfo.Blend0 != 0 ||
                surface.VertexInfo.Blend1 != 0 ||
                surface.VertexInfo.Blend2 != 0 ||
                surface.VertexInfo.Blend3 != 0 ||
                surface.VertexInfo.VertsBlend.Count != 0;
            if (surface.Deformed != hasBlendData)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} surface " +
                    $"{surfaceIndex} has inconsistent deformed and blend state.");
            }
            if (surface.Deformed && !allowDeformed)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} surface " +
                    $"{surfaceIndex} unexpectedly compiled as deformed geometry.");
            }
        }
    }

    private static void AccumulateGeometryBounds(
        XModelExportDocument document,
        IReadOnlyList<XModelExportBone> bones,
        GeometryBoundsAccumulator modelBounds,
        IReadOnlyList<GeometryBoundsAccumulator> boneBounds)
    {
        foreach (XModelExportVertex vertex in document.Vertices)
        {
            modelBounds.Add(vertex.Position);
            foreach (int boneIndex in vertex.Weights
                         .Select(weight => weight.BoneIndex)
                         .Distinct())
            {
                XModelExportBone bone = bones[boneIndex];
                boneBounds[boneIndex].Add(ToBoneLocal(vertex.Position, bone));
            }

            // IW4Studio's authored-bounds path treats bone zero as the model
            // containment root, including when child bones own the geometry.
            boneBounds[0].Add(ToBoneLocal(vertex.Position, bones[0]));
        }
    }

    private static bool SameSkeleton(
        IReadOnlyList<XModelExportBone> expected,
        IReadOnlyList<XModelExportBone> candidate)
    {
        if (expected.Count != candidate.Count)
        {
            return false;
        }
        for (int index = 0; index < expected.Count; index++)
        {
            XModelExportBone expectedBone = expected[index];
            XModelExportBone candidateBone = candidate[index];
            if (!string.Equals(
                    expectedBone.Name,
                    candidateBone.Name,
                    StringComparison.Ordinal) ||
                expectedBone.ParentIndex != candidateBone.ParentIndex ||
                Vector3.Distance(
                    expectedBone.GlobalOffset,
                    candidateBone.GlobalOffset) > SkeletonTolerance)
            {
                return false;
            }

            float expectedLength = expectedBone.GlobalRotation.LengthSquared();
            float candidateLength = candidateBone.GlobalRotation.LengthSquared();
            if (!float.IsFinite(expectedLength) || expectedLength <= 0f ||
                !float.IsFinite(candidateLength) || candidateLength <= 0f)
            {
                return false;
            }
            float dot = MathF.Abs(Quaternion.Dot(
                Quaternion.Normalize(expectedBone.GlobalRotation),
                Quaternion.Normalize(candidateBone.GlobalRotation)));
            if (1.0f - dot > SkeletonTolerance)
                return false;
        }
        return true;
    }

    private static XModelSkeleton CreateSkeleton(
        IReadOnlyList<XModelExportBone> bones,
        string modelName)
    {
        int rootBoneCount = bones.TakeWhile(bone => bone.ParentIndex == -1).Count();
        var boneNames = new ScriptStringReference[bones.Count];
        var parentList = new byte[bones.Count - rootBoneCount];
        var quats = new short[(bones.Count - rootBoneCount) * 4];
        var trans = new float[(bones.Count - rootBoneCount) * 3];
        var partClassification = new byte[bones.Count];
        var baseMat = new DObjAnimMat[bones.Count];

        for (int boneIndex = 0; boneIndex < bones.Count; boneIndex++)
        {
            XModelExportBone bone = bones[boneIndex];
            boneNames[boneIndex] = new ScriptStringReference(
                0,
                bone.Name.Length == 0 ? null : bone.Name,
                default,
                default);

            Quaternion globalRotation = bone.GlobalRotation;
            float globalLengthSquared = globalRotation.LengthSquared();
            baseMat[boneIndex] = new DObjAnimMat(
                new DObjQuat(
                    globalRotation.X,
                    globalRotation.Y,
                    globalRotation.Z,
                    globalRotation.W),
                ToVec3(bone.GlobalOffset),
                2.0f / globalLengthSquared);

            if (boneIndex < rootBoneCount)
                continue;

            int childIndex = boneIndex - rootBoneCount;
            parentList[childIndex] = checked((byte)(
                boneIndex - bone.ParentIndex));
            (Vector3 localOffset, Quaternion localRotation) =
                DeriveLocalTransform(
                    bones[bone.ParentIndex],
                    bone,
                    modelName,
                    boneIndex);
            int transOffset = childIndex * 3;
            trans[transOffset] = localOffset.X;
            trans[transOffset + 1] = localOffset.Y;
            trans[transOffset + 2] = localOffset.Z;
            int quatOffset = childIndex * 4;
            quats[quatOffset] = QuantizeQuaternionComponent(localRotation.X);
            quats[quatOffset + 1] = QuantizeQuaternionComponent(localRotation.Y);
            quats[quatOffset + 2] = QuantizeQuaternionComponent(localRotation.Z);
            quats[quatOffset + 3] = QuantizeQuaternionComponent(localRotation.W);
        }

        // XMODEL_EXPORT does not carry the source hit-location table. These
        // rigid map models therefore use the engine's HITLOC_NONE value.
        return new XModelSkeleton(
            Array.AsReadOnly(bones.ToArray()),
            rootBoneCount,
            Array.AsReadOnly(boneNames),
            Array.AsReadOnly(parentList),
            Array.AsReadOnly(quats),
            Array.AsReadOnly(trans),
            Array.AsReadOnly(partClassification),
            Array.AsReadOnly(baseMat));
    }

    private static (Vector3 Offset, Quaternion Rotation) DeriveLocalTransform(
        XModelExportBone parent,
        XModelExportBone child,
        string modelName,
        int boneIndex)
    {
        Quaternion parentRotation = Quaternion.Normalize(parent.GlobalRotation);
        Quaternion childRotation = Quaternion.Normalize(child.GlobalRotation);
        Matrix4x4 parentTransform = Matrix4x4.CreateFromQuaternion(parentRotation);
        parentTransform.Translation = parent.GlobalOffset;
        Matrix4x4 childTransform = Matrix4x4.CreateFromQuaternion(childRotation);
        childTransform.Translation = child.GlobalOffset;
        if (!Matrix4x4.Invert(parentTransform, out Matrix4x4 inverseParent))
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' bone {boneIndex} has a " +
                "non-invertible parent bind transform.");
        }

        Matrix4x4 localTransform = childTransform * inverseParent;
        Vector3 localOffset = localTransform.Translation;
        Quaternion localRotation = Quaternion.Normalize(
            Quaternion.CreateFromRotationMatrix(localTransform));
        if (localRotation.W < 0f)
        {
            localRotation = new Quaternion(
                -localRotation.X,
                -localRotation.Y,
                -localRotation.Z,
                -localRotation.W);
        }
        if (!IsFinite(localOffset) || !IsFiniteNonzero(localRotation))
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' bone {boneIndex} has a " +
                "non-finite derived local bind transform.");
        }
        return (localOffset, localRotation);
    }

    private static short QuantizeQuaternionComponent(float value)
    {
        float scaled = MathF.Round(
            Math.Clamp(value, -1f, 1f) * short.MaxValue,
            MidpointRounding.AwayFromZero);
        return checked((short)scaled);
    }

    private static IReadOnlyList<XBoneInfo> CreateBoneInfo(
        IReadOnlyList<GeometryBoundsAccumulator> boneBounds)
    {
        var result = new XBoneInfo[boneBounds.Count];
        for (int boneIndex = 0; boneIndex < boneBounds.Count; boneIndex++)
        {
            GeometryBoundsAccumulator bounds = boneBounds[boneIndex];
            result[boneIndex] = bounds.HasGeometry
                ? new XBoneInfo(
                    bounds.ToBounds(),
                    bounds.MaximumRadiusSquared)
                : new XBoneInfo(
                    new Bounds
                    {
                        MidPoint = new Vec3(),
                        HalfSize = new Vec3()
                    },
                    0f);
        }
        return Array.AsReadOnly(result);
    }

    private static Vector3 ToBoneLocal(
        Vector3 position,
        XModelExportBone bone) => Vector3.Transform(
        position - bone.GlobalOffset,
        Quaternion.Inverse(Quaternion.Normalize(bone.GlobalRotation)));

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFiniteNonzero(Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W) &&
        value.LengthSquared() > 0f;

    private static XModelExportDocument ReadXModelExport(
        string path,
        string modelName,
        int lodIndex)
    {
        string source = File.ReadAllText(path);
        string normalizedSource = RemoveNonRenderableIw3Triangles(
            source,
            modelName,
            lodIndex);
        (normalizedSource, IReadOnlySet<string> unnamedBoneSentinels) =
            SubstituteUnnamedBoneRows(
                normalizedSource,
                modelName,
                lodIndex);
        using TextReader reader = new StringReader(normalizedSource);
        if (XModelExportReader.TryRead(
                reader,
                out XModelExportDocument? document,
                out IReadOnlyList<XModelExportParseIssue> issues))
        {
            if (unnamedBoneSentinels.Count == 0)
                return document!;
            XModelExportBone[] bones = document!.Bones
                .Select(bone => unnamedBoneSentinels.Contains(bone.Name)
                    ? bone with { Name = string.Empty }
                    : bone)
                .ToArray();
            return new XModelExportDocument(
                Array.AsReadOnly(bones),
                document.Vertices,
                document.Triangles,
                document.Objects,
                document.Materials);
        }

        string details = issues.Count == 0
            ? "unknown XMODEL_EXPORT parse failure"
            : string.Join(
                "; ",
                issues.Select(issue =>
                    $"line {issue.Line}, column {issue.Column}: {issue.Message}"));
        throw new InvalidDataException(
            $"IW3 XModel '{modelName}' LOD {lodIndex} file '{path}' is " +
            $"invalid: {details}");
    }

    private static (string Source, IReadOnlySet<string> Sentinels)
        SubstituteUnnamedBoneRows(
            string source,
            string modelName,
            int lodIndex)
    {
        string[] lines = source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        int boneCountLine = FindDirective(lines, "NUMBONES", 0);
        int boneCount = ParseDirectiveCount(
            lines[boneCountLine],
            "NUMBONES",
            modelName,
            lodIndex);
        var sentinels = new HashSet<string>(StringComparer.Ordinal);
        int cursor = boneCountLine + 1;
        for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
        {
            int lineIndex = NextContentLine(lines, cursor);
            cursor = lineIndex + 1;
            string trimmed = lines[lineIndex].Trim();
            string prefix = $"BONE {boneIndex} ";
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal) ||
                !trimmed.EndsWith(" \"\"", StringComparison.Ordinal))
            {
                continue;
            }

            string parent = trimmed[
                prefix.Length..(trimmed.Length - " \"\"".Length)];
            if (!int.TryParse(
                    parent,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                continue;
            }
            string sentinel = $"__iw3_unnamed_bone_{boneIndex}__";
            while (source.Contains(sentinel, StringComparison.Ordinal))
                sentinel += '_';
            lines[lineIndex] = $"BONE {boneIndex} {parent} \"{sentinel}\"";
            sentinels.Add(sentinel);
        }
        return sentinels.Count == 0
            ? (source, sentinels)
            : (string.Join('\n', lines), sentinels);
    }

    private static string RemoveNonRenderableIw3Triangles(
        string source,
        string modelName,
        int lodIndex)
    {
        string[] lines = source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        int vertexCountLine = FindDirective(lines, "NUMVERTS", 0);
        int vertexCount = ParseDirectiveCount(
            lines[vertexCountLine],
            "NUMVERTS",
            modelName,
            lodIndex);
        int faceCountLine = FindDirective(
            lines,
            "NUMFACES",
            vertexCountLine + 1);
        int faceCount = ParseDirectiveCount(
            lines[faceCountLine],
            "NUMFACES",
            modelName,
            lodIndex);

        var positions = new Vector3[vertexCount];
        var foundPositions = new bool[vertexCount];
        for (int lineIndex = vertexCountLine + 1;
             lineIndex < faceCountLine;
             lineIndex++)
        {
            string[] parts = SplitFields(lines[lineIndex]);
            if (parts.Length != 2 || parts[0] != "VERT" ||
                !int.TryParse(
                    parts[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int vertexIndex) ||
                vertexIndex < 0 || vertexIndex >= vertexCount)
            {
                continue;
            }

            int offsetLine = NextContentLine(lines, lineIndex + 1);
            string trimmedOffset = lines[offsetLine].Trim();
            if (!trimmedOffset.StartsWith("OFFSET ", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} vertex " +
                    $"{vertexIndex} has no canonical OFFSET row.");
            }
            positions[vertexIndex] = ParseCommaVector3(
                trimmedOffset[7..],
                $"IW3 XModel '{modelName}' LOD {lodIndex} vertex {vertexIndex} offset");
            foundPositions[vertexIndex] = true;
        }
        if (foundPositions.Any(found => !found))
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' LOD {lodIndex} does not contain " +
                "one position for every declared vertex.");
        }

        var retainedFaceLines = new List<string[]>(faceCount);
        int cursor = faceCountLine + 1;
        int removedCount = 0;
        for (int faceIndex = 0; faceIndex < faceCount; faceIndex++)
        {
            var block = new string[13];
            for (int row = 0; row < block.Length; row++)
            {
                int contentLine = NextContentLine(lines, cursor);
                block[row] = lines[contentLine].Trim();
                cursor = contentLine + 1;
            }
            ValidateIw3FaceBlock(block, modelName, lodIndex, faceIndex);
            int first = ParseCornerVertexIndex(block[1]);
            int second = ParseCornerVertexIndex(block[5]);
            int third = ParseCornerVertexIndex(block[9]);
            if ((uint)first >= (uint)positions.Length ||
                (uint)second >= (uint)positions.Length ||
                (uint)third >= (uint)positions.Length)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' LOD {lodIndex} face " +
                    $"{faceIndex} references a vertex outside the declared table.");
            }

            Vector3 cross = Vector3.Cross(
                positions[second] - positions[first],
                positions[third] - positions[first]);
            if (first == second || first == third || second == third ||
                !IsFinite(cross) || cross.LengthSquared() <= 0.0000000001f)
            {
                removedCount++;
                continue;
            }
            retainedFaceLines.Add(block);
        }
        if (removedCount == 0)
            return source;

        int objectCountLine = NextContentLine(lines, cursor);
        if (!lines[objectCountLine].TrimStart()
                .StartsWith("NUMOBJECTS ", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' LOD {lodIndex} has unexpected " +
                "content after its declared face table.");
        }

        var output = new List<string>(lines.Length - removedCount * 14);
        output.AddRange(lines.Take(faceCountLine));
        output.Add($"NUMFACES {retainedFaceLines.Count}");
        foreach (string[] face in retainedFaceLines)
        {
            output.AddRange(face);
            output.Add(string.Empty);
        }
        output.AddRange(lines.Skip(objectCountLine));
        return string.Join('\n', output);
    }

    private static int FindDirective(
        IReadOnlyList<string> lines,
        string directive,
        int start)
    {
        for (int index = start; index < lines.Count; index++)
        {
            if (lines[index].TrimStart()
                .StartsWith(directive + " ", StringComparison.Ordinal))
            {
                return index;
            }
        }
        throw new InvalidDataException(
            $"XMODEL_EXPORT has no {directive} directive.");
    }

    private static int ParseDirectiveCount(
        string line,
        string directive,
        string modelName,
        int lodIndex)
    {
        string[] parts = SplitFields(line);
        if (parts.Length != 2 || parts[0] != directive ||
            !int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int count) ||
            count < 0)
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' LOD {lodIndex} has an invalid " +
                $"{directive} directive.");
        }
        return count;
    }

    private static int NextContentLine(
        IReadOnlyList<string> lines,
        int start)
    {
        for (int index = start; index < lines.Count; index++)
        {
            string value = lines[index].Trim();
            if (value.Length != 0 &&
                !value.StartsWith("//", StringComparison.Ordinal))
            {
                return index;
            }
        }
        throw new InvalidDataException(
            "XMODEL_EXPORT ended before its declared content was complete.");
    }

    private static void ValidateIw3FaceBlock(
        IReadOnlyList<string> rows,
        string modelName,
        int lodIndex,
        int faceIndex)
    {
        bool valid = SplitFields(rows[0]).FirstOrDefault() == "TRI";
        for (int corner = 0; corner < 3; corner++)
        {
            int offset = 1 + corner * 4;
            valid &= SplitFields(rows[offset]).FirstOrDefault() == "VERT";
            valid &= SplitFields(rows[offset + 1]).FirstOrDefault() == "NORMAL";
            valid &= SplitFields(rows[offset + 2]).FirstOrDefault() == "COLOR";
            valid &= SplitFields(rows[offset + 3]).FirstOrDefault() == "UV";
        }
        if (!valid)
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' LOD {lodIndex} face " +
                $"{faceIndex} does not use the canonical XMODEL_EXPORT v6 " +
                "row layout.");
        }
    }

    private static int ParseCornerVertexIndex(string row)
    {
        string[] parts = SplitFields(row);
        return parts.Length == 2 &&
            int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int index)
            ? index
            : -1;
    }

    private static Vector3 ParseCommaVector3(
        string value,
        string description)
    {
        string[] components = value.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (components.Length != 3 ||
            !TryParseFiniteSingle(components[0], out float x) ||
            !TryParseFiniteSingle(components[1], out float y) ||
            !TryParseFiniteSingle(components[2], out float z))
        {
            throw new InvalidDataException(
                $"{description} is not a finite three-component vector.");
        }
        return new Vector3(x, y, z);
    }

    private static bool TryParseFiniteSingle(string value, out float result) =>
        float.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) &&
        float.IsFinite(result);

    private static string[] SplitFields(string value) => value.Split(
        (char[]?)null,
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static XModelMetadata ReadMetadata(
        string path,
        string modelName)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.SequentialScan);
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' metadata root must be an object.");
            }

            RequireString(root, "_game", ExpectedGame, modelName);
            RequireString(root, "_type", ExpectedAssetType, modelName);
            string modelType = RequireStringValue(
                root,
                "type",
                modelName,
                null);
            if (modelType is not RigidModelType and not AnimatedModelType)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' metadata model type " +
                    $"'{modelType}' is unsupported; expected rigid or animated.");
            }
            int version = RequireInt32(root, "_version", modelName);
            if (version != ExpectedMetadataVersion)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' metadata version {version} is " +
                    $"unsupported; expected {ExpectedMetadataVersion}.");
            }
            uint flags = RequireUInt32(root, "flags", modelName);
            if (flags > byte.MaxValue)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' flags 0x{flags:x} cannot be " +
                    "represented by IW4.");
            }
            int collisionLod = root.TryGetProperty("collLod", out _)
                ? RequireInt32(root, "collLod", modelName)
                : -1;
            if (collisionLod is < -1 or > 3)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' collision LOD {collisionLod} " +
                    "is outside the IW3/IW4 range.");
            }
            if (!root.TryGetProperty("lods", out JsonElement lodsElement) ||
                lodsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' metadata has no LOD array.");
            }
            int lodCount = lodsElement.GetArrayLength();
            if (lodCount is < 1 or > 4)
            {
                throw new InvalidDataException(
                    $"IW3 XModel '{modelName}' has {lodCount} active LODs; " +
                    "IW4 requires between one and four.");
            }

            var lods = new List<XModelMetadataLod>(lodCount);
            float previousDistance = float.NegativeInfinity;
            int lodIndex = 0;
            foreach (JsonElement lodElement in lodsElement.EnumerateArray())
            {
                if (lodElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        $"IW3 XModel '{modelName}' LOD {lodIndex} metadata " +
                        "must be an object.");
                }
                float distance = RequireSingle(
                    lodElement,
                    "distance",
                    modelName,
                    lodIndex);
                if (!float.IsFinite(distance) || distance < 0f)
                {
                    throw new InvalidDataException(
                        $"IW3 XModel '{modelName}' LOD {lodIndex} distance " +
                        "must be finite and nonnegative.");
                }
                if (lodIndex > 0 && distance <= previousDistance)
                {
                    throw new InvalidDataException(
                        $"IW3 XModel '{modelName}' active LOD distances must " +
                        "be strictly increasing.");
                }
                string file = RequireStringValue(
                    lodElement,
                    "file",
                    modelName,
                    lodIndex);
                lods.Add(new XModelMetadataLod(distance, file));
                previousDistance = distance;
                lodIndex++;
            }

            return new XModelMetadata(
                string.Equals(
                    modelType,
                    AnimatedModelType,
                    StringComparison.Ordinal),
                (XModelFlags)(byte)flags,
                collisionLod,
                Array.AsReadOnly(lods.ToArray()));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' metadata '{path}' is invalid JSON: " +
                exception.Message,
                exception);
        }
    }

    private static void RequireString(
        JsonElement root,
        string propertyName,
        string expectedValue,
        string modelName)
    {
        string value = RequireStringValue(root, propertyName, modelName, null);
        if (!string.Equals(value, expectedValue, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' metadata property '{propertyName}' " +
                $"must be '{expectedValue}', not '{value}'.");
        }
    }

    private static string RequireStringValue(
        JsonElement root,
        string propertyName,
        string modelName,
        int? lodIndex)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element) ||
            element.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                MetadataFieldPrefix(modelName, lodIndex) +
                $" property '{propertyName}' must be a string.");
        }
        string? value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
        {
            throw new InvalidDataException(
                MetadataFieldPrefix(modelName, lodIndex) +
                $" property '{propertyName}' is empty or invalid.");
        }
        return value;
    }

    private static int RequireInt32(
        JsonElement root,
        string propertyName,
        string modelName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out int value))
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' metadata property '{propertyName}' " +
                "must be an Int32.");
        }
        return value;
    }

    private static uint RequireUInt32(
        JsonElement root,
        string propertyName,
        string modelName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetUInt32(out uint value))
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' metadata property '{propertyName}' " +
                "must be a UInt32.");
        }
        return value;
    }

    private static float RequireSingle(
        JsonElement root,
        string propertyName,
        string modelName,
        int lodIndex)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetSingle(out float value))
        {
            throw new InvalidDataException(
                $"IW3 XModel '{modelName}' LOD {lodIndex} metadata property " +
                $"'{propertyName}' must be a finite Single.");
        }
        return value;
    }

    private static string MetadataFieldPrefix(
        string modelName,
        int? lodIndex) => lodIndex.HasValue
            ? $"IW3 XModel '{modelName}' LOD {lodIndex.Value} metadata"
            : $"IW3 XModel '{modelName}' metadata";

    private static string ResolveContainedFile(
        string root,
        string relativePath,
        string description)
    {
        if (Path.IsPathFullyQualified(relativePath) ||
            relativePath.Contains('\0'))
        {
            throw new InvalidDataException(
                $"{description} path '{relativePath}' must be relative.");
        }
        string normalized = relativePath.Replace('\\', '/');
        if (normalized.Split('/').Any(segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException(
                $"{description} path '{relativePath}' is not a canonical " +
                "relative path.");
        }

        string fullPath = Path.GetFullPath(Path.Combine(
            root,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        string rootPrefix = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, FilePathComparison()))
        {
            throw new InvalidDataException(
                $"{description} path '{relativePath}' escapes the IW3 export root.");
        }
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"{description} file does not exist.",
                fullPath);
        }
        return fullPath;
    }

    private static string NormalizeOwnedAssetName(
        string value,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Replace('\\', '/');
        ValidateLatin1Name(normalized, description);
        if (normalized[0] == ',' || normalized[0] == '/' ||
            !string.Equals(normalized, normalized.Trim(), StringComparison.Ordinal) ||
            normalized.Split('/').Any(segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException(
                $"{description} '{value}' is not a canonical owned asset name.");
        }
        return normalized;
    }

    private static void ValidateLatin1Name(
        string value,
        string description)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains('\0') ||
            value.Any(character =>
                char.IsControl(character) || character > byte.MaxValue))
        {
            throw new InvalidDataException(
                $"{description} '{value}' is not a valid Latin-1 name.");
        }
    }

    private static uint[] ZeroPartBits() => new uint[6];

    private static Vec3 ToVec3(Vector3 value) => new()
    {
        X = value.X,
        Y = value.Y,
        Z = value.Z
    };

    private static StringComparer FilePathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static StringComparison FilePathComparison() =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed record XModelMetadata(
        bool IsAnimated,
        XModelFlags Flags,
        int CollisionLod,
        IReadOnlyList<XModelMetadataLod> Lods);

    private sealed record XModelMetadataLod(float Distance, string File);

    private sealed record RequestedXModel(
        string Name,
        XModelMetadata Metadata);

    private sealed record XModelExportLodSource(
        int Index,
        XModelMetadataLod Metadata,
        XModelExportDocument TangentSource,
        XModelExportDocument Document);

    private sealed record XModelSkeleton(
        IReadOnlyList<XModelExportBone> Bones,
        int RootBoneCount,
        IReadOnlyList<ScriptStringReference> BoneNames,
        IReadOnlyList<byte> ParentList,
        IReadOnlyList<short> Quats,
        IReadOnlyList<float> Trans,
        IReadOnlyList<byte> PartClassification,
        IReadOnlyList<DObjAnimMat> BaseMat);

    private sealed class GeometryBoundsAccumulator
    {
        private Vector3 _minimum;
        private Vector3 _maximum;

        internal bool HasGeometry { get; private set; }
        internal float MaximumRadiusSquared { get; private set; }

        internal void Add(Vector3 position)
        {
            if (!float.IsFinite(position.X) ||
                !float.IsFinite(position.Y) ||
                !float.IsFinite(position.Z))
            {
                throw new InvalidDataException(
                    "XMODEL_EXPORT geometry contains a non-finite position.");
            }
            if (!HasGeometry)
            {
                _minimum = position;
                _maximum = position;
                HasGeometry = true;
            }
            else
            {
                _minimum = Vector3.Min(_minimum, position);
                _maximum = Vector3.Max(_maximum, position);
            }
            MaximumRadiusSquared = MathF.Max(
                MaximumRadiusSquared,
                position.LengthSquared());
            if (!float.IsFinite(MaximumRadiusSquared))
            {
                throw new InvalidDataException(
                    "XMODEL_EXPORT geometry bounds exceed the IW4 finite range.");
            }
        }

        internal Bounds ToBounds()
        {
            if (!HasGeometry)
            {
                throw new InvalidOperationException(
                    "Geometry bounds require at least one position.");
            }
            Vector3 midpoint = (_minimum + _maximum) * 0.5f;
            Vector3 halfSize = (_maximum - _minimum) * 0.5f;
            if (!float.IsFinite(midpoint.X) ||
                !float.IsFinite(midpoint.Y) ||
                !float.IsFinite(midpoint.Z) ||
                !float.IsFinite(halfSize.X) ||
                !float.IsFinite(halfSize.Y) ||
                !float.IsFinite(halfSize.Z))
            {
                throw new InvalidDataException(
                    "XMODEL_EXPORT geometry bounds exceed the IW4 finite range.");
            }
            return new Bounds
            {
                MidPoint = ToVec3(midpoint),
                HalfSize = ToVec3(halfSize)
            };
        }
    }
}
