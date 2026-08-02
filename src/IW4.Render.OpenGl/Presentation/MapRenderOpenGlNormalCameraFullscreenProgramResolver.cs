using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Assets;
using IW4.Render.Materials;
using IW4.Render.OpenGl.Programs;
using IW4.Render.OpenGl.Shaders;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.Lifecycle;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Presentation;

internal sealed record MapRenderOpenGlNormalCameraFullscreenProgramSources(
    string FullscreenVertexGlsl,
    MapRenderOpenGlAuthoredFragmentSource FeedbackReplacePixelSource,
    MapRenderOpenGlAuthoredFragmentSource PostFxPixelSource,
    MapRenderOpenGlAuthoredFragmentSource? PostFxColor2PixelSource,
    MapRenderOpenGlNormalCameraGlowProgramSources? Glow,
    long AssetPoolRevision);

internal sealed record MapRenderOpenGlNormalCameraGlowFilterProgramSources(
    string VertexGlsl,
    MapRenderOpenGlAuthoredFragmentSource PixelSource);

internal sealed record MapRenderOpenGlNormalCameraGlowProgramSources(
    string SetupVertexGlsl,
    MapRenderOpenGlAuthoredFragmentSource SetupPixelSource,
    string ApplyVertexGlsl,
    MapRenderOpenGlAuthoredFragmentSource ApplyPixelSource,
    IReadOnlyList<MapRenderOpenGlNormalCameraGlowFilterProgramSources>
        SymmetricFilters);

