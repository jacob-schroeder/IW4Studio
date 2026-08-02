using System.Numerics;

using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.SceneBuilding;
using IW4.Render.OpenGl.StaticModels;
using IW4.Render.OpenGl.World;
using IW4.Render.World;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private StaticReceiverIdentityObjectGroup[]
        _staticReceiverSelectionGroups = [];

    private static readonly MapRenderWorldSurfacePageMembership[]
        ReceiverWorldPages =
        [
            MapRenderWorldSurfacePageMembership.PageZero,
            MapRenderWorldSurfacePageMembership.PageOne
        ];

    private static readonly MapRenderTechniqueVariantAllocation[]
        ReceiverAllocations =
        [
            MapRenderTechniqueVariantAllocation.Unshadowed,
            MapRenderTechniqueVariantAllocation.ShadowMapAllocated
        ];

    private static readonly MapRenderStaticModelReceiverPage[]
        ReceiverStaticPages =
        [
            MapRenderStaticModelReceiverPage.StaticModelRigidPage2,
            MapRenderStaticModelReceiverPage
                .StaticModelRigidNoSunShadowPage3
        ];

    private void InitializeWorldReceiverVariants(
        MapRenderScene scene,
        bool isolateWorldSurface)
    {
        _sceneTechniqueVariants = scene.TechniqueVariants;
        _sceneLightSelectorAsset =
            scene.WorldSource?.SceneLights.Source?.SelectorState;
        _cachedUnshadowedReceiverSelectorState = null;
        _cachedAllocatedReceiverSelectorState = null;
        _cachedAllocatedReceiverSunIndex = -1;
        _worldReceiverVariants = [];
        if (isolateWorldSurface ||
            scene.ReceiverVariants is not { } catalog ||
            scene.WorldSource is not { } source ||
            _sceneTechniqueVariants is null ||
            _sceneLightSelectorAsset is null)
        {
            return;
        }

        var result = new List<WorldReceiverVariantRuntime>(4);
        try
        {
            foreach (MapRenderWorldSurfacePageMembership page in
                     ReceiverWorldPages)
            foreach (MapRenderTechniqueVariantAllocation allocation in
                     ReceiverAllocations)
            {
                int channelIndex = result.Count;
                var key = new MapRenderWorldReceiverVariantKey(
                    page,
                    allocation);
                MapRenderTexturedBatch[] batches = catalog
                    .GetWorldBatches(page, allocation)
                    .ToArray();
                IReadOnlySet<AuthoredProgramGroupKey> authorized =
                    AuthorizeAtomicProgramGroups(
                        batches,
                        _ => true,
                        AuthoredProgramGroup,
                        PreflightAuthoredProgram);
                GlTexturedMesh[] resourceShells = batches
                    .Select(batch => CreateWorldTexturedResourceShell(
                        batch,
                        authorized,
                        allowGenericFallback: false))
                    .ToArray();
                // Resource shells contain only texture/program/state
                // resources. Geometry is uploaded exactly once into each
                // final arena below; there is no per-batch staging VBO/EBO.
                (GlTexturedMesh[] meshes,
                    GlMesh genericArena,
                    GlMesh[] translatedArenas) =
                    CreatePackedWorldGeometryArenas(
                        batches,
                        resourceShells);
                bool arenaOwnershipTransferred = false;
                try
                {

                var surfaceBatches =
                    new WorldSurfaceBatchRuntime?[meshes.Length];
                for (int batchIndex = 0;
                     batchIndex < meshes.Length;
                     batchIndex++)
                {
                    GlTexturedMesh mesh = meshes[batchIndex];
                    if (mesh.IndexCount == 0)
                        continue;

                    mesh = mesh with
                    {
                        WorldSurfaceIndex =
                            ResolveSingleWorldSurfaceIndex(
                                batches[batchIndex]),
                        WorldBounds = IncludeTexturedVertexBounds(
                            MapRenderBounds.Empty,
                            batches[batchIndex].Vertices)
                    };
                    bool allowsDecodedPerSurfaceFrustumCull =
                        AllowsDecodedPerSurfaceFrustumCull(mesh);
                    if (!MapRenderOpenGlWorldSurfaceSpanCatalog.TryCreate(
                            batches[batchIndex],
                            out MapRenderOpenGlWorldSurfaceSpan[] spans,
                            includeDecodedBounds:
                                allowsDecodedPerSurfaceFrustumCull))
                    {
                        // Exact receiver channels cannot use the base
                        // whole-batch fallback because it would draw surfaces
                        // owned by a different Event20 page.
                        meshes[batchIndex] = default;
                        continue;
                    }

                    meshes[batchIndex] = mesh;
                    surfaceBatches[batchIndex] = new(
                        spans,
                        allowsDecodedPerSurfaceFrustumCull);
                }

                // Preserve authored multipass atomicity after the final GL
                // and per-surface-span gates. A partially materialized group
                // is not a legal selector result.
                foreach (IGrouping<AuthoredProgramGroupKey, int> group in
                         Enumerable.Range(0, batches.Length)
                             .GroupBy(index =>
                                 AuthoredProgramGroup(batches[index])))
                {
                    if (group.All(index =>
                            meshes[index].IndexCount != 0 &&
                            surfaceBatches[index] is not null))
                    {
                        continue;
                    }
                    foreach (int index in group)
                    {
                        meshes[index] = default;
                        surfaceBatches[index] = null;
                    }
                }

                // Receiver channels own separate packed arenas. Assign exact
                // compatibility identities after their final atomic validity
                // gate so selected color and depth ranges can use the same
                // multi-draw path as base world geometry.
                AssignWorldMultiDrawBatchGroupIds(meshes);

                MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] groups =
                    BuildEditorTexturedDrawGroups(
                        batches,
                        meshes,
                        [],
                        [],
                        channelIndex);
                result.Add(new WorldReceiverVariantRuntime(
                    channelIndex,
                    key,
                    batches,
                    meshes,
                    surfaceBatches,
                    genericArena,
                    translatedArenas,
                    groups,
                    source.World.SurfaceCount));
                    arenaOwnershipTransferred = true;
                }
                finally
                {
                    if (!arenaOwnershipTransferred)
                    {
                        DeleteMesh(genericArena);
                        foreach (GlMesh translatedArena in translatedArenas)
                            DeleteMesh(translatedArena);
                    }
                }
            }
        }
        catch
        {
            foreach (WorldReceiverVariantRuntime channel in result)
                DeleteWorldReceiverVariant(channel);
            throw;
        }

        _worldReceiverVariants = result.ToArray();
    }

    private void DeleteWorldReceiverVariant(
        WorldReceiverVariantRuntime channel)
    {
        foreach (GlTexturedMesh mesh in channel.Meshes)
            DeleteTexturedMesh(mesh);
        DeleteMesh(channel.GenericArena);
        foreach (GlMesh translatedArena in channel.TranslatedArenas)
            DeleteMesh(translatedArena);
    }

    private MapRenderEditorDrawGroup<GlTexturedDrawCommand>[]
        BuildReceiverAwareEditorTexturedDrawGroups(
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] baseGroups)
    {
        ArgumentNullException.ThrowIfNull(baseGroups);
        int groupCount = baseGroups.Length;
        foreach (WorldReceiverVariantRuntime channel in
                 _worldReceiverVariants)
        {
            groupCount = checked(groupCount + channel.DrawGroups.Length);
        }
        foreach (StaticReceiverVariantRuntime channel in
                 _staticReceiverVariants)
        {
            groupCount = checked(groupCount + channel.DrawGroups.Length);
        }

        if (groupCount == baseGroups.Length)
            return baseGroups;

        var result = new MapRenderEditorDrawGroup<
            GlTexturedDrawCommand>[groupCount];
        int destination = 0;
        baseGroups.CopyTo(result, destination);
        destination += baseGroups.Length;
        foreach (WorldReceiverVariantRuntime channel in
                 _worldReceiverVariants)
        {
            channel.DrawGroups.CopyTo(result, destination);
            destination += channel.DrawGroups.Length;
        }
        foreach (StaticReceiverVariantRuntime channel in
                 _staticReceiverVariants)
        {
            channel.DrawGroups.CopyTo(result, destination);
            destination += channel.DrawGroups.Length;
        }
        return result;
    }

    private void InitializeStaticReceiverVariants(
        MapRenderScene scene,
        bool isolateWorldSurface)
    {
        _staticReceiverVariants = [];
        _staticReceiverSelectionGroups =
            BuildStaticReceiverSelectionGroups(
                _staticReceiverExpectedIdentities);
        _selectedStaticReceiverSurfaces.Clear();
        _selectedStaticReceiverOccurrences.Clear();
        if (isolateWorldSurface ||
            scene.ReceiverVariants is not { } catalog ||
            _sceneTechniqueVariants is null ||
            _sceneLightSelectorAsset is null)
        {
            return;
        }
        if (_progressiveStaticMaterializationEnabled)
        {
            InitializeProgressiveStaticReceiverVariants(scene);
            return;
        }

        var result = new List<StaticReceiverVariantRuntime>(4);
        try
        {
            foreach (MapRenderStaticModelReceiverPage page in
                     ReceiverStaticPages)
            foreach (MapRenderTechniqueVariantAllocation allocation in
                     ReceiverAllocations)
            {
                int channelIndex = result.Count;
                var key = new MapRenderStaticModelReceiverVariantKey(
                    page,
                    allocation);
                MapRenderInstancedTexturedBatch[] batches = catalog
                    .GetStaticModelBatches(page, allocation)
                    .ToArray();
                MapRenderOpenGlStaticResourceGroupPlan resourcePlan =
                    MapRenderOpenGlStaticResourceGroupPlan.Create(
                        batches,
                        requireReceiverIdentityClosure: true);
                IReadOnlySet<AuthoredProgramGroupKey> authorized =
                    AuthorizeAtomicProgramGroups(
                        batches,
                        _ => true,
                        AuthoredProgramGroup,
                        PreflightAuthoredProgram);
                GlTexturedMesh[] meshes = batches
                    .Select(batch => CreateInstancedTexturedMesh(
                        batch,
                        authorized,
                        allowGenericFallback: false))
                    .ToArray();

                for (int batchIndex = 0;
                     batchIndex < meshes.Length;
                     batchIndex++)
                {
                    GlTexturedMesh mesh = meshes[batchIndex];
                    if (mesh.IndexCount == 0 || mesh.InstanceBuffer == 0)
                        continue;
                    var instanceRuntime = new StaticInstanceBufferRuntime(
                        mesh,
                        batches[batchIndex].Instances,
                        batches[batchIndex].LodIndex,
                        isReceiverVariant: true);
                    RegisterStaticInstanceBufferRuntime(
                        mesh.InstanceBuffer,
                        instanceRuntime);
                }

                var surfaces = new Dictionary<
                    MapRenderStaticModelReceiverIdentity,
                    StaticReceiverSurfaceRuntime>();
                foreach (IGrouping<int, int> passGroup in
                         Enumerable.Range(0, batches.Length)
                             .GroupBy(index =>
                                 batches[index].EditorDrawGroupId))
                {
                    int[] passOrdinals = passGroup
                        .OrderBy(index => batches[index].Pass.PassIndex)
                        .ThenBy(index => index)
                        .ToArray();
                    bool groupReady = passGroup.Key >= 0 &&
                        passOrdinals.Length > 0 &&
                        passOrdinals.All(index =>
                            meshes[index].IndexCount != 0 &&
                            meshes[index].RsxProgram.Handle != 0 &&
                            meshes[index].InstanceBuffer != 0) &&
                        passOrdinals
                            .Select(index => batches[index].Instances.Count)
                            .Distinct()
                            .Count() == 1;
                    if (groupReady)
                    {
                        foreach (int index in passOrdinals)
                        {
                            if (meshes[index].EditorDepthPrepass is not null &&
                                meshes[index].DepthPrepassRsxProgram.Handle == 0)
                            {
                                groupReady = false;
                                break;
                            }
                        }
                    }

                    if (!groupReady)
                    {
                        foreach (int index in passOrdinals)
                            DeleteStaticReceiverMesh(ref meshes[index]);
                        continue;
                    }

                    int instanceCount =
                        batches[passOrdinals[0]].Instances.Count;
                    var groupSurfaces = new List<
                        (MapRenderStaticModelReceiverIdentity Identity,
                            StaticReceiverSurfaceRuntime Runtime)>(
                            instanceCount);
                    for (int instanceIndex = 0;
                         instanceIndex < instanceCount;
                         instanceIndex++)
                    {
                        var identity = new
                            MapRenderStaticModelReceiverIdentity(
                                batches[passOrdinals[0]]
                                    .Instances[instanceIndex],
                                batches[passOrdinals[0]].LodIndex);
                        if (passOrdinals.Any(index =>
                                new MapRenderStaticModelReceiverIdentity(
                                    batches[index].Instances[instanceIndex],
                                    batches[index].LodIndex) != identity))
                        {
                            groupReady = false;
                            break;
                        }

                        var occurrences =
                            new StaticReceiverPassOccurrence[
                                passOrdinals.Length];
                        for (int passIndex = 0;
                             passIndex < passOrdinals.Length;
                             passIndex++)
                        {
                            uint instanceBuffer = meshes[
                                passOrdinals[passIndex]].InstanceBuffer;
                            if (!_staticInstanceBuffers.TryGetValue(
                                    instanceBuffer,
                                    out StaticInstanceBufferRuntime?
                                        occurrenceRuntime))
                            {
                                groupReady = false;
                                break;
                            }
                            occurrences[passIndex] = new(
                                occurrenceRuntime,
                                instanceIndex);
                        }
                        if (!groupReady)
                            break;
                        groupSurfaces.Add((
                            identity,
                            new StaticReceiverSurfaceRuntime(
                                identity,
                                batches[passOrdinals[0]]
                                    .Pass.TechniqueSlot,
                                occurrences)));
                    }

                    if (!groupReady || groupSurfaces.Any(candidate =>
                            surfaces.ContainsKey(candidate.Identity)))
                    {
                        foreach (int index in passOrdinals)
                            DeleteStaticReceiverMesh(ref meshes[index]);
                        continue;
                    }
                    foreach (var candidate in groupSurfaces)
                        surfaces.Add(candidate.Identity, candidate.Runtime);
                }

                MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] groups =
                    BuildEditorTexturedDrawGroups(
                        [],
                        [],
                        batches,
                        meshes);
                foreach (MapRenderEditorDrawGroup<GlTexturedDrawCommand>
                         group in groups)
                foreach (GlTexturedDrawCommand command in
                         group.AuthoredPasses)
                {
                    if (command.Mesh.InstanceBuffer == 0 ||
                        !_staticInstanceBuffers.TryGetValue(
                            command.Mesh.InstanceBuffer,
                            out StaticInstanceBufferRuntime? runtime))
                    {
                        continue;
                    }
                    if (command.InstanceIndex.HasValue)
                        runtime.HasIsolatedDraw = true;
                    else
                        runtime.HasWholeBatchDraw = true;
                }
                result.Add(new StaticReceiverVariantRuntime(
                    channelIndex,
                    key,
                    batches,
                    meshes,
                    groups,
                    surfaces,
                    resourcePlan));
                StaticReceiverVariantRuntime runtimeChannel =
                    result[^1];
                Array.Fill(
                    runtimeChannel.ResolvedGroups,
                    true);
                StaticResourceSourceBatchCount = checked(
                    StaticResourceSourceBatchCount + batches.Length);
                StaticResourceResolvedBatchCount = checked(
                    StaticResourceResolvedBatchCount +
                    batches.Length);
                foreach (
                    MapRenderOpenGlStaticResourceGroupPlan.Group group in
                    Enumerable.Range(0, resourcePlan.GroupCount)
                        .Select(index => resourcePlan[index]))
                {
                    bool executable =
                        group.ReceiverStructureReady &&
                        group.BatchOrdinals.All(index =>
                            meshes[index].IndexCount != 0 &&
                            meshes[index].RsxProgram.Handle != 0 &&
                            meshes[index].InstanceBuffer != 0) &&
                        group.ReceiverIdentities.All(
                            surfaces.ContainsKey);
                    if (executable)
                    {
                        runtimeChannel.ExecutableGroups[
                            group.GroupIndex] = true;
                        StaticResourceMaterializedBatchCount =
                            checked(
                                StaticResourceMaterializedBatchCount +
                                group.BatchOrdinals.Length);
                    }
                    else
                    {
                        StaticResourceRejectedBatchCount = checked(
                            StaticResourceRejectedBatchCount +
                            group.BatchOrdinals.Length);
                    }
                }
            }
        }
        catch
        {
            foreach (StaticReceiverVariantRuntime channel in result)
                DeleteStaticReceiverVariant(channel);
            throw;
        }

        _staticReceiverVariants = result.ToArray();
        if (_staticReceiverVariants.Length != 0)
            StaticResourceMaterializationWaveCount++;
    }

    private void DeleteStaticReceiverMesh(ref GlTexturedMesh mesh)
    {
        if (mesh.InstanceBuffer != 0)
            RemoveStaticInstanceBufferRuntime(mesh.InstanceBuffer);
        DeleteTexturedMesh(mesh);
        mesh = default;
    }

    private void DeleteStaticReceiverVariant(
        IExactStaticVariantRuntime channel)
    {
        for (int index = 0; index < channel.Meshes.Length; index++)
        {
            GlTexturedMesh mesh = channel.Meshes[index];
            if (mesh.InstanceBuffer != 0)
                RemoveStaticInstanceBufferRuntime(mesh.InstanceBuffer);
            DeleteTexturedMesh(mesh);
        }
    }

    private void PrepareWorldReceiverVariantSelection(
        MapRenderWorldDpvsThreeViewFrame frame,
        MapRenderSunShadowAtlasReadyState? atlasReady,
        bool sunShadowPreflight = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (atlasReady is not null && sunShadowPreflight)
        {
            throw new ArgumentException(
                "A receiver selection cannot be both preflight-only and atlas-ready.",
                nameof(sunShadowPreflight));
        }
        _currentWorldReceiverTechniqueSelector = null;
        _baseWorldReceiverVisibilityActive = false;
        AdvanceReceiverSelectionGeneration();
        _selectedStaticReceiverSurfaces.Clear();
        _selectedStaticReceiverOccurrences.Clear();
        foreach (WorldReceiverVariantRuntime channel in
                 _worldReceiverVariants)
        {
            channel.BeginSelection();
        }

        if (_sceneTechniqueVariants is not { } techniqueCatalog ||
            _sceneLightSelectorAsset is not { } sceneLights ||
            ((atlasReady is not null || sunShadowPreflight) &&
             _selectedDirectionalSunPrimaryLightIndex is null))
        {
            if (atlasReady is not null || sunShadowPreflight)
            {
                throw new InvalidOperationException(
                    "Exact allocated receiver coverage lacks the immutable draw-method/light-selector inputs.");
            }
            return;
        }

        // With no exact channels, an unshadowed frame simply retains the base
        // preview. A completed atlas still has to walk the selector once so an
        // allocated surface cannot escape the fail-closed coverage gate.
        if (_worldReceiverVariants.Length == 0 &&
            _staticReceiverVariants.Length == 0 &&
            atlasReady is null &&
            !sunShadowPreflight)
        {
            return;
        }

        MapRenderSceneLightSelectorFrameState selectorFrame =
            ResolveReceiverSelectorFrame(
                frame,
                sceneLights,
                atlasReady,
                sunShadowPreflight);

        var context = new MapRenderTechniqueSelectionContext(
            techniqueCatalog.DrawMethod,
            selectorFrame);
        var selector = new MapRenderFrameTechniqueSelector(frame, context);
        _currentWorldReceiverTechniqueSelector = selector;
        if (_worldReceiverVariants.Length != 0)
        {
            ReadOnlySpan<uint> cameraSurfaceBits =
                frame.Camera.SurfaceBitSpan;
            if (_baseWorldReceiverVisibilityWords.Length !=
                cameraSurfaceBits.Length)
            {
                _baseWorldReceiverVisibilityWords =
                    new uint[cameraSurfaceBits.Length];
            }
            cameraSurfaceBits.CopyTo(
                _baseWorldReceiverVisibilityWords);
            _baseWorldReceiverVisibilityActive = true;
        }

        int missingAtlasWorld = 0;
        ReadOnlySpan<uint> visibleCameraSurfaceWords =
            frame.Camera.SurfaceBitSpan;
        for (int wordIndex = 0;
             wordIndex < visibleCameraSurfaceWords.Length;
             wordIndex++)
        {
            uint pending = visibleCameraSurfaceWords[wordIndex];
            while (pending != 0)
            {
                int bitInWord = BitOperations.LeadingZeroCount(pending);
                int surfaceIndex = checked(
                    wordIndex * 32 + bitInWord);
                pending &= ~(0x8000_0000u >> bitInWord);
                if (surfaceIndex >= frame.Camera.SurfaceCount)
                    break;

                MapRenderTechniqueVariantSet? retainedVariants =
                    techniqueCatalog.WorldSurfaces[surfaceIndex];
                if (retainedVariants is null ||
                    !selector.TryResolveWorldSurface(
                        surfaceIndex,
                        retainedVariants.PrimaryLightIndex,
                        out MapRenderFrameTechniqueSelectionValue
                            selection))
                {
                    continue;
                }

                MapRenderTechniqueVariantAllocation allocation =
                    selection.ShadowMapAllocated
                        ? MapRenderTechniqueVariantAllocation
                            .ShadowMapAllocated
                        : MapRenderTechniqueVariantAllocation.Unshadowed;
                if (!techniqueCatalog.RequiresWorldReceiverVariant(
                        surfaceIndex,
                        selection.PageMembership,
                        allocation))
                {
                    // The selected native draw-method axis owns no authored
                    // receiver camera-color phase. It is not a backend miss:
                    // another phase (for example GfxSky) or the retained
                    // material preview owns this surface.
                    continue;
                }
                if (!TryGetWorldReceiverVariant(
                        selection.PageMembership,
                        allocation,
                        out WorldReceiverVariantRuntime channel) ||
                    !channel.CanExecuteSurface(surfaceIndex))
                {
                    // Event20 page one is TrianglesNoSunShadow. Its missing
                    // alternate camera-color program remains a visible base
                    // fallback gap, but it cannot invalidate an atlas that
                    // the native page never samples.
                    if (selection.ShadowMapAllocated &&
                        selection.PageMembership ==
                            MapRenderWorldSurfacePageMembership.PageZero)
                    {
                        missingAtlasWorld++;
                    }
                    // Keep the existing preview submission visible until the
                    // missing exact implementation is supplied. Never turn an
                    // implementation gap into disappearing level geometry.
                    continue;
                }

                ClearMsbFirstBit(
                    _baseWorldReceiverVisibilityWords,
                    surfaceIndex);
                channel.SelectSurface(surfaceIndex);
            }
        }

        int missingAtlasStatic = 0;
        foreach (StaticReceiverIdentityObjectGroup objectGroup in
                 _staticReceiverSelectionGroups)
        {
            if (!IsStaticObjectVisible(objectGroup.ObjectIndex))
                continue;

            ReadOnlySpan<MapRenderStaticModelReceiverIdentity>
                candidateIdentities =
                    ResolveStaticReceiverSelectionCandidates(
                        objectGroup);
            foreach (MapRenderStaticModelReceiverIdentity identity in
                     candidateIdentities)
            {
                if ((uint)identity.ObjectIndex >=
                        (uint)techniqueCatalog
                            .StaticModelDrawInstances.Count ||
                    techniqueCatalog.StaticModelDrawInstances[
                        identity.ObjectIndex] is null ||
                    !selector.TryResolveStaticModelSurface(
                        identity,
                        out MapRenderStaticModelFrameTechniqueSelectionValue
                            selection))
                {
                    continue;
                }

                MapRenderTechniqueVariantAllocation allocation =
                    selection.ShadowMapAllocated
                        ? MapRenderTechniqueVariantAllocation
                            .ShadowMapAllocated
                        : MapRenderTechniqueVariantAllocation.Unshadowed;
                if (!IsProgressiveStaticIdentityRequired(identity))
                {
                    // This exact receiver resource remains deferred only when
                    // the current camera/LOD closure excludes its instance
                    // from both the color and depth queues. The base preview
                    // selection is likewise compacted out, so no visible
                    // fallback is lost.
                    continue;
                }
                if (!TryGetStaticReceiverVariant(
                        selection.Page,
                        allocation,
                        out StaticReceiverVariantRuntime channel) ||
                    !channel.Surfaces.TryGetValue(
                        identity,
                        out StaticReceiverSurfaceRuntime? surface) ||
                    selection.TechniqueSlot != surface.TechniqueSlot)
                {
                    // Native page three is StaticModelRigidNoSunShadow. Keep
                    // its base preview visible when the exact alternate color
                    // program is unavailable, but do not flash every real
                    // receiver off by rejecting an atlas page three cannot
                    // consume.
                    if (selection.ShadowMapAllocated &&
                        selection.Page ==
                            MapRenderStaticModelReceiverPage
                                .StaticModelRigidPage2)
                    {
                        missingAtlasStatic++;
                    }
                    continue;
                }

                bool allPassesAvailable = true;
                foreach (StaticReceiverPassOccurrence pass in
                         surface.Passes)
                {
                    if (!pass.Runtime.IsReceiverVariant ||
                        (uint)pass.InstanceIndex >=
                        (uint)pass.Runtime
                            .ReceiverSelectionGenerations.Length)
                    {
                        allPassesAvailable = false;
                        break;
                    }
                }
                if (!allPassesAvailable)
                {
                    if (selection.ShadowMapAllocated &&
                        selection.Page ==
                            MapRenderStaticModelReceiverPage
                                .StaticModelRigidPage2)
                    {
                        missingAtlasStatic++;
                    }
                    continue;
                }

                foreach (StaticReceiverPassOccurrence pass in
                         surface.Passes)
                {
                    pass.Runtime.SelectReceiverInstance(
                        pass.InstanceIndex,
                        _receiverSelectionGeneration);
                    _selectedStaticReceiverOccurrences.Add(
                        (pass.Runtime, pass.InstanceIndex));
                }
                _selectedStaticReceiverSurfaces.Add(identity);
            }
        }

        if (missingAtlasWorld != 0 || missingAtlasStatic != 0)
        {
            // The walk above deliberately performs selection and validation in
            // one pass. Roll back its partial publication before failing so the
            // caller can rebuild the unshadowed selection against the same
            // three-view frame without observing stale exact-channel bits.
            ResetWorldReceiverVariantSelection();
            throw new InvalidOperationException(
                $"The same-revision atlas has {missingAtlasWorld} world and {missingAtlasStatic} static-model atlas-consuming receiver surface(s) without a complete exact authored program group.");
        }
    }

    private void AuthorizePreflightedWorldReceiverVariantSelection(
        MapRenderWorldDpvsThreeViewFrame frame,
        MapRenderSunShadowAtlasReadyState atlasReady)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(atlasReady);
        if (!ReferenceEquals(atlasReady.Frame, frame))
        {
            throw new ArgumentException(
                "Receiver authorization requires the exact preflighted three-view frame.",
                nameof(atlasReady));
        }
        if (_currentWorldReceiverTechniqueSelector is null ||
            _sceneTechniqueVariants is not { } techniqueCatalog ||
            _sceneLightSelectorAsset is not { } sceneLights)
        {
            throw new InvalidOperationException(
                "The completed atlas has no successful exact receiver preflight to authorize.");
        }

        MapRenderSceneLightSelectorFrameState selectorFrame =
            ResolveReceiverSelectorFrame(
                frame,
                sceneLights,
                atlasReady);
        _currentWorldReceiverTechniqueSelector =
            new MapRenderFrameTechniqueSelector(
                frame,
                new MapRenderTechniqueSelectionContext(
                    techniqueCatalog.DrawMethod,
                    selectorFrame));
    }

    private MapRenderSceneLightSelectorFrameState
        ResolveReceiverSelectorFrame(
            MapRenderWorldDpvsThreeViewFrame frame,
            MapRenderSceneLightSelectorAssetState sceneLights,
            MapRenderSunShadowAtlasReadyState? atlasReady,
            bool sunShadowPreflight = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(sceneLights);
        if ((atlasReady is not null || sunShadowPreflight) &&
            _selectedDirectionalSunPrimaryLightIndex is { } sunIndex)
        {
            if (_cachedAllocatedReceiverSelectorState is not { } selectors ||
                _cachedAllocatedReceiverSunIndex != sunIndex)
            {
                var runtimeShadowBits =
                    new uint[(sceneLights.SceneLightCount + 31) / 32];
                runtimeShadowBits[sunIndex >> 5] |=
                    1u << (sunIndex & 31);
                MapRenderSceneLightSelectorFrameState created =
                    atlasReady is not null
                        ? sceneLights
                            .CreateSunShadowReadyNormalViewSelectorFrame(
                                atlasReady,
                                runtimeShadowBits)
                        : sceneLights
                            .CreateSunShadowPreflightNormalViewSelectorFrame(
                                frame.Revision,
                                runtimeShadowBits);
                _cachedAllocatedReceiverSelectorState =
                    created.Selectors;
                _cachedAllocatedReceiverSunIndex = sunIndex;
                return created;
            }

            return new MapRenderSceneLightSelectorFrameState(
                frame.Revision,
                selectors,
                atlasReady);
        }

        if (_cachedUnshadowedReceiverSelectorState is not
            { } unshadowedSelectors)
        {
            MapRenderSceneLightSelectorFrameState created =
                sceneLights.CreateUnshadowedNormalViewSelectorFrame(
                    frame.Revision);
            _cachedUnshadowedReceiverSelectorState =
                created.Selectors;
            return created;
        }

        return new MapRenderSceneLightSelectorFrameState(
            frame.Revision,
            unshadowedSelectors,
            sunShadowAtlasReady: null);
    }

    private bool TryGetWorldReceiverVariant(
        MapRenderWorldSurfacePageMembership page,
        MapRenderTechniqueVariantAllocation allocation,
        out WorldReceiverVariantRuntime channel)
    {
        int pageIndex = page switch
        {
            MapRenderWorldSurfacePageMembership.PageZero => 0,
            MapRenderWorldSurfacePageMembership.PageOne => 1,
            _ => -1
        };
        int allocationIndex = allocation switch
        {
            MapRenderTechniqueVariantAllocation.Unshadowed => 0,
            MapRenderTechniqueVariantAllocation.ShadowMapAllocated => 1,
            _ => -1
        };
        int channelIndex = pageIndex < 0 || allocationIndex < 0
            ? -1
            : pageIndex * ReceiverAllocations.Length + allocationIndex;
        if ((uint)channelIndex < (uint)_worldReceiverVariants.Length)
        {
            WorldReceiverVariantRuntime candidate =
                _worldReceiverVariants[channelIndex];
            if (candidate.Key.Page == page &&
                candidate.Key.Allocation == allocation)
            {
                channel = candidate;
                return true;
            }
        }

        channel = null!;
        return false;
    }

    private bool TryGetStaticReceiverVariant(
        MapRenderStaticModelReceiverPage page,
        MapRenderTechniqueVariantAllocation allocation,
        out StaticReceiverVariantRuntime channel)
    {
        int pageIndex = page switch
        {
            MapRenderStaticModelReceiverPage.StaticModelRigidPage2 => 0,
            MapRenderStaticModelReceiverPage
                .StaticModelRigidNoSunShadowPage3 => 1,
            _ => -1
        };
        int allocationIndex = allocation switch
        {
            MapRenderTechniqueVariantAllocation.Unshadowed => 0,
            MapRenderTechniqueVariantAllocation.ShadowMapAllocated => 1,
            _ => -1
        };
        int channelIndex = pageIndex < 0 || allocationIndex < 0
            ? -1
            : pageIndex * ReceiverAllocations.Length + allocationIndex;
        if ((uint)channelIndex < (uint)_staticReceiverVariants.Length)
        {
            StaticReceiverVariantRuntime candidate =
                _staticReceiverVariants[channelIndex];
            if (candidate.Key.Page == page &&
                candidate.Key.Allocation == allocation)
            {
                channel = candidate;
                return true;
            }
        }

        channel = null!;
        return false;
    }

    private void AdvanceReceiverSelectionGeneration()
    {
        _receiverSelectionGeneration = unchecked(
            _receiverSelectionGeneration + 1);
        if (_receiverSelectionGeneration != 0)
            return;

        foreach (StaticInstanceBufferRuntime runtime in
                 _staticInstanceBuffers.Values)
        {
            if (runtime.IsReceiverVariant)
                Array.Clear(runtime.ReceiverSelectionGenerations);
        }
        _receiverSelectionGeneration = 1;
    }

    private void ResetWorldReceiverVariantSelection()
    {
        _currentWorldReceiverTechniqueSelector = null;
        _baseWorldReceiverVisibilityActive = false;
        AdvanceReceiverSelectionGeneration();
        _selectedStaticReceiverSurfaces.Clear();
        _selectedStaticReceiverOccurrences.Clear();
        foreach (WorldReceiverVariantRuntime channel in
                 _worldReceiverVariants)
        {
            channel.BeginSelection();
        }
    }

    private static void ClearMsbFirstBit(uint[] words, int index) =>
        words[index >> 5] &= ~(0x8000_0000u >> (index & 31));

    private ReadOnlySpan<MapRenderStaticModelReceiverIdentity>
        ResolveStaticReceiverSelectionCandidates(
            StaticReceiverIdentityObjectGroup objectGroup)
    {
        if (!_usesDynamicStaticLods)
            return objectGroup.AllIdentities;

        int objectIndex = objectGroup.ObjectIndex;
        if ((uint)objectIndex >=
            (uint)_selectedStaticLodByObject.Length)
        {
            return [];
        }

        int selectedLod = _selectedStaticLodByObject[objectIndex];
        if (selectedLod == UnknownStaticLodIndex &&
            !_staticSchedulingByObjectIndex.ContainsKey(objectIndex))
        {
            // Unscheduled fallback identities retain their historical
            // all-LOD visibility when no prepared LOD is available.
            return objectGroup.AllIdentities;
        }

        return objectGroup.TryGetLod(
            selectedLod,
            out MapRenderStaticModelReceiverIdentity[] identities)
                ? identities
                : [];
    }

    private static StaticReceiverIdentityObjectGroup[]
        BuildStaticReceiverSelectionGroups(
            IReadOnlyList<MapRenderStaticModelReceiverIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        return identities
            .GroupBy(identity => identity.ObjectIndex)
            .Select(group => new StaticReceiverIdentityObjectGroup(
                group.Key,
                group.ToArray(),
                group.GroupBy(identity => identity.LodIndex)
                    .Select(lodGroup =>
                        new StaticReceiverIdentityLodGroup(
                            lodGroup.Key,
                            lodGroup.ToArray()))
                    .ToArray()))
            .ToArray();
    }

    private sealed class StaticReceiverIdentityObjectGroup
    {
        public StaticReceiverIdentityObjectGroup(
            int objectIndex,
            MapRenderStaticModelReceiverIdentity[] allIdentities,
            StaticReceiverIdentityLodGroup[] lodGroups)
        {
            ObjectIndex = objectIndex;
            AllIdentities = allIdentities;
            LodGroups = lodGroups;
        }

        public int ObjectIndex { get; }

        public MapRenderStaticModelReceiverIdentity[] AllIdentities
            { get; }

        private StaticReceiverIdentityLodGroup[] LodGroups { get; }

        public bool TryGetLod(
            int lodIndex,
            out MapRenderStaticModelReceiverIdentity[] identities)
        {
            foreach (StaticReceiverIdentityLodGroup group in LodGroups)
            {
                if (group.LodIndex != lodIndex)
                    continue;

                identities = group.Identities;
                return true;
            }

            identities = null!;
            return false;
        }
    }

    private sealed record StaticReceiverIdentityLodGroup(
        int LodIndex,
        MapRenderStaticModelReceiverIdentity[] Identities);
}
