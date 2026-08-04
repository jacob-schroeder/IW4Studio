using System.Buffers;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;

using IW4.Render.Assets;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Scheduling;
using IW4.Render.Shaders;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder
{
    private static MapRenderEditorDepthPrepassPlan?
        SelectEditorStandardDepthPrepass(
            MaterialAsset material,
            MaterialTechniqueSetAsset? techset,
            RenderAssetLookup lookup)
    {
        if (techset is null)
            return null;

        MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
            .FirstOrDefault(candidate =>
                candidate.Index ==
                MapRenderEditorDepthPrepassPlanner.StandardTechniqueSlot);
        if (slot?.Technique is not { } technique ||
            technique.Passes.Count != 1)
        {
            return null;
        }

        MaterialPassAsset pass = technique.Passes[0];
        MapRenderSelectedPassProgramSources sources = lookup.ResolveSources(
            techset,
            technique,
            new MapRenderSelectedTechniquePass(
                MapRenderEditorDepthPrepassPlanner.StandardPassIndex,
                pass));
        if (!sources.HasCompleteArguments ||
            !MapRenderStateDecoder.TryDecode(
                material,
                slot.Index,
                MapRenderEditorDepthPrepassPlanner.StandardPassIndex,
                lookup,
                out MapRenderState state))
        {
            return null;
        }

        return MapRenderEditorDepthPrepassPlanner.TryCreateStandard(
            material.Info.Name ?? string.Empty,
            techset.Name ?? string.Empty,
            material.StateFlags,
            slot.Index,
            technique.Name ?? string.Empty,
            MapRenderEditorDepthPrepassPlanner.StandardPassIndex,
            technique.Passes.Count,
            technique.Flags,
            sources.Arguments,
            sources.VertexProgram.Name,
            sources.PixelProgram.Name,
            state,
            out MapRenderEditorDepthPrepassPlan? plan,
            out _)
                ? plan
                : null;
    }

    internal static IReadOnlyList<T> PlanRendererPassSequence<T>(
        IReadOnlyList<IReadOnlyList<T>> techniquePassGroups,
        int observedTechniqueSlotCount,
        bool techniqueSlotsAreSequence)
    {
        ArgumentNullException.ThrowIfNull(techniquePassGroups);

        if (observedTechniqueSlotCount <= 0 ||
            techniquePassGroups.Count != observedTechniqueSlotCount)
        {
            return [];
        }

        if (techniqueSlotsAreSequence)
            return techniquePassGroups.SelectMany(group => group).ToArray();

        // One selector invocation indexes exactly one technique slot. More than
        // one unsequenced observation means the reducer combined separate runtime
        // submissions or captures; there is no engine-backed basis for choosing
        // one of them. Keep the preceding material preview instead of ranking a
        // fabricated "best" group.
        return techniquePassGroups.Count == 1
            ? techniquePassGroups[0].ToArray()
            : [];
    }

    internal static IReadOnlyList<T> AuthorizeAtomicRendererPassSequence<T>(
        int expectedPassCount,
        IReadOnlyList<T> preparedPasses,
        Func<T, bool> rendererProgramReady)
    {
        ArgumentNullException.ThrowIfNull(preparedPasses);
        ArgumentNullException.ThrowIfNull(rendererProgramReady);

        if (expectedPassCount <= 0 || preparedPasses.Count != expectedPassCount)
            return [];

        for (int passIndex = 0; passIndex < preparedPasses.Count; passIndex++)
        {
            if (!rendererProgramReady(preparedPasses[passIndex]))
                return [];
        }

        // Copy in the already-authored order. Callers commit only this returned
        // sequence, so a missing or blocked pass cannot leave a partial draw.
        return preparedPasses.ToArray();
    }

    /// <summary>
    /// Tests scene-build readiness for one exact receiver sidecar. An
    /// unshadowed invocation has no frame atlas and therefore requires all
    /// runtime samplers immediately. A shadow-allocated invocation retains a
    /// structurally complete translated program while its same-revision atlas
    /// sampler remains deliberately deferred to draw-time authorization.
    /// </summary>
    internal static bool ReceiverVariantProgramReadyForBuild(
        MapRenderShaderExecutionContract execution,
        MapRenderTechniqueVariantAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(execution);
        return allocation switch
        {
            MapRenderTechniqueVariantAllocation.Unshadowed =>
                execution.ProgramExecutionReady,
            MapRenderTechniqueVariantAllocation.ShadowMapAllocated =>
                execution.RendererProgramReady,
            _ => throw new ArgumentOutOfRangeException(nameof(allocation))
        };
    }

    /// <summary>
    /// Determines whether one exact draw-method slot owns a world receiver
    /// camera-color phase. Known null slots and known non-color techniques are
    /// authoritative absence. Missing material graph/state data remains
    /// required so a loader or renderer gap cannot silently opt a surface out
    /// of fail-closed exact coverage.
    /// </summary>
    internal static bool WorldReceiverVariantRequiredForBuild(
        MaterialAsset? material,
        MaterialTechniqueSetAsset? techniqueSet,
        RenderAssetLookup lookup,
        int? selectedTechniqueSlot)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        if (selectedTechniqueSlot is not { } techniqueSlot)
            return false;
        if (material is null || techniqueSet is null)
            return true;

        MaterialTechniqueSlot? slot = lookup
            .ResolveTechniqueSlots(techniqueSet)
            .FirstOrDefault(candidate => candidate.Index == techniqueSlot);
        if (slot is null)
            return true;
        if (slot.Technique is not { } technique)
        {
            return slot.Pointer.Type != PointerType.Null;
        }
        if (technique.PassCount != technique.Passes.Count)
            return true;

        for (int passIndex = 0;
             passIndex < technique.Passes.Count;
             passIndex++)
        {
            MaterialPassAsset pass = technique.Passes[passIndex];
            if (!MapRenderStateDecoder.TryDecode(
                    material,
                    techniqueSlot,
                    passIndex,
                    lookup,
                    out MapRenderState state))
            {
                return true;
            }

            string passClass = MapRenderPassClassifier.Classify(
                technique.Name ?? string.Empty,
                state,
                CountUnresolvedCodePixelSamplers(pass));
            if (MapRenderPassClassifier.CanSubmitToCameraColor(passClass))
                return true;
        }

        return false;
    }

    internal static IReadOnlyList<T> PlanRendererPassCandidates<T>(
        IReadOnlyList<T> authoredPasses,
        IReadOnlyList<T> fallbackPasses)
    {
        ArgumentNullException.ThrowIfNull(authoredPasses);
        ArgumentNullException.ThrowIfNull(fallbackPasses);

        // This is the structural authored-group choice. A retained base preview
        // is planned separately and never inserted into atomic authorization.
        return authoredPasses.Count > 0
            ? authoredPasses.ToArray()
            : fallbackPasses.ToArray();
    }

    internal static IReadOnlyList<T> RetainCompletedStageAfterAtomicAuthorization<T>(
        IReadOnlyList<T> authorizedAuthoredPasses,
        IReadOnlyList<T> retainedPreviewPasses)
    {
        ArgumentNullException.ThrowIfNull(authorizedAuthoredPasses);
        ArgumentNullException.ThrowIfNull(retainedPreviewPasses);

        // The preview is a distinct visualization channel, not part of the
        // engine-selected authored group. It is used only when that complete
        // group cannot be materialized by the current renderer stage.
        return authorizedAuthoredPasses.Count > 0
            ? authorizedAuthoredPasses.ToArray()
            : retainedPreviewPasses.ToArray();
    }

    private static IReadOnlyList<SelectedColorPass> SelectRendererColorPasses(
        MaterialAsset material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        int selectedTechniqueSlot,
        out string blockReason)
    {
        if (techset is null)
        {
            blockReason = "Technique set could not be resolved.";
            return [];
        }

        MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
            .FirstOrDefault(candidate => candidate.Index == selectedTechniqueSlot);
        if (slot?.Technique is not { } technique)
        {
            blockReason = "Selected technique slot could not be resolved.";
            return [];
        }

        var selectedPasses = new List<SelectedColorPass>(technique.Passes.Count);
        blockReason = "Selected technique has no renderer-capable camera-color material sampler.";
        for (int passIndex = 0; passIndex < technique.Passes.Count; passIndex++)
        {
            MaterialPassAsset pass = technique.Passes[passIndex];
            MaterialVertexDeclarationAsset? vertexDecl =
                pass.VertexDeclaration ?? lookup.ResolveVertexDeclaration(pass.VertexDeclPointer);
            pass.VertexShader = lookup.ResolveVertexShader(pass.VertexShaderPointer, pass.VertexShader);
            pass.PixelShader = lookup.ResolvePixelShader(pass.PixelShaderPointer, pass.PixelShader);
            IReadOnlyList<MaterialShaderArgumentAsset> args = lookup.ResolveShaderArgs(pass);
            int unresolvedCodeSamplerCount = CountUnresolvedCodePixelSamplers(pass);
            MapRenderState state = MapRenderStateDecoder.TryDecode(
                material,
                slot.Index,
                passIndex,
                lookup,
                out MapRenderState decodedState)
                ? decodedState
                : MapRenderState.Default;
            string techniqueName = technique.Name ?? string.Empty;
            string passClass = MapRenderPassClassifier.Classify(
                techniqueName,
                state,
                unresolvedCodeSamplerCount);
            if (!MapRenderPassClassifier.CanSubmitToCameraColor(passClass))
            {
                blockReason = $"Selected pass class {passClass} does not submit camera color.";
                continue;
            }

            SelectedColorPass? bestForPass = null;
            for (int argIndex = 0; argIndex < args.Count; argIndex++)
            {
                MaterialShaderArgumentAsset arg = args[argIndex];
                if (arg.Type != MaterialShaderArgumentType.MaterialPixelSampler)
                    continue;

                uint samplerHash = unchecked((uint)arg.ArgumentRaw);
                if (!TryResolveMaterialTexture(
                        material,
                        lookup,
                        samplerHash,
                        requireColor: false,
                        out MaterialTextureDef? texture,
                        out GfxImageAsset? image) ||
                    texture is null ||
                    image is null)
                {
                    blockReason = $"Selected camera-color pass sampler dest {arg.Dest} hash 0x{samplerHash:X8} has no decoded material resource.";
                    continue;
                }

                bool engineRouted = RsxShaderInputRouter.TrySelectSamplerSource(
                    pass,
                    arg,
                    vertexDecl,
                    texture.Semantic,
                    out byte routedTexCoordSource);
                byte texCoordSource = engineRouted
                    ? routedTexCoordSource
                    : GenericFallbackTexCoordSource;
                var renderPass = new MapRenderMaterialPass(
                    material.Info.Name ?? string.Empty,
                    techset.Name ?? string.Empty,
                    slot.Index,
                    techniqueName,
                    passClass,
                    passIndex,
                    argIndex,
                    arg.Dest,
                    samplerHash,
                    texture.Semantic,
                    texCoordSource,
                    pass.CustomSamplerFlags);
                var candidate = new SelectedColorPass(
                    texture,
                    image,
                    renderPass,
                    state,
                    unresolvedCodeSamplerCount,
                    texCoordSource,
                    engineRouted,
                    AuthoredProgramExecutable: true);

                if (bestForPass is not null &&
                    CompareEditorTechniqueCandidate(
                        candidate,
                        bestForPass) >= 0)
                    continue;

                bestForPass = candidate;
            }

            if (bestForPass is { } selectedPass)
                selectedPasses.Add(selectedPass);
        }

        if (selectedPasses.Count > 0)
            blockReason = string.Empty;
        return selectedPasses;
    }

    private static IReadOnlyList<SelectedColorPass> SelectEditorMaterialPasses(
        MaterialAsset material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        int? selectedTechniqueSlot,
        out string blockReason)
    {
        if (techset is null)
        {
            blockReason = "Editor technique set could not be resolved.";
            return [];
        }

        // A populated engine selector result owns the authored group. Failure
        // to materialize that exact slot retains the already-completed base
        // preview; it must not silently scan for a different technique.
        if (selectedTechniqueSlot is { } exactSlot)
        {
            return SelectRendererColorPasses(
                material,
                techset,
                lookup,
                exactSlot,
                out blockReason);
        }

        IReadOnlyList<MaterialTechniqueSlot> slots =
            lookup.ResolveTechniqueSlots(techset);
        string lastBlockReason =
            "Editor technique policy found no populated authored slot.";
        IReadOnlyList<int> orderedSlots =
            MapRenderEditorTechniquePolicy.OrderCandidateSlots(slots);
        foreach (int techniqueSlot in orderedSlots.Where(slot =>
                     slot is MapRenderEditorTechniquePolicy
                         .PreferredLitTechniqueSlot or
                         MapRenderEditorTechniquePolicy
                         .PreferredEmissiveTechniqueSlot))
        {
            IReadOnlyList<SelectedColorPass> passes =
                SelectRendererColorPasses(
                    material,
                    techset,
                    lookup,
                    techniqueSlot,
                    out string candidateBlockReason);
            if (passes.Count > 0)
            {
                blockReason = string.Empty;
                return passes;
            }

            lastBlockReason =
                $"slot={techniqueSlot}: {candidateBlockReason}";
        }

        IReadOnlyList<SelectedColorPass>? bestPasses = null;
        SelectedColorPass? bestRepresentative = null;
        foreach (int techniqueSlot in orderedSlots.Where(slot =>
                     slot is not MapRenderEditorTechniquePolicy
                         .PreferredLitTechniqueSlot and not
                         MapRenderEditorTechniquePolicy
                         .PreferredEmissiveTechniqueSlot))
        {
            IReadOnlyList<SelectedColorPass> passes =
                SelectRendererColorPasses(
                    material,
                    techset,
                    lookup,
                    techniqueSlot,
                    out string candidateBlockReason);
            if (passes.Count == 0)
            {
                lastBlockReason =
                    $"slot={techniqueSlot}: {candidateBlockReason}";
                continue;
            }

            SelectedColorPass representative = passes.Aggregate(
                (best, candidate) => CompareEditorTechniqueCandidate(
                    candidate,
                    best) < 0
                    ? candidate
                    : best);
            if (bestRepresentative is not null &&
                CompareEditorTechniqueCandidate(
                    representative,
                    bestRepresentative) >= 0)
            {
                continue;
            }

            bestPasses = passes;
            bestRepresentative = representative;
        }

        if (bestPasses is not null)
        {
            blockReason = string.Empty;
            return bestPasses;
        }

        blockReason = lastBlockReason;
        return [];
    }

    private static int CompareEditorTechniqueCandidate(
        SelectedColorPass left,
        SelectedColorPass right)
    {
        int result = EditorPassClassRank(left.Pass.PassClass).CompareTo(
            EditorPassClassRank(right.Pass.PassClass));
        if (result != 0)
            return result;
        result = (left.Texture.Semantic == ColorTextureSemantic ? 0 : 1)
            .CompareTo(right.Texture.Semantic == ColorTextureSemantic ? 0 : 1);
        if (result != 0)
            return result;
        result = (left.TexCoordSourceIsEngineRouted ? 0 : 1)
            .CompareTo(right.TexCoordSourceIsEngineRouted ? 0 : 1);
        if (result != 0)
            return result;
        result = left.UnresolvedCodeSamplerCount.CompareTo(
            right.UnresolvedCodeSamplerCount);
        if (result != 0)
            return result;
        result = (left.Pass.SamplerDest == 0 ? 0 : 1)
            .CompareTo(right.Pass.SamplerDest == 0 ? 0 : 1);
        if (result != 0)
            return result;
        result = left.Pass.TechniqueSlot.CompareTo(
            right.Pass.TechniqueSlot);
        if (result != 0)
            return result;
        result = left.Pass.PassIndex.CompareTo(right.Pass.PassIndex);
        if (result != 0)
            return result;
        result = left.Pass.SamplerArgIndex.CompareTo(
            right.Pass.SamplerArgIndex);
        return result != 0
            ? result
            : left.Pass.SamplerHash.CompareTo(right.Pass.SamplerHash);
    }

    private static int EditorPassClassRank(string passClass) =>
        passClass switch
        {
            MapRenderPassClassifier.CameraColor => 0,
            MapRenderPassClassifier.CameraColorWithUnresolvedCodeSamplers => 1,
            MapRenderPassClassifier.CameraColorWithMissingState => 2,
            _ => 3
        };

    private static SelectedColorPass? SelectStaticModelBaseSurfaceTexturePass(
        MaterialAsset material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup)
    {
        if (!TryResolveMaterialTexture(
                material,
                lookup,
                preferredHash: null,
                requireColor: true,
                out MaterialTextureDef? texture,
                out GfxImageAsset? image) ||
            texture is null ||
            image is null)
        {
            return null;
        }

        // Static XSurface backend source 2 is the routed tc0 input for
        // MTL_WORLDVERT_TEX_2_NRM_2: Verts1 + 0x04, two big-endian half-floats.
        // Source 0 is Verts0 position data and must never be used as UVs. Avoid
        // hydrating/scanning dependency-zone technique graphs for this
        // explicit generic material route.
        const byte staticTexCoordSource = GenericFallbackTexCoordSource;
        var pass = new MapRenderMaterialPass(
            material.Info.Name ?? string.Empty,
            techset?.Name ?? string.Empty,
            -1,
            "material.texture[semantic=0x02]",
            BaseSurfaceTexturePassClass,
            -1,
            -1,
            0,
            texture.NameHash,
            texture.Semantic,
            staticTexCoordSource,
            0);
        return new SelectedColorPass(
            texture,
            image,
            pass,
            GenericMaterialState,
            0,
            staticTexCoordSource,
            TexCoordSourceIsEngineRouted: true,
            AuthoredProgramExecutable: false);
    }

    private static SelectedColorPass? SelectMaterialColorUvPass(
        MaterialAsset material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup)
    {
        if (!TryResolveMaterialTexture(
                material,
                lookup,
                preferredHash: null,
                requireColor: true,
                out MaterialTextureDef? texture,
                out GfxImageAsset? image) ||
            texture is null ||
            image is null)
        {
            return null;
        }

        // The preview texture and UV stay on the completed material-table
        // route. A ranked authored pass contributes only a canonical alpha
        // test tuple below; it does not make this an authored-program draw.
        // World backend source 0x02 is the primary color UV row for every
        // eligible mp_boneyard surface.
        const byte texCoordSource = GenericFallbackTexCoordSource;
        MapRenderState fallbackState = GenericMaterialState;
        if (techset is not null &&
            TrySelectRoutedBaseSurfaceTexturePass(
                material,
                techset,
                lookup,
                out SelectedColorPass rankedAuthoredPass))
        {
            // The generic preview is not the authored program, but alpha test
            // is independent fixed-function state already emulated by the
            // generic shader. Retain only its canonical PS3 tuple; importing
            // blend, cull, depth, or stencil state here would turn the preview
            // into a partial authored-pass execution.
            fallbackState = rankedAuthoredPass.State;
        }
        var pass = new MapRenderMaterialPass(
            material.Info.Name ?? string.Empty,
            techset?.Name ?? string.Empty,
            -1,
            "material.texture[semantic=0x02]",
            BaseSurfaceTexturePassClass,
            -1,
            -1,
            0,
            texture.NameHash,
            texture.Semantic,
            texCoordSource,
            0);
        return new SelectedColorPass(
            texture,
            image,
            pass,
            fallbackState,
            0,
            texCoordSource,
            TexCoordSourceIsEngineRouted: false,
            AuthoredProgramExecutable: false);
    }

    private static bool TrySelectRoutedBaseSurfaceTexturePass(
        MaterialAsset material,
        MaterialTechniqueSetAsset techset,
        RenderAssetLookup lookup,
        out SelectedColorPass selectedPass)
    {
        Dictionary<uint, MaterialTextureDef> colorTexturesByHash = material.Textures
            .Where(texture => texture.Semantic == ColorTextureSemantic)
            .GroupBy(texture => texture.NameHash)
            .ToDictionary(group => group.Key, group => group.First());

        SelectedColorPass? bestPass = null;
        int bestRank = int.MaxValue;
        foreach (MaterialTechniqueSlot slot in lookup.ResolveTechniqueSlots(techset).Where(slot => slot.Technique is not null))
        {
            MaterialTechniqueAsset technique = slot.Technique!;
            for (int passIndex = 0; passIndex < technique.Passes.Count; passIndex++)
            {
                MaterialPassAsset pass = technique.Passes[passIndex];
                MaterialVertexDeclarationAsset? vertexDecl =
                    pass.VertexDeclaration ?? lookup.ResolveVertexDeclaration(pass.VertexDeclPointer);
                pass.VertexShader ??= lookup.ResolveVertexShader(pass.VertexShaderPointer);
                pass.PixelShader ??= lookup.ResolvePixelShader(pass.PixelShaderPointer);
                IReadOnlyList<MaterialShaderArgumentAsset> args = lookup.ResolveShaderArgs(pass);
                int unresolvedCodeSamplerCount = CountUnresolvedCodePixelSamplers(pass);
                for (int argIndex = 0; argIndex < args.Count; argIndex++)
                {
                    MaterialShaderArgumentAsset arg = args[argIndex];
                    uint samplerHash = unchecked((uint)arg.ArgumentRaw);
                    if (arg.Type != MaterialShaderArgumentType.MaterialPixelSampler ||
                        !colorTexturesByHash.TryGetValue(samplerHash, out MaterialTextureDef? texture) ||
                        !RsxShaderInputRouter.TrySelectSamplerSource(pass, arg, vertexDecl, texture.Semantic, out byte texCoordSource))
                    {
                        continue;
                    }

                    GfxImageAsset? image = texture.Image ?? lookup.ResolveImage(texture.DataPointer);
                    if (image is null)
                        continue;

                    MapRenderState authoredState =
                        MapRenderStateDecoder.TryDecode(
                            material,
                            slot.Index,
                            passIndex,
                            lookup,
                            out MapRenderState decodedState)
                            ? decodedState
                            : MapRenderState.Default;

                    var renderPass = new MapRenderMaterialPass(
                        material.Info.Name ?? string.Empty,
                        techset.Name ?? string.Empty,
                        slot.Index,
                        technique.Name ?? string.Empty,
                        BaseSurfaceTexturePassClass,
                        passIndex,
                        argIndex,
                        arg.Dest,
                        samplerHash,
                        texture.Semantic,
                        texCoordSource,
                        pass.CustomSamplerFlags);

                    var candidate = new SelectedColorPass(
                        texture,
                        image,
                        renderPass,
                        CreateGenericMaterialFallbackState(authoredState),
                        unresolvedCodeSamplerCount,
                        texCoordSource,
                        TexCoordSourceIsEngineRouted: true,
                        AuthoredProgramExecutable: false);
                    int rank = RankBaseSurfaceTextureCandidate(material, slot.Index, technique.Name ?? string.Empty, passIndex, pass, arg, lookup);
                    if (rank < bestRank)
                    {
                        bestRank = rank;
                        bestPass = candidate;
                    }
                }
            }
        }

        selectedPass = bestPass!;
        return bestPass is not null;
    }

    /// <summary>
    /// Retains the canonical PS3 fixed-function alpha-test tuple on the
    /// editor's generic material without importing any other authored render
    /// state. The generic fragment shader implements these exact tuples.
    /// </summary>
    internal static MapRenderState CreateGenericMaterialFallbackState(
        MapRenderState authoredState)
    {
        if (!authoredState.AlphaTestEnabled ||
            MapRenderAlphaTest.Resolve(authoredState) is null)
        {
            return GenericMaterialState;
        }

        return GenericMaterialState with
        {
            AlphaTestEnabled = true,
            AlphaFunc = authoredState.AlphaFunc,
            AlphaRef = authoredState.AlphaRef
        };
    }

    /// <summary>
    /// Applies the one authored fixed-function state component that the static
    /// model generic shader implements.  This is deliberately a copy of the
    /// selected fallback pass with a new state value: LOD selection, texture
    /// identity, UV routing, and authored-program eligibility stay unchanged.
    /// </summary>
    internal static SelectedColorPass ApplyStaticModelGenericFallbackState(
        SelectedColorPass selectedPass,
        MapRenderState authoredState) =>
        selectedPass with
        {
            State = CreateGenericMaterialFallbackState(authoredState)
        };

    private static SelectedColorPass ApplyStaticModelGenericFallbackState(
        MaterialAsset material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        SelectedColorPass selectedPass,
        IDictionary<MaterialAsset, MapRenderState>
            syntheticFallbackStateCache)
    {
        MapRenderState authoredState = MapRenderState.Default;
        bool hasAuthoredState = false;

        // Generic-selector and authored-candidate fallbacks retain their
        // source pass identity. Prefer that exact PS3 state row when present.
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
                hasAuthoredState = MapRenderStateDecoder.TryDecode(
                    material,
                    selectedPass.Pass.TechniqueSlot,
                    selectedPass.Pass.PassIndex,
                    lookup,
                    out authoredState);
            }
        }

        // The base-surface preview is synthetic and therefore has no source
        // pass ordinal. Reuse the ranked routed color pass solely as the
        // alpha-test state authority. TrySelectRoutedBaseSurfaceTexturePass
        // already strips all non-alpha state from its result.
        bool isSyntheticPass = selectedPass.Pass.TechniqueSlot < 0 ||
                               selectedPass.Pass.PassIndex < 0;
        if (!hasAuthoredState &&
            isSyntheticPass &&
            syntheticFallbackStateCache.TryGetValue(
                material,
                out MapRenderState cachedState))
        {
            authoredState = cachedState;
            hasAuthoredState = true;
        }

        if (!hasAuthoredState &&
            techset is not null &&
            TrySelectRoutedBaseSurfaceTexturePass(
                material,
                techset,
                lookup,
                out SelectedColorPass rankedAuthoredPass))
        {
            authoredState = rankedAuthoredPass.State;
        }

        if (isSyntheticPass &&
            !syntheticFallbackStateCache.ContainsKey(material))
        {
            // Static material selection itself is already cached per
            // material/selector. This second, material-only cache prevents
            // different primary-light selector buckets from rescanning the
            // same 37-slot technique graph just to recover one alpha tuple.
            syntheticFallbackStateCache.Add(material, authoredState);
        }

        return ApplyStaticModelGenericFallbackState(
            selectedPass,
            authoredState);
    }

    private static int RankBaseSurfaceTextureCandidate(
        MaterialAsset material,
        int techniqueSlot,
        string techniqueName,
        int passIndex,
        MaterialPassAsset pass,
        MaterialShaderArgumentAsset samplerArg,
        RenderAssetLookup lookup)
    {
        int unresolvedCodeSamplerCount = CountUnresolvedCodePixelSamplers(pass);
        MapRenderState state = MapRenderState.Default;
        if (MapRenderStateDecoder.TryDecode(material, techniqueSlot, passIndex, lookup, out MapRenderState decodedState))
            state = decodedState;

        string passClass = MapRenderPassClassifier.Classify(techniqueName, state, unresolvedCodeSamplerCount);
        int passClassRank = passClass switch
        {
            MapRenderPassClassifier.CameraColor => 0,
            MapRenderPassClassifier.CameraColorWithUnresolvedCodeSamplers => 1,
            MapRenderPassClassifier.CameraColorWithMissingState => 2,
            MapRenderPassClassifier.NonFillColorWrite => 4,
            MapRenderPassClassifier.NonColorWrite => 5,
            MapRenderPassClassifier.NonColorWire => 6,
            MapRenderPassClassifier.ShadowDepth => 9,
            _ => 8
        };
        int samplerRank = samplerArg.Dest == 0 ? 0 : 1;
        return checked(passClassRank * 1_000_000 +
                       samplerRank * 100_000 +
                       Math.Min(unresolvedCodeSamplerCount, 99) * 1_000 +
                       Math.Max(techniqueSlot, 0) * 10 +
                       Math.Max(passIndex, 0));
    }

    private static SelectedColorPass? SelectGenericMaterialFallbackPass(
        MaterialAsset material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        int? selectedTechniqueSlot)
    {
        if (techset is not null && selectedTechniqueSlot.HasValue)
        {
            MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
                .FirstOrDefault(candidate => candidate.Index == selectedTechniqueSlot.Value);
            if (slot?.Technique is { } technique)
            {
                SelectedColorPass? fallbackSelectedPass = null;
                for (int passIndex = 0; passIndex < technique.Passes.Count; passIndex++)
                {
                    MaterialPassAsset pass = technique.Passes[passIndex];
                    MaterialVertexDeclarationAsset? vertexDecl =
                        pass.VertexDeclaration ?? lookup.ResolveVertexDeclaration(pass.VertexDeclPointer);
                    pass.VertexShader ??= lookup.ResolveVertexShader(pass.VertexShaderPointer);
                    pass.PixelShader ??= lookup.ResolvePixelShader(pass.PixelShaderPointer);
                    IReadOnlyList<MaterialShaderArgumentAsset> args = lookup.ResolveShaderArgs(pass);
                    for (int argIndex = 0; argIndex < args.Count; argIndex++)
                    {
                        MaterialShaderArgumentAsset arg = args[argIndex];
                        if (arg.Type != MaterialShaderArgumentType.MaterialPixelSampler)
                            continue;

                        uint samplerHash = unchecked((uint)arg.ArgumentRaw);
                        if (TryResolveMaterialTexture(material, lookup, samplerHash, requireColor: true, out MaterialTextureDef? texture, out GfxImageAsset? image))
                        {
                            bool texCoordSourceIsEngineRouted = RsxShaderInputRouter.TrySelectSamplerSource(
                                pass,
                                arg,
                                vertexDecl,
                                texture!.Semantic,
                                out byte routedTexCoordSource);
                            byte texCoordSource = texCoordSourceIsEngineRouted
                                ? routedTexCoordSource
                                : GenericFallbackTexCoordSource;
                            return CreateGenericMaterialFallbackPass(
                                material,
                                techset,
                                slot.Index,
                                technique.Name ?? string.Empty,
                                passIndex,
                                argIndex,
                                arg.Dest,
                                samplerHash,
                                texture!,
                                image!,
                                CountUnresolvedCodePixelSamplers(pass),
                                texCoordSource,
                                texCoordSourceIsEngineRouted,
                                false,
                                pass.CustomSamplerFlags);
                        }

                        if (fallbackSelectedPass is null &&
                            TryResolveMaterialTexture(material, lookup, samplerHash, requireColor: false, out texture, out image))
                        {
                            bool texCoordSourceIsEngineRouted = RsxShaderInputRouter.TrySelectSamplerSource(
                                pass,
                                arg,
                                vertexDecl,
                                texture!.Semantic,
                                out byte routedTexCoordSource);
                            byte texCoordSource = texCoordSourceIsEngineRouted
                                ? routedTexCoordSource
                                : GenericFallbackTexCoordSource;
                            fallbackSelectedPass = CreateGenericMaterialFallbackPass(
                                material,
                                techset,
                                slot.Index,
                                technique.Name ?? string.Empty,
                                passIndex,
                                argIndex,
                                arg.Dest,
                                samplerHash,
                                texture!,
                                image!,
                                CountUnresolvedCodePixelSamplers(pass),
                                texCoordSource,
                                texCoordSourceIsEngineRouted,
                                false,
                                pass.CustomSamplerFlags);
                        }
                    }
                }

                if (fallbackSelectedPass is not null)
                    return fallbackSelectedPass;
            }
        }

        if (techset is not null)
        {
            foreach (MaterialTechniqueSlot slot in lookup.ResolveTechniqueSlots(techset).Where(slot => slot.Technique is not null))
            {
                MaterialTechniqueAsset technique = slot.Technique!;
                for (int passIndex = 0; passIndex < technique.Passes.Count; passIndex++)
                {
                    MaterialPassAsset pass = technique.Passes[passIndex];
                    MaterialVertexDeclarationAsset? vertexDecl =
                        pass.VertexDeclaration ?? lookup.ResolveVertexDeclaration(pass.VertexDeclPointer);
                    pass.VertexShader ??= lookup.ResolveVertexShader(pass.VertexShaderPointer);
                    pass.PixelShader ??= lookup.ResolvePixelShader(pass.PixelShaderPointer);
                    IReadOnlyList<MaterialShaderArgumentAsset> args = lookup.ResolveShaderArgs(pass);
                    for (int argIndex = 0; argIndex < args.Count; argIndex++)
                    {
                        MaterialShaderArgumentAsset arg = args[argIndex];
                        if (arg.Type != MaterialShaderArgumentType.MaterialPixelSampler)
                            continue;

                        uint samplerHash = unchecked((uint)arg.ArgumentRaw);
                        if (!TryResolveMaterialTexture(material, lookup, samplerHash, requireColor: true, out MaterialTextureDef? texture, out GfxImageAsset? image) ||
                            !RsxShaderInputRouter.TrySelectSamplerSource(pass, arg, vertexDecl, texture!.Semantic, out byte texCoordSource))
                        {
                            continue;
                        }

                        return CreateGenericMaterialFallbackPass(
                            material,
                            techset,
                            slot.Index,
                            technique.Name ?? string.Empty,
                            passIndex,
                            argIndex,
                            arg.Dest,
                            samplerHash,
                            texture,
                            image!,
                            CountUnresolvedCodePixelSamplers(pass),
                            texCoordSource,
                            true,
                            false,
                            pass.CustomSamplerFlags);
                    }
                }
            }
        }

        if (!TryResolveMaterialTexture(material, lookup, preferredHash: null, requireColor: true, out MaterialTextureDef? fallbackTexture, out GfxImageAsset? fallbackImage) &&
            !TryResolveMaterialTexture(material, lookup, preferredHash: null, requireColor: false, out fallbackTexture, out fallbackImage))
        {
            return null;
        }

        return CreateGenericMaterialFallbackPass(
            material,
            techset,
            selectedTechniqueSlot ?? -1,
            ResolveTechniqueName(techset, lookup, selectedTechniqueSlot),
            -1,
            -1,
            0,
            fallbackTexture!.NameHash,
            fallbackTexture,
            fallbackImage!,
            0,
            GenericFallbackTexCoordSource,
            false,
            false,
            0);
    }

    private static bool TryResolveMaterialTexture(
        MaterialAsset material,
        RenderAssetLookup lookup,
        uint? preferredHash,
        bool requireColor,
        out MaterialTextureDef? texture,
        out GfxImageAsset? image)
    {
        foreach (MaterialTextureDef candidate in material.Textures)
        {
            if (preferredHash.HasValue && candidate.NameHash != preferredHash.Value)
                continue;
            if (requireColor && candidate.Semantic != ColorTextureSemantic)
                continue;

            // Semantic 0x0b owns a MaterialWater union arm. Its sampleable
            // normal-map image is nested in water_t; the texture-table data
            // pointer addresses the water_t root and must never be
            // reinterpreted as a direct GfxImage pointer.
            GfxImageAsset? candidateImage = candidate.Water is { } water
                ? water.Image ??
                  lookup.ResolveImage(water.ImagePointer.Untyped)
                : candidate.Image ??
                  lookup.ResolveImage(candidate.DataPointer);
            if (candidateImage is null)
                continue;

            texture = candidate;
            image = candidateImage;
            return true;
        }

        texture = null;
        image = null;
        return false;
    }

    private static int FindMaterialTextureOrdinal(
        MaterialAsset material,
        MaterialTextureDef? texture)
    {
        if (texture is null)
            return -1;

        for (int ordinal = 0; ordinal < material.Textures.Count; ordinal++)
        {
            if (ReferenceEquals(material.Textures[ordinal], texture))
                return ordinal;
        }

        return -1;
    }

    private static SelectedColorPass CreateGenericMaterialFallbackPass(
        MaterialAsset material,
        MaterialTechniqueSetAsset? techset,
        int techniqueSlot,
        string techniqueName,
        int passIndex,
        int argIndex,
        ushort samplerDest,
        uint samplerHash,
        MaterialTextureDef texture,
        GfxImageAsset image,
        int unresolvedCodeSamplerCount,
        byte texCoordSource,
        bool texCoordSourceIsEngineRouted,
        bool authoredProgramExecutable,
        byte customSamplerFlags)
    {
        var pass = new MapRenderMaterialPass(
            material.Info.Name ?? string.Empty,
            techset?.Name ?? string.Empty,
            techniqueSlot,
            techniqueName,
            GenericMaterialFallbackPassClass,
            passIndex,
            argIndex,
            samplerDest,
            samplerHash,
            texture.Semantic,
            texCoordSource,
            customSamplerFlags);

        return new SelectedColorPass(
            texture,
            image,
            pass,
            GenericMaterialState,
            unresolvedCodeSamplerCount,
            texCoordSource,
            texCoordSourceIsEngineRouted,
            authoredProgramExecutable);
    }

    private static SelectedColorPass? SelectAuthoredMaterialCandidatePass(
        MaterialAsset material,
        MaterialTechniqueSetAsset techset,
        RenderAssetLookup lookup)
    {
        Dictionary<uint, MaterialTextureDef> texturesByHash = material.Textures
            .Where(texture => texture.Semantic == ColorTextureSemantic)
            .GroupBy(texture => texture.NameHash)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (MaterialTechniqueSlot slot in lookup.ResolveTechniqueSlots(techset).Where(slot => slot.Technique is not null))
        {
            MaterialTechniqueAsset technique = slot.Technique!;
            for (int passIndex = 0; passIndex < technique.Passes.Count; passIndex++)
            {
                MaterialPassAsset pass = technique.Passes[passIndex];
                MaterialVertexDeclarationAsset? vertexDecl =
                    pass.VertexDeclaration ?? lookup.ResolveVertexDeclaration(pass.VertexDeclPointer);
                pass.VertexShader ??= lookup.ResolveVertexShader(pass.VertexShaderPointer);
                pass.PixelShader ??= lookup.ResolvePixelShader(pass.PixelShaderPointer);
                IReadOnlyList<MaterialShaderArgumentAsset> args = lookup.ResolveShaderArgs(pass);
                for (int argIndex = 0; argIndex < args.Count; argIndex++)
                {
                    MaterialShaderArgumentAsset arg = args[argIndex];
                    if (arg.Type != MaterialShaderArgumentType.MaterialPixelSampler ||
                        !texturesByHash.TryGetValue(unchecked((uint)arg.ArgumentRaw), out MaterialTextureDef? texture) ||
                        !RsxShaderInputRouter.TrySelectSamplerSource(pass, arg, vertexDecl, texture.Semantic, out byte texCoordSource))
                    {
                        continue;
                    }

                    GfxImageAsset? image = texture.Image ?? lookup.ResolveImage(texture.DataPointer);
                    if (image is null)
                        continue;

                    var candidatePass = new MapRenderMaterialPass(
                        material.Info.Name ?? string.Empty,
                        techset.Name ?? string.Empty,
                        slot.Index,
                        technique.Name ?? string.Empty,
                        AuthoredMaterialCandidatePassClass,
                        passIndex,
                        argIndex,
                        arg.Dest,
                        unchecked((uint)arg.ArgumentRaw),
                        texture.Semantic,
                        texCoordSource,
                        pass.CustomSamplerFlags);
                    return new SelectedColorPass(
                        texture,
                        image,
                        candidatePass,
                        GenericMaterialState,
                        CountUnresolvedCodePixelSamplers(pass),
                        texCoordSource,
                        TexCoordSourceIsEngineRouted: true,
                        AuthoredProgramExecutable: false);
                }
            }
        }

        return null;
    }

    internal static int CountUnresolvedCodePixelSamplers(MaterialPassAsset pass)
    {
        int count = 0;
        foreach (MaterialShaderArgumentAsset arg in pass.Args)
        {
            if (arg.Type == MaterialShaderArgumentType.CodePixelSampler &&
                !IsMappedRuntimeCodePixelSampler(
                    unchecked((uint)arg.ArgumentRaw)))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// A code sampler is mapped only after the ABI catalog carries a concrete
    /// renderer-owned publication contract for it.
    /// </summary>
    internal static bool IsMappedRuntimeCodePixelSampler(uint rawSampler) =>
        MapRenderCodePixelSamplerAbi.TryResolve(
            rawSampler,
            out MapRenderCodePixelSamplerAbiEntry entry) &&
        entry.HasRuntimeRequirement;

    private static MaterialTechniqueSetAsset? ResolveTechniqueSet(MaterialAsset? material, RenderAssetLookup lookup)
    {
        if (material is null)
            return null;

        return material.TechniqueSet ?? lookup.ResolveTechniqueSet(material.TechniqueSetPointer);
    }

    private static string ResolveTechniqueName(
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        int? selectedTechniqueSlot)
    {
        if (techset is null || !selectedTechniqueSlot.HasValue)
            return string.Empty;

        MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
            .FirstOrDefault(candidate => candidate.Index == selectedTechniqueSlot.Value);
        return slot?.Technique?.Name ?? string.Empty;
    }

    private static WorldVertexLayoutSelection ResolveWorldVertexLayout(
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        SelectedColorPass selectedPass)
    {
        if (techset is null)
            return WorldVertexLayoutSelection.Unresolved(null);

        MaterialWorldVertexFormat logicalFormat = techset.WorldVertexFormat;
        if (selectedPass.Pass.TechniqueSlot < 0 || selectedPass.Pass.PassIndex < 0)
        {
            // Synthetic fallback passes have no technique flags to prove an Event20
            // selection. Use the layered world-format row explicitly as diagnostic
            // policy; never mislabel it as a runtime-selected engine row.
            int fallbackBackendRow = WorldVertexLayout.ResolveGenericFallbackBackendRow(logicalFormat);
            return WorldVertexLayout.HasBackendRow(fallbackBackendRow)
                ? new WorldVertexLayoutSelection(
                    logicalFormat,
                    fallbackBackendRow,
                    $"generic material fallback effective row {fallbackBackendRow}")
                : WorldVertexLayoutSelection.Unresolved(logicalFormat);
        }

        MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
            .FirstOrDefault(candidate => candidate.Index == selectedPass.Pass.TechniqueSlot);
        if (slot?.Technique is not { } technique)
        {
            return WorldVertexLayoutSelection.Unresolved(logicalFormat);
        }
        if ((uint)selectedPass.Pass.PassIndex >= (uint)technique.Passes.Count)
        {
            return WorldVertexLayoutSelection.Unresolved(logicalFormat);
        }

        int backendRow = WorldVertexLayout.ResolveEffectiveBackendRow(
            technique.Flags,
            logicalFormat);
        return WorldVertexLayout.HasBackendRow(backendRow)
            ? new WorldVertexLayoutSelection(
                logicalFormat,
                backendRow,
                $"engine effective row {backendRow}")
            : WorldVertexLayoutSelection.Unresolved(logicalFormat);
    }

    private static WorldVertexDecoder? SelectWorldVertexDecoder(
        GfxWorldAsset gfxMap,
        WorldVertexLayoutSelection layout,
        byte texCoordSource,
        bool texCoordSourceIsEngineRouted,
        out MapRenderUvRoute uvRoute)
    {
        uvRoute = new MapRenderUvRoute(
            "unresolved",
            layout.FormatText,
            texCoordSource,
            0,
            0,
            0,
            0,
            0,
            MapRenderUvBaseMode.Engine,
            0,
            1,
            1f,
            1f,
            0f,
            0f);

        if (!layout.IsResolved)
            return null;

        VertexSource? texCoord = null;
        VertexSource? blendWeights = null;
        if (WorldVertexLayout.TryGetSource(layout.BackendRow, source: 0x01, out WorldVertexSource blendSource) &&
            blendSource.ComponentCount >= 4 &&
            blendSource.RsxType == 0x04 &&
            WorldVertexLayout.TryGetStreamStride(layout.BackendRow, blendSource.StreamIndex, out byte blendStride))
        {
            blendWeights = new VertexSource(
                blendSource.StreamIndex,
                blendStride,
                blendSource.ByteOffset,
                blendSource.ComponentCount,
                blendSource.RsxType);
        }
        string uvLabel = texCoordSourceIsEngineRouted
            ? layout.Label
            : $"{layout.Label} generic fallback source 0x{texCoordSource:X2}";
        if (WorldVertexLayout.TryGetSource(layout.BackendRow, texCoordSource, out WorldVertexSource texCoordSourceRow))
        {
            if (WorldVertexLayout.TryGetStreamStride(
                    layout.BackendRow,
                    texCoordSourceRow.StreamIndex,
                    out byte texCoordStride))
            {
                texCoord = new VertexSource(
                    texCoordSourceRow.StreamIndex,
                    texCoordStride,
                    texCoordSourceRow.ByteOffset,
                    texCoordSourceRow.ComponentCount,
                    texCoordSourceRow.RsxType);
            }
            else if (texCoordSourceRow.IsUnavailableSourceTuple)
            {
                texCoord = new VertexSource(
                    texCoordSourceRow.StreamIndex,
                    0,
                    texCoordSourceRow.ByteOffset,
                    texCoordSourceRow.ComponentCount,
                    texCoordSourceRow.RsxType);
                if (texCoordSourceIsEngineRouted)
                {
                    uvLabel = $"{layout.Label} disabled source";
                }
            }
        }

        if (texCoord is { } source)
        {
            uvRoute = BuildUvRoute(
                layout,
                texCoordSource,
                source,
                uvLabel);
        }

        WorldVertexLightingSources lightingSources =
            WorldVertexLightingSourceResolver.Resolve(layout);
        return new WorldVertexDecoder(
            gfxMap.WorldDraw.VertexData.PackedVertices,
            gfxMap.WorldDraw.VertexLayerData.PackedLayerData,
            texCoord,
            blendWeights,
            lightingSources.LightmapTexCoord,
            lightingSources.Normal);
    }

    private static MapRenderUvRoute BuildUvRoute(
        WorldVertexLayoutSelection layout,
        byte texCoordSource,
        VertexSource source,
        string label)
    {
        return new MapRenderUvRoute(
            label,
            layout.FormatText,
            texCoordSource,
            source.StreamIndex,
            source.Stride,
            source.Offset,
            source.FormatByte0,
            source.FormatByte1,
            source.BaseMode,
            source.ComponentA,
            source.ComponentB,
            source.ScaleU,
            source.ScaleV,
            source.AddU,
            source.AddV);
    }

    private static StaticVertexDecoder? SelectStaticVertexDecoder(byte texCoordSource)
    {
        int staticBackendRow = (int)StaticXSurfaceSourceFormat;
        if (!WorldVertexLayout.TryGetSource(
                staticBackendRow,
                texCoordSource,
                out WorldVertexSource source))
        {
            return null;
        }

        if (WorldVertexLayout.TryGetStreamStride(staticBackendRow, source.StreamIndex, out byte stride))
        {
            return new StaticVertexDecoder(new VertexSource(
                source.StreamIndex,
                stride,
                source.ByteOffset,
                source.ComponentCount,
                source.RsxType));
        }

        if (source.IsUnavailableSourceTuple)
        {
            return new StaticVertexDecoder(new VertexSource(
                source.StreamIndex,
                0,
                source.ByteOffset,
                source.ComponentCount,
                source.RsxType));
        }

        return null;
    }

    private static MapRenderUvRoute BuildStaticModelUvRoute(byte texCoordSource)
    {
        int staticBackendRow = (int)StaticXSurfaceSourceFormat;
        if (!WorldVertexLayout.TryGetSource(
                staticBackendRow,
                texCoordSource,
                out WorldVertexSource source) ||
            !WorldVertexLayout.TryGetStreamStride(
                staticBackendRow,
                source.StreamIndex,
                out byte stride))
        {
            return MapRenderUvRoute.StaticModel(texCoordSource);
        }

        MapRenderUvBaseMode baseMode = source.StreamIndex == 1
            ? MapRenderUvBaseMode.Stream1ZeroBase
            : MapRenderUvBaseMode.Stream0LocalIndexOnly;
        return new MapRenderUvRoute(
            "static model tc0",
            StaticXSurfaceSourceFormat.ToString(),
            texCoordSource,
            source.StreamIndex,
            stride,
            source.ByteOffset,
            source.ComponentCount,
            source.RsxType,
            baseMode,
            0,
            1,
            1f,
            1f,
            0f,
            0f);
    }

}
