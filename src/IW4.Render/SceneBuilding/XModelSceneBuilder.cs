using System.Numerics;
using IW4.AssetExchange.XModel;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.Render.Assets;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Geometry.XModel;
using IW4.Render.Materials;
using IW4.Render.Shaders;
using IW4.Render.Techniques;
using IW4.Render.Textures;
using IW4.Render.Transforms;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Builds a backend-neutral render projection for one loaded XModel.
/// </summary>
public sealed class XModelSceneBuilder
{
    public XModelRenderScene BuildMaterialPreview(
        MaterialAsset material,
        RenderAssetSource assetSource,
        IGfxImagePayloadResolver imagePayloadResolver,
        IReadOnlyList<BaseAsset> stagedAssets)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(assetSource);
        ArgumentNullException.ThrowIfNull(imagePayloadResolver);
        ArgumentNullException.ThrowIfNull(stagedAssets);
        string materialName = !string.IsNullOrWhiteSpace(material.Info.Name)
            ? material.Info.Name
            : throw new InvalidOperationException(
                "The material preview requires a loaded material name.");
        XSurface sphere = CreateMaterialPreviewSphere();
        var diagnostics = new List<string>();
        long poolRevision = assetSource.AssetPool.Revision;
        var lookup = new RenderAssetLookup(
            assetSource,
            imagePayloadResolver,
            stagedAssets);
        var textureCache = new RenderTextureCache(
            preferProvenAuthoredPayloads: false);
        var failedTextureCacheKeys =
            new HashSet<RenderTextureCacheKey>();
        var shaderTranslationCache = new ShaderTranslationCache();

        XModelRenderSurface surface;
        if (!lookup.TryResolveCanonicalMaterialTechniqueBinding(
                materialName,
                poolRevision,
                out MaterialTechniqueBinding? canonicalBinding) ||
            canonicalBinding is null)
        {
            string blockedStatus =
                $"material={materialName};canonicalGraph=unresolved@revision{poolRevision}";
            diagnostics.Add($"LOD 0 surface 0: {blockedStatus}.");
            surface = ProjectSurface(
                materialName,
                lodIndex: 0,
                geometrySurfaceIndex: 0,
                parentMaterialIndex: 0,
                materialName,
                sphere,
                boneCount: 0,
                selectedTechniqueSlot: -1,
                selectedTechniqueName: string.Empty,
                [],
                authoredGroupReady: false,
                blockedStatus,
                diagnostics);
        }
        else
        {
            AuthoredCameraColorTechniqueSelection selectedTechnique =
                AuthoredCameraColorTechniqueSelector.Select(
                    material,
                    canonicalBinding.TechniqueSet,
                    lookup);
            surface = BuildAuthoredSurface(
                materialName,
                lodIndex: 0,
                geometrySurfaceIndex: 0,
                parentMaterialIndex: 0,
                materialName,
                sphere,
                boneCount: 0,
                material,
                canonicalBinding.TechniqueSet,
                selectedTechnique,
                lookup,
                imagePayloadResolver,
                textureCache,
                failedTextureCacheKeys,
                shaderTranslationCache,
                diagnostics);
        }

        if (!lookup.HasCanonicalAssetPoolRevision(poolRevision))
        {
            throw new InvalidOperationException(
                $"The canonical asset-pool revision changed while building material preview '{materialName}': " +
                $"start={poolRevision};end={assetSource.AssetPool.Revision}.");
        }

