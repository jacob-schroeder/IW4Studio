using Silk.NET.OpenGL;

using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Geometry.Shadows;
using IW4.Render.Textures;
using Texture = IW4.Render.Textures.Texture;
using TextureTarget = Silk.NET.OpenGL.TextureTarget;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private void EnsureTextureResidentForCriticalDraw(uint handle)
    {
        if (handle == 0 ||
            !_textureHandles.TryGetEntry(
                handle,
                out MapRenderOpenGlTextureResidencyEntry entry))
        {
            return;
        }

        _criticalTextureHandles.Add(handle);
        entry.MarkVisible(_activeRenderFrameIndex);
        if (entry.IsResident)
            return;

        UploadTextureStorage(entry);
        entry.MarkResident(_activeRenderFrameIndex);
        _frameTextureUploadCount++;
        _frameTextureUploadBytes = checked(
            _frameTextureUploadBytes +
            entry.EstimatedResidentBytes);
        if (entry.UsesDirectAuthoredBcUpload)
        {
            _frameAuthoredBcUploadBytes = checked(
                _frameAuthoredBcUploadBytes +
                entry.EstimatedResidentBytes);
        }
    }

    private void AccountSceneTexturePayloads(
        MapRenderScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        foreach (MapRenderSky sky in scene.Skies)
            AccountTexturePayload(sky.Texture);
        AccountWorldBatches(scene.TexturedBatches);
        AccountStaticBatches(scene.InstancedTexturedBatches);
        AccountStaticBatches(scene.StaticModelLodTexturedBatches);
        AccountStaticBatches(
            scene.ExactNormalCameraStaticModelTexturedBatches);
        AccountWorldBatches(
            scene.ShadowAllocatedWorldTexturedBatches);
        AccountStaticBatches(
            scene.ShadowAllocatedStaticModelTexturedBatches);
        if (scene.ReceiverVariants is { } receiverVariants)
        {
            foreach (IReadOnlyList<MapRenderTexturedBatch> batches in
                     receiverVariants.World.Values)
            {
                AccountWorldBatches(batches);
            }
            foreach (IReadOnlyList<MapRenderInstancedTexturedBatch> batches in
                     receiverVariants.StaticModels.Values)
            {
                AccountStaticBatches(batches);
            }
        }
        foreach (MapRenderWorldSunShadowCasterBatch caster in
                 scene.SunShadowWorldCasterBatches)
        {
            AccountTexturePayload(caster.CutoutTexture);
        }
        foreach (MapRenderStaticSunShadowCasterBatch caster in
                 scene.SunShadowStaticCasterBatches)
        {
            AccountTexturePayload(caster.CutoutTexture);
        }
    }

    private void AccountWorldBatches(
        IEnumerable<MapRenderTexturedBatch> batches)
    {
        foreach (MapRenderTexturedBatch batch in batches)
        {
            AccountTexturePayload(batch.Texture);
            AccountTexturePayload(batch.LightmapTexture);
            AccountBatchBindings(
                batch.ColorLayers,
                batch.MaterialSamplers.Select(binding => binding.Binding));
        }
    }

    private void AccountStaticBatches(
        IEnumerable<MapRenderInstancedTexturedBatch> batches)
    {
        foreach (MapRenderInstancedTexturedBatch batch in batches)
        {
            AccountTexturePayload(batch.Texture);
            AccountBatchBindings(
                batch.ColorLayers,
                batch.MaterialSamplers.Select(binding => binding.Binding));
        }
    }

    private void AccountBatchBindings(
        IEnumerable<IW4.Render.Materials.MaterialColorLayer>
            colorLayers,
        IEnumerable<IW4.Render.Materials.MaterialSamplerBinding>
            materialSamplers)
    {
        foreach (IW4.Render.Materials.MaterialColorLayer layer in
                 colorLayers)
        {
            AccountTexturePayload(layer.Texture);
        }
        foreach (IW4.Render.Materials.MaterialSamplerBinding binding in
                 materialSamplers)
        {
            AccountTexturePayload(binding.Texture);
        }
    }

    private void AccountTexturePayload(
        Texture? texture)
    {
        if (texture is null ||
            !_texturePayloadsAccounted.Add(texture))
        {
            return;
        }

        texture.VisitDecodedFallbackPayloads(
            payload =>
            {
                if (_observedDecodedTexturePayloads.TryGetValue(
                        payload,
                        out _))
                {
                    return;
                }
                _observedDecodedTexturePayloads.Add(
                    payload,
                    TexturePayloadMarker);
                _textureDecodedFallbackBytesObserved = checked(
                    _textureDecodedFallbackBytesObserved +
                    payload.Length);
            });
        if (!OpenGlAuthoredBcUploadPlan.TryCreate(
                texture,
                out OpenGlAuthoredBcUploadPlan plan))
        {
            return;
        }

        foreach (TextureAuthoredSubresource subresource in
                 plan.Subresources)
        {
            byte[] payload = subresource.SharedPayload;
            if (_observedAuthoredTexturePayloads.TryGetValue(
                    payload,
                    out _))
            {
                continue;
            }
            _observedAuthoredTexturePayloads.Add(
                payload,
                TexturePayloadMarker);
            _textureAuthoredBcSourceBytes = checked(
                _textureAuthoredBcSourceBytes +
                payload.Length);
        }
    }

    /// <summary>
    /// Admits only texture storage referenced by this frame's visible draw
    /// groups. Stable texture objects retain a complete opaque fallback while
    /// nonvisible full-resolution storage ages out under the byte budget.
    /// </summary>
    private void PrepareTextureResidencyForVisibleDraws(
        IReadOnlyList<
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>> groups,
        IReadOnlyList<
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>> depthGroups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(depthGroups);
        if (_activeRenderFrameIndex < 0)
        {
            throw new InvalidOperationException(
                "Texture residency can only be prepared inside an active frame.");
        }

        _visibleTextureHandles.Clear();
        _textureAdmissionScratch.Clear();

        foreach (uint handle in _criticalTextureHandles)
            RequireTextureForCurrentFrame(handle);
        foreach (GlSkyMesh sky in _skies)
            RequireTextureForCurrentFrame(sky.Texture);

        // A translated authored pass has no semantically valid placeholder
        // texture. Admit its complete static sampler set before the general
        // visible set while retaining the same frame upload budget.
        RequireTranslatedAuthoredMaterialSamplersFirst(groups);
        RequireGroupTextures(groups);
        RequireGroupTextures(depthGroups);

        void RequireGroupTextures(
            IReadOnlyList<
                MapRenderEditorDrawGroup<GlTexturedDrawCommand>>
                requiredGroups)
        {
            for (int groupIndex = 0;
                 groupIndex < requiredGroups.Count;
                 groupIndex++)
            {
                MapRenderEditorDrawGroup<GlTexturedDrawCommand> group =
                    requiredGroups[groupIndex];
                if (!IsTexturedDrawGroupVisibleForFrame(group))
                    continue;

                IReadOnlyList<GlTexturedDrawCommand> commands =
                    group.AuthoredPasses;
                for (int commandIndex = 0;
                     commandIndex < commands.Count;
                     commandIndex++)
                {
                    RequireMeshTexturesForCurrentFrame(
                        commands[commandIndex].Mesh);
                }
            }
        }

        long residencyBudget = Math.Max(
            0,
            TextureResidencyBudgetBytes);
        long uploadBudget = Math.Max(
            0,
            TextureUploadBudgetBytesPerFrame);
        int graceFrames = Math.Max(
            0,
            TextureEvictionGraceFrames);
        long residentBytes = _textureHandles.ResidentBytes;
        long requestedBytes = 0;
        foreach (MapRenderOpenGlTextureResidencyEntry entry in
                 _textureAdmissionScratch)
        {
            requestedBytes = requestedBytes >
                long.MaxValue - entry.EstimatedResidentBytes
                    ? long.MaxValue
                    : requestedBytes +
                        entry.EstimatedResidentBytes;
        }
        if (residentBytes > residencyBudget ||
            requestedBytes >
                Math.Max(0, residencyBudget - residentBytes))
        {
            MapRenderOpenGlTextureResidencyPolicy
                .CollectEvictionCandidates(
                    _textureHandles.Entries,
                    _visibleTextureHandles,
                    _activeRenderFrameIndex,
                    graceFrames,
                    _textureEvictionScratch);
        }
        else
        {
            _textureEvictionScratch.Clear();
        }
        int evictionCandidateIndex = 0;

        foreach (MapRenderOpenGlTextureResidencyEntry entry in
                 _textureAdmissionScratch)
        {
            if (entry.IsResident)
                continue;
            if (entry.EstimatedResidentBytes > residencyBudget ||
                uploadBudget == 0)
            {
                _frameTextureDeferredCount++;
                continue;
            }

            while (residentBytes + entry.EstimatedResidentBytes >
                   residencyBudget &&
                   evictionCandidateIndex <
                       _textureEvictionScratch.Count)
            {
                MapRenderOpenGlTextureResidencyEntry eviction =
                    _textureEvictionScratch[
                        evictionCandidateIndex++];
                EvictTextureStorage(eviction);
                residentBytes = checked(
                    residentBytes -
                    eviction.EstimatedResidentBytes);
            }
            if (residentBytes + entry.EstimatedResidentBytes >
                residencyBudget)
            {
                _frameTextureDeferredCount++;
                continue;
            }

            // Permit one individually larger upload to cross the per-frame
            // transfer budget. Otherwise that visible texture could starve
            // forever despite fitting the residency budget.
            if (_frameTextureUploadCount != 0 &&
                _frameTextureUploadBytes +
                    entry.EstimatedResidentBytes >
                uploadBudget)
            {
                _frameTextureDeferredCount++;
                continue;
            }

            UploadTextureStorage(entry);
            entry.MarkResident(_activeRenderFrameIndex);
            residentBytes = checked(
                residentBytes + entry.EstimatedResidentBytes);
            _frameTextureUploadCount++;
            _frameTextureUploadBytes = checked(
                _frameTextureUploadBytes +
                entry.EstimatedResidentBytes);
            if (entry.UsesDirectAuthoredBcUpload)
            {
                _frameAuthoredBcUploadBytes = checked(
                    _frameAuthoredBcUploadBytes +
                    entry.EstimatedResidentBytes);
            }
        }

        while (residentBytes > residencyBudget &&
               evictionCandidateIndex <
                   _textureEvictionScratch.Count)
        {
            MapRenderOpenGlTextureResidencyEntry eviction =
                _textureEvictionScratch[evictionCandidateIndex++];
            EvictTextureStorage(eviction);
            residentBytes = checked(
                residentBytes -
                eviction.EstimatedResidentBytes);
        }
    }

    private void RequireTranslatedAuthoredMaterialSamplersFirst(
        IReadOnlyList<
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>> groups)
    {
        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++)
        {
            MapRenderEditorDrawGroup<GlTexturedDrawCommand> group =
                groups[groupIndex];
            if (!IsTexturedDrawGroupVisibleForFrame(group))
            {
                continue;
            }

            IReadOnlyList<GlTexturedDrawCommand> commands =
                group.AuthoredPasses;
            for (int commandIndex = 0;
                 commandIndex < commands.Count;
                 commandIndex++)
            {
                GlTexturedMesh mesh = commands[commandIndex].Mesh;
                if (mesh.RsxProgram.Handle == 0)
                    continue;
                foreach (GlRsxSamplerBinding binding in
                         mesh.RsxSamplerBindings)
                {
                    RequireTextureForCurrentFrame(binding.Texture);
                }
            }
        }
    }

    private bool IsTexturedDrawGroupReadyForColorExecution(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group) =>
        AreTranslatedAuthoredMaterialSamplersResident(group);

    private bool AreTranslatedAuthoredMaterialSamplersResident(
        MapRenderEditorDrawGroup<GlTexturedDrawCommand> group)
    {
        IReadOnlyList<GlTexturedDrawCommand> commands =
            group.AuthoredPasses;
        for (int commandIndex = 0;
             commandIndex < commands.Count;
             commandIndex++)
        {
            GlTexturedMesh mesh = commands[commandIndex].Mesh;
            if (mesh.RsxProgram.Handle == 0)
                continue;

            IReadOnlyList<ShaderRuntimeSamplerRequirement>
                runtimeRequirements =
                    mesh.ShaderExecution?.RuntimeSamplerRequirements ?? [];
            foreach (int destination in
                     mesh.RsxProgram.SamplerDestinations)
            {
                if (runtimeRequirements.Any(requirement =>
                        requirement.Destination == destination))
                {
                    continue;
                }

                GlRsxSamplerBinding? requiredBinding = null;
                for (int bindingIndex = 0;
                     bindingIndex < mesh.RsxSamplerBindings.Length;
                     bindingIndex++)
                {
                    GlRsxSamplerBinding candidate =
                        mesh.RsxSamplerBindings[bindingIndex];
                    if (candidate.Destination == destination)
                    {
                        requiredBinding = candidate;
                        break;
                    }
                }
                if (requiredBinding is not { } binding)
                    return false;
                if (!_textureHandles.TryGetEntry(
                        binding.Texture,
                        out MapRenderOpenGlTextureResidencyEntry entry) ||
                    !entry.IsResident)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private void RequireMeshTexturesForCurrentFrame(
        GlTexturedMesh mesh)
    {
        foreach (uint texture in mesh.ColorTextures)
            RequireTextureForCurrentFrame(texture);
        RequireTextureForCurrentFrame(mesh.LightmapTexture);
        foreach (uint texture in mesh.NormalTextures)
            RequireTextureForCurrentFrame(texture);
        foreach (uint texture in mesh.SpecularTextures)
            RequireTextureForCurrentFrame(texture);
        foreach (GlRsxSamplerBinding binding in mesh.RsxSamplerBindings)
            RequireTextureForCurrentFrame(binding.Texture);
    }

    private void RequireTextureForCurrentFrame(uint handle)
    {
        if (handle == 0 ||
            !_visibleTextureHandles.Add(handle) ||
            !_textureHandles.TryGetEntry(
                handle,
                out MapRenderOpenGlTextureResidencyEntry entry))
        {
            return;
        }

        entry.MarkVisible(_activeRenderFrameIndex);
        if (!entry.IsResident)
            _textureAdmissionScratch.Add(entry);
    }

    private void UploadTextureStorage(
        MapRenderOpenGlTextureResidencyEntry entry)
    {
        TextureMutationBinding restore =
            BindTextureForMutation(entry);
        try
        {
            UploadTextureStorageBound(entry);
        }
        finally
        {
            RestoreTextureAfterMutation(entry, restore);
        }
    }

    private void EvictTextureStorage(
        MapRenderOpenGlTextureResidencyEntry entry)
    {
        if (!entry.IsResident || entry.IsPinned)
            return;
        TextureMutationBinding restore =
            BindTextureForMutation(entry);
        try
        {
            EvictTextureStorageBound(entry);
        }
        finally
        {
            RestoreTextureAfterMutation(entry, restore);
        }
        entry.MarkEvicted();
        ReleaseRendererOwnedDecodedFallback(entry);
        _frameTextureEvictionCount++;
        _frameTextureEvictionBytes = checked(
            _frameTextureEvictionBytes +
            entry.EstimatedResidentBytes);
    }

    private void ReleaseRendererOwnedDecodedFallback(
        MapRenderOpenGlTextureResidencyEntry entry)
    {
        long releasedBytes =
            entry.ReleaseDecodedAuthoredBcFallback();
        if (releasedBytes == 0)
            return;

        _rendererDecodedBcFallbackBytesRetained = checked(
            _rendererDecodedBcFallbackBytesRetained -
            releasedBytes);
    }

    private void EvictTextureStorageBound(
        MapRenderOpenGlTextureResidencyEntry entry)
    {
        for (int face = 0; face < entry.FaceCount; face++)
        {
            TextureTarget uploadTarget = entry.FaceCount == 6
                ? (TextureTarget)(
                    (int)TextureTarget.TextureCubeMapPositiveX + face)
                : TextureTarget.Texture2D;
            for (int level = entry.StorageLevelCount - 1;
                 level >= 0;
                 level--)
            {
                _gl.TexImage2D(
                    uploadTarget,
                    level,
                    InternalFormat.Rgba8,
                    width: 0,
                    height: 0,
                    border: 0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    null);
            }
        }

        InitializeTextureFallbackStorageBound(
            entry.Target,
            entry.FaceCount);
        ApplyTextureSwizzle(
            RsxTextureSwizzleDecoder.Decode(
                entry.Source.RsxTextureCommandState
                    .TexSwizzlePayload),
            entry.Target);
        ApplyTextureSampler(
            entry.Source.DecodedSamplerState,
            maxMipLevel: 0,
            entry.Target);
    }

    private TextureMutationBinding BindTextureForMutation(
        MapRenderOpenGlTextureResidencyEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        int previousTextureUnit = _state.GetActiveTextureUnit();
        _state.ActiveTexture(0);
        uint previousTexture =
            _state.GetTextureBinding(0, entry.Target);
        _state.BindTexture(entry.Target, entry.Handle);
        _gl.PixelStore(
            PixelStoreParameter.UnpackAlignment,
            1);
        return new TextureMutationBinding(
            previousTextureUnit,
            previousTexture);
    }

    private void RestoreTextureAfterMutation(
        MapRenderOpenGlTextureResidencyEntry entry,
        TextureMutationBinding restore)
    {
        _state.BindTexture(
            entry.Target,
            restore.PreviousTexture);
        _state.ActiveTexture(restore.PreviousTextureUnit);
    }

    private readonly record struct TextureMutationBinding(
        int PreviousTextureUnit,
        uint PreviousTexture);
}
