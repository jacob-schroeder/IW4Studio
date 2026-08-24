using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Numerics;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;
using IW4.AssetExchange.XModel;
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
                        else if (XModelImportedMaterialCompiler.TryCompile(
                                     baseline.Name,
                                     importedMaterial,
                                     mapping.Material,
                                     out MaterialAsset? authoredMaterial,
                                     out GfxImageAsset? authoredColorImage,
                                     out GfxImageAsset? authoredNormalImage,
                                     out string? blocker))
                        {
                            material = authoredMaterial!;
                            providers[AssetKey.FromDefinition(material)] = material;
                            providers[AssetKey.FromDefinition(authoredColorImage!)] = authoredColorImage!;
                            if (authoredNormalImage is not null)
                            {
                                providers[AssetKey.FromDefinition(authoredNormalImage)] =
                                    authoredNormalImage;
                            }
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
                else if (!XModelCollisionTreeValidator.TryValidate(
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
