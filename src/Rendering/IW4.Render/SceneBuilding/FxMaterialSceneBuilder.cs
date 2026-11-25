using System.Numerics;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Render.Assets;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Shaders;
using IW4.Render.Techniques;
using IW4.Render.Textures;
using IW4.Render.Transforms;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Prepares canonical authored camera-color passes for the material names in
/// one FX preview. Geometry on these surfaces is a reference triangle for
/// renderer resource preparation; each draw uses the sampled FX vertices.
/// </summary>
public sealed class FxMaterialSceneBuilder
{
    private const int FxBackendRow = (int)MaterialVertexDeclarationType.Generic;
    private static readonly FxPreviewVertex[] ReferenceTriangle =
    [
        new(Vector3.Zero, -Vector3.UnitY, new Vector2(0f, 1f), Vector4.One,
            Vector3.UnitX, true),
        new(Vector3.UnitX, -Vector3.UnitY, new Vector2(1f, 1f), Vector4.One,
            Vector3.UnitX, true),
        new(Vector3.UnitZ, -Vector3.UnitY, new Vector2(0f, 0f), Vector4.One,
            Vector3.UnitX, true)
    ];
    private static readonly Vector3[] ReferencePositions =
        ReferenceTriangle.Select(vertex =>
            RenderCoordinateConverter.GameToRenderPosition(vertex.Position)).ToArray();
    private static readonly RenderBounds ReferenceBounds =
        ReferencePositions.Aggregate(RenderBounds.Empty, (bounds, point) => bounds.Include(point));

