using IW4.Render.Techniques;
using System.Buffers;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Runtime.Assets.Images;

using IW4.Render.Assets;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.SceneBuilding.Batching;
using IW4.Render.Shaders;
using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder
{
    private sealed record PreparedColorLayer(
        MaterialColorLayer Layer,
        WorldVertexDecoder Decoder);

    private sealed record PreparedStaticColorLayer(
        MaterialColorLayer Layer,
        XSurfaceVertexDecoder Decoder);

    /// <summary>
    /// Exact immutable inputs for material-sampler preparation that vary per
    /// world surface. The material/pass argument scan is already represented
    /// by <see cref="WorldMaterialSamplerPlan"/>; runtime custom resources vary
    /// only by the two native surface indices. Keeping color-layer structure in
    /// the key makes it safe to reuse the completed binding list across
    /// surfaces without conflating different decoded textures or UV routes.
    /// </summary>
    private readonly record struct WorldMaterialSamplerPreparationKey(
        WorldMaterialSamplerPlan SamplerPlan,
        MaterialCustomSamplerFlags CustomSamplerFlags,
        WorldVertexLayoutSelection VertexLayout,
        byte LightmapIndex,
        byte ReflectionProbeIndex,
        MaterialColorLayersIdentity ColorLayers);

    private sealed class MaterialColorLayersIdentity :
        IEquatable<MaterialColorLayersIdentity>
    {
        private readonly IReadOnlyList<MaterialColorLayer> _layers;
        private readonly int _hashCode;

        internal MaterialColorLayersIdentity(
            IReadOnlyList<MaterialColorLayer> layers)
        {
            ArgumentNullException.ThrowIfNull(layers);
            _layers = layers;
            var hash = new HashCode();
            foreach (MaterialColorLayer layer in _layers)
                hash.Add(Entry.Create(layer));
            _hashCode = hash.ToHashCode();
        }

        public bool Equals(MaterialColorLayersIdentity? other)
        {
            if (other is null || _layers.Count != other._layers.Count)
                return false;

            for (int index = 0; index < _layers.Count; index++)
            {
                if (Entry.Create(_layers[index]) !=
                    Entry.Create(other._layers[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) =>
            obj is MaterialColorLayersIdentity other && Equals(other);

        public override int GetHashCode() => _hashCode;

        private readonly record struct Entry(
            MaterialSamplerIdentity Identity,
            TextureBindingKey Texture,
            UvRouteBatchKey UvRoute)
        {
            internal static Entry Create(MaterialColorLayer layer) => new(
                layer.Identity,
                TextureBindingKey.Create(layer.Texture),
                UvRouteBatchKey.Create(layer.UvRoute));
        }
    }

    private static void AppendTexturedSurface(
        Dictionary<WorldTexturedBatchKey, TexturedBatchBuilder> batches,
        MaterialPassIdentity pass,
        MaterialSamplerIdentity primarySampler,
        Texture texture,
        Texture? lightmapTexture,
        IReadOnlyList<MaterialColorLayer> colorLayers,
        IReadOnlyList<MapRenderWorldMaterialSamplerBinding> materialSamplers,
        ShaderExecutionContract shaderExecution,
        UvRoute uvRoute,
        RenderState state,
        MapRenderEditorDepthPrepassPlan? editorDepthPrepass,
        ShaderExecutionContract? depthPrepassShaderExecution,
        int unresolvedCodeSamplerCount,
        List<float> vertices,
        List<float> rsxVertexInputs,
        List<uint> indices,
        MapRenderPickRange pickRange,
        int? editorDrawGroupSurfaceIndex,
        byte sceneLightIndex)
    {
        var key = new WorldTexturedBatchKey(
            pass,
            primarySampler,
            texture,
            lightmapTexture,
            colorLayers,
            materialSamplers,
            shaderExecution,
            uvRoute,
            state,
            editorDepthPrepass,
            depthPrepassShaderExecution,
            unresolvedCodeSamplerCount,
            editorDrawGroupSurfaceIndex,
            sceneLightIndex);
        if (!batches.TryGetValue(key, out TexturedBatchBuilder? batch))
        {
            batch = new TexturedBatchBuilder(
                pass,
                primarySampler,
                texture,
                lightmapTexture,
                colorLayers,
                materialSamplers,
                shaderExecution,
                uvRoute,
                state,
                editorDepthPrepass,
                depthPrepassShaderExecution,
                unresolvedCodeSamplerCount,
                sceneLightIndex);
            batches.Add(key, batch);
        }

        int firstPickIndex = batch.Indices.Count;
        uint baseIndex = checked((uint)(batch.Vertices.Count / MapRenderScene.TexturedVertexFloatCount));
        batch.Vertices.AddRange(vertices);
        batch.RsxVertexInputs.AddRange(rsxVertexInputs);
        foreach (uint index in indices)
            batch.Indices.Add(baseIndex + index);
        AddPickRange(
            batch.PickRanges,
            pickRange.Kind,
            pickRange.ObjectIndex,
            pickRange.SurfaceIndex,
            firstPickIndex,
            batch.Indices.Count,
            pickRange.Name);
    }

    private static IReadOnlyList<MaterialColorLayer> CreateSingleColorLayer(
        MaterialSamplerIdentity primarySampler,
        Texture texture,
        UvRoute uvRoute)
    {
        return
        [
            new MaterialColorLayer(
                0,
                primarySampler,
                texture,
                uvRoute,
                -1)
        ];
    }

    private static IReadOnlyList<MapRenderWorldMaterialSamplerBinding> CreateMaterialSamplerBindings(
        IReadOnlyList<MaterialColorLayer> colorLayers)
    {
        return colorLayers.Select(layer => CreateWorldBinding(
            layer.Identity,
            layer.Texture.Name,
            layer.Texture,
            layer.UvRoute)).ToArray();
    }

    private static IReadOnlyList<PreparedColorLayer> PrepareWorldColorLayers(
        bool enableEditorMultiTexture,
        MaterialAsset? material,
        WorldMaterialSamplerPlan samplerPlan,
        EditorMaterialTexturePlan? texturePlan,
        SelectedColorPass selectedPass,
        WorldVertexLayoutSelection worldVertexLayout,
        WorldVertexDecoderResolver vertexDecoderResolver,
        Texture primaryTexture,
        UvRoute primaryUvRoute,
        WorldVertexDecoder primaryDecoder,
        IGfxImagePayloadResolver imageStreams,
        RenderTextureCache textureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        var prepared = new List<PreparedColorLayer>(MapRenderScene.MaxColorLayerCount)
        {
            new(
                new MaterialColorLayer(
                    0,
                    selectedPass.PrimarySampler,
                    primaryTexture,
                    primaryUvRoute,
                    -1),
                primaryDecoder)
        };

        if (!enableEditorMultiTexture ||
            material is null ||
            texturePlan is null ||
            samplerPlan.SourcePass is null)
        {
            return prepared;
        }

        MaterialPassAsset sourcePass = samplerPlan.SourcePass;
        MaterialVertexDeclarationAsset? vertexDecl =
            samplerPlan.VertexDeclaration;
        int newlyDecodedTextureCount = 0;
        int newlySkippedTextureCount = 0;

        AddColorRole(EditorMaterialTextureRole.ColorLayer1, 1, 0);
        AddColorRole(EditorMaterialTextureRole.ColorLayer2, 2, 1);
        AddColorRole(EditorMaterialTextureRole.ColorLayer3, 3, 2);
        AddColorRole(EditorMaterialTextureRole.ColorLayer4, 4, 3);
        decodedTextureCount += newlyDecodedTextureCount;
        skippedTextureCount += newlySkippedTextureCount;
        return prepared;

        void AddColorRole(
            EditorMaterialTextureRole role,
            int layerIndex,
            int blendWeightComponent)
        {
            if (prepared.Count >= MapRenderScene.MaxColorLayerCount ||
                !texturePlan.TryGetUniqueBinding(
                    role,
                    out EditorMaterialTextureBinding? planned) ||
                planned is null ||
                planned.NameHash == selectedPass.PrimarySampler.SamplerHash ||
                planned.Image is not { } image)
            {
                return;
            }

            MaterialTextureDef texture =
                material.Textures[planned.TextureTableOrdinal];
            if (!samplerPlan.TryGetUniqueArgument(
                    planned.NameHash,
                    out WorldMaterialSamplerArgumentMatch argumentMatch))
            {
                return;
            }

            MaterialShaderArgumentAsset arg = argumentMatch.Argument;
            int argIndex = argumentMatch.ArgumentIndex;
            if (!RsxShaderInputRouter.TrySelectSamplerSource(
                    sourcePass,
                    arg,
                    vertexDecl,
                    texture.Semantic,
                    out MaterialStreamSource texCoordSource))
            {
                return;
            }

            WorldVertexDecoderSelection decoderSelection =
                vertexDecoderResolver(
                worldVertexLayout,
                texCoordSource,
                true);
            WorldVertexDecoder? decoder = decoderSelection.Decoder;
            UvRoute uvRoute = decoderSelection.UvRoute;
            if (decoder is null || !decoder.HasTexCoord ||
                !TryDecodeTexture(
                    image,
                    texture.SamplerState,
                    imageStreams,
                    textureCache,
                    failedTextureCacheKeys,
                    true,
                    ref newlyDecodedTextureCount,
                    ref newlySkippedTextureCount,
                    out Texture? decodedTexture) ||
                decodedTexture is null)
            {
                return;
            }

            prepared.Add(new PreparedColorLayer(
                new MaterialColorLayer(
                    layerIndex,
                    new MaterialSamplerIdentity(
                        argIndex,
                        arg.Dest,
                        planned.NameHash,
                        planned.TextureSemantic),
                    decodedTexture,
                    uvRoute,
                    blendWeightComponent),
                decoder));
        }
    }

    private static IReadOnlyList<MapRenderWorldMaterialSamplerBinding> PrepareStaticMaterialSamplerBindings(
        MaterialAsset material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        GfxWorldAsset gfxMap,
        IMapRenderWorldTextureBindingResolver worldTextureBindings,
        SelectedColorPass selectedPass,
        byte? reflectionProbeIndex,
        UvRoute uvRoute,
        IReadOnlyList<MaterialColorLayer> colorLayers,
        IGfxImagePayloadResolver imageStreams,
        RenderTextureCache textureCache,
        MapRenderWorldTextureCache worldTextureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        HashSet<MapRenderWorldTextureCacheKey> failedWorldTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        var bindings = new List<MapRenderWorldMaterialSamplerBinding>();
        var seen = new HashSet<(ushort Dest, uint Hash)>();
        if (techset is not null &&
            selectedPass.Pass.TechniquePass.TechniqueSlot >= 0 &&
            selectedPass.Pass.TechniquePass.PassIndex >= 0)
        {
            MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
                .FirstOrDefault(candidate =>
                    candidate.Index ==
                        selectedPass.Pass.TechniquePass.TechniqueSlot);
            if (slot?.Technique is { } technique &&
                (uint)selectedPass.Pass.TechniquePass.PassIndex <
                    (uint)technique.Passes.Count)
            {
                MaterialPassAsset sourcePass =
                    technique.Passes[
                        selectedPass.Pass.TechniquePass.PassIndex];
                IReadOnlyList<MaterialShaderArgumentAsset> args =
                    lookup.ResolveShaderArgs(sourcePass);
                for (int argIndex = 0; argIndex < args.Count; argIndex++)
                {
                    MaterialShaderArgumentAsset arg = args[argIndex];
                    if (arg.Type !=
                        MaterialShaderArgumentType.MaterialPixelSampler)
                    {
                        continue;
                    }

                    uint samplerHash = arg.MaterialNameHash;
                    if (!seen.Add((arg.Dest, samplerHash)))
                        continue;

                    if (!MaterialTextureResolver.TryResolve(
                            material,
                            lookup,
                            samplerHash,
                            requireColor: false,
                            out MaterialTextureDef? materialTexture,
                            out GfxImageAsset? image) ||
                        materialTexture is null ||
                        image is null)
                    {
                        bindings.Add(CreateWorldBinding(
                            new MaterialSamplerIdentity(
                                argIndex,
                                arg.Dest,
                                samplerHash,
                                materialTexture?.Semantic ?? 0),
                            image?.Name ?? string.Empty,
                            null,
                            uvRoute,
                            EditorTextureRole: materialTexture is null
                                ? EditorMaterialTextureRole.Unknown
                                : EditorMaterialTextureRoleClassifier
                                    .Classify(materialTexture).Role,
                            TextureTableOrdinal: MaterialTextureResolver.FindOrdinal(
                                material,
                                materialTexture)));
                        continue;
                    }

                    bool decoded = TryDecodeTexture(
                        image,
                        materialTexture.SamplerState,
                        imageStreams,
                        textureCache,
                        failedTextureCacheKeys,
                        true,
                        ref decodedTextureCount,
                        ref skippedTextureCount,
                        out Texture? decodedTexture) &&
                        decodedTexture is not null;
                    bindings.Add(CreateWorldBinding(
                        new MaterialSamplerIdentity(
                            argIndex,
                            arg.Dest,
                            samplerHash,
                            materialTexture.Semantic),
                        decodedTexture?.Name ?? image.Name ?? string.Empty,
                        decodedTexture,
                        uvRoute,
                        EditorTextureRole:
                            EditorMaterialTextureRoleClassifier
                                .Classify(materialTexture).Role,
                        TextureTableOrdinal: MaterialTextureResolver.FindOrdinal(
                            material,
                            materialTexture)));
                }
            }
        }

        foreach (MaterialColorLayer layer in colorLayers)
        {
            if (!seen.Add((
                    layer.Identity.SamplerDest,
                    layer.Identity.SamplerHash)))
                continue;

            bindings.Add(CreateWorldBinding(
                layer.Identity,
                layer.Texture.Name,
                layer.Texture,
                layer.UvRoute));
        }

        AppendStaticCustomSamplerBindings(
            bindings,
            selectedPass.Pass.TechniquePass.CustomSamplerFlags,
            gfxMap,
            worldTextureBindings,
            reflectionProbeIndex,
            textureCache,
            worldTextureCache,
            failedTextureCacheKeys,
            failedWorldTextureCacheKeys,
            ref decodedTextureCount,
            ref skippedTextureCount);

        return bindings;
    }

    private static void AppendStaticCustomSamplerBindings(
        List<MapRenderWorldMaterialSamplerBinding> bindings,
        MaterialCustomSamplerFlags customSamplerFlags,
        GfxWorldAsset gfxMap,
        IMapRenderWorldTextureBindingResolver worldTextureBindings,
        byte? reflectionProbeIndex,
        RenderTextureCache textureCache,
        MapRenderWorldTextureCache worldTextureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        HashSet<MapRenderWorldTextureCacheKey> failedWorldTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        MapRenderWorldRuntimeTextureIdentity? reflectionIdentity =
            ResolveStaticReflectionProbeIdentity(
                customSamplerFlags,
                reflectionProbeIndex);
        if (reflectionIdentity is not { } identity)
            return;

        bindings.Add(CreateWorldReflectionProbeSamplerBinding(
            worldTextureBindings.ResolveWorldRuntimeTexture(
                gfxMap,
                identity),
            textureCache,
            worldTextureCache,
            failedTextureCacheKeys,
            failedWorldTextureCacheKeys,
            ref decodedTextureCount,
            ref skippedTextureCount));
    }

    internal static MapRenderWorldRuntimeTextureIdentity?
        ResolveStaticReflectionProbeIdentity(
            MaterialCustomSamplerFlags customSamplerFlags,
            byte? reflectionProbeIndex)
    {
        var selection = new MaterialCustomSamplerSelection(
            customSamplerFlags);
        if (!selection.BindsReflectionProbe)
            return null;
        if (reflectionProbeIndex is not { } ordinal)
        {
            throw new InvalidOperationException(
                "A static pass requesting the native reflection sampler lost its authored probe identity during batching.");
        }

        return new MapRenderWorldRuntimeTextureIdentity(
            MapRenderWorldRuntimeTextureKind.ReflectionProbe,
            ordinal);
    }

    private static IReadOnlyList<PreparedStaticColorLayer> PrepareStaticColorLayers(
        bool enableEditorMultiTexture,
        MaterialAsset material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        EditorMaterialTexturePlan? texturePlan,
        SelectedColorPass selectedPass,
        Texture primaryTexture,
        UvRoute primaryUvRoute,
        XSurfaceVertexDecoder primaryDecoder,
        IGfxImagePayloadResolver imageStreams,
        RenderTextureCache textureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        var prepared = new List<PreparedStaticColorLayer>(
            MapRenderScene.MaxColorLayerCount)
        {
            new(
                CreateSingleColorLayer(
                    selectedPass.PrimarySampler,
                    primaryTexture,
                    primaryUvRoute)[0],
                primaryDecoder)
        };

        if (!enableEditorMultiTexture ||
            techset is null ||
            texturePlan is null ||
            selectedPass.Pass.TechniquePass.TechniqueSlot < 0 ||
            selectedPass.Pass.TechniquePass.PassIndex < 0)
        {
            return prepared;
        }

        MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
            .FirstOrDefault(candidate =>
                candidate.Index ==
                    selectedPass.Pass.TechniquePass.TechniqueSlot);
        if (slot?.Technique is not { } technique ||
            (uint)selectedPass.Pass.TechniquePass.PassIndex >=
                (uint)technique.Passes.Count)
        {
            return prepared;
        }

        MaterialPassAsset sourcePass =
            technique.Passes[selectedPass.Pass.TechniquePass.PassIndex];
        sourcePass.VertexShader ??=
            lookup.ResolveVertexShader(sourcePass.VertexShaderPointer);
        sourcePass.PixelShader ??=
            lookup.ResolvePixelShader(sourcePass.PixelShaderPointer);
        MaterialVertexDeclarationAsset? vertexDecl =
            sourcePass.VertexDeclaration ??
            lookup.ResolveVertexDeclaration(sourcePass.VertexDeclPointer);
        IReadOnlyList<MaterialShaderArgumentAsset> args =
            lookup.ResolveShaderArgs(sourcePass);
        int newlyDecodedTextureCount = 0;
        int newlySkippedTextureCount = 0;

        AddColorRole(EditorMaterialTextureRole.ColorLayer1, 1);
        AddColorRole(EditorMaterialTextureRole.ColorLayer2, 2);
        AddColorRole(EditorMaterialTextureRole.ColorLayer3, 3);
        AddColorRole(EditorMaterialTextureRole.ColorLayer4, 4);
        decodedTextureCount += newlyDecodedTextureCount;
        skippedTextureCount += newlySkippedTextureCount;
        return prepared;

        void AddColorRole(
            EditorMaterialTextureRole role,
            int layerIndex)
        {
            if (prepared.Count >= MapRenderScene.MaxColorLayerCount ||
                !texturePlan.TryGetUniqueBinding(
                    role,
                    out EditorMaterialTextureBinding? planned) ||
                planned is null ||
                planned.NameHash == selectedPass.PrimarySampler.SamplerHash ||
                (uint)planned.TextureTableOrdinal >=
                    (uint)material.Textures.Count ||
                planned.Image is not { } image)
            {
                return;
            }

            MaterialTextureDef materialTexture =
                material.Textures[planned.TextureTableOrdinal];
            (MaterialShaderArgumentAsset Argument, int Index)[] matchingArgs =
                args.Select((argument, index) => (argument, index))
                    .Where(candidate =>
                        candidate.argument.Type ==
                            MaterialShaderArgumentType.MaterialPixelSampler &&
                        candidate.argument.MaterialNameHash ==
                            planned.NameHash)
                    .Select(candidate =>
                        (candidate.argument, candidate.index))
                    .ToArray();
            if (matchingArgs.Length != 1)
                return;

            (MaterialShaderArgumentAsset arg, int argIndex) = matchingArgs[0];
            XSurfaceVertexDecoder decoder = primaryDecoder;
            UvRoute uvRoute = primaryUvRoute with
            {
                Label = $"static color layer {layerIndex} reuses base UV"
            };
            if (RsxShaderInputRouter.TrySelectSamplerSource(
                    sourcePass,
                    arg,
                    vertexDecl,
                    planned.TextureSemantic,
                    out MaterialStreamSource texCoordSource) &&
                SelectStaticVertexDecoder(texCoordSource) is { } routedDecoder)
            {
                decoder = routedDecoder;
                uvRoute = BuildStaticModelUvRoute(texCoordSource);
            }

            if (!TryDecodeTexture(
                    image,
                    materialTexture.SamplerState,
                    imageStreams,
                    textureCache,
                    failedTextureCacheKeys,
                    true,
                    ref newlyDecodedTextureCount,
                    ref newlySkippedTextureCount,
                    out Texture? decodedTexture) ||
                decodedTexture is null)
            {
                return;
            }

            prepared.Add(new PreparedStaticColorLayer(
                new MaterialColorLayer(
                    layerIndex,
                    new MaterialSamplerIdentity(
                        argIndex,
                        arg.Dest,
                        planned.NameHash,
                        planned.TextureSemantic),
                    decodedTexture,
                    uvRoute,
                    BlendWeightComponent: -1),
                decoder));
        }
    }

    private static WorldMaterialSamplerPlan BuildWorldMaterialSamplerPlan(
        MaterialAsset material,
        MaterialTechniqueSetAsset techset,
        RenderAssetLookup lookup,
        SelectedColorPass selectedPass)
    {
        if (selectedPass.Pass.TechniquePass.TechniqueSlot < 0 ||
            selectedPass.Pass.TechniquePass.PassIndex < 0)
        {
            return WorldMaterialSamplerPlan.Empty;
        }

        MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
            .FirstOrDefault(candidate =>
                candidate.Index ==
                    selectedPass.Pass.TechniquePass.TechniqueSlot);
        if (slot?.Technique is not { } technique ||
            (uint)selectedPass.Pass.TechniquePass.PassIndex >=
                (uint)technique.Passes.Count)
        {
            return WorldMaterialSamplerPlan.Empty;
        }

        MaterialPassAsset sourcePass =
            technique.Passes[
                selectedPass.Pass.TechniquePass.PassIndex];
        sourcePass.VertexShader ??=
            lookup.ResolveVertexShader(sourcePass.VertexShaderPointer);
        sourcePass.PixelShader ??=
            lookup.ResolvePixelShader(sourcePass.PixelShaderPointer);
        MaterialVertexDeclarationAsset? vertexDeclaration =
            sourcePass.VertexDeclaration ??
            lookup.ResolveVertexDeclaration(sourcePass.VertexDeclPointer);
        IReadOnlyList<MaterialShaderArgumentAsset> args =
            lookup.ResolveShaderArgs(sourcePass);
        var entries = new List<WorldMaterialSamplerPlanEntry>();
        var seen = new HashSet<(ushort Dest, uint Hash)>();
        for (int argumentIndex = 0;
             argumentIndex < args.Count;
             argumentIndex++)
        {
            MaterialShaderArgumentAsset argument = args[argumentIndex];
            if (argument.Type !=
                MaterialShaderArgumentType.MaterialPixelSampler)
            {
                continue;
            }

            uint samplerHash = argument.MaterialNameHash;
            if (!seen.Add((argument.Dest, samplerHash)))
                continue;

            bool resolved = MaterialTextureResolver.TryResolve(
                material,
                lookup,
                samplerHash,
                requireColor: false,
                out MaterialTextureDef? materialTexture,
                out GfxImageAsset? image);
            EditorMaterialTextureRole editorTextureRole =
                resolved && materialTexture is not null
                    ? EditorMaterialTextureRoleClassifier
                        .Classify(materialTexture).Role
                    : EditorMaterialTextureRole.Unknown;
            entries.Add(new WorldMaterialSamplerPlanEntry(
                argumentIndex,
                argument,
                samplerHash,
                materialTexture,
                image,
                editorTextureRole,
                resolved
                    ? MaterialTextureResolver.FindOrdinal(material, materialTexture)
                    : -1));
        }

        return new WorldMaterialSamplerPlan(
            sourcePass,
            vertexDeclaration,
            entries.ToArray(),
            args);
    }

    private static IReadOnlyList<MapRenderWorldMaterialSamplerBinding> PrepareWorldMaterialSamplerBindings(
        WorldMaterialSamplerPlan samplerPlan,
        IMapRenderWorldTextureBindingResolver worldTextureBindings,
        SelectedColorPass selectedPass,
        WorldVertexLayoutSelection worldVertexLayout,
        WorldVertexDecoderResolver vertexDecoderResolver,
        GfxWorldAsset gfxMap,
        GfxSurface surface,
        IReadOnlyList<MaterialColorLayer> colorLayers,
        IGfxImagePayloadResolver imageStreams,
        RenderTextureCache textureCache,
        MapRenderWorldTextureCache worldTextureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        HashSet<MapRenderWorldTextureCacheKey> failedWorldTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        var bindings = new List<MapRenderWorldMaterialSamplerBinding>();
        var seen = new HashSet<(ushort Dest, uint Hash)>();

        foreach (WorldMaterialSamplerPlanEntry planned in samplerPlan.Entries)
        {
            MaterialShaderArgumentAsset arg = planned.Argument;
            uint samplerHash = planned.SamplerHash;
            seen.Add((arg.Dest, samplerHash));
            MaterialTextureDef? materialTexture = planned.MaterialTexture;
            GfxImageAsset? image = planned.Image;
            if (materialTexture is null || image is null)
            {
                bindings.Add(CreateWorldBinding(
                    new MaterialSamplerIdentity(
                        planned.ArgumentIndex,
                        arg.Dest,
                        samplerHash,
                        materialTexture?.Semantic ?? 0),
                    image?.Name ?? string.Empty,
                    null,
                    null,
                    EditorTextureRole: planned.EditorTextureRole,
                    TextureTableOrdinal: planned.TextureTableOrdinal));
                continue;
            }

            MaterialColorLayer? decodedColorLayer =
                colorLayers.FirstOrDefault(layer =>
                    layer.Identity.SamplerDest == arg.Dest &&
                    layer.Identity.SamplerHash == samplerHash);
            Texture? decodedTexture = decodedColorLayer?.Texture;
            bool textureDecoded = decodedTexture is not null;
            if (!textureDecoded)
            {
                textureDecoded = TryDecodeTexture(
                    image,
                    materialTexture.SamplerState,
                    imageStreams,
                    textureCache,
                    failedTextureCacheKeys,
                    true,
                    ref decodedTextureCount,
                    ref skippedTextureCount,
                    out decodedTexture) && decodedTexture is not null;
            }

            UvRoute? samplerUvRoute = null;
            MaterialStreamSource texCoordSource = default;
            bool routeResolved = samplerPlan.SourcePass is { } sourcePass &&
                samplerPlan.VertexDeclaration is { } vertexDeclaration &&
                RsxShaderInputRouter.TrySelectSamplerSource(
                    sourcePass,
                    arg,
                    vertexDeclaration,
                    materialTexture.Semantic,
                    out texCoordSource);
            if (routeResolved)
            {
                WorldVertexDecoderSelection decoderSelection =
                    vertexDecoderResolver(
                    worldVertexLayout,
                    texCoordSource,
                    true);
                samplerUvRoute = decoderSelection.UvRoute;
            }

            bindings.Add(CreateWorldBinding(
                new MaterialSamplerIdentity(
                    planned.ArgumentIndex,
                    arg.Dest,
                    samplerHash,
                    materialTexture.Semantic),
                decodedTexture?.Name ?? image.Name ?? string.Empty,
                decodedTexture,
                samplerUvRoute,
                EditorTextureRole: planned.EditorTextureRole,
                TextureTableOrdinal: planned.TextureTableOrdinal));
        }

        foreach (MaterialColorLayer layer in colorLayers)
        {
            if (!seen.Add((
                    layer.Identity.SamplerDest,
                    layer.Identity.SamplerHash)))
                continue;

            bindings.Add(CreateWorldBinding(
                layer.Identity,
                layer.Texture.Name,
                layer.Texture,
                layer.UvRoute));
        }

        AppendWorldCustomSamplerBindings(
            bindings,
            selectedPass.Pass.TechniquePass.CustomSamplerFlags,
            gfxMap,
            worldTextureBindings,
            surface,
            imageStreams,
            textureCache,
            worldTextureCache,
            failedTextureCacheKeys,
            failedWorldTextureCacheKeys,
            ref decodedTextureCount,
            ref skippedTextureCount);

        return bindings;
    }

    private static void AppendWorldCustomSamplerBindings(
        List<MapRenderWorldMaterialSamplerBinding> bindings,
        MaterialCustomSamplerFlags customSamplerFlags,
        GfxWorldAsset gfxMap,
        IMapRenderWorldTextureBindingResolver worldTextureBindings,
        GfxSurface surface,
        IGfxImagePayloadResolver imageStreams,
        RenderTextureCache textureCache,
        MapRenderWorldTextureCache worldTextureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        HashSet<MapRenderWorldTextureCacheKey> failedWorldTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        var selection = new MaterialCustomSamplerSelection(customSamplerFlags);

        foreach (MaterialCustomSamplerFlags sampler in
                 selection.EnumerateBindingsInNativeOrder())
        {
            switch (sampler)
            {
                case MaterialCustomSamplerFlags.ReflectionProbe:
                    {
                        var identity = new MapRenderWorldRuntimeTextureIdentity(
                            MapRenderWorldRuntimeTextureKind.ReflectionProbe,
                            surface.ReflectionProbeIndex);
                        bindings.Add(CreateWorldReflectionProbeSamplerBinding(
                            worldTextureBindings.ResolveWorldRuntimeTexture(
                                gfxMap,
                                identity),
                            textureCache,
                            worldTextureCache,
                            failedTextureCacheKeys,
                            failedWorldTextureCacheKeys,
                            ref decodedTextureCount,
                            ref skippedTextureCount));
                        break;
                    }
                case MaterialCustomSamplerFlags.SecondaryLightmap:
                    {
                        var identity = new MapRenderWorldRuntimeTextureIdentity(
                            MapRenderWorldRuntimeTextureKind.SecondaryLightmap,
                            surface.LightmapIndex);
                        bindings.Add(CreateWorldCustomImageSamplerBinding(
                            worldTextureBindings.ResolveWorldRuntimeTexture(
                                gfxMap,
                                identity),
                            RsxImplicitSamplerStateEncoding.Lightmap,
                            imageStreams,
                            textureCache,
                            worldTextureCache,
                            failedTextureCacheKeys,
                            failedWorldTextureCacheKeys,
                            ref decodedTextureCount,
                            ref skippedTextureCount));
                        break;
                    }
                case MaterialCustomSamplerFlags.PrimaryLightmap:
                    {
                        var identity = new MapRenderWorldRuntimeTextureIdentity(
                            MapRenderWorldRuntimeTextureKind.PrimaryLightmap,
                            surface.LightmapIndex);
                        bindings.Add(CreateWorldCustomImageSamplerBinding(
                            worldTextureBindings.ResolveWorldRuntimeTexture(
                                gfxMap,
                                identity),
                            RsxImplicitSamplerStateEncoding.Lightmap,
                            imageStreams,
                            textureCache,
                            worldTextureCache,
                            failedTextureCacheKeys,
                            failedWorldTextureCacheKeys,
                            ref decodedTextureCount,
                            ref skippedTextureCount));
                        break;
                    }
                default:
                    throw new InvalidOperationException(
                        $"Unsupported Event20 custom sampler flag 0x{(byte)sampler:X2}.");
            }
        }
    }

    private static MapRenderWorldMaterialSamplerBinding CreateWorldReflectionProbeSamplerBinding(
        MapRenderWorldTextureAssetBinding runtimeBinding,
        RenderTextureCache textureCache,
        MapRenderWorldTextureCache worldTextureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        HashSet<MapRenderWorldTextureCacheKey> failedWorldTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        if (!runtimeBinding.IsReady ||
            runtimeBinding.DescriptorImage is not { } image ||
            runtimeBinding.Descriptor is not { } descriptor)
        {
            return CreateMissingCustomSamplerBinding(
                1,
                runtimeBinding.Identity);
        }

        MapRenderWorldTextureCacheKey cacheKey =
            MapRenderWorldTextureCacheKey.WorldRuntimeCube(
                image,
                RsxImplicitSamplerStateEncoding.ReflectionProbe,
                runtimeBinding.Identity);
        Texture? texture = null;
        if (worldTextureCache.TryGetValue(cacheKey, out Texture? cached))
            texture = cached;
        else if (!failedWorldTextureCacheKeys.Contains(cacheKey) &&
                 TryCreateWorldTextureFromCapturedResource(
                     runtimeBinding,
                     RsxImplicitSamplerStateEncoding.ReflectionProbe,
                     out texture))
        {
            worldTextureCache.Add(cacheKey, texture!);
            decodedTextureCount++;
        }
        else if (!failedWorldTextureCacheKeys.Contains(cacheKey) &&
                 image.PayloadBytes.Count > 0)
        {
            string authoredFormat =
                GfxImageDecoder.DescribeFormat(image.Format);
            IReadOnlyList<TextureAuthoredSubresource>
                authoredSubresources = [];
            bool capturedAuthored =
                AuthoredTexturePayloadCapture.TryCaptureCube(
                    image,
                    image.PayloadBytes,
                    image.Width,
                    image.Height,
                    Math.Max(1, (int)image.LevelCount),
                    firstMipLevel: 0,
                    authoredFormat,
                    out authoredSubresources);
            bool completeProvenAuthored =
                capturedAuthored &&
                AuthoredTexturePayloadCapture
                    .IsCompleteProvenChain(
                        authoredSubresources,
                        TextureTarget.TextureCube,
                        image.Width,
                        image.Height);
            DecodedRgbaGfxCube cube = default;
            bool hasDecoded =
                !(textureCache.PreferProvenAuthoredPayloads &&
                  completeProvenAuthored) &&
                GfxImageDecoder.TryDecodeCubeRgba(
                    image,
                    image.PayloadBytes,
                    out cube,
                    out _);
            bool canPublishAuthoredOnly =
                textureCache.PreferProvenAuthoredPayloads &&
                completeProvenAuthored;
            if (hasDecoded || canPublishAuthoredOnly)
            {
                IReadOnlyList<TextureCubeFace>? faces = hasDecoded
                    ? cube.Faces
                        .Select(face => new TextureCubeFace(
                            face[0].RgbaBytes,
                            face.Skip(1)
                                .Select(mip => new TextureMip(
                                    mip.Width,
                                    mip.Height,
                                    mip.RgbaBytes))
                                .ToArray()))
                        .ToArray()
                    : null;
                DecodedRgbaGfxImage? top = hasDecoded
                    ? cube.Faces[0][0]
                    : null;
                texture = new Texture(
                    top?.Name ?? image.Name ?? "unnamed_cube",
                    top?.Width ?? image.Width,
                    top?.Height ?? image.Height,
                    top?.Format ?? authoredFormat,
                    RsxImplicitSamplerStateEncoding.ReflectionProbe,
                    RsxSamplerDecoder.Decode(
                        RsxImplicitSamplerStateEncoding.ReflectionProbe,
                        image.MinLodControl,
                        image.UseSrgbReads),
                    RsxTextureCommandBuilder.FromDescriptor(
                        descriptor),
                    hasDecoded && cube.Faces
                        .SelectMany(face => face)
                        .Any(mip => mip.HasTransparency) || !hasDecoded,
                    top?.RgbaBytes ?? [],
                    faces is null ? [] : faces[0].MipLevels,
                    TextureTarget.TextureCube,
                    faces,
                    authoredSubresources);
                worldTextureCache.Add(cacheKey, texture);
                if (hasDecoded)
                    decodedTextureCount++;
                else
                    skippedTextureCount++;
            }
            else
            {
                failedWorldTextureCacheKeys.Add(cacheKey);
                skippedTextureCount++;
            }
        }
        else
        {
            failedWorldTextureCacheKeys.Add(cacheKey);
            skippedTextureCount++;
        }

        return CreateWorldBinding(
            new MaterialSamplerIdentity(
                SamplerArgIndex: -1,
                SamplerDest: 1,
                SamplerHash: 0,
                image.TextureSemantic),
            image.Name ?? string.Empty,
            texture,
            null,
            runtimeBinding.Identity);
    }

    private static MapRenderWorldMaterialSamplerBinding CreateWorldCustomImageSamplerBinding(
        MapRenderWorldTextureAssetBinding runtimeBinding,
        byte samplerState,
        IGfxImagePayloadResolver imageStreams,
        RenderTextureCache textureCache,
        MapRenderWorldTextureCache worldTextureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        HashSet<MapRenderWorldTextureCacheKey> failedWorldTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        ushort destination = checked((ushort)runtimeBinding.Identity.SamplerDestination);
        if (!runtimeBinding.IsReady ||
            runtimeBinding.DescriptorImage is not { } image ||
            runtimeBinding.Descriptor is not { } descriptor)
        {
            return CreateMissingCustomSamplerBinding(
                destination,
                runtimeBinding.Identity);
        }

        Texture? texture;
        if (runtimeBinding.Resource is { } capturedResource)
        {
            MapRenderWorldTextureCacheKey capturedCacheKey =
                MapRenderWorldTextureCacheKey.CapturedWorldTexture(
                    image,
                    samplerState,
                    runtimeBinding.Identity,
                    capturedResource.Shape,
                    capturedResource.ContentSha256);
            if (worldTextureCache.TryGetValue(capturedCacheKey, out texture))
            {
            }
            else if (TryCreateWorldTextureFromCapturedResource(
                         runtimeBinding,
                         samplerState,
                         out texture))
            {
                worldTextureCache.Add(capturedCacheKey, texture!);
                decodedTextureCount++;
            }
            else
            {
                _ = TryDecodeTexture(
                    image,
                    samplerState,
                    imageStreams,
                    textureCache,
                    failedTextureCacheKeys,
                    true,
                    ref decodedTextureCount,
                    ref skippedTextureCount,
                    out texture) && texture is not null;
            }
        }
        else
        {
            _ = TryDecodeTexture(
                image,
                samplerState,
                imageStreams,
                textureCache,
                failedTextureCacheKeys,
                true,
                ref decodedTextureCount,
                ref skippedTextureCount,
                out texture) && texture is not null;
        }
        RsxTextureCommandState descriptorState =
            RsxTextureCommandBuilder.FromDescriptor(descriptor);
        Texture? runtimeTexture = texture is null
            ? null
            : texture.RsxTextureCommandState == descriptorState
                ? texture
                : texture with
                {
                    RsxTextureCommandState = descriptorState
                };
        return CreateWorldBinding(
            new MaterialSamplerIdentity(
                SamplerArgIndex: -1,
                destination,
                SamplerHash: 0,
                image.TextureSemantic),
            image.Name ?? string.Empty,
            runtimeTexture,
            null,
            runtimeBinding.Identity);
    }

    private static bool TryCreateWorldTextureFromCapturedResource(
        MapRenderWorldTextureAssetBinding runtimeBinding,
        byte samplerState,
        out Texture? texture)
    {
        texture = null;
        if (!runtimeBinding.IsRenderResourceReady ||
            runtimeBinding.Resource is not { } resource ||
            runtimeBinding.DescriptorImage is not { } image ||
            runtimeBinding.Descriptor is not { } descriptor)
        {
            return false;
        }

        TextureTarget target = resource.Shape switch
        {
            TextureSamplerShape.TwoDimensional =>
                TextureTarget.Texture2D,
            TextureSamplerShape.Cube =>
                TextureTarget.TextureCube,
            _ => throw new InvalidOperationException(
                $"Captured runtime texture shape {resource.Shape} cannot be materialized as a render texture.")
        };
        int faceCount = target == TextureTarget.TextureCube ? 6 : 1;
        int mipCount = resource.Subresources.Count / faceCount;
        if (mipCount <= 0 || resource.Subresources.Count != faceCount * mipCount)
        {
            throw new InvalidOperationException(
                "Captured runtime texture subresources lost their face/mip shape.");
        }

        byte[] topRgba;
        IReadOnlyList<TextureMip> topMipLevels;
        IReadOnlyList<TextureCubeFace>? cubeFaces = null;
        if (target == TextureTarget.TextureCube)
        {
            cubeFaces = Enumerable.Range(0, faceCount)
                .Select(faceOrdinal =>
                {
                    DecodedTextureSubresourceSnapshot[] face =
                        resource.Subresources
                            .Skip(faceOrdinal * mipCount)
                            .Take(mipCount)
                            .ToArray();
                    return new TextureCubeFace(
                        face[0].SharedPixelBytes,
                        face.Skip(1)
                            .Select(mip => new TextureMip(
                                mip.Width,
                                mip.Height,
                                mip.SharedPixelBytes))
                            .ToArray());
                })
                .ToArray();
            topRgba = cubeFaces[0].RgbaBytes;
            topMipLevels = cubeFaces[0].MipLevels;
        }
        else
        {
            DecodedTextureSubresourceSnapshot top =
                resource.Subresources[0];
            topRgba = top.SharedPixelBytes;
            topMipLevels = resource.Subresources
                .Skip(1)
                .Select(mip => new TextureMip(
                    mip.Width,
                    mip.Height,
                    mip.SharedPixelBytes))
                .ToArray();
        }

        texture = new Texture(
            resource.Name,
            resource.Width,
            resource.Height,
            resource.Format,
            samplerState,
            RsxSamplerDecoder.Decode(
                samplerState,
                image.MinLodControl,
                image.UseSrgbReads),
            RsxTextureCommandBuilder.FromDescriptor(descriptor),
            resource.HasTransparency,
            topRgba,
            topMipLevels,
            target,
            cubeFaces,
            PixelFormat: resource.PixelFormat);
        return true;
    }

    private static MapRenderWorldMaterialSamplerBinding CreateMissingCustomSamplerBinding(
        ushort destination,
        MapRenderWorldRuntimeTextureIdentity? runtimeIdentity = null) =>
        CreateWorldBinding(
            new MaterialSamplerIdentity(
                SamplerArgIndex: -1,
                destination,
                SamplerHash: 0,
                TextureSemantic: 0),
            string.Empty,
            null,
            null,
            runtimeIdentity);

    private static MapRenderWorldMaterialSamplerBinding CreateWorldBinding(
        MaterialSamplerIdentity identity,
        string textureName,
        Texture? texture,
        UvRoute? uvRoute,
        MapRenderWorldRuntimeTextureIdentity? RuntimeTextureIdentity = null,
        EditorMaterialTextureRole EditorTextureRole =
            EditorMaterialTextureRole.Unknown,
        int TextureTableOrdinal = -1,
        string? ExternalResourceIdentity = null) => new(
            new MaterialSamplerBinding(
                identity,
                textureName,
                texture,
                uvRoute,
                EditorTextureRole,
                TextureTableOrdinal,
                ExternalResourceIdentity),
            RuntimeTextureIdentity);

}
