using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Assets;
using IW4.Render.OpenGl.Programs;
using IW4.Render.OpenGl.Shaders;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.Lifecycle;
using IW4.Render.Shaders;
using IW4.Render.Techniques;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Presentation;

internal sealed record MapRenderOpenGlNormalCameraFullscreenProgramSources(
    string FullscreenVertexGlsl,
    OpenGlAuthoredFragmentSource FeedbackReplacePixelSource,
    OpenGlAuthoredFragmentSource PostFxPixelSource,
    OpenGlAuthoredFragmentSource? PostFxColor2PixelSource,
    MapRenderOpenGlNormalCameraGlowProgramSources? Glow,
    long AssetPoolRevision);

internal sealed record MapRenderOpenGlNormalCameraGlowFilterProgramSources(
    string VertexGlsl,
    OpenGlAuthoredFragmentSource PixelSource);

internal sealed record MapRenderOpenGlNormalCameraGlowProgramSources(
    string SetupVertexGlsl,
    OpenGlAuthoredFragmentSource SetupPixelSource,
    string ApplyVertexGlsl,
    OpenGlAuthoredFragmentSource ApplyPixelSource,
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
        GL gl,
        MapRenderWorldSceneSource source,
        bool requireFilmColorManipulation = false,
        bool requireGlow = false,
        bool useGlowSetupColor2 = false)
    {
        ArgumentNullException.ThrowIfNull(gl);
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
        var fragmentPrograms = new RsxFragmentGlsl330ProgramResolver(gl);

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
                    [
                        (ushort)MaterialConstantSource.ColorTintBase,
                        (ushort)MaterialConstantSource.ColorTintDelta,
                        (ushort)MaterialConstantSource
                            .ColorTintQuadraticDelta,
                        (ushort)MaterialConstantSource.ColorBias
                    ],
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
                ? [
                    (ushort)MaterialConstantSource.GlowSetup,
                    (ushort)MaterialConstantSource.ColorTintBase,
                    (ushort)MaterialConstantSource.ColorTintDelta,
                    (ushort)MaterialConstantSource
                        .ColorTintQuadraticDelta,
                    (ushort)MaterialConstantSource.ColorBias
                ]
                : [
                    (ushort)MaterialConstantSource.GlowSetup,
                    (ushort)MaterialConstantSource.ColorTintBase,
                    (ushort)MaterialConstantSource.ColorTintDelta,
                    (ushort)MaterialConstantSource.ColorBias
                ],
            [0, 1, 2, 3, 16, 467],
            vertexPrograms,
            fragmentPrograms);
        ResolvedMaterialProgram apply = ResolveExactMaterialProgram(
            lookup,
            revision,
            recipe.GlowApplyBloom,
            [0, 8],
            [(ushort)MaterialConstantSource.GlowApply],
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
                Enumerable.Range(
                        (int)MaterialConstantSource.FilterTap0,
                        tapHalfCount)
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

    private static OpenGlAuthoredFragmentSource
        ComposeFixedFunctionPixelProgram(
        ResolvedMaterialProgram program,
        MapRenderNormalCameraMaterialAssetContract contract)
    {
        if (!OpenGlFixedFunctionEpilogue.TryCompose(
                program.RenderState,
                program.Translation.FragmentProgramControl,
                suppressShaderPackerForDiagnosticOutput: false,
                out _,
                out _,
                out string epilogue))
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' has an unsupported fixed-function epilogue.");
        }
        return OpenGlFixedFunctionEpilogue.Apply(
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
        MapRenderNormalCameraMaterialProgramResolution resolved =
            MapRenderNormalCameraMaterialProgramResolver.ResolveExact(
                lookup,
                revision,
                contract,
                expectedVertexInputDestinations,
                expectedCodePixelSourceRows,
                expectedVertexConstantDestinations);
        RsxShaderTranslationResult translation = resolved.Translation;

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
            resolved.RenderState,
            vertexResolution.Glsl!,
            fragmentResolution.Source!);
    }

    internal sealed record ResolvedMaterialProgram(
        RsxShaderTranslationResult Translation,
        RenderState RenderState,
        string VertexGlsl,
        OpenGlAuthoredFragmentSource PixelSource);
}
