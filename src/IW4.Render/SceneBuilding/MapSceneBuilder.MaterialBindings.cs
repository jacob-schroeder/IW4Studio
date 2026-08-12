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
        MapRenderColorLayer Layer,
        WorldVertexDecoder Decoder);

    private sealed record PreparedStaticColorLayer(
        MapRenderColorLayer Layer,
        XSurfaceVertexDecoder Decoder);

    private static void AppendTexturedSurface(
        Dictionary<WorldTexturedBatchKey, TexturedBatchBuilder> batches,
        MapRenderMaterialPass pass,
        MapRenderTexture texture,
        MapRenderTexture? lightmapTexture,
        IReadOnlyList<MapRenderColorLayer> colorLayers,
        IReadOnlyList<MapRenderMaterialSamplerBinding> materialSamplers,
        MapRenderShaderExecutionContract shaderExecution,
        MapRenderUvRoute uvRoute,
        MapRenderState state,
        MapRenderEditorDepthPrepassPlan? editorDepthPrepass,
        MapRenderShaderExecutionContract? depthPrepassShaderExecution,
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

    private static IReadOnlyList<MapRenderColorLayer> CreateSingleColorLayer(
        MapRenderMaterialPass pass,
        MapRenderTexture texture,
        MapRenderUvRoute uvRoute)
    {
        return
        [
            new MapRenderColorLayer(
                0,
                pass.SamplerArgIndex,
                pass.SamplerDest,
                pass.SamplerHash,
                pass.TextureSemantic,
                texture,
                uvRoute,
                -1)
        ];
    }

    private static IReadOnlyList<MapRenderMaterialSamplerBinding> CreateMaterialSamplerBindings(
        IReadOnlyList<MapRenderColorLayer> colorLayers)
    {
        return colorLayers.Select(layer => new MapRenderMaterialSamplerBinding(
            layer.SamplerArgIndex,
            layer.SamplerDest,
            layer.SamplerHash,
            layer.TextureSemantic,
            layer.Texture.Name,
            layer.Texture,
            layer.UvRoute)).ToArray();
    }

    private static IReadOnlyList<PreparedColorLayer> PrepareWorldColorLayers(
        bool enableEditorMultiTexture,
        MaterialAsset? material,
        WorldMaterialSamplerPlan samplerPlan,
        MapRenderEditorMaterialTexturePlan? texturePlan,
        SelectedColorPass selectedPass,
        WorldVertexLayoutSelection worldVertexLayout,
        WorldVertexDecoderResolver vertexDecoderResolver,
        MapRenderTexture primaryTexture,
        MapRenderUvRoute primaryUvRoute,
        WorldVertexDecoder primaryDecoder,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        var prepared = new List<PreparedColorLayer>(MapRenderScene.MaxColorLayerCount)
        {
            new(
                new MapRenderColorLayer(
                    0,
                    selectedPass.Pass.SamplerArgIndex,
                    selectedPass.Pass.SamplerDest,
                    selectedPass.Pass.SamplerHash,
                    selectedPass.Pass.TextureSemantic,
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

        AddColorRole(MapRenderEditorMaterialTextureRole.ColorLayer1, 1, 0);
        AddColorRole(MapRenderEditorMaterialTextureRole.ColorLayer2, 2, 1);
        AddColorRole(MapRenderEditorMaterialTextureRole.ColorLayer3, 3, 2);
        AddColorRole(MapRenderEditorMaterialTextureRole.ColorLayer4, 4, 3);
        decodedTextureCount += newlyDecodedTextureCount;
        skippedTextureCount += newlySkippedTextureCount;
        return prepared;

        void AddColorRole(
            MapRenderEditorMaterialTextureRole role,
            int layerIndex,
            int blendWeightComponent)
        {
            if (prepared.Count >= MapRenderScene.MaxColorLayerCount ||
                !texturePlan.TryGetUniqueBinding(
                    role,
                    out MapRenderEditorMaterialTextureBinding? planned) ||
                planned is null ||
                planned.NameHash == selectedPass.Pass.SamplerHash ||
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
                    out byte texCoordSource))
            {
                return;
            }

            WorldVertexDecoderSelection decoderSelection =
                vertexDecoderResolver(
                worldVertexLayout,
                texCoordSource,
                true);
            WorldVertexDecoder? decoder = decoderSelection.Decoder;
            MapRenderUvRoute uvRoute = decoderSelection.UvRoute;
            if (decoder is null || !decoder.HasTexCoord ||
                !TryDecodeTexture(
                    texture,
                    image,
                    imageStreams,
                    textureCache,
                    failedTextureCacheKeys,
                    true,
                    ref newlyDecodedTextureCount,
                    ref newlySkippedTextureCount,
                    out MapRenderTexture? decodedTexture) ||
                decodedTexture is null)
            {
                return;
            }

            prepared.Add(new PreparedColorLayer(
                new MapRenderColorLayer(
                    layerIndex,
                    argIndex,
                    arg.Dest,
                    planned.NameHash,
                    planned.TextureSemantic,
                    decodedTexture,
                    uvRoute,
                    blendWeightComponent),
                decoder));
        }
    }

    private static IReadOnlyList<MapRenderMaterialSamplerBinding> PrepareStaticMaterialSamplerBindings(
        MaterialAsset material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        GfxWorldAsset gfxMap,
        IMapRenderWorldTextureBindingResolver worldTextureBindings,
        SelectedColorPass selectedPass,
        byte? reflectionProbeIndex,
        MapRenderUvRoute uvRoute,
        IReadOnlyList<MapRenderColorLayer> colorLayers,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        var bindings = new List<MapRenderMaterialSamplerBinding>();
        var seen = new HashSet<(ushort Dest, uint Hash)>();
        if (techset is not null &&
            selectedPass.Pass.TechniqueSlot >= 0 &&
            selectedPass.Pass.PassIndex >= 0)
        {
            MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
                .FirstOrDefault(candidate =>
                    candidate.Index == selectedPass.Pass.TechniqueSlot);
            if (slot?.Technique is { } technique &&
                (uint)selectedPass.Pass.PassIndex <
                    (uint)technique.Passes.Count)
            {
                MaterialPassAsset sourcePass =
                    technique.Passes[selectedPass.Pass.PassIndex];
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

                    uint samplerHash = unchecked((uint)arg.ArgumentRaw);
                    if (!seen.Add((arg.Dest, samplerHash)))
                        continue;

                    if (!TryResolveMaterialTexture(
                            material,
                            lookup,
                            samplerHash,
                            requireColor: false,
                            out MaterialTextureDef? materialTexture,
                            out GfxImageAsset? image) ||
                        materialTexture is null ||
                        image is null)
                    {
                        bindings.Add(new MapRenderMaterialSamplerBinding(
                            argIndex,
                            arg.Dest,
                            samplerHash,
                            materialTexture?.Semantic ?? 0,
                            image?.Name ?? string.Empty,
                            null,
                            uvRoute,
                            EditorTextureRole: materialTexture is null
                                ? MapRenderEditorMaterialTextureRole.Unknown
                                : MapRenderEditorMaterialTextureRoleClassifier
                                    .Classify(materialTexture).Role,
                            TextureTableOrdinal: FindMaterialTextureOrdinal(
                                material,
                                materialTexture)));
                        continue;
                    }

                    bool decoded = TryDecodeTexture(
                        materialTexture,
                        image,
                        imageStreams,
                        textureCache,
                        failedTextureCacheKeys,
                        true,
                        ref decodedTextureCount,
                        ref skippedTextureCount,
                        out MapRenderTexture? decodedTexture) &&
                        decodedTexture is not null;
                    bindings.Add(new MapRenderMaterialSamplerBinding(
                        argIndex,
                        arg.Dest,
                        samplerHash,
                        materialTexture.Semantic,
                        decodedTexture?.Name ?? image.Name ?? string.Empty,
                        decodedTexture,
                        uvRoute,
                        EditorTextureRole:
                            MapRenderEditorMaterialTextureRoleClassifier
                                .Classify(materialTexture).Role,
                        TextureTableOrdinal: FindMaterialTextureOrdinal(
                            material,
                            materialTexture)));
                }
            }
        }

        foreach (MapRenderColorLayer layer in colorLayers)
        {
            if (!seen.Add((layer.SamplerDest, layer.SamplerHash)))
                continue;

            bindings.Add(new MapRenderMaterialSamplerBinding(
                layer.SamplerArgIndex,
                layer.SamplerDest,
                layer.SamplerHash,
                layer.TextureSemantic,
                layer.Texture.Name,
                layer.Texture,
                layer.UvRoute));
        }

        AppendStaticCustomSamplerBindings(
            bindings,
            selectedPass.Pass.CustomSamplerFlags,
            gfxMap,
            worldTextureBindings,
            reflectionProbeIndex,
            textureCache,
            failedTextureCacheKeys,
            ref decodedTextureCount,
            ref skippedTextureCount);

        return bindings;
    }

    private static void AppendStaticCustomSamplerBindings(
        List<MapRenderMaterialSamplerBinding> bindings,
        byte customSamplerFlags,
        GfxWorldAsset gfxMap,
        IMapRenderWorldTextureBindingResolver worldTextureBindings,
        byte? reflectionProbeIndex,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
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
            failedTextureCacheKeys,
            ref decodedTextureCount,
            ref skippedTextureCount));
    }

    internal static MapRenderWorldRuntimeTextureIdentity?
        ResolveStaticReflectionProbeIdentity(
            byte customSamplerFlags,
            byte? reflectionProbeIndex)
    {
        var selection = new MapRenderWorldCustomSamplerSelection(
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
        MapRenderEditorMaterialTexturePlan? texturePlan,
        SelectedColorPass selectedPass,
        MapRenderTexture primaryTexture,
        MapRenderUvRoute primaryUvRoute,
        XSurfaceVertexDecoder primaryDecoder,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        var prepared = new List<PreparedStaticColorLayer>(
            MapRenderScene.MaxColorLayerCount)
        {
            new(
                CreateSingleColorLayer(
                    selectedPass.Pass,
                    primaryTexture,
                    primaryUvRoute)[0],
                primaryDecoder)
        };

        if (!enableEditorMultiTexture ||
            techset is null ||
            texturePlan is null ||
            selectedPass.Pass.TechniqueSlot < 0 ||
            selectedPass.Pass.PassIndex < 0)
        {
            return prepared;
        }

        MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
            .FirstOrDefault(candidate =>
                candidate.Index == selectedPass.Pass.TechniqueSlot);
        if (slot?.Technique is not { } technique ||
            (uint)selectedPass.Pass.PassIndex >= (uint)technique.Passes.Count)
        {
            return prepared;
        }

        MaterialPassAsset sourcePass =
            technique.Passes[selectedPass.Pass.PassIndex];
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

        AddColorRole(MapRenderEditorMaterialTextureRole.ColorLayer1, 1);
        AddColorRole(MapRenderEditorMaterialTextureRole.ColorLayer2, 2);
        AddColorRole(MapRenderEditorMaterialTextureRole.ColorLayer3, 3);
        AddColorRole(MapRenderEditorMaterialTextureRole.ColorLayer4, 4);
        decodedTextureCount += newlyDecodedTextureCount;
        skippedTextureCount += newlySkippedTextureCount;
        return prepared;

        void AddColorRole(
            MapRenderEditorMaterialTextureRole role,
            int layerIndex)
        {
            if (prepared.Count >= MapRenderScene.MaxColorLayerCount ||
                !texturePlan.TryGetUniqueBinding(
                    role,
                    out MapRenderEditorMaterialTextureBinding? planned) ||
                planned is null ||
                planned.NameHash == selectedPass.Pass.SamplerHash ||
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
                        unchecked((uint)candidate.argument.ArgumentRaw) ==
                            planned.NameHash)
                    .Select(candidate =>
                        (candidate.argument, candidate.index))
                    .ToArray();
            if (matchingArgs.Length != 1)
                return;

            (MaterialShaderArgumentAsset arg, int argIndex) = matchingArgs[0];
            XSurfaceVertexDecoder decoder = primaryDecoder;
            MapRenderUvRoute uvRoute = primaryUvRoute with
            {
                Label = $"static color layer {layerIndex} reuses base UV"
            };
            if (RsxShaderInputRouter.TrySelectSamplerSource(
                    sourcePass,
                    arg,
                    vertexDecl,
                    planned.TextureSemantic,
                    out byte texCoordSource) &&
                SelectStaticVertexDecoder(texCoordSource) is { } routedDecoder)
            {
                decoder = routedDecoder;
                uvRoute = BuildStaticModelUvRoute(texCoordSource);
            }

            if (!TryDecodeTexture(
                    materialTexture,
                    image,
                    imageStreams,
                    textureCache,
                    failedTextureCacheKeys,
                    true,
                    ref newlyDecodedTextureCount,
                    ref newlySkippedTextureCount,
                    out MapRenderTexture? decodedTexture) ||
                decodedTexture is null)
            {
                return;
            }

            prepared.Add(new PreparedStaticColorLayer(
                new MapRenderColorLayer(
                    layerIndex,
                    argIndex,
                    arg.Dest,
                    planned.NameHash,
                    planned.TextureSemantic,
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
        if (selectedPass.Pass.TechniqueSlot < 0 ||
            selectedPass.Pass.PassIndex < 0)
        {
            return WorldMaterialSamplerPlan.Empty;
        }

        MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
            .FirstOrDefault(candidate =>
                candidate.Index == selectedPass.Pass.TechniqueSlot);
        if (slot?.Technique is not { } technique ||
            (uint)selectedPass.Pass.PassIndex >=
                (uint)technique.Passes.Count)
        {
            return WorldMaterialSamplerPlan.Empty;
        }

        MaterialPassAsset sourcePass =
            technique.Passes[selectedPass.Pass.PassIndex];
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

            uint samplerHash = unchecked((uint)argument.ArgumentRaw);
            if (!seen.Add((argument.Dest, samplerHash)))
                continue;

            bool resolved = TryResolveMaterialTexture(
                material,
                lookup,
                samplerHash,
                requireColor: false,
                out MaterialTextureDef? materialTexture,
                out GfxImageAsset? image);
            MapRenderEditorMaterialTextureRole editorTextureRole =
                resolved && materialTexture is not null
                    ? MapRenderEditorMaterialTextureRoleClassifier
                        .Classify(materialTexture).Role
                    : MapRenderEditorMaterialTextureRole.Unknown;
            entries.Add(new WorldMaterialSamplerPlanEntry(
                argumentIndex,
                argument,
                samplerHash,
                materialTexture,
                image,
                editorTextureRole,
                resolved
                    ? FindMaterialTextureOrdinal(material, materialTexture)
                    : -1));
        }

        return new WorldMaterialSamplerPlan(
            sourcePass,
            vertexDeclaration,
            entries.ToArray(),
            args);
    }

    private static IReadOnlyList<MapRenderMaterialSamplerBinding> PrepareWorldMaterialSamplerBindings(
        WorldMaterialSamplerPlan samplerPlan,
        IMapRenderWorldTextureBindingResolver worldTextureBindings,
        SelectedColorPass selectedPass,
        WorldVertexLayoutSelection worldVertexLayout,
        WorldVertexDecoderResolver vertexDecoderResolver,
        GfxWorldAsset gfxMap,
        GfxSurface surface,
        IReadOnlyList<MapRenderColorLayer> colorLayers,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        var bindings = new List<MapRenderMaterialSamplerBinding>();
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
                bindings.Add(new MapRenderMaterialSamplerBinding(
                    planned.ArgumentIndex,
                    arg.Dest,
                    samplerHash,
                    materialTexture?.Semantic ?? 0,
                    image?.Name ?? string.Empty,
                    null,
                    null,
                    EditorTextureRole: planned.EditorTextureRole,
                    TextureTableOrdinal: planned.TextureTableOrdinal));
                continue;
            }

            MapRenderColorLayer? decodedColorLayer =
                colorLayers.FirstOrDefault(layer =>
                    layer.SamplerDest == arg.Dest &&
                    layer.SamplerHash == samplerHash);
            MapRenderTexture? decodedTexture = decodedColorLayer?.Texture;
            bool textureDecoded = decodedTexture is not null;
            if (!textureDecoded)
            {
                textureDecoded = TryDecodeTexture(
                    materialTexture,
                    image,
                    imageStreams,
                    textureCache,
                    failedTextureCacheKeys,
                    true,
                    ref decodedTextureCount,
                    ref skippedTextureCount,
                    out decodedTexture) && decodedTexture is not null;
            }

            MapRenderUvRoute? samplerUvRoute = null;
            byte texCoordSource = 0;
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

            bindings.Add(new MapRenderMaterialSamplerBinding(
                planned.ArgumentIndex,
                arg.Dest,
                samplerHash,
                materialTexture.Semantic,
                decodedTexture?.Name ?? image.Name ?? string.Empty,
                decodedTexture,
                samplerUvRoute,
                EditorTextureRole: planned.EditorTextureRole,
                TextureTableOrdinal: planned.TextureTableOrdinal));
        }

        foreach (MapRenderColorLayer layer in colorLayers)
        {
            if (!seen.Add((layer.SamplerDest, layer.SamplerHash)))
                continue;

            bindings.Add(new MapRenderMaterialSamplerBinding(
                layer.SamplerArgIndex,
                layer.SamplerDest,
                layer.SamplerHash,
                layer.TextureSemantic,
                layer.Texture.Name,
                layer.Texture,
                layer.UvRoute));
        }

        AppendWorldCustomSamplerBindings(
            bindings,
            selectedPass.Pass.CustomSamplerFlags,
            gfxMap,
            worldTextureBindings,
            surface,
            imageStreams,
            textureCache,
            failedTextureCacheKeys,
            ref decodedTextureCount,
            ref skippedTextureCount);

        return bindings;
    }

    private static void AppendWorldCustomSamplerBindings(
        List<MapRenderMaterialSamplerBinding> bindings,
        byte customSamplerFlags,
        GfxWorldAsset gfxMap,
        IMapRenderWorldTextureBindingResolver worldTextureBindings,
        GfxSurface surface,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        var selection = new MapRenderWorldCustomSamplerSelection(customSamplerFlags);

        foreach (MapRenderWorldCustomSamplerFlags sampler in
                 selection.EnumerateBindingsInNativeOrder())
        {
            switch (sampler)
            {
                case MapRenderWorldCustomSamplerFlags.ReflectionProbe:
                    {
                        var identity = new MapRenderWorldRuntimeTextureIdentity(
                            MapRenderWorldRuntimeTextureKind.ReflectionProbe,
                            surface.ReflectionProbeIndex);
                        bindings.Add(CreateWorldReflectionProbeSamplerBinding(
                            worldTextureBindings.ResolveWorldRuntimeTexture(
                                gfxMap,
                                identity),
                            textureCache,
                            failedTextureCacheKeys,
                            ref decodedTextureCount,
                            ref skippedTextureCount));
                        break;
                    }
                case MapRenderWorldCustomSamplerFlags.SecondaryLightmap:
                    {
                        var identity = new MapRenderWorldRuntimeTextureIdentity(
                            MapRenderWorldRuntimeTextureKind.SecondaryLightmap,
                            surface.LightmapIndex);
                        bindings.Add(CreateWorldCustomImageSamplerBinding(
                            worldTextureBindings.ResolveWorldRuntimeTexture(
                                gfxMap,
                                identity),
                            LightmapPrimarySamplerState,
                            imageStreams,
                            textureCache,
                            failedTextureCacheKeys,
                            ref decodedTextureCount,
                            ref skippedTextureCount));
                        break;
                    }
                case MapRenderWorldCustomSamplerFlags.PrimaryLightmap:
                    {
                        var identity = new MapRenderWorldRuntimeTextureIdentity(
                            MapRenderWorldRuntimeTextureKind.PrimaryLightmap,
                            surface.LightmapIndex);
                        bindings.Add(CreateWorldCustomImageSamplerBinding(
                            worldTextureBindings.ResolveWorldRuntimeTexture(
                                gfxMap,
                                identity),
                            LightmapPrimarySamplerState,
                            imageStreams,
                            textureCache,
                            failedTextureCacheKeys,
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

    private static MapRenderMaterialSamplerBinding CreateWorldReflectionProbeSamplerBinding(
        MapRenderWorldTextureAssetBinding runtimeBinding,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
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

        var textureDef = new MaterialTextureDef
        {
            NameHash = 0,
            SamplerState = ReflectionProbeSamplerState,
            Semantic = image.TextureSemantic,
            Image = image
        };
        MapRenderTextureCacheKey cacheKey =
            MapRenderTextureCacheKey.RuntimeCube(
                textureDef,
                image,
                runtimeBinding.Identity);
        MapRenderTexture? texture = null;
        if (textureCache.TryGetValue(cacheKey, out MapRenderTexture? cached))
            texture = cached;
        else if (!failedTextureCacheKeys.Contains(cacheKey) &&
                 TryCreateWorldTextureFromCapturedResource(
                     runtimeBinding,
                     ReflectionProbeSamplerState,
                     out texture))
        {
            textureCache.Add(cacheKey, texture!);
            decodedTextureCount++;
        }
        else if (!failedTextureCacheKeys.Contains(cacheKey) &&
                 image.PayloadBytes.Count > 0)
        {
            string authoredFormat =
                GfxImageDecoder.DescribeFormat(image.Format);
            IReadOnlyList<MapRenderTextureAuthoredSubresource>
                authoredSubresources = [];
            bool capturedAuthored =
                MapRenderAuthoredTexturePayloadCapture.TryCaptureCube(
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
                MapRenderAuthoredTexturePayloadCapture
                    .IsCompleteProvenChain(
                        authoredSubresources,
                        MapRenderTextureTarget.TextureCube,
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
                IReadOnlyList<MapRenderTextureCubeFace>? faces = hasDecoded
                    ? cube.Faces
                        .Select(face => new MapRenderTextureCubeFace(
                            face[0].RgbaBytes,
                            face.Skip(1)
                                .Select(mip => new MapRenderTextureMip(
                                    mip.Width,
                                    mip.Height,
                                    mip.RgbaBytes))
                                .ToArray()))
                        .ToArray()
                    : null;
                DecodedRgbaGfxImage? top = hasDecoded
                    ? cube.Faces[0][0]
                    : null;
                texture = new MapRenderTexture(
                    top?.Name ?? image.Name ?? "unnamed_cube",
                    top?.Width ?? image.Width,
                    top?.Height ?? image.Height,
                    top?.Format ?? authoredFormat,
                    ReflectionProbeSamplerState,
                    MapRenderSamplerDecoder.Decode(
                        ReflectionProbeSamplerState,
                        image.Pad0F,
                        image.Pad1B),
                    MapRenderRsxTextureCommandBuilder.FromDescriptor(
                        descriptor),
                    hasDecoded && cube.Faces
                        .SelectMany(face => face)
                        .Any(mip => mip.HasTransparency) || !hasDecoded,
                    top?.RgbaBytes ?? [],
                    faces is null ? [] : faces[0].MipLevels,
                    MapRenderTextureTarget.TextureCube,
                    faces,
                    authoredSubresources);
                textureCache.Add(cacheKey, texture);
                if (hasDecoded)
                    decodedTextureCount++;
                else
                    skippedTextureCount++;
            }
            else
            {
                failedTextureCacheKeys.Add(cacheKey);
                skippedTextureCount++;
            }
        }
        else
        {
            failedTextureCacheKeys.Add(cacheKey);
            skippedTextureCount++;
        }

        return new MapRenderMaterialSamplerBinding(
            -1,
            1,
            0,
            image.TextureSemantic,
            image.Name ?? string.Empty,
            texture,
            null,
            runtimeBinding.Identity);
    }

    private static MapRenderMaterialSamplerBinding CreateWorldCustomImageSamplerBinding(
        MapRenderWorldTextureAssetBinding runtimeBinding,
        byte samplerState,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
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

        var textureDef = new MaterialTextureDef
        {
            NameHash = 0,
            SamplerState = samplerState,
            Semantic = image.TextureSemantic,
            Image = image
        };
        MapRenderTexture? texture;
        if (runtimeBinding.Resource is { } capturedResource)
        {
            MapRenderTextureCacheKey capturedCacheKey =
                MapRenderTextureCacheKey.CapturedRuntimeTexture(
                    textureDef,
                    image,
                    runtimeBinding.Identity,
                    capturedResource.Shape,
                    capturedResource.ContentSha256);
            if (textureCache.TryGetValue(capturedCacheKey, out texture))
            {
            }
            else if (TryCreateWorldTextureFromCapturedResource(
                         runtimeBinding,
                         samplerState,
                         out texture))
            {
                textureCache.Add(capturedCacheKey, texture!);
                decodedTextureCount++;
            }
            else
            {
                _ = TryDecodeTexture(
                    textureDef,
                    image,
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
                textureDef,
                image,
                imageStreams,
                textureCache,
                failedTextureCacheKeys,
                true,
                ref decodedTextureCount,
                ref skippedTextureCount,
                out texture) && texture is not null;
        }
        MapRenderRsxTextureCommandState descriptorState =
            MapRenderRsxTextureCommandBuilder.FromDescriptor(descriptor);
        MapRenderTexture? runtimeTexture = texture is null
            ? null
            : texture.RsxTextureCommandState == descriptorState
                ? texture
                : texture with
                {
                    RsxTextureCommandState = descriptorState
                };
        return new MapRenderMaterialSamplerBinding(
            -1,
            destination,
            0,
            image.TextureSemantic,
            image.Name ?? string.Empty,
            runtimeTexture,
            null,
            runtimeBinding.Identity);
    }

    private static bool TryCreateWorldTextureFromCapturedResource(
        MapRenderWorldTextureAssetBinding runtimeBinding,
        byte samplerState,
        out MapRenderTexture? texture)
    {
        texture = null;
        if (!runtimeBinding.IsRenderResourceReady ||
            runtimeBinding.Resource is not { } resource ||
            runtimeBinding.DescriptorImage is not { } image ||
            runtimeBinding.Descriptor is not { } descriptor)
        {
            return false;
        }

        MapRenderTextureTarget target = resource.Shape switch
        {
            MapRenderSelectedPassSamplerShape.TwoDimensional =>
                MapRenderTextureTarget.Texture2D,
            MapRenderSelectedPassSamplerShape.Cube =>
                MapRenderTextureTarget.TextureCube,
            _ => throw new InvalidOperationException(
                $"Captured runtime texture shape {resource.Shape} cannot be materialized as a render texture.")
        };
        int faceCount = target == MapRenderTextureTarget.TextureCube ? 6 : 1;
        int mipCount = resource.Subresources.Count / faceCount;
        if (mipCount <= 0 || resource.Subresources.Count != faceCount * mipCount)
        {
            throw new InvalidOperationException(
                "Captured runtime texture subresources lost their face/mip shape.");
        }

        byte[] topRgba;
        IReadOnlyList<MapRenderTextureMip> topMipLevels;
        IReadOnlyList<MapRenderTextureCubeFace>? cubeFaces = null;
        if (target == MapRenderTextureTarget.TextureCube)
        {
            cubeFaces = Enumerable.Range(0, faceCount)
                .Select(faceOrdinal =>
                {
                    MapRenderDecodedTextureSubresourceSnapshot[] face =
                        resource.Subresources
                            .Skip(faceOrdinal * mipCount)
                            .Take(mipCount)
                            .ToArray();
                    return new MapRenderTextureCubeFace(
                        face[0].SharedRgbaBytes,
                        face.Skip(1)
                            .Select(mip => new MapRenderTextureMip(
                                mip.Width,
                                mip.Height,
                                mip.SharedRgbaBytes))
                            .ToArray());
                })
                .ToArray();
            topRgba = cubeFaces[0].RgbaBytes;
            topMipLevels = cubeFaces[0].MipLevels;
        }
        else
        {
            MapRenderDecodedTextureSubresourceSnapshot top =
                resource.Subresources[0];
            topRgba = top.SharedRgbaBytes;
            topMipLevels = resource.Subresources
                .Skip(1)
                .Select(mip => new MapRenderTextureMip(
                    mip.Width,
                    mip.Height,
                    mip.SharedRgbaBytes))
                .ToArray();
        }

        texture = new MapRenderTexture(
            resource.Name,
            resource.Width,
            resource.Height,
            resource.Format,
            samplerState,
            MapRenderSamplerDecoder.Decode(
                samplerState,
                image.Pad0F,
                image.Pad1B),
            MapRenderRsxTextureCommandBuilder.FromDescriptor(descriptor),
            resource.HasTransparency,
            topRgba,
            topMipLevels,
            target,
            cubeFaces);
        return true;
    }

    private static MapRenderMaterialSamplerBinding CreateMissingCustomSamplerBinding(
        ushort destination,
        MapRenderWorldRuntimeTextureIdentity? runtimeIdentity = null) => new(
            -1,
            destination,
            0,
            0,
            string.Empty,
            null,
            null,
            runtimeIdentity);

}