    public XModelRenderScene Build(
        IReadOnlyList<string> materialNames,
        RenderAssetSource assetSource,
        IGfxImagePayloadResolver imagePayloadResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(materialNames);
        ArgumentNullException.ThrowIfNull(assetSource);
        ArgumentNullException.ThrowIfNull(imagePayloadResolver);
        long poolRevision = assetSource.AssetPool.Revision;
        var lookup = new RenderAssetLookup(assetSource, imagePayloadResolver);
        var textureCache = new RenderTextureCache(preferProvenAuthoredPayloads: false);
        var failedTextureCacheKeys = new HashSet<RenderTextureCacheKey>();
        var shaderTranslationCache = new ShaderTranslationCache();
        var diagnostics = new List<string>();
        var surfaces = new List<XModelRenderSurface>();

        foreach (string name in materialNames.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(name)) continue;
            EnsurePoolRevision();
            int surfaceIndex = surfaces.Count;
            if (!lookup.TryResolveCanonicalMaterialTechniqueBinding(
                    name, poolRevision, out MaterialTechniqueBinding? binding) ||
                binding is null)
            {
                string status = $"material={name};canonicalGraph=unresolved@revision{poolRevision}";
                diagnostics.Add($"FX material {name}: {status}.");
                surfaces.Add(CreateSurface(name, surfaceIndex, -1, string.Empty,
                    [], false, status));
                continue;
            }

            MaterialAsset material = binding.Material;
            MaterialTechniqueSetAsset techniqueSet = binding.TechniqueSet;
            AuthoredCameraColorTechniqueSelection selected =
                AuthoredCameraColorTechniqueSelector.Select(material, techniqueSet, lookup);
            if (selected.Passes.Count == 0)
            {
                string status = $"material={name};authoredGroup=blocked:{selected.Blocker}";
                diagnostics.Add($"FX material {name}: {status}.");
                surfaces.Add(CreateSurface(name, surfaceIndex, selected.TechniqueSlot,
                    selected.TechniqueName, [], false, status));
                continue;
            }

            var passes = new List<XModelRenderAuthoredPass>(selected.Passes.Count);
            var blockers = new List<string>();
            foreach (AuthoredCameraColorPassSelection selectedPass in selected.Passes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MaterialPassAsset sourcePass = selectedPass.SourcePass;
                AuthoredMaterialSamplerResolver.TrySelectPrimary(
                    material, sourcePass, selectedPass.Arguments, lookup,
                    MaterialStreamSource.TexCoord0,
                    out AuthoredMaterialPrimarySamplerSelection? primarySampler);
                var passIdentity = new MaterialPassIdentity(
                    material.Info.Name ?? name,
                    new TechniquePassIdentity(
                        techniqueSet.Name ?? string.Empty,
                        selected.TechniqueSlot,
                        selected.TechniqueName,
                        selectedPass.PassClass,
                        selectedPass.PassIndex,
                        sourcePass.CustomSamplerFlags));
                IReadOnlyList<MaterialSamplerBinding> resolvedSamplers =
                    XModelMaterialSamplerBindingBuilder.Build(
                        material, sourcePass, selectedPass.Arguments, lookup,
                        imagePayloadResolver, textureCache, failedTextureCacheKeys);
                ShaderVertexInputBinding[] genericInputs =
                    MaterialVertexInputBindingPlanner.Resolve(
                        techniqueSet, lookup, passIdentity, FxBackendRow);
                int backendRow = genericInputs.Any(input =>
                    input.Source == MaterialStreamSource.Tangent)
                    ? (int)MaterialVertexDeclarationType.StaticModelCache
                    : FxBackendRow;
                ShaderVertexInputBinding[] vertexInputs = backendRow == FxBackendRow
                    ? genericInputs
                    : MaterialVertexInputBindingPlanner.Resolve(
                        techniqueSet, lookup, passIdentity, backendRow);
                MaterialSamplerBinding[] samplers = resolvedSamplers
                    .Select(sampler => WithFxUvRoute(sampler, backendRow)).ToArray();
                bool payloadReady = FxRenderVertexPayload.Project(
                    ReferenceTriangle, vertexInputs, out float[] payload,
                    out string payloadBlocker);
                string[] samplerBlockers = resolvedSamplers
                    .Where(sampler => sampler.UvRoute is { } route &&
                        (route.TexCoordSource != MaterialStreamSource.TexCoord0 ||
                         !WorldVertexLayout.TryGetSource(
                             backendRow, route.TexCoordSource, out WorldVertexSource source) ||
                         !WorldVertexLayout.TryGetStreamStride(
                             backendRow, source.StreamIndex, out _)))
                    .Select(sampler => $"samplerUv={sampler.UvRoute!.TexCoordSource}")
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (samplerBlockers.Length > 0)
                {
                    payloadReady = false;
                    payloadBlocker = string.Join('|', samplerBlockers);
                }
                ShaderExecutionContract execution =
                    AuthoredMaterialExecutionPlanner.CreateContract(
                        material, techniqueSet, lookup, passIdentity,
                        primarySampler?.Identity, selectedPass.State,
                        primarySampler?.Image.Name ?? string.Empty,
                        samplers, payloadReady, payloadBlocker,
                        authoredSourcePassAvailable: true,
                        shaderTranslationCache: shaderTranslationCache,
                        fixedVertexSourceBackendRow: backendRow,
                        explicitVertexInputs: vertexInputs);
                var passBlockers = new List<string>();
                if (!selectedPass.StateReady)
                    passBlockers.Add("renderState=unresolved");
                passBlockers.AddRange(execution.RendererBlockers);
                string diagnostic = passBlockers.Count == 0
                    ? execution.RuntimeSamplerRequirements.Count == 0
                        ? "authoredPass=ready"
                        : "authoredPass=runtimeDeferred"
                    : "authoredPass=blocked:" +
                      string.Join('|', passBlockers.Distinct(StringComparer.Ordinal));
                if (passBlockers.Count > 0)
                    blockers.Add($"pass{selectedPass.PassIndex}({diagnostic})");
                passes.Add(new XModelRenderAuthoredPass(
                    surfaceIndex, passes.Count, passIdentity,
                    primarySampler?.Identity, selectedPass.State,
                    execution, samplers, payload, diagnostic));
            }

            bool ready = passes.Count == selected.Passes.Count && blockers.Count == 0;
            string groupStatus = ready
                ? $"techniqueSlot={selected.TechniqueSlot};passes={passes.Count};ready"
                : $"techniqueSlot={selected.TechniqueSlot};passes={passes.Count};blocked:" +
                  string.Join(';', blockers);
            if (!ready) diagnostics.Add($"FX material {name}: {groupStatus}.");
            surfaces.Add(CreateSurface(name, surfaceIndex, selected.TechniqueSlot,
                selected.TechniqueName, passes, ready, groupStatus));
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsurePoolRevision();
        var lod = new XModelRenderLod(0, 0f, ReferenceBounds, surfaces);
        return new XModelRenderScene("FX material passes", [lod], 0,
            ReferenceBounds, [], diagnostics, lookup, poolRevision);

        void EnsurePoolRevision()
        {
            if (assetSource.AssetPool.Revision != poolRevision ||
                !lookup.HasCanonicalAssetPoolRevision(poolRevision))
            {
                throw new InvalidOperationException(
                    "The canonical asset pool changed while preparing FX materials.");
            }
        }
    }

    private static XModelRenderSurface CreateSurface(
        string materialName, int surfaceIndex, int techniqueSlot,
        string techniqueName, IReadOnlyList<XModelRenderAuthoredPass> passes,
        bool ready, string status) =>
        new(surfaceIndex, surfaceIndex, materialName, ReferencePositions,
            skinningVertices: null, [0u, 1u, 2u], [], ReferenceBounds,
            techniqueSlot, techniqueName, passes, ready, status);

    private static MaterialSamplerBinding WithFxUvRoute(
        MaterialSamplerBinding sampler, int backendRow)
    {
        if (sampler.UvRoute is not { } route)
            return sampler;
        if (!WorldVertexLayout.TryGetSource(
                backendRow, route.TexCoordSource,
                out WorldVertexSource source) ||
            !WorldVertexLayout.TryGetStreamStride(
                backendRow, source.StreamIndex, out byte stride))
        {
            return sampler with { UvRoute = null };
        }
        return sampler with
        {
            UvRoute = new UvRoute(
                "FX generated vertex", ((MaterialVertexDeclarationType)backendRow).ToString(),
                route.TexCoordSource,
                source.StreamIndex, stride, source.ByteOffset,
                source.ComponentCount, source.RsxType,
                source.StreamIndex == 1
                    ? UvBaseMode.Stream1ZeroBase
                    : UvBaseMode.Stream0LocalIndexOnly,
                0, 1,
                1f, 1f, 0f, 0f)
        };
    }
}
