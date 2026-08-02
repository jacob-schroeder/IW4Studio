using IW4.Render.Assets;
using IW4.Render.OpenGl.Presentation;
using IW4.Render.OpenGl.Shaders;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.Lifecycle;

namespace IW4.Render.OpenGl.FloatZ;

internal sealed record MapRenderOpenGlNormalCameraFloatZProgramSources(
    string FloatZVertexGlsl,
    MapRenderOpenGlAuthoredFragmentSource FloatZPixelSource,
    string ProcessedFloatZVertexGlsl,
    MapRenderOpenGlAuthoredFragmentSource ProcessedFloatZPixelSource,
    long AssetPoolRevision);

/// <summary>
/// Resolves and translates the two exact canonical fullscreen programs used
/// by the normal-camera FloatZ lifecycle.
/// </summary>
internal static class MapRenderOpenGlNormalCameraFloatZProgramResolver
{
    public static MapRenderOpenGlNormalCameraFloatZProgramSources Resolve(
        MapRenderWorldSceneSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        long revision = source.AssetPoolRevisionAtConstruction;
        RenderAssetLookup lookup = source.AssetLookup;
        if (!lookup.HasCanonicalAssetPoolRevision(revision))
        {
            throw new InvalidOperationException(
                "FloatZ materials require the scene's exact active canonical asset-pool revision.");
        }

        MapRenderNormalCameraFloatZRecipe recipe =
            MapRenderNormalCameraFloatZRecipe.Current;
        var vertexPrograms = new RsxVertexGlsl330ProgramResolver();
        var fragmentPrograms = new RsxFragmentGlsl330ProgramResolver();

        MapRenderOpenGlNormalCameraFullscreenProgramResolver
            .ResolvedMaterialProgram floatZ =
                MapRenderOpenGlNormalCameraFullscreenProgramResolver
                    .ResolveExactMaterialProgram(
                        lookup,
                        revision,
                        recipe.FloatZ,
                        [0],
                        [],
                        [0, 1, 2, 3, 17, 18],
                        vertexPrograms,
                        fragmentPrograms);
        MapRenderOpenGlNormalCameraFullscreenProgramResolver
            .ResolvedMaterialProgram processedFloatZ =
                MapRenderOpenGlNormalCameraFullscreenProgramResolver
                    .ResolveExactMaterialProgram(
                        lookup,
                        revision,
                        recipe.ProcessedFloatZ,
                        [0],
                        [0x20],
                        [0, 1, 2, 3, 17, 18],
                        vertexPrograms,
                        fragmentPrograms);

        return new MapRenderOpenGlNormalCameraFloatZProgramSources(
            floatZ.VertexGlsl,
            floatZ.PixelSource,
            processedFloatZ.VertexGlsl,
            processedFloatZ.PixelSource,
            revision);
    }
}