        var lod = new XModelRenderLod(
            lodIndex: 0,
            distance: 0f,
            surface.Bounds,
            [surface]);
        return new XModelRenderScene(
            materialName,
            [lod],
            defaultLodIndex: 0,
            surface.Bounds,
            bones: [],
            diagnostics);
    }

    public XModelRenderScene Build(
        XModelAsset model,
        RenderAssetSource assetSource,
        IGfxImagePayloadResolver imagePayloadResolver) =>
        Build(model, assetSource, imagePayloadResolver, []);

    public XModelRenderScene Build(
        XModelAsset model,
        RenderAssetSource assetSource,
        IGfxImagePayloadResolver imagePayloadResolver,
        IReadOnlyList<BaseAsset> stagedAssets)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assetSource);
        ArgumentNullException.ThrowIfNull(imagePayloadResolver);
        ArgumentNullException.ThrowIfNull(stagedAssets);
        string modelName = !string.IsNullOrWhiteSpace(model.Name)
            ? model.Name
            : throw new InvalidOperationException(
                "The XModel projection requires a loaded model name.");
        var diagnostics = new List<string>();
        IReadOnlyList<XModelRenderBone> bones =
            ProjectBones(model, diagnostics);

        if (model.NumSurfs == 0 &&
            model.Lods.All(lod => lod.NumSurfs == 0))
        {
            diagnostics.Add(
                $"XModel '{modelName}' contains no renderable surfaces.");
            return new XModelRenderScene(
                modelName,
                [],
                defaultLodIndex: -1,
                RenderBounds.Empty,
                bones,
                diagnostics);
        }
        if (!XModelLodGeometryCatalog.TryCreate(
                model,
                out IReadOnlyList<XModelLodGeometry>
                    lodGeometries))
        {
            throw new InvalidOperationException(
                $"XModel '{modelName}' does not contain a complete " +
                "loaded LOD geometry catalog.");
        }

        long poolRevision = assetSource.AssetPool.Revision;
        var lookup = new RenderAssetLookup(
            assetSource,
            imagePayloadResolver,
            stagedAssets);
        var textureCache = new RenderTextureCache(
            preferProvenAuthoredPayloads: false);
        var failedTextureCacheKeys =
            new HashSet<RenderTextureCacheKey>();
        var shaderTranslationCache = new ShaderTranslationCache();
        var lods = new List<XModelRenderLod>(lodGeometries.Count);
        RenderBounds aggregateBounds = RenderBounds.Empty;

        foreach (XModelLodGeometry lodGeometry in lodGeometries)
        {
            EnsurePoolRevision();
            if (!float.IsFinite(lodGeometry.Lod.Dist))
            {
                throw IncompleteLod(
                    modelName,
                    lodGeometry.LodIndex,
                    "distance is not finite");
            }

            var surfaces = new List<XModelRenderSurface>(
                lodGeometry.SurfaceCount);
            RenderBounds lodBounds = RenderBounds.Empty;
            for (int surfaceOffset = 0;
                 surfaceOffset < lodGeometry.SurfaceCount;
                 surfaceOffset++)
            {
                EnsurePoolRevision();
                int parentMaterialIndex = checked(
                    lodGeometry.MaterialSurfaceStart + surfaceOffset);
                MaterialAsset? loadedMaterial =
                    (uint)parentMaterialIndex < (uint)model.Materials.Count
                        ? model.Materials[parentMaterialIndex]
                        : null;
                string? loadedMaterialName = loadedMaterial?.Info.Name;
                if (loadedMaterial is null ||
                    string.IsNullOrWhiteSpace(loadedMaterialName))
                {
                    throw IncompleteLod(
                        modelName,
                        lodGeometry.LodIndex,
                        $"surface {surfaceOffset} has no parent material at index {parentMaterialIndex}");
                }

                if ((!lookup.TryResolveCanonicalMaterialTechniqueBinding(
                         loadedMaterialName,
                         poolRevision,
                         out MaterialTechniqueBinding? binding) &&
                     !lookup.TryResolveStagedMaterialTechniqueBinding(
                         loadedMaterial,
                         poolRevision,
                         out binding)) ||
                    binding is null)
                {
                    XSurface blockedSurface =
                        lodGeometry.ModelSurfs.Surfaces[surfaceOffset];
                    XModelRenderSurface blockedProjection = ProjectSurface(
                        modelName,
                        lodGeometry.LodIndex,
                        surfaceOffset,
                        parentMaterialIndex,
                        loadedMaterialName,
                        blockedSurface,
                        model.NumBones,
                        selectedTechniqueSlot: -1,
                        selectedTechniqueName: string.Empty,
                        [],
                        authoredGroupReady: false,
                        $"material={loadedMaterialName};canonicalGraph=unresolved@revision{poolRevision}",
                        diagnostics);
                    surfaces.Add(blockedProjection);
                    lodBounds = IncludeBounds(
                        lodBounds,
                        blockedProjection.Bounds);
                    continue;
                }

                MaterialAsset material = binding.Material;
                MaterialTechniqueSetAsset techniqueSet =
                    binding.TechniqueSet;
                AuthoredCameraColorTechniqueSelection selectedTechnique =
                    AuthoredCameraColorTechniqueSelector.Select(
                        material,
                        techniqueSet,
                        lookup);
                XSurface surface =
                    lodGeometry.ModelSurfs.Surfaces[surfaceOffset];
                XModelRenderSurface projection = BuildAuthoredSurface(
                    modelName,
                    lodGeometry.LodIndex,
                    surfaceOffset,
                    parentMaterialIndex,
                    loadedMaterialName,
                    surface,
                    model.NumBones,
                    material,
                    techniqueSet,
                    selectedTechnique,
                    lookup,
                    imagePayloadResolver,
                    textureCache,
                    failedTextureCacheKeys,
                    shaderTranslationCache,
                    diagnostics);
                surfaces.Add(projection);
                lodBounds = IncludeBounds(lodBounds, projection.Bounds);
            }

            if (!lodBounds.IsValid)
            {
                diagnostics.Add(
                    $"LOD {lodGeometry.LodIndex} has no visible Fit bounds because every valid triangle was proven alpha-test invisible; authored topology was retained.");
            }

            var lod = new XModelRenderLod(
                lodGeometry.LodIndex,
                lodGeometry.Lod.Dist,
                lodBounds,
                surfaces);
            lods.Add(lod);
            aggregateBounds = IncludeBounds(aggregateBounds, lodBounds);
        }

        EnsurePoolRevision();
        return new XModelRenderScene(
            modelName,
            lods,
            lodGeometries[0].LodIndex,
            aggregateBounds,
            bones,
            diagnostics);

        void EnsurePoolRevision()
        {
            if (!lookup.HasCanonicalAssetPoolRevision(poolRevision))
            {
                throw new InvalidOperationException(
                    $"The canonical asset-pool revision changed while building XModel '{modelName}': " +
                    $"start={poolRevision};end={assetSource.AssetPool.Revision}.");
            }
        }
    }

    public XModelRenderScene BuildComposite(
        IReadOnlyList<XAnimPreviewModelComponent> components,
        int preferredLodIndex,
        RenderAssetSource assetSource,
        IGfxImagePayloadResolver imagePayloadResolver) =>
        BuildComposite(
            components,
            preferredLodIndex,
            assetSource,
            imagePayloadResolver,
            []);

    /// <summary>
    /// Builds one renderable LOD for an ordered, attached XModel composition.
    /// Every component retains its own skinning bind space while its bone
    /// influences are rebased into the shared animation palette.
    /// </summary>
    public XModelRenderScene BuildComposite(
        IReadOnlyList<XAnimPreviewModelComponent> components,
        int preferredLodIndex,
        RenderAssetSource assetSource,
        IGfxImagePayloadResolver imagePayloadResolver,
        IReadOnlyList<BaseAsset> stagedAssets)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(assetSource);
        ArgumentNullException.ThrowIfNull(imagePayloadResolver);
        ArgumentNullException.ThrowIfNull(stagedAssets);
        if (preferredLodIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(preferredLodIndex));
        if (!XAnimPreviewComposition.TryProject(
                components,
                out XAnimPreviewComposition? composition,
                out string reason) ||
            composition is null)
        {
            throw new InvalidOperationException(reason);
        }

        var diagnostics = new List<string>();
        var surfaces = new List<XModelRenderSurface>();
        var usedGroupIds = new HashSet<int>();
        int nextGroupId = 0;
        float distance = 0f;
        bool hasDistance = false;
        RenderBounds aggregateBounds = RenderBounds.Empty;
        foreach (XAnimPreviewCompositionComponent component in
                 composition.Components)
        {
            XModelAsset model = component.Source.Model;
            string modelName = string.IsNullOrWhiteSpace(model.Name)
                ? "<unnamed XModel>"
                : model.Name;
            XModelRenderScene modelScene = Build(
                model,
                assetSource,
                imagePayloadResolver,
                stagedAssets);
            XModelRenderLod selectedLod = SelectCompositeLod(
                modelScene,
                preferredLodIndex,
                diagnostics);
            if (!selectedLod.HasCompleteSkinning)
            {
                throw new InvalidOperationException(
                    $"LOD {selectedLod.LodIndex} of '{modelName}' does not contain complete skinning for every rendered surface.");
            }
            if (!hasDistance)
            {
                distance = selectedLod.Distance;
                hasDistance = true;
            }

            diagnostics.AddRange(modelScene.Diagnostics.Select(diagnostic =>
                $"{modelName}: {diagnostic}"));
            foreach (XModelRenderSurface sourceSurface in
                     selectedLod.Surfaces)
            {
                int geometrySurfaceIndex = surfaces.Count;
                int groupId = ResolveCompositeGroupId(
                    sourceSurface.AuthoredPasses,
                    usedGroupIds,
                    ref nextGroupId);
                XModelRenderSurface surface = ComposeSurface(
                    sourceSurface,
                    geometrySurfaceIndex,
                    groupId,
                    component,
                    composition.Bones.Count);
                surfaces.Add(surface);
                aggregateBounds = IncludeBounds(
                    aggregateBounds,
                    surface.Bounds);
            }
        }

        if (surfaces.Count == 0)
        {
            throw new InvalidOperationException(
                "The XModel composition contains no renderable surfaces.");
        }

        IReadOnlyList<XModelRenderBone> bones = Array.AsReadOnly(
            composition.Bones
                .Select((bone, boneIndex) => new XModelRenderBone(
                    boneIndex,
                    bone.Name,
                    ToRenderCoordinates(
                        bone.BindGlobalTransform.Translation)))
                .ToArray());
        var lod = new XModelRenderLod(
            preferredLodIndex,
            distance,
            aggregateBounds,
            surfaces);
        string name = string.Join(
            " + ",
            components.Select(component =>
                string.IsNullOrWhiteSpace(component.Model.Name)
                    ? "<unnamed XModel>"
                    : component.Model.Name));
        return new XModelRenderScene(
            name,
            [lod],
            preferredLodIndex,
            aggregateBounds,
            bones,
            diagnostics);
    }

    private static XModelRenderLod SelectCompositeLod(
        XModelRenderScene scene,
        int preferredLodIndex,
        List<string> diagnostics)
    {
        XModelRenderLod? selected = scene.Lods.FirstOrDefault(lod =>
            lod.LodIndex == preferredLodIndex);
        if (selected is not null)
            return selected;

        selected = scene.Lods.FirstOrDefault(lod =>
            lod.LodIndex == scene.DefaultLodIndex) ??
            scene.Lods.FirstOrDefault();
        if (selected is null)
        {
            throw new InvalidOperationException(
                $"XModel '{scene.Name}' contains no renderable LOD for the composition.");
        }

        diagnostics.Add(
            $"XModel '{scene.Name}' has no LOD {preferredLodIndex}; composed LOD {selected.LodIndex} instead.");
        return selected;
    }

    private static int ResolveCompositeGroupId(
        IReadOnlyList<XModelRenderAuthoredPass> passes,
        HashSet<int> usedGroupIds,
        ref int nextGroupId)
    {
        if (passes.Count == 0)
            return -1;

        int sourceGroupId = passes[0].GroupId;
        if (passes.Any(pass => pass.GroupId != sourceGroupId))
        {
            throw new InvalidOperationException(
                "An authored surface contains passes from more than one draw group.");
        }
        if (usedGroupIds.Add(sourceGroupId))
            return sourceGroupId;

        while (!usedGroupIds.Add(nextGroupId))
            nextGroupId = checked(nextGroupId + 1);
        return nextGroupId++;
    }

    private static XModelRenderSurface ComposeSurface(
        XModelRenderSurface source,
        int geometrySurfaceIndex,
        int groupId,
        XAnimPreviewCompositionComponent component,
        int combinedBoneCount)
    {
        IReadOnlyList<XModelRenderSkinningVertex>? sourceSkinning =
            source.SkinningVertices;
        if (sourceSkinning is null ||
            sourceSkinning.Count != source.Positions.Count)
        {
            throw new InvalidOperationException(
                $"Surface {source.GeometrySurfaceIndex} of '{component.Source.Model.Name}' has incomplete skinning.");
        }

        var skinning = new XModelRenderSkinningVertex[
            sourceSkinning.Count];
        for (int vertexIndex = 0;
             vertexIndex < sourceSkinning.Count;
             vertexIndex++)
        {
            XModelRenderSkinningVertex vertex = sourceSkinning[vertexIndex];
            if (vertex.Influences.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Surface {source.GeometrySurfaceIndex} vertex {vertexIndex} of '{component.Source.Model.Name}' has no skinning influences.");
            }

            var influences = new XModelRenderBoneInfluence[
                vertex.Influences.Length];
            for (int influenceIndex = 0;
                 influenceIndex < vertex.Influences.Length;
                 influenceIndex++)
            {
                XModelRenderBoneInfluence influence =
                    vertex.Influences[influenceIndex];
                if ((uint)influence.BoneIndex >=
                    (uint)component.Bones.Count)
                {
                    throw new InvalidOperationException(
                        $"Surface {source.GeometrySurfaceIndex} vertex {vertexIndex} of '{component.Source.Model.Name}' references bone {influence.BoneIndex} outside its skeleton.");
                }

                int combinedBoneIndex = checked(
                    component.BoneOffset + influence.BoneIndex);
                if ((uint)combinedBoneIndex >= (uint)combinedBoneCount)
                {
                    throw new InvalidOperationException(
                        "A composed skinning influence exceeds the combined bone palette.");
                }
                influences[influenceIndex] = new XModelRenderBoneInfluence(
                    combinedBoneIndex,
                    influence.Weight);
            }

            skinning[vertexIndex] = new XModelRenderSkinningVertex(
                vertex.BindPosition,
                vertex.BindNormal,
                vertex.BindTangent,
                influences);
        }

        bool transformBindPayload =
            component.Source.AttachmentBoneName is not null;
        Vector3[] positions = source.Positions
            .Select(position => TransformRenderPosition(
                position,
                component.AttachmentBindTransform))
            .ToArray();
        var authoredPasses = new XModelRenderAuthoredPass[
            source.AuthoredPasses.Count];
        for (int passIndex = 0;
             passIndex < source.AuthoredPasses.Count;
             passIndex++)
        {
            XModelRenderAuthoredPass pass = source.AuthoredPasses[passIndex];
            if (!transformBindPayload && pass.GroupId == groupId)
            {
                authoredPasses[passIndex] = pass;
                continue;
            }

            float[] rsxVertexInputs = pass.RsxVertexInputs.ToArray();
            if (transformBindPayload && rsxVertexInputs.Length > 0)
            {
                TransformRsxVertexInputs(
                    rsxVertexInputs,
                    pass.ShaderExecution.VertexInputs,
                    source.Positions.Count,
                    component.AttachmentBindTransform);
            }
            authoredPasses[passIndex] = new XModelRenderAuthoredPass(
                groupId,
                pass.GroupPassIndex,
                pass.Pass,
                pass.PrimarySampler,
                pass.State,
                pass.ShaderExecution,
                pass.MaterialSamplers,
                rsxVertexInputs,
                pass.Diagnostic);
        }

        RenderBounds bounds = TransformRenderBounds(
            source.Bounds,
            component.AttachmentBindTransform);
        return new XModelRenderSurface(
            geometrySurfaceIndex,
            source.ParentMaterialIndex,
            source.MaterialName,
            positions,
            skinning,
            source.Indices,
            source.CollisionIndices,
            bounds,
            source.SelectedTechniqueSlot,
            source.SelectedTechniqueName,
            authoredPasses,
            source.AuthoredGroupReady,
            source.AuthoredMaterialStatus);
    }

    private static void TransformRsxVertexInputs(
        float[] values,
        IReadOnlyList<ShaderVertexInputBinding> bindings,
        int vertexCount,
        Matrix4x4 transform)
    {
        const int componentCount =
            XSurfaceVertexDecoder.RsxVertexInputComponentCount;
        int vertexStride = checked(
            XSurfaceVertexDecoder.RsxVertexInputCount * componentCount);
        if (values.Length != checked(vertexCount * vertexStride))
        {
            throw new InvalidOperationException(
                "An authored-pass vertex payload does not cover the composed surface.");
        }

        foreach (ShaderVertexInputBinding binding in bindings)
        {
            if (binding.IsDisabledDefaultAttribute ||
                binding.Source is not (
                    MaterialStreamSource.Position or
                    MaterialStreamSource.Normal or
                    MaterialStreamSource.Tangent))
            {
                continue;
            }

            int destination = (byte)binding.Destination;
            for (int vertexIndex = 0;
                 vertexIndex < vertexCount;
                 vertexIndex++)
            {
                int offset = checked(
                    vertexIndex * vertexStride +
                    destination * componentCount);
                var value = new Vector3(
                    values[offset],
                    values[offset + 1],
                    values[offset + 2]);
                value = binding.Source == MaterialStreamSource.Position
                    ? Vector3.Transform(value, transform)
                    : NormalizeDirection(
                        Vector3.TransformNormal(value, transform));
                values[offset] = value.X;
                values[offset + 1] = value.Y;
                values[offset + 2] = value.Z;
            }
        }
    }

    private static RenderBounds TransformRenderBounds(
        RenderBounds bounds,
        Matrix4x4 transform)
    {
        if (!bounds.IsValid)
            return bounds;

        RenderBounds transformed = RenderBounds.Empty;
        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int z = 0; z < 2; z++)
                {
                    var corner = new Vector3(
                        x == 0 ? bounds.Min.X : bounds.Max.X,
                        y == 0 ? bounds.Min.Y : bounds.Max.Y,
                        z == 0 ? bounds.Min.Z : bounds.Max.Z);
                    transformed = transformed.Include(
                        TransformRenderPosition(corner, transform));
                }
            }
        }
        return transformed;
    }

    private static Vector3 TransformRenderPosition(
        Vector3 renderPosition,
        Matrix4x4 gameTransform)
    {
        Vector3 gamePosition =
            RenderCoordinateConverter.RenderToGamePosition(renderPosition);
        return ToRenderCoordinates(
            Vector3.Transform(gamePosition, gameTransform));
    }

    private static Vector3 NormalizeDirection(Vector3 value)
    {
        float lengthSquared = value.LengthSquared();
        return !float.IsFinite(lengthSquared) ||
               lengthSquared <= float.Epsilon
            ? Vector3.Zero
            : value / MathF.Sqrt(lengthSquared);
    }

    private static XModelRenderSurface BuildAuthoredSurface(
        string modelName,
        int lodIndex,
        int geometrySurfaceIndex,
        int parentMaterialIndex,
        string materialName,
        XSurface surface,
        int boneCount,
        MaterialAsset material,
        MaterialTechniqueSetAsset techniqueSet,
        AuthoredCameraColorTechniqueSelection selectedTechnique,
        RenderAssetLookup lookup,
        IGfxImagePayloadResolver imagePayloads,
        RenderTextureCache textureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        ShaderTranslationCache shaderTranslationCache,
        List<string> diagnostics)
    {
        if (selectedTechnique.Passes.Count == 0)
        {
            string blockedStatus =
                $"material={materialName};authoredGroup=blocked:{selectedTechnique.Blocker}";
            diagnostics.Add(
                $"LOD {lodIndex} surface {geometrySurfaceIndex}: {blockedStatus}.");
            return ProjectSurface(
                modelName,
                lodIndex,
                geometrySurfaceIndex,
                parentMaterialIndex,
                materialName,
                surface,
                boneCount,
                selectedTechnique.TechniqueSlot,
                selectedTechnique.TechniqueName,
                [],
                authoredGroupReady: false,
                blockedStatus,
                diagnostics);
        }

        int groupId = checked(lodIndex * 0x10000 + geometrySurfaceIndex);
        var packets = new List<XModelRenderAuthoredPass>(
            selectedTechnique.Passes.Count);
        var groupBlockers = new List<string>();
        IReadOnlyList<int> retainedSourceVertices =
            ResolveRetainedSourceVertices(surface);
        int passOrdinal = 0;
        foreach (AuthoredCameraColorPassSelection selectedPass in
                 selectedTechnique.Passes)
        {
            AuthoredMaterialSamplerResolver.TrySelectPrimary(
                material,
                selectedPass.SourcePass,
                selectedPass.Arguments,
                lookup,
                XSurfaceVertexDecoder.DefaultTexCoordSource,
                out AuthoredMaterialPrimarySamplerSelection? primarySampler);
            var materialPass = new MaterialPassIdentity(
                material.Info.Name ?? string.Empty,
                new TechniquePassIdentity(
                    techniqueSet.Name ?? string.Empty,
                    selectedTechnique.TechniqueSlot,
                    selectedTechnique.TechniqueName,
                    selectedPass.PassClass,
                    selectedPass.PassIndex,
                    selectedPass.SourcePass.CustomSamplerFlags));
            IReadOnlyList<MaterialSamplerBinding> materialSamplers =
                XModelMaterialSamplerBindingBuilder.Build(
                    material,
                    selectedPass.SourcePass,
                    selectedPass.Arguments,
                    lookup,
                    imagePayloads,
                    textureCache,
                    failedTextureCacheKeys);
            ShaderVertexInputBinding[] vertexInputs =
                MaterialVertexInputBindingPlanner.Resolve(
                    techniqueSet,
                    lookup,
                    materialPass,
                    XSurfaceVertexDecoder.BackendRow);
            bool vertexPayloadReady = TryBuildRsxVertexInputs(
                surface,
                retainedSourceVertices,
                vertexInputs,
                out float[] rsxVertexInputs,
                out string vertexPayloadBlocker);
            ShaderExecutionContract execution =
                AuthoredMaterialExecutionPlanner.CreateContract(
                    material,
                    techniqueSet,
                    lookup,
                    materialPass,
                    primarySampler?.Identity,
                    selectedPass.State,
                    primarySampler?.Image.Name ?? string.Empty,
                    materialSamplers,
                    vertexPayloadReady,
                    vertexPayloadBlocker,
                    authoredSourcePassAvailable: true,
                    shaderTranslationCache: shaderTranslationCache,
                    fixedVertexSourceBackendRow:
                        XSurfaceVertexDecoder.BackendRow,
                    explicitVertexInputs: vertexInputs);
            var blockers = new List<string>();
            if (!selectedPass.StateReady)
                blockers.Add("renderState=unresolved");
            blockers.AddRange(execution.RendererBlockers);
            string diagnostic = blockers.Count == 0
                ? execution.RuntimeSamplerRequirements.Count == 0
                    ? "authoredPass=ready"
                    : "authoredPass=runtimeDeferred:" +
                      string.Join(',', execution.RuntimeSamplerRequirements
                          .Select(requirement =>
                              $"{requirement.ResourceIdentity}@{requirement.Destination}"))
                : "authoredPass=blocked:" +
                  string.Join('|', blockers.Distinct(StringComparer.Ordinal));
            if (blockers.Count > 0)
            {
                groupBlockers.Add(
                    $"pass{selectedPass.PassIndex}({diagnostic})");
            }
            packets.Add(new XModelRenderAuthoredPass(
                groupId,
                passOrdinal++,
                materialPass,
                primarySampler?.Identity,
                selectedPass.State,
                execution,
                materialSamplers,
                rsxVertexInputs,
                diagnostic));
        }

        bool authoredGroupReady =
            packets.Count == selectedTechnique.Passes.Count &&
            groupBlockers.Count == 0;
        string status = authoredGroupReady
            ? packets.Any(packet =>
                packet.ShaderExecution.RuntimeSamplerRequirements.Count > 0)
                ? $"techniqueSlot={selectedTechnique.TechniqueSlot};passes={packets.Count};runtimeInputs=deferred"
                : $"techniqueSlot={selectedTechnique.TechniqueSlot};passes={packets.Count};ready"
            : $"techniqueSlot={selectedTechnique.TechniqueSlot};passes={packets.Count};blocked:" +
              string.Join(';', groupBlockers);
        if (!authoredGroupReady)
        {
            diagnostics.Add(
                $"LOD {lodIndex} surface {geometrySurfaceIndex}: {status}.");
        }

        return ProjectSurface(
            modelName,
            lodIndex,
            geometrySurfaceIndex,
            parentMaterialIndex,
            materialName,
            surface,
            boneCount,
            selectedTechnique.TechniqueSlot,
            selectedTechnique.TechniqueName,
            packets,
            authoredGroupReady,
            status,
            diagnostics);
    }

    private static XModelRenderSurface ProjectSurface(
        string modelName,
        int lodIndex,
        int geometrySurfaceIndex,
        int parentMaterialIndex,
        string materialName,
        XSurface surface,
        int boneCount,
        int selectedTechniqueSlot,
        string selectedTechniqueName,
        IReadOnlyList<XModelRenderAuthoredPass> authoredPasses,
        bool authoredGroupReady,
        string authoredMaterialStatus,
        List<string> diagnostics)
    {
        if (surface.VertCount == 0 || surface.TriCount == 0)
        {
            throw IncompleteLod(
                modelName,
                lodIndex,
                $"surface {geometrySurfaceIndex} declares no geometry");
        }

        var gamePositions = new Vector3[surface.VertCount];
        var decodedPositions = new Vector3[surface.VertCount];
        var decodedVertices = new bool[surface.VertCount];
        for (int vertexIndex = 0;
             vertexIndex < surface.VertCount;
             vertexIndex++)
        {
            if (!XSurfaceVertexDecoder.TryReadPosition(
                    surface,
                    vertexIndex,
                    out Vector3 gamePosition))
            {
                continue;
            }
            gamePositions[vertexIndex] = gamePosition;
            decodedPositions[vertexIndex] = ToRenderCoordinates(gamePosition);
            decodedVertices[vertexIndex] = true;
        }

        IReadOnlyList<int> retainedSourceVertices =
            ResolveRetainedSourceVertices(surface);
        var positions = new List<Vector3>(retainedSourceVertices.Count);
        List<XModelRenderSkinningVertex>? skinningVertices = null;
        XSurfaceVertexDecoder? basisDecoder = null;
        IReadOnlyList<IReadOnlyList<XModelExportBoneWeight>> sourceWeights =
            [];
        if (boneCount > 0)
        {
            bool weightsReady;
            try
            {
                weightsReady = XModelSurfaceSkinningProjector.TryProject(
                    surface,
                    boneCount,
                    out sourceWeights,
                    out _);
            }
            catch (OverflowException)
            {
                weightsReady = false;
            }

            if (!weightsReady)
            {
                sourceWeights = [];
            }
            else if (XSurfaceVertexDecoder.TryCreate(
                         XSurfaceVertexDecoder.DefaultTexCoordSource,
                         out basisDecoder) &&
                     basisDecoder is not null)
            {
                skinningVertices = new List<XModelRenderSkinningVertex>(
                    retainedSourceVertices.Count);
            }
        }
        var projectedIndexBySource = new int[surface.VertCount];
        Array.Fill(projectedIndexBySource, -1);
        foreach (int sourceIndex in retainedSourceVertices)
        {
            if (!decodedVertices[sourceIndex])
                continue;
            projectedIndexBySource[sourceIndex] = positions.Count;
            positions.Add(decodedPositions[sourceIndex]);
            if (skinningVertices is not null)
            {
                if (!basisDecoder!.TryReadNormal(
                        surface,
                        sourceIndex,
                        out Vector3 bindNormal) ||
                    !basisDecoder.TryReadTangent(
                        surface,
                        sourceIndex,
                        out Vector3 bindTangent) ||
                    (uint)sourceIndex >= (uint)sourceWeights.Count)
                {
                    skinningVertices = null;
                    continue;
                }

                XModelRenderBoneInfluence[] influences =
                    sourceWeights[sourceIndex]
                        .Select(weight => new XModelRenderBoneInfluence(
                            weight.BoneIndex,
                            weight.Weight))
                        .ToArray();
                skinningVertices.Add(new XModelRenderSkinningVertex(
                    gamePositions[sourceIndex],
                    bindNormal,
                    bindTangent,
                    influences));
            }
        }

        var indices = new List<uint>(surface.TriCount * 3);
        var collisionIndices = new List<uint>();
        RenderBounds topologyBounds = RenderBounds.Empty;
        RenderBounds visibleBounds = RenderBounds.Empty;
        int skippedTriangles = 0;
        int transparentTriangles = 0;
        bool canApplyTransparentFitSemantics =
            authoredGroupReady &&
            AllPassesProveVertexAlphaTest(authoredPasses);
        XSurfaceVertexDecoder.TryCreate(
            XSurfaceVertexDecoder.DefaultTexCoordSource,
            out XSurfaceVertexDecoder? colorDecoder);
        for (int triangleIndex = 0;
             triangleIndex < surface.TriCount;
             triangleIndex++)
        {
            int indexOffset = triangleIndex * 3;
            if (indexOffset + 2 >= surface.TriIndices.Count)
            {
                skippedTriangles++;
                continue;
            }
            int i0 = surface.TriIndices[indexOffset];
            int i1 = surface.TriIndices[indexOffset + 1];
            int i2 = surface.TriIndices[indexOffset + 2];
            if ((uint)i0 >= surface.VertCount ||
                (uint)i1 >= surface.VertCount ||
                (uint)i2 >= surface.VertCount ||
                projectedIndexBySource[i0] < 0 ||
                projectedIndexBySource[i1] < 0 ||
                projectedIndexBySource[i2] < 0 ||
                i0 == i1 || i1 == i2 || i2 == i0)
            {
                skippedTriangles++;
                continue;
            }

            indices.Add(checked((uint)projectedIndexBySource[i0]));
            indices.Add(checked((uint)projectedIndexBySource[i1]));
            indices.Add(checked((uint)projectedIndexBySource[i2]));
            if (surface.VertList.Any(rigid => rigid.CollisionTree is not null && triangleIndex >= rigid.TriOffset && triangleIndex < rigid.TriOffset + rigid.TriCount))
            {
                collisionIndices.Add(checked((uint)projectedIndexBySource[i0]));
                collisionIndices.Add(checked((uint)projectedIndexBySource[i1]));
                collisionIndices.Add(checked((uint)projectedIndexBySource[i2]));
            }
            topologyBounds = topologyBounds
                .Include(decodedPositions[i0])
                .Include(decodedPositions[i1])
                .Include(decodedPositions[i2]);
            if (canApplyTransparentFitSemantics &&
                colorDecoder is not null &&
                IsFullyTransparent(colorDecoder, surface, i0, i1, i2))
            {
                transparentTriangles++;
                continue;
            }
            visibleBounds = visibleBounds
                .Include(decodedPositions[i0])
                .Include(decodedPositions[i1])
                .Include(decodedPositions[i2]);
        }

        if (indices.Count == 0 || !topologyBounds.IsValid)
        {
            throw IncompleteLod(
                modelName,
                lodIndex,
                $"surface {geometrySurfaceIndex} has no valid triangles");
        }
        if (skippedTriangles > 0)
        {
            diagnostics.Add(
                $"LOD {lodIndex} surface {geometrySurfaceIndex}: skipped {skippedTriangles} of {surface.TriCount} invalid triangles.");
        }
        if (transparentTriangles > 0)
        {
            diagnostics.Add(
                $"LOD {lodIndex} surface {geometrySurfaceIndex}: excluded {transparentTriangles} of {surface.TriCount} triangles proven alpha-test invisible by the selected authored pass from Fit bounds; authored topology was retained.");
        }

        return new XModelRenderSurface(
            geometrySurfaceIndex,
            parentMaterialIndex,
            materialName,
            positions,
            skinningVertices,
            indices,
            collisionIndices,
            visibleBounds,
            selectedTechniqueSlot,
            selectedTechniqueName,
            authoredPasses,
            authoredGroupReady,
            authoredMaterialStatus);
    }

    private static IReadOnlyList<int> ResolveRetainedSourceVertices(
        XSurface surface)
    {
        var retained = new SortedSet<int>();
        for (int triangleIndex = 0;
             triangleIndex < surface.TriCount;
             triangleIndex++)
        {
            int indexOffset = triangleIndex * 3;
            if (indexOffset + 2 >= surface.TriIndices.Count)
                continue;
            int i0 = surface.TriIndices[indexOffset];
            int i1 = surface.TriIndices[indexOffset + 1];
            int i2 = surface.TriIndices[indexOffset + 2];
            if ((uint)i0 >= surface.VertCount ||
                (uint)i1 >= surface.VertCount ||
                (uint)i2 >= surface.VertCount ||
                i0 == i1 || i1 == i2 || i2 == i0 ||
                !XSurfaceVertexDecoder.TryReadPosition(
                    surface,
                    i0,
                    out _) ||
                !XSurfaceVertexDecoder.TryReadPosition(
                    surface,
                    i1,
                    out _) ||
                !XSurfaceVertexDecoder.TryReadPosition(
                    surface,
                    i2,
                    out _))
            {
                continue;
            }
            retained.Add(i0);
            retained.Add(i1);
            retained.Add(i2);
        }
        return retained.ToArray();
    }

    private static XSurface CreateMaterialPreviewSphere()
    {
        const int longitudeSegments = 32;
        const int latitudeSegments = 16;
        int ringCount = latitudeSegments - 1;
        int ringStride = longitudeSegments + 1;
        int topStart = 0;
        int ringStart = longitudeSegments;
        int bottomStart = checked(
            ringStart + ringCount * ringStride);
        int vertexCount = checked(bottomStart + longitudeSegments);
        int triangleCount = checked(
            2 * longitudeSegments * (latitudeSegments - 1));
        byte[] verts0 = new byte[checked(
            vertexCount * XSurfaceVertexCodec.StreamStride)];
        byte[] verts1 = new byte[verts0.Length];
        ushort[] indices = new ushort[checked(triangleCount * 3)];

        for (int longitude = 0;
             longitude < longitudeSegments;
             longitude++)
        {
            float u = (longitude + 0.5f) / longitudeSegments;
            float phi = u * MathF.Tau;
            Vector3 tangent = new(-MathF.Sin(phi), MathF.Cos(phi), 0f);
            XSurfaceVertexCodec.WriteVertex(
                verts0,
                verts1,
                topStart + longitude,
                Vector3.UnitZ,
                new Vector2(u, 0f),
                Vector4.One,
                Vector3.UnitZ,
                tangent);
            XSurfaceVertexCodec.WriteVertex(
                verts0,
                verts1,
                bottomStart + longitude,
                -Vector3.UnitZ,
                new Vector2(u, 1f),
                Vector4.One,
                -Vector3.UnitZ,
                tangent);
        }

        for (int latitude = 1;
             latitude < latitudeSegments;
             latitude++)
        {
            float v = latitude / (float)latitudeSegments;
            float theta = v * MathF.PI;
            float radial = MathF.Sin(theta);
            float z = MathF.Cos(theta);
            int currentRingStart = checked(
                ringStart + (latitude - 1) * ringStride);
            for (int longitude = 0;
                 longitude <= longitudeSegments;
                 longitude++)
            {
                float u = longitude / (float)longitudeSegments;
                float phi = u * MathF.Tau;
                var normal = new Vector3(
                    radial * MathF.Cos(phi),
                    radial * MathF.Sin(phi),
                    z);
                var tangent = new Vector3(
                    -MathF.Sin(phi),
                    MathF.Cos(phi),
                    0f);
                XSurfaceVertexCodec.WriteVertex(
                    verts0,
                    verts1,
                    currentRingStart + longitude,
                    normal,
                    new Vector2(u, v),
                    Vector4.One,
                    normal,
                    tangent);
            }
        }

        int nextIndex = 0;
        for (int longitude = 0;
             longitude < longitudeSegments;
             longitude++)
        {
            AddTriangle(
                topStart + longitude,
                ringStart + longitude,
                ringStart + longitude + 1);
        }
        for (int ring = 0; ring < ringCount - 1; ring++)
        {
            int firstRing = checked(ringStart + ring * ringStride);
            int secondRing = checked(firstRing + ringStride);
            for (int longitude = 0;
                 longitude < longitudeSegments;
                 longitude++)
            {
                int first = firstRing + longitude;
                int second = secondRing + longitude;
                AddTriangle(first, second, second + 1);
                AddTriangle(first, second + 1, first + 1);
            }
        }
        int lastRing = checked(
            ringStart + (ringCount - 1) * ringStride);
        for (int longitude = 0;
             longitude < longitudeSegments;
             longitude++)
        {
            AddTriangle(
                lastRing + longitude,
                bottomStart + longitude,
                lastRing + longitude + 1);
        }

        return new XSurface
        {
            DeformedRaw = 0,
            StreamFlags = XSurfaceStreamFlags.None,
            VertCount = checked((ushort)vertexCount),
            TriCount = checked((ushort)triangleCount),
            TriIndices = Array.AsReadOnly(indices),
            VertexInfo = new XSurfaceVertexInfo(),
            Verts0 = Array.AsReadOnly(verts0),
            Verts1 = Array.AsReadOnly(verts1)
        };

        void AddTriangle(int first, int second, int third)
        {
            indices[nextIndex++] = checked((ushort)first);
            indices[nextIndex++] = checked((ushort)second);
            indices[nextIndex++] = checked((ushort)third);
        }
    }

    private static bool TryBuildRsxVertexInputs(
        XSurface surface,
        IReadOnlyList<int> retainedSourceVertices,
        IReadOnlyList<ShaderVertexInputBinding> bindings,
        out float[] values,
        out string blocker)
    {
        values = [];
        blocker = string.Empty;
        if (bindings.Count == 0)
        {
            blocker = "vertexDeclarationRoutes=missing";
            return false;
        }

        var payload = new List<float>(checked(
            retainedSourceVertices.Count *
            XSurfaceVertexDecoder.RsxVertexInputCount *
            XSurfaceVertexDecoder.RsxVertexInputComponentCount));
        Span<Vector4> vertexInputs = stackalloc Vector4[
            XSurfaceVertexDecoder.RsxVertexInputCount];
        foreach (int sourceIndex in retainedSourceVertices)
        {
            if (!XSurfaceVertexDecoder.TryReadRsxVertexInputs(
                    surface,
                    sourceIndex,
                    bindings,
                    vertexInputs,
                    out string decodeBlocker))
            {
                blocker = $"vertex{sourceIndex}:{decodeBlocker}";
                return false;
            }
            foreach (Vector4 input in vertexInputs)
            {
                payload.Add(input.X);
                payload.Add(input.Y);
                payload.Add(input.Z);
                payload.Add(input.W);
            }
        }

        values = payload.ToArray();
        return true;
    }

    private static bool AllPassesProveVertexAlphaTest(
        IReadOnlyList<XModelRenderAuthoredPass> passes) =>
        passes.Count > 0 && passes.All(PassProvesVertexAlphaTest);

    private static bool PassProvesVertexAlphaTest(
        XModelRenderAuthoredPass pass)
    {
        ShaderExecutionContract execution = pass.ShaderExecution;
        if (execution.Purpose != ShaderExecutionPurpose.CameraColor ||
            !execution.ProgramIrReady ||
            execution.VertexProgramIr is not { HasValidUpload: true } vertex ||
            execution.FragmentProgramIr is not { HasValidUpload: true } fragment ||
            AlphaTest.Resolve(pass.State) is not (
                AlphaTestMode.GreaterZero or
                AlphaTestMode.GreaterEqual128) ||
            !HasExactVertexColorBinding(execution.VertexInputs) ||
            !HasCompleteBaseColorSampler(pass, execution) ||
            !VertexProgramCopiesColorAlpha(vertex) ||
            !FragmentProgramMultipliesColorAlpha(fragment))
        {
            return false;
        }

        return true;
    }

    private static bool HasExactVertexColorBinding(
        IReadOnlyList<ShaderVertexInputBinding> bindings)
    {
        ShaderVertexInputBinding[] colorBindings = bindings
            .Where(binding => binding.Destination == MaterialStreamDestination.Color0)
            .ToArray();
        return colorBindings.Length == 1 &&
               colorBindings[0] is
               {
                   Source: MaterialStreamSource.Color,
                   ComponentCount: 4,
                   RsxType: RsxVertexElementType.Unsigned8Normalized
               } &&
               !colorBindings[0].IsDisabledDefaultAttribute;
    }

    private static bool HasCompleteBaseColorSampler(
        XModelRenderAuthoredPass pass,
        ShaderExecutionContract execution)
    {
        MaterialSamplerBinding[] samplerBindings = pass
            .MaterialSamplers
            .Where(binding => binding.Identity.SamplerDest == 0)
            .ToArray();
        if (samplerBindings.Length != 1 ||
            samplerBindings[0].Identity.SamplerArgIndex < 0 ||
            samplerBindings[0].Texture is not
            {
                Target: TextureTarget.Texture2D,
                HasCompleteDecodedPayload: true
            })
        {
            return false;
        }

        return execution.MaterialSamplerDestinations.Count(binding =>
                   binding.Destination == 0 &&
                   binding.IsOperationallyResolved) == 1 &&
               execution.CustomSamplerDestinations.All(binding =>
                   binding.Destination != 0) &&
               execution.CodeSamplerDestinations.All(binding =>
                   binding.Destination != 0);
    }

    private static bool VertexProgramCopiesColorAlpha(
        RsxVertexProgramIr program)
    {
        if (program.Instructions.IsDefaultOrEmpty ||
            program.Instructions.Any(instruction =>
                instruction.HasControlFlow))
        {
            return false;
        }

        var outputAlphaWriters = new List<(
            RsxVertexInstruction Instruction,
            bool Scalar)>();
        foreach (RsxVertexInstruction instruction in program.Instructions)
        {
            if (instruction.Result != RsxVertexResult.FrontColor0)
                continue;
            if (instruction.VecResult &&
                instruction.VectorOpcode != RsxVertexVectorOpcode.Nop &&
                (instruction.VectorWriteMask & RsxVertexWriteMask.W) != 0)
            {
                outputAlphaWriters.Add((instruction, Scalar: false));
            }
            if (instruction.ScaResult &&
                instruction.ScalarOpcode != RsxVertexScalarOpcode.Nop &&
                (instruction.ScalarWriteMask & RsxVertexWriteMask.W) != 0)
            {
                outputAlphaWriters.Add((instruction, Scalar: true));
            }
        }

        if (outputAlphaWriters.Count != 1 ||
            outputAlphaWriters[0].Scalar)
        {
            return false;
        }

        RsxVertexInstruction writer = outputAlphaWriters[0].Instruction;
        return writer.VectorOpcode == RsxVertexVectorOpcode.Move &&
               !writer.Saturate &&
               !writer.Source0Abs &&
               (!writer.CondTestEnabled ||
                writer.ConditionTest == RsxConditionTest.True) &&
               RsxVertexInstruction.SourceRegisterKind(writer.Source0) ==
                    RsxVertexRegisterType.Input &&
               writer.InputAttribute == RsxVertexInputAttribute.Color0 &&
               (writer.Source0 & 0x10000u) == 0 &&
               ((writer.Source0 >> 8) & 3) == 3 &&
               !writer.IndexConst;
    }

    private static bool FragmentProgramMultipliesColorAlpha(
        RsxFragmentProgramIr program)
    {
        if (!program.ProgramControl.IsValid ||
            program.Instructions.Length != 2)
        {
            return false;
        }

        RsxFragmentInstruction sample = program.Instructions[0];
        RsxFragmentInstruction multiply = program.Instructions[1];
        bool expectedFp16 = !program.ProgramControl.UsesFp32ColorExports;
        RsxFragmentColorExport[] targetZeroExports = program.ColorExports
            .Where(export => export.ColorTarget == 0)
            .ToArray();
        if (targetZeroExports.Length != 1 ||
            targetZeroExports[0].Fp16 != expectedFp16 ||
            targetZeroExports[0].RegisterIndex != 0 ||
            (targetZeroExports[0].WrittenComponentMask &
                RsxFragmentWriteMask.W) == 0 ||
            !IsUnconditionalFullWrite(sample, end: false) ||
            sample.OpcodeType != RsxFragmentOpcode.Texture ||
            sample.TextureUnit != 0 ||
            sample.DestFp16 != expectedFp16 ||
            sample.DestRegister != 0 ||
            sample.SourceAttribute !=
                RsxFragmentInputAttribute.TextureCoordinate0 ||
            !IsIdentityFragmentInput(sample.Source0Operand) ||
            !IsUnconditionalFullWrite(multiply, end: true) ||
            multiply.OpcodeType != RsxFragmentOpcode.Multiply ||
            multiply.DestFp16 != expectedFp16 ||
            multiply.DestRegister != 0 ||
            multiply.SourceAttribute != RsxFragmentInputAttribute.Color0)
        {
            return false;
        }

        return (IsColorZeroInput(multiply.Source0Operand) &&
                IsRegister(
                    multiply.Source1Operand,
                    expectedFp16,
                    registerIndex: 0)) ||
               (IsRegister(
                    multiply.Source0Operand,
                    expectedFp16,
                    registerIndex: 0) &&
                IsColorZeroInput(multiply.Source1Operand));
    }

    private static bool IsUnconditionalFullWrite(
        RsxFragmentInstruction instruction,
        bool end) =>
        !instruction.IsControlFlow &&
        instruction.End == end &&
        !instruction.NoDest &&
        instruction.WriteMask == RsxFragmentWriteMask.All &&
        !instruction.Saturate &&
        instruction.Scale == RsxFragmentResultScale.None &&
        !instruction.CondWriteEnabled &&
        instruction.ConditionTest == RsxConditionTest.True &&
        !instruction.ConditionWriteRegister1 &&
        !instruction.ConditionReadRegister1;

    private static bool IsIdentityFragmentInput(
        RsxFragmentOperand operand) =>
        operand.RegisterKind == RsxFragmentRegisterType.Input &&
        IsIdentityFragmentOperand(operand);

    private static bool IsColorZeroInput(
        RsxFragmentOperand operand) =>
        operand.RegisterKind == RsxFragmentRegisterType.Input &&
        IsIdentityFragmentOperand(operand);

    private static bool IsRegister(
        RsxFragmentOperand operand,
        bool fp16,
        int registerIndex) =>
        operand.RegisterKind == RsxFragmentRegisterType.Temporary &&
        operand.Fp16 == fp16 &&
        operand.RegisterIndex == registerIndex &&
        IsIdentityFragmentOperand(operand);

    private static bool IsIdentityFragmentOperand(
        RsxFragmentOperand operand) =>
        !operand.Negate &&
        !operand.Absolute &&
        operand.SwizzleX == RsxSwizzleComponent.X &&
        operand.SwizzleY == RsxSwizzleComponent.Y &&
        operand.SwizzleZ == RsxSwizzleComponent.Z &&
        operand.SwizzleW == RsxSwizzleComponent.W;

    private static bool IsFullyTransparent(
        XSurfaceVertexDecoder decoder,
        XSurface surface,
        int i0,
        int i1,
        int i2) =>
        decoder.TryReadColor(surface, i0, out Vector4 c0) && c0.W <= 0f &&
        decoder.TryReadColor(surface, i1, out Vector4 c1) && c1.W <= 0f &&
        decoder.TryReadColor(surface, i2, out Vector4 c2) && c2.W <= 0f;

    private static IReadOnlyList<XModelRenderBone> ProjectBones(
        XModelAsset model,
        List<string> diagnostics)
    {
        int declaredBoneCount = model.NumBones;
        if (declaredBoneCount == 0)
            return [];

        int availableBoneCount = Math.Min(
            declaredBoneCount,
            Math.Min(model.BoneNames.Count, model.BaseMat.Count));
        var bones = new List<XModelRenderBone>(availableBoneCount);
        for (int boneIndex = 0;
             boneIndex < availableBoneCount;
             boneIndex++)
        {
            string? name = model.BoneNames[boneIndex]?.Text;
            DObjAnimMat? basePose = model.BaseMat[boneIndex];
            if (string.IsNullOrWhiteSpace(name) || basePose is null)
                continue;

            var gamePosition = new Vector3(
                basePose.Trans.X,
                basePose.Trans.Y,
                basePose.Trans.Z);
            if (!IsFinite(gamePosition))
                continue;

            bones.Add(new XModelRenderBone(
                boneIndex,
                name,
                ToRenderCoordinates(gamePosition)));
        }

        if (bones.Count != declaredBoneCount)
        {
            diagnostics.Add(
                $"Projected {bones.Count} of {declaredBoneCount} named bone markers; incomplete names or base-pose translations were skipped.");
        }
        return bones;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static Vector3 ToRenderCoordinates(Vector3 value) =>
        RenderCoordinateConverter.GameToRenderPosition(value);

    private static RenderBounds IncludeBounds(
        RenderBounds bounds,
        RenderBounds other) =>
        other.IsValid
            ? bounds.Include(other.Min).Include(other.Max)
            : bounds;

    private static InvalidOperationException IncompleteLod(
        string modelName,
        int lodIndex,
        string detail) =>
        new(
            $"XModel '{modelName}' loaded LOD {lodIndex} is incomplete: {detail}.");
}