/// <summary>
/// Resolves and translates the fullscreen material graphs consumed by the
/// active adapter.
/// </summary>
internal static class
    MapRenderOpenGlNormalCameraFullscreenProgramResolver
{
    public static MapRenderOpenGlNormalCameraFullscreenProgramSources Resolve(
        MapRenderWorldSceneSource source,
        bool requireFilmColorManipulation = false,
        bool requireGlow = false,
        bool useGlowSetupColor2 = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        long revision = source.AssetPoolRevisionAtConstruction;
        RenderAssetLookup lookup = source.AssetLookup;
        if (!lookup.HasCanonicalAssetPoolRevision(revision))
        {
            throw new InvalidOperationException(
                "Normal-camera fullscreen materials require the scene's exact active canonical asset-pool revision.");
        }

        MapRenderEditorPreviewNormalCameraRecipe recipe =
            MapRenderEditorPreviewNormalCameraRecipe.Current;
        MapRenderNormalCameraMaterialAssetContract feedbackContract =
            recipe.FeedbackReplace;
        MapRenderNormalCameraMaterialAssetContract postFxContract =
            recipe.PostFx;
        MapRenderNormalCameraMaterialAssetContract postFxColor2Contract =
            recipe.PostFxColor2;
        var vertexPrograms = new RsxVertexGlsl330ProgramResolver();
        var fragmentPrograms = new RsxFragmentGlsl330ProgramResolver();

        ResolvedMaterialProgram feedback = ResolveExactMaterialProgram(
            lookup,
            revision,
            feedbackContract,
            [0, 8],
            [],
            [0, 1, 2, 3],
            vertexPrograms,
            fragmentPrograms);
        ResolvedMaterialProgram postFx = ResolveExactMaterialProgram(
            lookup,
            revision,
            postFxContract,
            [0, 8],
            [],
            [0, 1, 2, 3],
            vertexPrograms,
            fragmentPrograms);
        ResolvedMaterialProgram? postFxColor2 =
            requireFilmColorManipulation
                ? ResolveExactMaterialProgram(
                    lookup,
                    revision,
                    postFxColor2Contract,
                    [0, 8],
                    [0x2e, 0x2f, 0x30, 0x2d],
                    [0, 1, 2, 3],
                    vertexPrograms,
                    fragmentPrograms)
                : null;
        MapRenderOpenGlNormalCameraGlowProgramSources? glow = requireGlow
            ? ResolveGlow(
                lookup,
                revision,
                recipe,
                useGlowSetupColor2,
                vertexPrograms,
                fragmentPrograms)
            : null;

        if (!string.Equals(
                feedback.VertexGlsl,
                postFx.VertexGlsl,
                StringComparison.Ordinal) ||
            (postFxColor2 is not null &&
             !string.Equals(
                 feedback.VertexGlsl,
                 postFxColor2.VertexGlsl,
                 StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "feedbackreplace and postfx no longer translate to one exact textured_simple vertex program.");
        }

        return new MapRenderOpenGlNormalCameraFullscreenProgramSources(
            feedback.VertexGlsl,
            feedback.PixelSource,
            postFx.PixelSource,
            postFxColor2?.PixelSource,
            glow,
            revision);
    }

    private static MapRenderOpenGlNormalCameraGlowProgramSources ResolveGlow(
        RenderAssetLookup lookup,
        long revision,
        MapRenderEditorPreviewNormalCameraRecipe recipe,
        bool useGlowSetupColor2,
        RsxVertexGlsl330ProgramResolver vertexPrograms,
        RsxFragmentGlsl330ProgramResolver fragmentPrograms)
    {
        MapRenderNormalCameraMaterialAssetContract setupContract =
            useGlowSetupColor2
                ? recipe.GlowConsistentSetupColor2
                : recipe.GlowConsistentSetup;
        ResolvedMaterialProgram setup = ResolveExactMaterialProgram(
            lookup,
            revision,
            setupContract,
            [0, 8],
            useGlowSetupColor2
                ? [0x2b, 0x2e, 0x2f, 0x30, 0x2d]
                : [0x2b, 0x2e, 0x2f, 0x2d],
            [0, 1, 2, 3, 16, 467],
            vertexPrograms,
            fragmentPrograms);
        ResolvedMaterialProgram apply = ResolveExactMaterialProgram(
            lookup,
            revision,
            recipe.GlowApplyBloom,
            [0, 8],
            [0x2c],
            [0, 1, 2, 3],
            vertexPrograms,
            fragmentPrograms);

        var filters = new MapRenderOpenGlNormalCameraGlowFilterProgramSources[8];
        for (int index = 0; index < filters.Length; index++)
        {
            int tapHalfCount = index + 1;
            ResolvedMaterialProgram filter = ResolveExactMaterialProgram(
                lookup,
                revision,
                recipe.GlowSymmetricFilters[index],
                [0, 8],
                Enumerable.Range(0x0a, tapHalfCount)
                    .Select(value => checked((ushort)value))
                    .ToArray(),
                Enumerable.Range(12, tapHalfCount)
                    .Prepend(0)
                    .Prepend(3)
                    .Prepend(2)
                    .Prepend(1)
                    .Order()
                    .ToArray(),
                vertexPrograms,
                fragmentPrograms);
            filters[index] = new
                MapRenderOpenGlNormalCameraGlowFilterProgramSources(
                    filter.VertexGlsl,
                    filter.PixelSource);
        }

        return new MapRenderOpenGlNormalCameraGlowProgramSources(
            setup.VertexGlsl,
            ComposeFixedFunctionPixelProgram(
                setup,
                setupContract),
            apply.VertexGlsl,
            apply.PixelSource,
            filters);
    }

    private static MapRenderOpenGlAuthoredFragmentSource
        ComposeFixedFunctionPixelProgram(
        ResolvedMaterialProgram program,
        MapRenderNormalCameraMaterialAssetContract contract)
    {
        MapRenderState state = MapRenderStateDecoder.Decode(
            contract.StateBits0,
            contract.StateBits1,
            tail: 0);
        if (!MapRenderOpenGlFixedFunctionEpilogue.TryCompose(
                state,
                program.Translation.FragmentProgramControl,
                suppressShaderPackerForDiagnosticOutput: false,
                out _,
                out _,
                out string epilogue))
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' has an unsupported fixed-function epilogue.");
        }
        return MapRenderOpenGlFixedFunctionEpilogue.Apply(
            program.PixelSource,
            epilogue);
    }

    internal static ResolvedMaterialProgram ResolveExactMaterialProgram(
        RenderAssetLookup lookup,
        long revision,
        MapRenderNormalCameraMaterialAssetContract contract,
        IReadOnlyList<int> expectedVertexInputDestinations,
        IReadOnlyList<ushort> expectedCodePixelSourceRows,
        IReadOnlyList<int> expectedVertexConstantDestinations,
        RsxVertexGlsl330ProgramResolver vertexPrograms,
        RsxFragmentGlsl330ProgramResolver fragmentPrograms)
    {
        if (!lookup.TryResolveCanonicalMaterialTechniqueBinding(
                contract.MaterialName,
                revision,
                out MapRenderMaterialTechniqueBinding? binding))
        {
            throw new InvalidOperationException(
                $"Canonical fullscreen material '{contract.MaterialName}' is unavailable at asset-pool revision {revision}.");
        }

        MaterialAsset material = binding.Material;
        MaterialTechniqueSetAsset techniqueSet = binding.TechniqueSet;
        MaterialTechniqueSlot slot = binding.TechniqueSlots.SingleOrDefault(
                candidate => candidate.Index == contract.TechniqueSlot) ??
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' has no technique slot {contract.TechniqueSlot}.");
        MaterialTechniqueAsset technique = slot.Technique ??
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' technique slot {contract.TechniqueSlot} is unresolved.");
        if (!string.Equals(material.Info.Name, contract.MaterialName,
                StringComparison.Ordinal) ||
            !string.Equals(techniqueSet.Name, contract.TechniqueSetName,
                StringComparison.Ordinal) ||
            !string.Equals(technique.Name, contract.TechniqueName,
                StringComparison.Ordinal) ||
            technique.Flags != contract.TechniqueFlags ||
            technique.PassCount != contract.PassCount ||
            technique.Passes.Count != contract.PassCount)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' no longer matches its PS3 material/technique identity contract.");
        }

        MaterialPassAsset pass = technique.Passes.Single();
        MaterialShaderAsset vertex = lookup.ResolveVertexShader(
                pass.VertexShaderPointer,
                pass.VertexShader) ??
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' has no vertex program.");
        MaterialShaderAsset pixel = lookup.ResolvePixelShader(
                pass.PixelShaderPointer,
                pass.PixelShader) ??
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' has no pixel program.");
        if (!string.Equals(vertex.Name, contract.VertexShaderName,
                StringComparison.Ordinal) ||
            !string.Equals(pixel.Name, contract.PixelShaderName,
                StringComparison.Ordinal) ||
            vertex.Data is not { } vertexData ||
            pixel.Data is not { } pixelData)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' shader identities or program bytes are unavailable.");
        }
        IReadOnlyList<MaterialShaderArgumentAsset> arguments =
            lookup.ResolveShaderArgs(pass);
        if (arguments.Count != contract.Arguments.Count ||
            !arguments.Select(argument =>
                    (argument.Type,
                     argument.Dest,
                     unchecked((uint)argument.ArgumentRaw)))
                .SequenceEqual(contract.Arguments.Select(argument =>
                    (argument.Type,
                     argument.Destination,
                     argument.RawValue))))
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' shader arguments no longer match the PS3 contract.");
        }

        if ((uint)contract.TechniqueSlot >=
                (uint)material.StateBitsEntries.Count ||
            material.StateBitsEntries[contract.TechniqueSlot].TechniqueSlot !=
                contract.TechniqueSlot)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' has no exact state-bits slot row.");
        }
        int stateIndex = material.StateBitsEntries[contract.TechniqueSlot]
            .StateBitsIndex;
        if ((uint)stateIndex >= (uint)material.StateBits.Count)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' state-bits row is unavailable.");
        }
        IReadOnlyList<uint> loadBits = lookup.ResolveStateLoadBits(
            material.StateBits[stateIndex]);
        if (loadBits.Count < 2 ||
            loadBits[0] != contract.StateBits0 ||
            loadBits[1] != contract.StateBits1)
        {
            string actual = loadBits.Count >= 2
                ? $"0x{loadBits[0]:X8}/0x{loadBits[1]:X8}"
                : $"unavailable ({loadBits.Count} words)";
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' state words " +
                $"{actual} no longer match the PS3 contract " +
                $"0x{contract.StateBits0:X8}/0x{contract.StateBits1:X8}.");
        }

        RsxShaderTranslationResult translation = RsxShaderTranslator.Translate(
            vertexData,
            pixelData,
            pass,
            material);
        if (!translation.ProgramIrReady ||
            translation.Blockers.Count != 0 ||
            !translation.ReadVertexInputDestinations.SequenceEqual(
                expectedVertexInputDestinations) ||
            !translation.ReadVertexConstantDestinations.SequenceEqual(
                expectedVertexConstantDestinations) ||
            !translation.ReadFragmentSamplerDestinations.SequenceEqual([0]) ||
            !translation.CodePixelConstantPatchPlans
                .Select(plan => plan.CodeIndex)
                .SequenceEqual(expectedCodePixelSourceRows) ||
            translation.CodePixelConstantPatchPlans.Any(plan =>
                !plan.IsDirectSourceResolved))
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' is outside its exact translated program subset.");
        }

        RsxVertexGlsl330ProgramResolution vertexResolution =
            vertexPrograms.Resolve(translation.VertexProgramIr);
        if (!vertexResolution.IsReady)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' OpenGL vertex lowering failed: {vertexResolution.FailureReason}");
        }

        RsxFragmentGlsl330ProgramResolution fragmentResolution =
            fragmentPrograms.Resolve(translation.FragmentProgramIr);
        if (!fragmentResolution.IsReady)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' OpenGL fragment lowering failed: {fragmentResolution.FailureReason}");
        }

        return new ResolvedMaterialProgram(
            translation,
            vertexResolution.Glsl!,
            fragmentResolution.Source!);
    }

    internal sealed record ResolvedMaterialProgram(
        RsxShaderTranslationResult Translation,
        string VertexGlsl,
        MapRenderOpenGlAuthoredFragmentSource PixelSource);
}
