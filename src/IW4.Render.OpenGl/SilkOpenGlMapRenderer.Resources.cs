using Silk.NET.OpenGL;
using IW4.Render.World;

using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.Lighting;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling;
using IW4.Render.OpenGl.Presentation;
using IW4.Render.OpenGl.StaticModels;
using IW4.Render.OpenGl.World;
using IW4.Render.Visibility;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private void InitializeFixedSamplerUniforms()
    {
        _gl.UseProgram(_texturedProgram);
        for (int layerIndex = 0;
             layerIndex < _texturedColorSamplerLocations.Length;
             layerIndex++)
        {
            _gl.Uniform1(
                _texturedColorSamplerLocations[layerIndex],
                layerIndex);
        }
        _gl.Uniform1(
            _texturedLightmapSamplerLocation,
            MapRenderScene.MaxColorLayerCount);
        int normalTextureUnitStart = MapRenderScene.MaxColorLayerCount + 1;
        for (int index = 0;
             index < _texturedNormalSamplerLocations.Length;
             index++)
        {
            _gl.Uniform1(
                _texturedNormalSamplerLocations[index],
                normalTextureUnitStart + index);
        }
        int specularTextureUnitStart =
            normalTextureUnitStart + _texturedNormalSamplerLocations.Length;
        for (int index = 0;
             index < _texturedSpecularSamplerLocations.Length;
             index++)
        {
            _gl.Uniform1(
                _texturedSpecularSamplerLocations[index],
                specularTextureUnitStart + index);
        }
        _gl.Uniform1(
            _texturedStaticModelLightingSamplerLocation,
            GenericStaticModelLightingTextureUnit);
        System.Numerics.Vector4 staticLightingTransform =
            MapRenderStaticModelLightingAtlas.SamplerTransform;
        _gl.Uniform3(
            _texturedStaticModelLightingSamplerTransformLocation,
            staticLightingTransform.X,
            staticLightingTransform.Y,
            staticLightingTransform.Z);

        _gl.UseProgram(_skyProgram);
        _gl.Uniform1(_skyTextureLocation, 0);
        _gl.UseProgram(0);
    }

    private void EstablishStateShadowBaseline()
    {
        MapRenderPixelExtent targetExtent =
            MapRenderOpenGlNormalCameraTargetExtentPolicy.Resolve(
                SurfaceExtents,
                _editorPreviewPresentationSession is not null);
        _state.InvalidateAll();
        _state.EstablishKnownTextureBaseline(textureUnitCount: 16);
        _state.BindFramebuffer(FramebufferTarget.Framebuffer, _hostFramebuffer);
        _gl.DrawBuffer(_hostFramebuffer == 0
            ? DrawBufferMode.Back
            : DrawBufferMode.ColorAttachment0);
        _state.BindVertexArray(0);
        _state.BindArrayBuffer(0);
        _state.UseProgram(0);
        _state.Viewport(
            0,
            0,
            targetExtent.Width,
            targetExtent.Height);
        ApplyDefaultRenderState();
    }

    private void CreateEditorPreviewPresentationSession(
        MapRenderWorldSceneSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            _editorPreviewPresentationSession =
                EditorPresentationSession
                    .Create(
                        _gl,
                        _editorPreviewPresentationContextIdentity,
                        EditorPreviewLinkProfileIdentity,
                        source,
                        _renderSceneSnapshot ??
                            throw new InvalidOperationException(
                                "Live Preview requires a frozen render scene snapshot."),
                        _shaderCompilationCounter,
                        _sharedProgramUsage,
                        _editorPreviewEffectivePost,
                        stateShadow: _state);
        }
        catch
        {
            DeleteLoadedResources();
            throw;
        }
    }

    private static MapRenderTexturedBatch? CreateIsolatedWorldSurfaceBatch(
        MapRenderTexturedBatch batch,
        int surfaceIndex)
    {
        MapRenderPickRange[] ranges = batch.PickRanges
            .Where(range =>
                range.Kind == MapRenderPickKind.GfxSurface &&
                range.SurfaceIndex == surfaceIndex)
            .ToArray();
        if (ranges.Length == 0)
            return null;

        uint[] indices = ranges
            .SelectMany(range => batch.Indices
                .Skip(range.FirstIndex)
                .Take(range.IndexCount))
            .ToArray();
        return batch with
        {
            PickRanges = ranges,
            Indices = indices
        };
    }

    private void BuildWorldSurfaceBatchRuntimes(
        IReadOnlyList<MapRenderTexturedBatch> batches)
    {
        if (batches.Count != _textured.Length)
        {
            throw new ArgumentException(
                "World batch and uploaded mesh counts must match.",
                nameof(batches));
        }

        _worldSurfaceBatches =
            new WorldSurfaceBatchRuntime?[_textured.Length];
        _worldSurfaceCandidateCount = 0;
        _worldSurfaceCandidateIndexCount = 0;
        _worldSurfaceFallbackBatchCount = 0;
        for (int batchIndex = 0;
             batchIndex < batches.Count;
             batchIndex++)
        {
            GlTexturedMesh mesh = _textured[batchIndex];
            if (mesh.IndexCount == 0)
                continue;

            bool allowsDecodedPerSurfaceFrustumCull =
                AllowsDecodedPerSurfaceFrustumCull(mesh);
            if (MapRenderOpenGlWorldSurfaceSpanCatalog.TryCreate(
                    batches[batchIndex],
                    out MapRenderOpenGlWorldSurfaceSpan[] spans,
                    includeDecodedBounds:
                        allowsDecodedPerSurfaceFrustumCull))
            {
                _worldSurfaceBatches[batchIndex] = new(
                    spans,
                    allowsDecodedPerSurfaceFrustumCull);
                _worldSurfaceCandidateCount = checked(
                    _worldSurfaceCandidateCount + spans.Length);
                foreach (MapRenderOpenGlWorldSurfaceSpan span in spans)
                {
                    _worldSurfaceCandidateIndexCount = checked(
                        _worldSurfaceCandidateIndexCount + span.IndexCount);
                }
                continue;
            }

            // Retain the previous whole-batch path when range metadata is
            // incomplete. This optimization must never suppress unowned
            // geometry.
            _worldSurfaceFallbackBatchCount++;
            _worldSurfaceCandidateCount++;
            _worldSurfaceCandidateIndexCount = checked(
                _worldSurfaceCandidateIndexCount + mesh.IndexCount);
        }
    }

    private static bool AllowsDecodedPerSurfaceFrustumCull(
        GlTexturedMesh mesh) =>
        // The fixed generic preview vertex path only applies the host
        // view-projection matrix. Wind is its sole position deformation.
        // Translated RSX programs can consume dynamic code constants and their
        // decoded static positions are not conservative cull bounds.
        mesh.RsxProgram.Handle == 0 &&
        mesh.VegetationAnimation?.IsEnabled != true;

    private sealed class WorldReceiverVariantRuntime
    {
        public WorldReceiverVariantRuntime(
            int channelIndex,
            MapRenderWorldReceiverVariantKey key,
            MapRenderTexturedBatch[] batches,
            GlTexturedMesh[] meshes,
            WorldSurfaceBatchRuntime?[] surfaceBatches,
            GlMesh genericArena,
            GlMesh[] translatedArenas,
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] drawGroups,
            int surfaceCount)
        {
            if (channelIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(channelIndex));
            if (surfaceCount < 0)
                throw new ArgumentOutOfRangeException(nameof(surfaceCount));
            ArgumentNullException.ThrowIfNull(batches);
            ArgumentNullException.ThrowIfNull(meshes);
            ArgumentNullException.ThrowIfNull(surfaceBatches);
            ArgumentNullException.ThrowIfNull(translatedArenas);
            ArgumentNullException.ThrowIfNull(drawGroups);
            if (batches.Length != meshes.Length ||
                meshes.Length != surfaceBatches.Length)
            {
                throw new ArgumentException(
                    "Receiver batches, meshes, and span runtimes must retain one ordinal space.");
            }

            ChannelIndex = channelIndex;
            Key = key;
            Batches = batches;
            Meshes = meshes;
            SurfaceBatches = surfaceBatches;
            GenericArena = genericArena;
            TranslatedArenas = translatedArenas;
            DrawGroups = drawGroups;
            SelectionWords = new uint[checked((surfaceCount + 31) / 32)];
            ExecutableSurfaces = new bool[surfaceCount];
            for (int batchIndex = 0;
                 batchIndex < surfaceBatches.Length;
                 batchIndex++)
            {
                if (meshes[batchIndex].IndexCount == 0 ||
                    surfaceBatches[batchIndex] is not { } batch)
                {
                    continue;
                }
                foreach (MapRenderOpenGlWorldSurfaceSpan span in batch.Spans)
                {
                    if ((uint)span.SurfaceIndex <
                        (uint)ExecutableSurfaces.Length)
                    {
                        ExecutableSurfaces[span.SurfaceIndex] = true;
                    }
                }
            }
        }

        public int ChannelIndex { get; }
        public MapRenderWorldReceiverVariantKey Key { get; }
        public MapRenderTexturedBatch[] Batches { get; }
        public GlTexturedMesh[] Meshes { get; }
        public WorldSurfaceBatchRuntime?[] SurfaceBatches { get; }
        public GlMesh GenericArena { get; }
        public GlMesh[] TranslatedArenas { get; }
        public MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] DrawGroups
            { get; }
        public uint[] SelectionWords { get; }
        public bool[] ExecutableSurfaces { get; }

        public int SelectionCount { get; private set; }

        public bool CanExecuteSurface(int surfaceIndex) =>
            (uint)surfaceIndex < (uint)ExecutableSurfaces.Length &&
            ExecutableSurfaces[surfaceIndex];

        public void BeginSelection()
        {
            Array.Clear(SelectionWords);
            SelectionCount = 0;
        }

        public void SelectSurface(int surfaceIndex)
        {
            if ((uint)surfaceIndex >= (uint)ExecutableSurfaces.Length)
                throw new ArgumentOutOfRangeException(nameof(surfaceIndex));

            uint mask = 0x8000_0000u >> (surfaceIndex & 31);
            ref uint word = ref SelectionWords[surfaceIndex >> 5];
            if ((word & mask) != 0)
                return;
            word |= mask;
            SelectionCount++;
        }
    }

    private interface IExactStaticVariantRuntime
    {
        MapRenderInstancedTexturedBatch[] Batches { get; }
        GlTexturedMesh[] Meshes { get; }
        MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] DrawGroups
            { get; set; }
        Dictionary<
            MapRenderStaticModelReceiverIdentity,
            StaticReceiverSurfaceRuntime> Surfaces { get; }
        MapRenderOpenGlStaticResourceGroupPlan ResourcePlan { get; }
        bool[] ResolvedGroups { get; }
        bool[] ExecutableGroups { get; }
        MapRenderOpenGlProgressiveStaticDrawGroupCache DrawGroupCache
            { get; }
    }

    private sealed class ExactNormalCameraStaticRuntime :
        IExactStaticVariantRuntime
    {
        public ExactNormalCameraStaticRuntime(
            MapRenderInstancedTexturedBatch[] batches,
            MapRenderOpenGlStaticResourceGroupPlan resourcePlan)
        {
            ArgumentNullException.ThrowIfNull(batches);
            ArgumentNullException.ThrowIfNull(resourcePlan);
            Batches = batches;
            Meshes = new GlTexturedMesh[batches.Length];
            DrawGroups = [];
            Surfaces = new Dictionary<
                MapRenderStaticModelReceiverIdentity,
                StaticReceiverSurfaceRuntime>();
            ResourcePlan = resourcePlan;
            ResolvedGroups = new bool[resourcePlan.GroupCount];
            ExecutableGroups = new bool[resourcePlan.GroupCount];
            DrawGroupCache =
                new MapRenderOpenGlProgressiveStaticDrawGroupCache(
                    resourcePlan.GroupCount);
        }

        public MapRenderInstancedTexturedBatch[] Batches { get; }
        public GlTexturedMesh[] Meshes { get; }
        public MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] DrawGroups
            { get; set; }
        public Dictionary<
            MapRenderStaticModelReceiverIdentity,
            StaticReceiverSurfaceRuntime> Surfaces { get; }
        public MapRenderOpenGlStaticResourceGroupPlan ResourcePlan { get; }
        public bool[] ResolvedGroups { get; }
        public bool[] ExecutableGroups { get; }
        public MapRenderOpenGlProgressiveStaticDrawGroupCache DrawGroupCache
            { get; }
    }

    private sealed class StaticReceiverVariantRuntime :
        IExactStaticVariantRuntime
    {
        public StaticReceiverVariantRuntime(
            int channelIndex,
            MapRenderStaticModelReceiverVariantKey key,
            MapRenderInstancedTexturedBatch[] batches,
            GlTexturedMesh[] meshes,
            MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] drawGroups,
            Dictionary<
                MapRenderStaticModelReceiverIdentity,
                StaticReceiverSurfaceRuntime> surfaces,
            MapRenderOpenGlStaticResourceGroupPlan resourcePlan)
        {
            if (channelIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(channelIndex));
            ArgumentNullException.ThrowIfNull(batches);
            ArgumentNullException.ThrowIfNull(meshes);
            ArgumentNullException.ThrowIfNull(drawGroups);
            ArgumentNullException.ThrowIfNull(surfaces);
            ArgumentNullException.ThrowIfNull(resourcePlan);
            if (batches.Length != meshes.Length)
            {
                throw new ArgumentException(
                    "Static receiver batches and meshes must retain one ordinal space.");
            }

            ChannelIndex = channelIndex;
            Key = key;
            Batches = batches;
            Meshes = meshes;
            DrawGroups = drawGroups;
            Surfaces = surfaces;
            ResourcePlan = resourcePlan;
            ResolvedGroups = new bool[resourcePlan.GroupCount];
            ExecutableGroups = new bool[resourcePlan.GroupCount];
            DrawGroupCache =
                new MapRenderOpenGlProgressiveStaticDrawGroupCache(
                    resourcePlan.GroupCount);
        }

        public int ChannelIndex { get; }
        public MapRenderStaticModelReceiverVariantKey Key { get; }
        public MapRenderInstancedTexturedBatch[] Batches { get; }
        public GlTexturedMesh[] Meshes { get; }
        public MapRenderEditorDrawGroup<GlTexturedDrawCommand>[] DrawGroups
            { get; set; }
        public Dictionary<
            MapRenderStaticModelReceiverIdentity,
            StaticReceiverSurfaceRuntime> Surfaces { get; }
        public MapRenderOpenGlStaticResourceGroupPlan ResourcePlan
            { get; }
        public bool[] ResolvedGroups { get; }
        public bool[] ExecutableGroups { get; }
        public MapRenderOpenGlProgressiveStaticDrawGroupCache DrawGroupCache
            { get; }
    }

    private sealed class StaticReceiverSurfaceRuntime
    {
        public StaticReceiverSurfaceRuntime(
            MapRenderStaticModelReceiverIdentity identity,
            int techniqueSlot,
            StaticReceiverPassOccurrence[] passes)
        {
            ArgumentNullException.ThrowIfNull(passes);
            if (passes.Length == 0)
                throw new ArgumentException(
                    "An executable static receiver surface requires at least one pass.",
                    nameof(passes));
            Identity = identity;
            TechniqueSlot = techniqueSlot;
            Passes = passes;
        }

        public MapRenderStaticModelReceiverIdentity Identity { get; }
        public int TechniqueSlot { get; }
        public StaticReceiverPassOccurrence[] Passes { get; }
    }

    private readonly record struct StaticReceiverPassOccurrence(
        StaticInstanceBufferRuntime Runtime,
        int InstanceIndex);

    private sealed class WorldSurfaceBatchRuntime
    {
        public WorldSurfaceBatchRuntime(
            MapRenderOpenGlWorldSurfaceSpan[] spans,
            bool allowsDecodedPerSurfaceFrustumCull)
        {
            ArgumentNullException.ThrowIfNull(spans);
            if (spans.Length == 0)
                throw new ArgumentException(
                    "A world surface batch requires at least one span.",
                    nameof(spans));

            Spans = spans;
            AllowsDecodedPerSurfaceFrustumCull =
                allowsDecodedPerSurfaceFrustumCull;
            VisibleRuns =
                new MapRenderOpenGlWorldVisibleRun[spans.Length];
        }

        public MapRenderOpenGlWorldSurfaceSpan[] Spans { get; }

        public MapRenderOpenGlWorldVisibleRun[] VisibleRuns { get; }

        public bool AllowsDecodedPerSurfaceFrustumCull { get; }

        public int RunCount { get; private set; }

        public MapRenderOpenGlWorldSurfaceCompactionResult Compact(
            ReadOnlySpan<uint> dpvsSurfaceWords,
            bool hasDpvsVisibility,
            MapRenderCameraFrustum? frustum)
        {
            MapRenderOpenGlWorldSurfaceCompactionResult result =
                MapRenderOpenGlWorldSurfaceRunCompactor.Compact(
                    Spans,
                    dpvsSurfaceWords,
                    hasDpvsVisibility,
                    frustum,
                    VisibleRuns);
            RunCount = result.RunCount;
            return result;
        }

        public void ClearVisibleRuns() => RunCount = 0;
    }

}
