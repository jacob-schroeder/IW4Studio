using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;

using IW4.Assets.Assets.Material;
using IW4.Render.Execution;
using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.StaticModels;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Shaders;
using IW4.Render.Techniques;
using IW4.Render.Textures;

namespace IW4.Render.Resources;

/// <summary>
/// Freezes the current sky, diagnostic, and collision-wireframe vertical
/// slices of a MapRenderScene into semantic, backend-neutral scene-lifetime
/// resources.
/// </summary>
public static class RenderSceneSnapshotBuilder
{
    private const string DecodedRgba8Format = "RGBA8_UNORM";
    private const string DecodedRg16FloatFormat = "RG16_FLOAT";

    public static RenderSceneSnapshot Create(
        MapRenderScene scene,
        long revision = 0) =>
        Create(scene, revision, includeDiagnosticGeometry: true);

    /// <summary>
    /// Compatibility path for a preview mode that proves diagnostic geometry
    /// is unreachable before scene resources are frozen. This keeps isolated
    /// world-surface loading independent of data the historical renderer did
    /// not validate or upload.
    /// </summary>
    internal static RenderSceneSnapshot Create(
        MapRenderScene scene,
        long revision,
        bool includeDiagnosticGeometry)
        => Create(
            scene,
            revision,
            includeDiagnosticGeometry,
            includeWireframeGeometry: includeDiagnosticGeometry,
            includeCompatibilityDrawResources: true,
            includeNormalCameraDrawResources: true,
            includeAllStaticLodDrawResources: true,
            preferProvenAuthoredTexturePayloads: false);

    /// <summary>
    /// Creates the lightweight semantic snapshot used by the interactive
    /// OpenGL map window. OpenGL uploads textured world/static geometry
    /// directly from <see cref="MapRenderScene"/> and owns its draw grouping,
    /// so freezing the same multi-megabyte payloads into compatibility and
    /// normal-camera descriptors would duplicate data without contributing to
    /// the displayed frame. Sky resources remain because presentation planning
    /// consumes their semantic identities; the compact collision wireframe is
    /// retained as the editor's explicit overlay/isolation channel.
    /// </summary>
    internal static RenderSceneSnapshot CreateInteractiveOpenGl(
        MapRenderScene scene,
        long revision = 0) =>
        Create(
            scene,
            revision,
            includeDiagnosticGeometry: false,
            includeWireframeGeometry: true,
            includeCompatibilityDrawResources: false,
            includeNormalCameraDrawResources: false,
            includeAllStaticLodDrawResources: false,
            preferProvenAuthoredTexturePayloads: true);

    /// <summary>
    /// Creates the complete immutable normal-camera inventory consumed by the
    /// native Metal backend while retaining the interactive renderer's proven
    /// authored-BC memory policy. Diagnostic solids remain excluded; sky,
    /// textured world/static, and the explicit collision wireframe are owned
    /// by the snapshot before it reaches the native window thread.
    /// </summary>
    internal static RenderSceneSnapshot CreateInteractiveMetal(
        MapRenderScene scene,
        long revision = 0) =>
        Create(
            scene,
            revision,
            includeDiagnosticGeometry: false,
            includeWireframeGeometry: true,
            includeCompatibilityDrawResources: false,
            includeNormalCameraDrawResources: true,
            includeAllStaticLodDrawResources: true,
            preferProvenAuthoredTexturePayloads: true);

    private static RenderSceneSnapshot Create(
        MapRenderScene scene,
        long revision,
        bool includeDiagnosticGeometry,
        bool includeWireframeGeometry,
        bool includeCompatibilityDrawResources,
        bool includeNormalCameraDrawResources,
        bool includeAllStaticLodDrawResources,
        bool preferProvenAuthoredTexturePayloads)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentException.ThrowIfNullOrWhiteSpace(scene.Name);
        ArgumentNullException.ThrowIfNull(scene.Skies);

        MapRenderSky[] sourceSkies = scene.Skies.ToArray();
        if (sourceSkies.Any(sky => sky is null))
        {
            int nullOrdinal = Array.FindIndex(sourceSkies, sky => sky is null);
            throw InvalidSky(nullOrdinal, "the source submission is null");
        }

        var vertexLayouts = new List<RenderVertexLayoutDescriptor>();
        var instanceLayouts = new List<RenderInstanceLayoutDescriptor>();
        var geometries = new List<RenderGeometryDescriptor>(sourceSkies.Length);
        var instances = new List<RenderInstanceDescriptor>();
        var textures = new List<RenderTextureDescriptor>();
        var samplers = new List<RenderSamplerDescriptor>();
        var submissions = new List<RenderSkySubmissionSnapshot>(sourceSkies.Length);
        var diagnostics = new List<RenderDiagnosticSubmissionSnapshot>();
        RenderWireframeSubmissionSnapshot? wireframe = null;
        var textureResources = new Dictionary<Texture, TextureResources>(
            ReferenceEqualityComparer.Instance);

        RenderVertexLayoutDescriptor? skyVertexLayout = null;
        if (sourceSkies.Length > 0)
        {
            skyVertexLayout = CreateSkyVertexLayout();
            vertexLayouts.Add(skyVertexLayout);
        }

        for (int ordinal = 0; ordinal < sourceSkies.Length; ordinal++)
        {
            try
            {
                MapRenderSky source = sourceSkies[ordinal];
                ValidateSkySource(source);

                RenderSemanticIdentity geometryIdentity = Identity(
                    RenderSemanticResourceKind.Geometry,
                    "scene.sky",
                    ordinal,
                    "geometry");
                var geometry = new RenderGeometryDescriptor(
                    geometryIdentity,
                    skyVertexLayout!,
                    RenderGeometryCoordinateSpace.Render,
                    RenderPrimitiveTopology.TriangleList,
                    RenderIndexFormat.Unsigned32,
                    source.Vertices.Length / MapRenderScene.VertexFloatCount,
                    source.Indices.Length,
                    EncodeSingles(source.Vertices),
                    EncodeUInt32(source.Indices));
                geometries.Add(geometry);

                if (!textureResources.TryGetValue(
                        source.Texture,
                        out TextureResources resources))
                {
                    int resourceOrdinal = textureResources.Count;
                    RenderSemanticIdentity textureIdentity = Identity(
                        RenderSemanticResourceKind.Texture,
                        "scene.sky.texture",
                        resourceOrdinal);
                    RenderSemanticIdentity samplerIdentity = Identity(
                        RenderSemanticResourceKind.Sampler,
                        "scene.sky.sampler",
                        resourceOrdinal);
                    var texture = CreateTextureDescriptor(
                        source.Texture,
                        textureIdentity,
                        preferProvenAuthoredPayload:
                            preferProvenAuthoredTexturePayloads);
                    var sampler = new RenderSamplerDescriptor(
                        samplerIdentity,
                        source.Texture.DecodedSamplerState);
                    resources = new TextureResources(texture, sampler);
                    textureResources.Add(source.Texture, resources);
                    textures.Add(texture);
                    samplers.Add(sampler);
                }

                submissions.Add(new RenderSkySubmissionSnapshot(
                    ordinal,
                    source.WorldSkyIndex,
                    source.Source,
                    source.SkyStartSurfPositions,
                    source.SurfaceIndices,
                    Identity(
                        RenderSemanticResourceKind.Draw,
                        "scene.sky",
                        ordinal,
                        "draw"),
                    geometry.Identity,
                    skyVertexLayout!.Identity,
                    resources.Texture.Identity,
                    resources.Sampler.Identity,
                    source.ShaderPass,
                    source.ShaderPrimarySampler,
                    (byte)source.ShaderTexCoordSource,
                    source.ShaderExecution));
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException or
                OverflowException)
            {
                throw InvalidSky(ordinal, exception.Message, exception);
            }
        }

        if (includeDiagnosticGeometry)
        {
            AppendDiagnosticResources(
                scene,
                vertexLayouts,
                instanceLayouts,
                geometries,
                instances,
                diagnostics);
        }
        if (includeWireframeGeometry)
        {
            wireframe = AppendWireframeResources(
                scene,
                vertexLayouts,
                geometries);
        }

        RenderMaterialDrawPacketAdmission? materialDrawPacketAdmission = null;
        RenderWorldDrawPacketAdmission? worldDrawPacketAdmission = null;
        if (includeCompatibilityDrawResources)
        {
            materialDrawPacketAdmission =
                AppendMaterialDrawPacketResources(
                    scene,
                    vertexLayouts,
                    geometries,
                    textures,
                    samplers);
            worldDrawPacketAdmission =
                AppendLoadedCameraColorWorldDrawPacketResources(
                    scene,
                    vertexLayouts,
                    geometries,
                    textures,
                    samplers);
        }

        RenderNormalCameraDrawSnapshot? normalCameraDraws =
            includeNormalCameraDrawResources
                ? AppendNormalCameraDrawResources(
                    scene,
                    includeAllStaticLodDrawResources,
                    preferProvenAuthoredTexturePayloads)
                : null;

        var resourceSnapshot = new RenderResourceSnapshot(
            vertexLayouts,
            instanceLayouts,
            geometries,
            instances,
            textures,
            samplers);
        return new RenderSceneSnapshot(
            scene.Name,
            revision,
            resourceSnapshot,
            submissions,
            diagnostics,
            wireframe,
            materialDrawPacketAdmission,
            worldDrawPacketAdmission,
            normalCameraDraws);
    }

    private static RenderWorldDrawPacketAdmission
        AppendLoadedCameraColorWorldDrawPacketResources(
            MapRenderScene scene,
            ICollection<RenderVertexLayoutDescriptor> vertexLayouts,
            ICollection<RenderGeometryDescriptor> geometries,
            ICollection<RenderTextureDescriptor> textures,
            ICollection<RenderSamplerDescriptor> samplers)
    {
        if (scene.TexturedBatches is null)
        {
            return new RenderWorldDrawPacketAdmission(
                packet: null,
                rejections: [],
                RenderWorldDrawPacketAdmissionFailure
                    .SourceCollectionMissing,
                "LOADED_CAMERA_COLOR_TEXTURED_BATCH_COLLECTION_MISSING");
        }

        MapRenderTexturedBatch[] sourceBatches =
            scene.TexturedBatches.ToArray();
        if (sourceBatches.Length == 0)
        {
            return new RenderWorldDrawPacketAdmission(
                packet: null,
                rejections: [],
                RenderWorldDrawPacketAdmissionFailure.NoSourceBatches,
                "NO_LOADED_CAMERA_COLOR_TEXTURED_BATCHES");
        }

        var rejections =
            new List<RenderWorldDrawPacketCandidateRejection>();
        for (int sourceOrdinal = 0;
             sourceOrdinal < sourceBatches.Length;
             sourceOrdinal++)
        {
            MapRenderTexturedBatch? source = sourceBatches[sourceOrdinal];
            List<RenderWorldDrawPacketCandidateRejectionCode> codes =
                ValidateLoadedCameraColorWorldCandidate(source);
            if (codes.Count != 0)
            {
                rejections.Add(new RenderWorldDrawPacketCandidateRejection(
                    sourceOrdinal,
                    codes));
                continue;
            }

            try
            {
                RenderWorldDrawPacketSnapshot packet =
                    CreateLoadedCameraColorWorldDrawPacket(
                        source!,
                        sourceOrdinal);
                vertexLayouts.Add(packet.VertexLayout);
                geometries.Add(packet.Geometry);
                textures.Add(packet.Texture);
                samplers.Add(packet.Sampler);
                return new RenderWorldDrawPacketAdmission(
                    packet,
                    rejections,
                    RenderWorldDrawPacketAdmissionFailure.None,
                    rejectionReason: null);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException or
                OverflowException)
            {
                rejections.Add(new RenderWorldDrawPacketCandidateRejection(
                    sourceOrdinal,
                    [
                        RenderWorldDrawPacketCandidateRejectionCode
                            .ResourceSnapshotCreationFailed
                    ]));
            }
        }

        string reason = string.Concat(
            "NO_ELIGIBLE_LOADED_CAMERA_COLOR_BASE_TEXTURE_BATCH; candidates=",
            string.Join(
                ";",
                rejections.Select(rejection => string.Concat(
                    rejection.SourceOrdinal.ToString(
                        "D8",
                        CultureInfo.InvariantCulture),
                    "[",
                    rejection.Reason,
                    "]"))));
        return new RenderWorldDrawPacketAdmission(
            packet: null,
            rejections,
            RenderWorldDrawPacketAdmissionFailure
                .NoEligibleLoadedCameraColorBatch,
            reason);
    }

    private static List<RenderWorldDrawPacketCandidateRejectionCode>
        ValidateLoadedCameraColorWorldCandidate(
            MapRenderTexturedBatch? source)
    {
        var codes =
            new List<RenderWorldDrawPacketCandidateRejectionCode>();
        void Add(RenderWorldDrawPacketCandidateRejectionCode code)
        {
            if (!codes.Contains(code))
                codes.Add(code);
        }

        if (source is null)
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode.NullBatch);
            return codes;
        }

        MaterialPassIdentity? pass = source.Pass;
        if (pass is null)
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode.MissingPass);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(pass.MaterialName) ||
                string.IsNullOrWhiteSpace(
                    pass.TechniquePass.TechniqueSetName) ||
                string.IsNullOrWhiteSpace(
                    pass.TechniquePass.TechniqueName))
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .MissingMaterialIdentity);
            }
            if (!string.Equals(
                    pass.TechniquePass.PassClass,
                    MaterialPassClassifier.CameraColor,
                    StringComparison.Ordinal))
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .NotLoadedCameraColorPass);
            }
            if (!RenderLoadedCameraColorCompatibilityProfile.MatchesTechnique(
                    pass))
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .UnsupportedTechniqueIdentity);
            }
        }

        ShaderExecutionContract? shader = source.ShaderExecution;
        if (shader is null)
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .MissingShaderExecution);
        }
        else
        {
            if (shader.Purpose !=
                ShaderExecutionPurpose.CameraColor)
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .ShaderPurposeNotCameraColor);
            }
            if (!shader.ProgramIrReady)
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .ProgramIrNotReady);
            }
            if (shader.VertexProgramIr is null ||
                shader.FragmentProgramIr is null ||
                !shader.VertexProgram.HasProgramData ||
                !shader.PixelProgram.HasProgramData ||
                shader.VertexProgramIr is { HasValidUpload: false } ||
                shader.FragmentProgramIr is { HasValidUpload: false })
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .ProgramIrMissing);
            }
            if (!shader.VertexInputPayloadReady)
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .VertexInputPayloadNotReady);
            }
            if (!shader.RendererProgramReady)
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .RendererProgramNotReady);
            }
            if (shader.RendererBlockers.Count != 0)
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .ShaderExecutionContractInconsistent);
            }
            if (!shader.ProgramExecutionReady ||
                !string.Equals(
                    source.ShaderExecutionStatus,
                    shader.ProgramExecutionStatus,
                    StringComparison.Ordinal))
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .ProgramExecutionNotReady);
            }
            if (shader.RuntimeSamplerRequirements.Count != 0)
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .RuntimeSamplerRequirementsPresent);
            }
            if (!RenderLoadedCameraColorCompatibilityProfile.MatchesShader(
                    shader))
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .UnsupportedShaderProfile);
            }
            if (pass is not null &&
                (!RenderLoadedCameraColorCompatibilityProfile.MatchesPass(
                     pass,
                     source.PrimarySampler,
                     (byte)source.UvRoute.TexCoordSource) ||
                 shader.MaterialSamplerDestinations.Count != 1 ||
                 shader.MaterialSamplerDestinations.Count == 1 &&
                 (shader.MaterialSamplerDestinations[0].ArgumentIndex !=
                      RenderLoadedCameraColorCompatibilityProfile
                          .SamplerArgIndex ||
                  shader.MaterialSamplerDestinations[0].Destination !=
                      RenderLoadedCameraColorCompatibilityProfile
                          .SamplerDestination ||
                  !shader.MaterialSamplerDestinations[0]
                      .IsOperationallyResolved ||
                  !string.Equals(
                      shader.MaterialSamplerDestinations[0].TextureTarget,
                      "Texture2D",
                      StringComparison.Ordinal)) ||
                 shader.CustomSamplerDestinations.Count != 0 ||
                 shader.CodeSamplerDestinations.Count != 0 ||
                 shader.ProgramSamplerDestinations.Count != 1 ||
                 shader.ProgramSamplerDestinations.Count == 1 &&
                 shader.ProgramSamplerDestinations[0] !=
                    RenderLoadedCameraColorCompatibilityProfile
                        .SamplerDestination))
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .UnsupportedSamplerProgramShape);
            }
        }

        if (source.LightmapTexture is not null)
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .LightmapPresent);
        }
        if (source.ColorLayers is not { Count: 1 })
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .ColorLayerCountNotOne);
        }
        if (source.MaterialSamplers is not { Count: 1 })
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .MaterialSamplerCountNotOne);
        }
        if (source.UnresolvedCodeSamplerCount != 0)
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .UnresolvedCodeSamplers);
        }
        if (source.EditorDepthPrepass is not null ||
            source.DepthPrepassShaderExecution is not null)
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .DepthPrepassPresent);
        }
        if (!RenderLoadedCameraColorCompatibilityProfile.MatchesRawState(
                source.State))
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .UnsupportedStateProfile);
        }
        else if (!RenderLoadedCameraColorCompatibilityProfile.Matches(
                     source.State))
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .SourceStateInconsistentWithRawBits);
        }
        if (source.UvRoute is null ||
            !RenderLoadedCameraColorCompatibilityProfile.MatchesUvRoute(
                source.UvRoute))
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .UnsupportedUvProfile);
        }
        if (!HasValidMaterialDrawGeometry(
                source.Vertices,
                source.Indices))
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .GeometryMissingOrMalformed);
        }
        else
        {
            int vertexCount = source.Vertices.Length /
                MapRenderScene.TexturedVertexFloatCount;
            if (source.RsxVertexInputs is null ||
                source.RsxVertexInputs.Length != checked(
                    vertexCount *
                    RenderWorldDrawPacketSnapshot
                        .RsxVertexInputFloatStride))
            {
                Add(RenderWorldDrawPacketCandidateRejectionCode
                    .RsxVertexPayloadMissingOrMalformed);
            }
        }

        Texture? texture = source.Texture;
        if (texture is null ||
            texture.Target != TextureTarget.Texture2D)
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .TextureNotTwoDimensional);
        }
        else if (!texture.HasCompleteDecodedPayload)
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .DecodedRgbaMipChainIncomplete);
        }

        if (pass is not null &&
            source.ColorLayers is { Count: 1 } layers &&
            source.MaterialSamplers is { Count: 1 } materialSamplers &&
            (!HasMatchingBaseTextureBinding(
                 source,
                 pass,
                 layers[0],
                 materialSamplers[0].Binding) ||
             materialSamplers[0].RuntimeTextureIdentity is not null))
        {
            Add(RenderWorldDrawPacketCandidateRejectionCode
                .BaseTextureBindingMismatch);
        }

        ValidateLoadedCameraColorRanges(
            source.PickRanges,
            source.Indices?.Length ?? 0,
            Add);
        return codes;
    }

    private static void ValidateLoadedCameraColorRanges(
        IReadOnlyList<MapRenderPickRange>? ranges,
        int geometryIndexCount,
        Action<RenderWorldDrawPacketCandidateRejectionCode> add)
    {
        ArgumentNullException.ThrowIfNull(add);
        if (ranges is null || ranges.Count == 0)
        {
            add(RenderWorldDrawPacketCandidateRejectionCode
                .MissingGfxSurfaceRanges);
            return;
        }

        int expectedFirstIndex = 0;
        var surfaceIndices = new HashSet<int>();
        foreach (MapRenderPickRange range in ranges)
        {
            if (range.Kind != MapRenderPickKind.GfxSurface)
            {
                add(RenderWorldDrawPacketCandidateRejectionCode
                    .PickRangeKindNotGfxSurface);
            }
            if (range.ObjectIndex < 0 || range.SurfaceIndex < 0)
            {
                add(RenderWorldDrawPacketCandidateRejectionCode
                    .NegativeSurfaceOrObjectIndex);
            }
            else if (range.ObjectIndex != range.SurfaceIndex)
            {
                add(RenderWorldDrawPacketCandidateRejectionCode
                    .GfxSurfaceOwnershipMismatch);
            }
            else if (!surfaceIndices.Add(range.SurfaceIndex))
            {
                add(RenderWorldDrawPacketCandidateRejectionCode
                    .DuplicateSurfaceIndex);
            }
            if (range.FirstIndex != expectedFirstIndex)
            {
                add(RenderWorldDrawPacketCandidateRejectionCode
                    .PickRangeFirstIndexNotContiguous);
            }
            if (range.IndexCount <= 0)
            {
                add(RenderWorldDrawPacketCandidateRejectionCode
                    .PickRangeIndexCountNotPositive);
            }
            else if (range.IndexCount % 3 != 0)
            {
                add(RenderWorldDrawPacketCandidateRejectionCode
                    .PickRangeIndexCountNotTriangleAligned);
            }

            long end = (long)range.FirstIndex + range.IndexCount;
            if (end < 0 || end > geometryIndexCount)
            {
                add(RenderWorldDrawPacketCandidateRejectionCode
                    .PickRangeExceedsGeometry);
            }
            if (end is >= 0 and <= int.MaxValue)
                expectedFirstIndex = (int)end;
        }
        if (expectedFirstIndex != geometryIndexCount)
        {
            add(RenderWorldDrawPacketCandidateRejectionCode
                .PickRangesDoNotCoverGeometry);
        }
    }

    private static RenderWorldDrawPacketSnapshot
        CreateLoadedCameraColorWorldDrawPacket(
            MapRenderTexturedBatch source,
            int sourceOrdinal)
    {
        string prefix = string.Concat(
            "scene.loaded-camera-color-world-draw-packet.",
            sourceOrdinal.ToString("D8", CultureInfo.InvariantCulture));
        var vertexLayout = new RenderVertexLayoutDescriptor(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.VertexLayout,
                prefix + ".vertex-layout.position-uv0-f32.stride-88"),
            RenderWorldDrawPacketSnapshot.VertexStrideBytes,
            [
                new RenderVertexElementDescriptor(
                    RenderVertexSemantic.Position,
                    0,
                    RenderVertexElementFormat.Float32x3,
                    0),
                new RenderVertexElementDescriptor(
                    RenderVertexSemantic.TextureCoordinate,
                    0,
                    RenderVertexElementFormat.Float32x2,
                    3 * sizeof(float))
            ]);
        var geometry = new RenderGeometryDescriptor(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Geometry,
                prefix + ".geometry"),
            vertexLayout,
            RenderGeometryCoordinateSpace.Render,
            RenderPrimitiveTopology.TriangleList,
            RenderIndexFormat.Unsigned32,
            source.Vertices.Length /
                MapRenderScene.TexturedVertexFloatCount,
            source.Indices.Length,
            EncodeSingles(source.Vertices),
            EncodeUInt32(source.Indices));
        RenderTextureDescriptor texture = CreateTextureDescriptor(
            source.Texture,
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Texture,
                prefix + ".texture"));
        var sampler = new RenderSamplerDescriptor(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Sampler,
                prefix + ".sampler"),
            source.Texture.DecodedSamplerState);

        return new RenderWorldDrawPacketSnapshot(
            RenderWorldDrawPacketCompatibilityProfile
                .LoadedCameraColorBaseTextureAlphaGequal128CullFrontDepthLequalV1,
            sourceOrdinal,
            new RenderMaterialPassProvenanceSnapshot(
                source.Pass,
                source.PrimarySampler,
                (byte)source.UvRoute.TexCoordSource),
            new RenderMaterialTextureBindingProvenanceSnapshot(
                source.ColorLayers[0],
                source.MaterialSamplers[0].Binding,
                source.MaterialSamplers[0].RuntimeTextureIdentity),
            new RenderMaterialUvRouteSnapshot(source.UvRoute),
            source.State,
            source.SceneLightIndex,
            new RenderWorldShaderProvenanceSnapshot(
                source.ShaderExecution,
                source.ShaderExecutionStatus),
            source.PickRanges,
            source.RsxVertexInputs,
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Draw,
                prefix + ".draw.full-batch"),
            vertexLayout,
            geometry,
            texture,
            sampler);
    }

    private static RenderMaterialDrawPacketAdmission
        AppendMaterialDrawPacketResources(
            MapRenderScene scene,
            ICollection<RenderVertexLayoutDescriptor> vertexLayouts,
            ICollection<RenderGeometryDescriptor> geometries,
            ICollection<RenderTextureDescriptor> textures,
            ICollection<RenderSamplerDescriptor> samplers)
    {
        if (scene.TexturedBatches is null)
        {
            return new RenderMaterialDrawPacketAdmission(
                packet: null,
                rejections: [],
                RenderMaterialDrawPacketAdmissionFailure.SourceCollectionMissing,
                "TEXTURED_BATCH_COLLECTION_MISSING");
        }

        MapRenderTexturedBatch[] sourceBatches =
            scene.TexturedBatches.ToArray();
        if (sourceBatches.Length == 0)
        {
            return new RenderMaterialDrawPacketAdmission(
                packet: null,
                rejections: [],
                RenderMaterialDrawPacketAdmissionFailure.NoSourceBatches,
                "NO_TEXTURED_BATCHES");
        }

        var rejections =
            new List<RenderMaterialDrawPacketCandidateRejection>();
        for (int sourceOrdinal = 0;
             sourceOrdinal < sourceBatches.Length;
             sourceOrdinal++)
        {
            MapRenderTexturedBatch? source = sourceBatches[sourceOrdinal];
            List<RenderMaterialDrawPacketCandidateRejectionCode> codes =
                ValidateMaterialDrawPacketCandidate(source);
            if (codes.Count != 0)
            {
                rejections.Add(
                    new RenderMaterialDrawPacketCandidateRejection(
                        sourceOrdinal,
                        codes));
                continue;
            }

            try
            {
                RenderMaterialDrawPacketSnapshot packet =
                    CreateMaterialDrawPacket(source!, sourceOrdinal);
                vertexLayouts.Add(packet.VertexLayout);
                geometries.Add(packet.Geometry);
                textures.Add(packet.Texture);
                samplers.Add(packet.Sampler);
                return new RenderMaterialDrawPacketAdmission(
                    packet,
                    rejections,
                    RenderMaterialDrawPacketAdmissionFailure.None,
                    rejectionReason: null);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException or
                OverflowException)
            {
                rejections.Add(
                    new RenderMaterialDrawPacketCandidateRejection(
                        sourceOrdinal,
                        [
                            RenderMaterialDrawPacketCandidateRejectionCode
                                .ResourceSnapshotCreationFailed
                        ]));
            }
        }

        string reason = string.Concat(
            "NO_ELIGIBLE_GENERIC_OPAQUE_TEXTURED_BATCH; candidates=",
            string.Join(
                ";",
                rejections.Select(rejection => string.Concat(
                    rejection.SourceOrdinal.ToString(
                        "D8",
                        CultureInfo.InvariantCulture),
                    "[",
                    rejection.Reason,
                    "]"))));
        return new RenderMaterialDrawPacketAdmission(
            packet: null,
            rejections,
            RenderMaterialDrawPacketAdmissionFailure.NoEligibleBatch,
            reason);
    }

    private static List<RenderMaterialDrawPacketCandidateRejectionCode>
        ValidateMaterialDrawPacketCandidate(MapRenderTexturedBatch? source)
    {
        var codes =
            new List<RenderMaterialDrawPacketCandidateRejectionCode>();
        if (source is null)
        {
            codes.Add(
                RenderMaterialDrawPacketCandidateRejectionCode.NullBatch);
            return codes;
        }

        MaterialPassIdentity? pass = source.Pass;
        if (pass is null)
        {
            codes.Add(
                RenderMaterialDrawPacketCandidateRejectionCode.MissingPass);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(pass.MaterialName) ||
                pass.TechniquePass.TechniqueSetName is null ||
                pass.TechniquePass.TechniqueName is null ||
                pass.TechniquePass.PassClass is null)
            {
                codes.Add(
                    RenderMaterialDrawPacketCandidateRejectionCode
                        .MissingMaterialIdentity);
            }
            if (pass.TechniquePass.TechniqueSlot != -1 ||
                pass.TechniquePass.PassIndex != -1 ||
                !string.Equals(
                    pass.TechniquePass.TechniqueName,
                    "material.texture[semantic=0x02]",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    pass.TechniquePass.PassClass,
                    "MaterialColor",
                    StringComparison.Ordinal))
            {
                codes.Add(
                    RenderMaterialDrawPacketCandidateRejectionCode
                        .UnsupportedSourcePass);
            }
        }

        if (source.LightmapTexture is not null)
        {
            codes.Add(
                RenderMaterialDrawPacketCandidateRejectionCode
                    .LightmapPresent);
        }
        if (source.ColorLayers is not { Count: 1 })
        {
            codes.Add(
                RenderMaterialDrawPacketCandidateRejectionCode
                    .ColorLayerCountNotOne);
        }
        if (source.MaterialSamplers is not { Count: 1 })
        {
            codes.Add(
                RenderMaterialDrawPacketCandidateRejectionCode
                    .MaterialSamplerCountNotOne);
        }
        if (source.UnresolvedCodeSamplerCount != 0)
        {
            codes.Add(
                RenderMaterialDrawPacketCandidateRejectionCode
                    .UnresolvedCodeSamplers);
        }
        if (source.EditorDepthPrepass is not null ||
            source.DepthPrepassShaderExecution is not null)
        {
            codes.Add(
                RenderMaterialDrawPacketCandidateRejectionCode
                    .DepthPrepassPresent);
        }
        if (source.State !=
            RenderMaterialDrawPacketSnapshot.RequiredEffectiveState)
        {
            codes.Add(
                RenderMaterialDrawPacketCandidateRejectionCode
                    .UnsupportedGenericOpaqueState);
        }
        if (!HasValidMaterialDrawGeometry(
                source.Vertices,
                source.Indices))
        {
            codes.Add(
                RenderMaterialDrawPacketCandidateRejectionCode
                    .GeometryMissingOrMalformed);
        }

        Texture? texture = source.Texture;
        if (texture is null ||
            texture.Target != TextureTarget.Texture2D)
        {
            codes.Add(
                RenderMaterialDrawPacketCandidateRejectionCode
                    .TextureNotTwoDimensional);
        }
        else if (!texture.HasCompleteDecodedPayload)
        {
            codes.Add(
                RenderMaterialDrawPacketCandidateRejectionCode
                    .DecodedRgbaMipChainIncomplete);
        }

        if (pass is not null &&
            source.ColorLayers is { Count: 1 } layers &&
            source.MaterialSamplers is { Count: 1 } materialSamplers &&
            !HasMatchingBaseTextureBinding(
                source,
                pass,
                layers[0],
                materialSamplers[0].Binding))
        {
            codes.Add(
                RenderMaterialDrawPacketCandidateRejectionCode
                    .BaseTextureBindingMismatch);
        }

        return codes;
    }

    private static bool HasMatchingBaseTextureBinding(
        MapRenderTexturedBatch source,
        MaterialPassIdentity pass,
        MaterialColorLayer? layer,
        MaterialSamplerBinding? sampler)
    {
        if (layer is null || sampler is null ||
            source.Texture is null || source.UvRoute is null ||
            layer.Texture is null || layer.UvRoute is null ||
            sampler.Texture is null || sampler.UvRoute is null)
        {
            return false;
        }

        return layer.LayerIndex == 0 &&
            layer.BlendWeightComponent == -1 &&
            layer.Identity == source.PrimarySampler &&
            ReferenceEquals(layer.Texture, source.Texture) &&
            layer.UvRoute == source.UvRoute &&
            sampler.Identity == layer.Identity &&
            string.Equals(
                sampler.TextureName,
                source.Texture.Name,
                StringComparison.Ordinal) &&
            ReferenceEquals(sampler.Texture, source.Texture) &&
            sampler.UvRoute == source.UvRoute &&
            source.UvRoute.TexCoordSource ==
                layer.UvRoute.TexCoordSource;
    }

    private static bool HasValidMaterialDrawGeometry(
        float[]? vertices,
        uint[]? indices)
    {
        if (vertices is null || indices is null ||
            vertices.Length == 0 ||
            vertices.Length % MapRenderScene.TexturedVertexFloatCount != 0 ||
            vertices.Any(value => !float.IsFinite(value)) ||
            indices.Length == 0 || indices.Length % 3 != 0)
        {
            return false;
        }

        uint vertexCount = checked((uint)(
            vertices.Length / MapRenderScene.TexturedVertexFloatCount));
        return indices.All(index => index < vertexCount);
    }

    private static RenderMaterialDrawPacketSnapshot CreateMaterialDrawPacket(
        MapRenderTexturedBatch source,
        int sourceOrdinal)
    {
        string prefix = string.Concat(
            "scene.material-draw-packet.",
            sourceOrdinal.ToString("D8", CultureInfo.InvariantCulture));
        var vertexLayout = new RenderVertexLayoutDescriptor(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.VertexLayout,
                prefix + ".vertex-layout.position-uv0-f32.stride-88"),
            RenderMaterialDrawPacketSnapshot.VertexStrideBytes,
            [
                new RenderVertexElementDescriptor(
                    RenderVertexSemantic.Position,
                    0,
                    RenderVertexElementFormat.Float32x3,
                    0),
                new RenderVertexElementDescriptor(
                    RenderVertexSemantic.TextureCoordinate,
                    0,
                    RenderVertexElementFormat.Float32x2,
                    3 * sizeof(float))
            ]);
        var geometry = new RenderGeometryDescriptor(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Geometry,
                prefix + ".geometry"),
            vertexLayout,
            RenderGeometryCoordinateSpace.Render,
            RenderPrimitiveTopology.TriangleList,
            RenderIndexFormat.Unsigned32,
            source.Vertices.Length /
                MapRenderScene.TexturedVertexFloatCount,
            source.Indices.Length,
            EncodeSingles(source.Vertices),
            EncodeUInt32(source.Indices));
        RenderTextureDescriptor texture = CreateTextureDescriptor(
            source.Texture,
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Texture,
                prefix + ".texture"));
        var sampler = new RenderSamplerDescriptor(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Sampler,
                prefix + ".sampler"),
            source.Texture.DecodedSamplerState);

        MaterialColorLayer layer = source.ColorLayers[0];
        MaterialSamplerBinding binding = source.MaterialSamplers[0].Binding;
        return new RenderMaterialDrawPacketSnapshot(
            sourceOrdinal,
            new RenderMaterialPassProvenanceSnapshot(
                source.Pass,
                source.PrimarySampler,
                (byte)source.UvRoute.TexCoordSource),
            new RenderMaterialTextureBindingProvenanceSnapshot(
                layer,
                binding,
                source.MaterialSamplers[0].RuntimeTextureIdentity),
            new RenderMaterialUvRouteSnapshot(source.UvRoute),
            source.State,
            source.SceneLightIndex,
            source.ShaderExecutionStatus,
            source.PickRanges,
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Draw,
                prefix + ".draw"),
            vertexLayout,
            geometry,
            texture,
            sampler);
    }

    private static RenderWireframeSubmissionSnapshot?
        AppendWireframeResources(
            MapRenderScene scene,
            ICollection<RenderVertexLayoutDescriptor> vertexLayouts,
            ICollection<RenderGeometryDescriptor> geometries)
    {
        ArgumentNullException.ThrowIfNull(scene.WireVertices);
        ArgumentNullException.ThrowIfNull(scene.WireIndices);
        bool hasVertices = scene.WireVertices.Length != 0;
        bool hasIndices = scene.WireIndices.Length != 0;
        if (!hasVertices || !hasIndices)
            return null;

        try
        {
            ValidateWireframeGeometry(
                scene.WireVertices,
                scene.WireIndices);
            RenderVertexLayoutDescriptor vertexLayout =
                CreateWireframeVertexLayout();
            vertexLayouts.Add(vertexLayout);
            var geometryIdentity = new RenderSemanticIdentity(
                RenderSemanticResourceKind.Geometry,
                "scene.wireframe.geometry");
            geometries.Add(new RenderGeometryDescriptor(
                geometryIdentity,
                vertexLayout,
                RenderGeometryCoordinateSpace.Render,
                RenderPrimitiveTopology.LineList,
                RenderIndexFormat.Unsigned32,
                scene.WireVertices.Length /
                    MapRenderScene.VertexFloatCount,
                scene.WireIndices.Length,
                EncodeSingles(scene.WireVertices),
                EncodeUInt32(scene.WireIndices)));

            return new RenderWireframeSubmissionSnapshot(
                new RenderSemanticIdentity(
                    RenderSemanticResourceKind.Draw,
                    "scene.wireframe.draw"),
                geometryIdentity,
                vertexLayout.Identity);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            OverflowException)
        {
            throw InvalidWireframe(exception.Message, exception);
        }
    }

    private static void AppendDiagnosticResources(
        MapRenderScene scene,
        ICollection<RenderVertexLayoutDescriptor> vertexLayouts,
        ICollection<RenderInstanceLayoutDescriptor> instanceLayouts,
        ICollection<RenderGeometryDescriptor> geometries,
        ICollection<RenderInstanceDescriptor> instances,
        ICollection<RenderDiagnosticSubmissionSnapshot> submissions)
    {
        ArgumentNullException.ThrowIfNull(scene.SolidVertices);
        ArgumentNullException.ThrowIfNull(scene.SolidIndices);
        ArgumentNullException.ThrowIfNull(scene.FallbackSolidVertices);
        ArgumentNullException.ThrowIfNull(scene.FallbackSolidIndices);
        ArgumentNullException.ThrowIfNull(scene.InstancedSolidBatches);

        bool hasFallback = HasRealizableGeometry(
            scene.FallbackSolidVertices,
            scene.FallbackSolidIndices);
        bool hasSolid = HasRealizableGeometry(
            scene.SolidVertices,
            scene.SolidIndices);
        MapRenderInstancedSolidBatch[] sourceBatches =
            scene.InstancedSolidBatches.ToArray();
        if (sourceBatches.Any(batch => batch is null))
        {
            int nullBatch = Array.FindIndex(
                sourceBatches,
                batch => batch is null);
            throw InvalidDiagnostic(
                checked(nullBatch + 2),
                "the instanced-solid source batch is null");
        }
        var hasInstanced = new bool[sourceBatches.Length];
        for (int batchIndex = 0;
             batchIndex < sourceBatches.Length;
             batchIndex++)
        {
            MapRenderInstancedSolidBatch batch = sourceBatches[batchIndex];
            try
            {
                ArgumentNullException.ThrowIfNull(batch.Vertices);
                ArgumentNullException.ThrowIfNull(batch.Indices);
                ArgumentNullException.ThrowIfNull(batch.Instances);
                hasInstanced[batchIndex] =
                    HasRealizableGeometry(
                        batch.Vertices,
                        batch.Indices) &&
                    batch.Instances.Count > 0;
            }
            catch (ArgumentNullException exception)
            {
                throw InvalidDiagnostic(
                    checked(batchIndex + 2),
                    exception.Message,
                    exception);
            }
        }
        if (!hasFallback && !hasSolid && !hasInstanced.Any(value => value))
            return;

        RenderVertexLayoutDescriptor vertexLayout =
            CreateDiagnosticVertexLayout();
        vertexLayouts.Add(vertexLayout);
        RenderInstanceLayoutDescriptor? instanceLayout = null;
        if (hasInstanced.Any(value => value))
        {
            instanceLayout = CreateDiagnosticInstanceLayout();
            instanceLayouts.Add(instanceLayout);
        }

        if (hasFallback)
        {
            AppendDiagnosticGeometry(
                sourceOrdinal: 0,
                RenderDiagnosticSubmissionKind.FallbackSolid,
                instancedBatchIndex: null,
                scene.FallbackSolidVertices,
                scene.FallbackSolidIndices,
                instances: null,
                vertexLayout,
                instanceLayout: null,
                geometries,
                destinationInstances: instances,
                submissions);
        }
        if (hasSolid)
        {
            AppendDiagnosticGeometry(
                sourceOrdinal: 1,
                RenderDiagnosticSubmissionKind.Solid,
                instancedBatchIndex: null,
                scene.SolidVertices,
                scene.SolidIndices,
                instances: null,
                vertexLayout,
                instanceLayout: null,
                geometries,
                destinationInstances: instances,
                submissions);
        }
        for (int batchIndex = 0;
             batchIndex < sourceBatches.Length;
             batchIndex++)
        {
            if (!hasInstanced[batchIndex])
                continue;
            MapRenderInstancedSolidBatch batch = sourceBatches[batchIndex];
            AppendDiagnosticGeometry(
                checked(batchIndex + 2),
                RenderDiagnosticSubmissionKind.InstancedSolid,
                batchIndex,
                batch.Vertices,
                batch.Indices,
                batch.Instances,
                vertexLayout,
                instanceLayout!,
                geometries,
                instances,
                submissions);
        }
    }

    private static void AppendDiagnosticGeometry(
        int sourceOrdinal,
        RenderDiagnosticSubmissionKind kind,
        int? instancedBatchIndex,
        float[] vertices,
        uint[] indices,
        IReadOnlyList<MapRenderStaticModelInstance>? instances,
        RenderVertexLayoutDescriptor vertexLayout,
        RenderInstanceLayoutDescriptor? instanceLayout,
        ICollection<RenderGeometryDescriptor> geometries,
        ICollection<RenderInstanceDescriptor> destinationInstances,
        ICollection<RenderDiagnosticSubmissionSnapshot> submissions)
    {
        try
        {
            ValidateDiagnosticGeometry(vertices, indices);
            RenderSemanticIdentity geometryIdentity = Identity(
                RenderSemanticResourceKind.Geometry,
                "scene.diagnostics",
                sourceOrdinal,
                "geometry");
            geometries.Add(new RenderGeometryDescriptor(
                geometryIdentity,
                vertexLayout,
                RenderGeometryCoordinateSpace.Render,
                RenderPrimitiveTopology.TriangleList,
                RenderIndexFormat.Unsigned32,
                vertices.Length / MapRenderScene.VertexFloatCount,
                indices.Length,
                EncodeSingles(vertices),
                EncodeUInt32(indices)));

            RenderSemanticIdentity? instancesIdentity = null;
            RenderSemanticIdentity? instanceLayoutIdentity = null;
            if (instances is not null)
            {
                ArgumentNullException.ThrowIfNull(instanceLayout);
                if (instances.Count == 0)
                {
                    throw new InvalidDataException(
                        "instanced diagnostic geometry has no instances");
                }
                float[] packed = new float[checked(
                    instances.Count *
                    MapRenderStaticInstanceBufferPacker
                        .PlacementOnlyFloatStride)];
                MapRenderStaticInstanceBufferPacker.PackAll(
                    instances,
                    MapRenderStaticInstanceLightingPayload.None,
                    packed);
                if (packed.Any(value => !float.IsFinite(value)))
                {
                    throw new InvalidDataException(
                        "instance transform payload contains a non-finite value");
                }

                instancesIdentity = Identity(
                    RenderSemanticResourceKind.Instances,
                    "scene.diagnostics",
                    sourceOrdinal,
                    "instances");
                instanceLayoutIdentity = instanceLayout.Identity;
                destinationInstances.Add(new RenderInstanceDescriptor(
                    instancesIdentity.Value,
                    instanceLayout,
                    instances.Count,
                    EncodeSingles(packed),
                    RenderPayloadByteOrder.LittleEndian));
            }

            submissions.Add(new RenderDiagnosticSubmissionSnapshot(
                sourceOrdinal,
                kind,
                instancedBatchIndex,
                Identity(
                    RenderSemanticResourceKind.Draw,
                    "scene.diagnostics",
                    sourceOrdinal,
                    "draw"),
                geometryIdentity,
                vertexLayout.Identity,
                instancesIdentity,
                instanceLayoutIdentity));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            OverflowException)
        {
            throw InvalidDiagnostic(
                sourceOrdinal,
                exception.Message,
                exception);
        }
    }

    private static RenderVertexLayoutDescriptor
        CreateDiagnosticVertexLayout() =>
            new(
                new RenderSemanticIdentity(
                    RenderSemanticResourceKind.VertexLayout,
                    "scene.diagnostics.vertex-layout.position-color-f32x3.stride-24"),
                checked(MapRenderScene.VertexFloatCount * sizeof(float)),
                [
                    new RenderVertexElementDescriptor(
                        RenderVertexSemantic.Position,
                        semanticIndex: 0,
                        RenderVertexElementFormat.Float32x3,
                        offsetBytes: 0),
                    new RenderVertexElementDescriptor(
                        RenderVertexSemantic.Color,
                        semanticIndex: 0,
                        RenderVertexElementFormat.Float32x3,
                        offsetBytes: 3 * sizeof(float))
                ]);

    private static RenderVertexLayoutDescriptor
        CreateWireframeVertexLayout() =>
            new(
                new RenderSemanticIdentity(
                    RenderSemanticResourceKind.VertexLayout,
                    "scene.wireframe.vertex-layout.position-color-f32x3.stride-24"),
                checked(MapRenderScene.VertexFloatCount * sizeof(float)),
                [
                    new RenderVertexElementDescriptor(
                        RenderVertexSemantic.Position,
                        semanticIndex: 0,
                        RenderVertexElementFormat.Float32x3,
                        offsetBytes: 0),
                    new RenderVertexElementDescriptor(
                        RenderVertexSemantic.Color,
                        semanticIndex: 0,
                        RenderVertexElementFormat.Float32x3,
                        offsetBytes: 3 * sizeof(float))
                ]);

    private static RenderInstanceLayoutDescriptor
        CreateDiagnosticInstanceLayout() =>
            new(
                new RenderSemanticIdentity(
                    RenderSemanticResourceKind.InstanceLayout,
                    "scene.diagnostics.instance-layout.transform-rows-f32x4.stride-48"),
                MapRenderStaticInstanceBufferPacker
                    .PlacementOnlyFloatStride * sizeof(float),
                [
                    new RenderInstanceElementDescriptor(
                        RenderInstanceSemantic.TransformRow,
                        semanticIndex: 0,
                        RenderVertexElementFormat.Float32x4,
                        offsetBytes: 0),
                    new RenderInstanceElementDescriptor(
                        RenderInstanceSemantic.TransformRow,
                        semanticIndex: 1,
                        RenderVertexElementFormat.Float32x4,
                        offsetBytes: 4 * sizeof(float)),
                    new RenderInstanceElementDescriptor(
                        RenderInstanceSemantic.TransformRow,
                        semanticIndex: 2,
                        RenderVertexElementFormat.Float32x4,
                        offsetBytes: 8 * sizeof(float))
                ]);

    private static bool HasRealizableGeometry(
        float[] vertices,
        uint[] indices) =>
        vertices.Length != 0 &&
        indices.Length != 0;

    private static void ValidateDiagnosticGeometry(
        float[] vertices,
        uint[] indices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        if (vertices.Length == 0 ||
            vertices.Length % MapRenderScene.VertexFloatCount != 0)
        {
            throw new InvalidDataException(
                $"vertex payload must contain complete {MapRenderScene.VertexFloatCount}-float position/color vertices");
        }
        if (vertices.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("vertex payload contains a non-finite value");
        if (indices.Length == 0 || indices.Length % 3 != 0)
        {
            throw new InvalidDataException(
                "index payload must contain complete triangle-list draws");
        }
        uint vertexCount = checked((uint)(
            vertices.Length / MapRenderScene.VertexFloatCount));
        if (indices.Any(index => index >= vertexCount))
            throw new InvalidDataException("index payload references a missing vertex");
    }

    private static void ValidateWireframeGeometry(
        float[] vertices,
        uint[] indices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        if (vertices.Length == 0 ||
            vertices.Length % MapRenderScene.VertexFloatCount != 0)
        {
            throw new InvalidDataException(
                $"vertex payload must contain complete {MapRenderScene.VertexFloatCount}-float position/color vertices");
        }
        if (vertices.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("vertex payload contains a non-finite value");
        if (indices.Length == 0 || indices.Length % 2 != 0)
        {
            throw new InvalidDataException(
                "index payload must contain complete line-list draws");
        }
        uint vertexCount = checked((uint)(
            vertices.Length / MapRenderScene.VertexFloatCount));
        if (indices.Any(index => index >= vertexCount))
            throw new InvalidDataException("index payload references a missing vertex");
    }

    private static InvalidDataException InvalidDiagnostic(
        int sourceOrdinal,
        string reason,
        Exception? inner = null) =>
        new(
            $"Cannot freeze diagnostic source ordinal {sourceOrdinal}: {reason}.",
            inner);

    private static InvalidDataException InvalidWireframe(
        string reason,
        Exception? inner = null) =>
        new(
            $"Cannot freeze collision wireframe: {reason}.",
            inner);

    private static RenderVertexLayoutDescriptor CreateSkyVertexLayout() =>
        new(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.VertexLayout,
                "scene.sky.vertex-layout.position-f32x3.stride-24"),
            checked(MapRenderScene.VertexFloatCount * sizeof(float)),
            [
                new RenderVertexElementDescriptor(
                    RenderVertexSemantic.Position,
                    0,
                    RenderVertexElementFormat.Float32x3,
                    0)
            ]);

    public static RenderTextureDescriptor CreateTextureDescriptor(
        Texture texture,
        RenderSemanticIdentity identity,
        bool preferProvenAuthoredPayload = false)
    {
        ValidateTextureSource(texture);
        int arrayLayerCount = texture.Target switch
        {
            TextureTarget.Texture2D => 1,
            TextureTarget.TextureCube => 6,
            _ => throw new InvalidDataException(
                $"unsupported texture target {texture.Target}"),
        };
        Dictionary<(int Face, int Mip), DecodedTextureSubresource>
            decodedBySubresource = CollectDecodedSubresources(texture);
        Dictionary<(int Face, int Mip), TextureAuthoredSubresource>
            authoredBySubresource = [];
        foreach (TextureAuthoredSubresource? authored in
                 texture.EffectiveAuthoredSubresources)
        {
            if (authored is null)
            {
                throw new InvalidDataException(
                    "Authored texture subresources cannot contain null.");
            }
            if (authored.FaceOrdinal >= arrayLayerCount)
            {
                throw new InvalidDataException(
                    $"Authored texture layer {authored.FaceOrdinal} exceeds " +
                    $"the {arrayLayerCount}-layer texture shape.");
            }
            if (!authoredBySubresource.TryAdd(
                    (authored.FaceOrdinal, authored.MipLevel),
                    authored))
            {
                throw new InvalidDataException(
                    $"Authored texture layer {authored.FaceOrdinal}, mip " +
                    $"{authored.MipLevel} is duplicated.");
            }
        }

        (int Face, int Mip)[] coordinates = decodedBySubresource.Keys
            .Concat(authoredBySubresource.Keys)
            .Distinct()
            .ToArray();
        if (coordinates.Length == 0)
        {
            throw new InvalidDataException(
                "Texture has neither authored nor decoded payloads.");
        }
        int mipCount = checked(coordinates.Max(value => value.Mip) + 1);
        var subresources = new List<RenderTextureSubresourceDescriptor>(
            checked(arrayLayerCount * mipCount));
        for (int layer = 0; layer < arrayLayerCount; layer++)
        {
            int width = texture.Width;
            int height = texture.Height;
            for (int mipLevel = 0; mipLevel < mipCount; mipLevel++)
            {
                authoredBySubresource.TryGetValue(
                    (layer, mipLevel),
                    out TextureAuthoredSubresource? authored);
                decodedBySubresource.TryGetValue(
                    (layer, mipLevel),
                    out DecodedTextureSubresource? decoded);
                if (authored is null && decoded is null)
                {
                    throw new InvalidDataException(
                        $"Texture layer {layer}, mip {mipLevel} has no " +
                        "authored or decoded payload.");
                }
                if (authored is not null &&
                    (authored.Width != width || authored.Height != height))
                {
                    throw new InvalidDataException(
                        $"Authored texture layer {layer}, mip {mipLevel} " +
                        "does not match canonical mip dimensions.");
                }
                if (decoded is not null &&
                    (decoded.Width != width || decoded.Height != height))
                {
                    throw new InvalidDataException(
                        $"Decoded texture layer {layer}, mip {mipLevel} " +
                        "does not match canonical mip dimensions.");
                }

                AddTextureSubresource(
                    subresources,
                    mipLevel,
                    arrayLayer: layer,
                    width,
                    height,
                    decoded?.PixelBytes,
                    texture.PixelFormat,
                    authored,
                    preferProvenAuthoredPayload);
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }
        }

        return new RenderTextureDescriptor(
            identity,
            texture.Name,
            texture.Format,
            texture.Target == TextureTarget.TextureCube
                ? RenderTextureDimension.TextureCube
                : RenderTextureDimension.Texture2D,
            texture.Width,
            texture.Height,
            mipCount,
            arrayLayerCount,
            texture.HasTransparency,
            new RenderTextureSourceDescriptor(texture.RsxTextureCommandState),
            subresources);
    }

    private static Dictionary<(int Face, int Mip), DecodedTextureSubresource>
        CollectDecodedSubresources(Texture texture)
    {
        var decoded = new Dictionary<
            (int Face, int Mip),
            DecodedTextureSubresource>();
        if (texture.Target == TextureTarget.Texture2D)
        {
            if (texture.PixelBytes.Length == 0)
            {
                if (texture.MipLevels.Count != 0)
                {
                    throw new InvalidDataException(
                        "A 2D decoded mip chain cannot exist without its base level.");
                }
                return decoded;
            }

            AddDecoded(texture.PixelBytes, texture.Width, texture.Height, 0, 0);
            int width = texture.Width;
            int height = texture.Height;
            for (int mipIndex = 0;
                 mipIndex < texture.MipLevels.Count;
                 mipIndex++)
            {
                TextureMip mip = texture.MipLevels[mipIndex] ??
                    throw new InvalidDataException(
                        $"2D texture mip {mipIndex + 1} is null");
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
                if (mip.Width != width || mip.Height != height)
                {
                    throw new InvalidDataException(
                        $"2D texture mip {mipIndex + 1} has noncanonical dimensions");
                }
                AddDecoded(
                    mip.PixelBytes,
                    width,
                    height,
                    face: 0,
                    mip: mipIndex + 1);
            }
            return decoded;
        }

        if (texture.CubeFaces is null)
        {
            if (texture.PixelBytes.Length != 0 || texture.MipLevels.Count != 0)
            {
                throw new InvalidDataException(
                    "An authored-only cubemap cannot retain orphaned top-level decoded payloads.");
            }
            return decoded;
        }
        if (texture.CubeFaces.Count != 6 ||
            texture.CubeFaces.Any(face => face is null))
        {
            throw new InvalidDataException(
                "Decoded cubemap payload must contain exactly six faces.");
        }

        IReadOnlyList<TextureCubeFace> faces = texture.CubeFaces;
        int expectedMipLevels = faces[0].MipLevels?.Count ??
            throw new InvalidDataException("cubemap face mip list is null");
        for (int layer = 0; layer < faces.Count; layer++)
        {
            TextureCubeFace face = faces[layer];
            if (face.MipLevels is null ||
                face.MipLevels.Count != expectedMipLevels)
            {
                throw new InvalidDataException(
                    "all cubemap faces must contain the same decoded mip count");
            }
            AddDecoded(
                face.RgbaBytes,
                texture.Width,
                texture.Height,
                layer,
                mip: 0);
            int width = texture.Width;
            int height = texture.Height;
            for (int mipIndex = 0; mipIndex < face.MipLevels.Count; mipIndex++)
            {
                TextureMip mip = face.MipLevels[mipIndex] ??
                    throw new InvalidDataException(
                        $"cubemap face {layer} mip {mipIndex + 1} is null");
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
                if (mip.Width != width || mip.Height != height)
                {
                    throw new InvalidDataException(
                        $"cubemap face {layer} mip {mipIndex + 1} has noncanonical dimensions");
                }
                AddDecoded(
                    mip.PixelBytes,
                    width,
                    height,
                    layer,
                    mipIndex + 1);
            }
        }

        TextureCubeFace firstFace = faces[0];
        if (!texture.PixelBytes.AsSpan().SequenceEqual(firstFace.RgbaBytes))
        {
            throw new InvalidDataException(
                "top-level cubemap payload must match face zero");
        }
        if (texture.MipLevels.Count != firstFace.MipLevels.Count)
        {
            throw new InvalidDataException(
                "top-level cubemap mip count must match face zero");
        }
        for (int mipIndex = 0; mipIndex < texture.MipLevels.Count; mipIndex++)
        {
            TextureMip topLevelMip = texture.MipLevels[mipIndex] ??
                throw new InvalidDataException(
                    $"top-level cubemap mip {mipIndex + 1} is null");
            TextureMip faceMip = firstFace.MipLevels[mipIndex];
            if (topLevelMip.Width != faceMip.Width ||
                topLevelMip.Height != faceMip.Height ||
                !topLevelMip.PixelBytes.AsSpan().SequenceEqual(
                    faceMip.PixelBytes))
            {
                throw new InvalidDataException(
                    $"top-level cubemap mip {mipIndex + 1} must match face zero");
            }
        }
        return decoded;

        void AddDecoded(
            byte[] pixelBytes,
            int width,
            int height,
            int face,
            int mip)
        {
            ValidatePixelPayload(
                pixelBytes,
                width,
                height,
                $"texture layer {face}, mip {mip}");
            decoded.Add(
                (face, mip),
                new DecodedTextureSubresource(
                    width,
                    height,
                    pixelBytes));
        }
    }

    private static void AddTextureSubresource(
        ICollection<RenderTextureSubresourceDescriptor> destination,
        int mipLevel,
        int arrayLayer,
        int width,
        int height,
        byte[]? pixelBytes,
        DecodedTexturePixelFormat pixelFormat,
        TextureAuthoredSubresource? authored,
        bool preferProvenAuthoredPayload = false)
    {
        var payloads = new List<RenderTexturePayloadDescriptor>(
            authored is null || pixelBytes is null ? 1 : 2);
        if (authored is not null)
        {
            if (authored.FaceOrdinal != arrayLayer ||
                authored.MipLevel != mipLevel ||
                authored.Width != width ||
                authored.Height != height)
            {
                throw new InvalidDataException(
                    "Authored texture payload metadata does not match its decoded face/mip fallback.");
            }
            payloads.Add(new RenderTexturePayloadDescriptor(
                RenderTexturePayloadKind.Authored,
                authored.Format,
                authored.RowPitchBytes,
                authored.SlicePitchBytes,
                authored.SharedPayload,
                authored.IsDirectUploadLayoutProven));
        }
        if (pixelBytes is not null &&
            !(preferProvenAuthoredPayload &&
              authored?.IsDirectUploadLayoutProven == true))
        {
            int rowPitch = checked(width * 4);
            int slicePitch = checked(rowPitch * height);
            (RenderTexturePayloadKind kind, string format) = pixelFormat switch
            {
                DecodedTexturePixelFormat.Rgba8Unorm =>
                    (RenderTexturePayloadKind.DecodedRgba8,
                     DecodedRgba8Format),
                DecodedTexturePixelFormat.Rg16Float =>
                    (RenderTexturePayloadKind.DecodedRg16Float,
                     DecodedRg16FloatFormat),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(pixelFormat),
                    pixelFormat,
                    null)
            };
            payloads.Add(new RenderTexturePayloadDescriptor(
                kind,
                format,
                rowPitch,
                slicePitch,
                pixelBytes,
                isDirectUploadLayoutProven: true));
        }
        destination.Add(new RenderTextureSubresourceDescriptor(
            mipLevel,
            arrayLayer,
            width,
            height,
            payloads));
    }

    private static void ValidateSkySource(MapRenderSky sky)
    {
        if (!Enum.IsDefined(sky.Source))
            throw new InvalidDataException($"undefined sky source {sky.Source}");
        if (sky.WorldSkyIndex < 0)
            throw new InvalidDataException("world-sky index cannot be negative");
        ArgumentNullException.ThrowIfNull(sky.SkyStartSurfPositions);
        ArgumentNullException.ThrowIfNull(sky.SurfaceIndices);
        if (sky.SkyStartSurfPositions.Count == 0 ||
            sky.SkyStartSurfPositions.Count != sky.SurfaceIndices.Count)
        {
            throw new InvalidDataException(
                "surface-position and resolved-surface lists must be non-empty and have equal lengths");
        }
        if (sky.SkyStartSurfPositions.Any(value => value < 0) ||
            sky.SurfaceIndices.Any(value => value < 0))
        {
            throw new InvalidDataException(
                "surface-position and resolved-surface lists cannot contain negative values");
        }

        ArgumentNullException.ThrowIfNull(sky.Vertices);
        ArgumentNullException.ThrowIfNull(sky.Indices);
        if (sky.Vertices.Length == 0 ||
            sky.Vertices.Length % MapRenderScene.VertexFloatCount != 0)
        {
            throw new InvalidDataException(
                $"vertex payload must contain complete {MapRenderScene.VertexFloatCount}-float vertices");
        }
        if (sky.Vertices.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("vertex payload contains a non-finite value");
        if (sky.Indices.Length == 0 || sky.Indices.Length % 3 != 0)
        {
            throw new InvalidDataException(
                "index payload must contain complete triangle-list draws");
        }
        uint vertexCount = checked((uint)(
            sky.Vertices.Length / MapRenderScene.VertexFloatCount));
        if (sky.Indices.Any(index => index >= vertexCount))
            throw new InvalidDataException("index payload references a missing vertex");

        ArgumentNullException.ThrowIfNull(sky.Texture);
        if (sky.Texture.Target != TextureTarget.TextureCube)
            throw new InvalidDataException("sky texture must be a cubemap");
    }

    private static void ValidateTextureSource(Texture texture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texture.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(texture.Format);
        if (texture.Width <= 0 || texture.Height <= 0)
            throw new InvalidDataException("texture dimensions must be positive");
        if (!Enum.IsDefined(texture.Target))
            throw new InvalidDataException($"undefined texture target {texture.Target}");
        if (texture.Target == TextureTarget.TextureCube &&
            texture.Width != texture.Height)
        {
            throw new InvalidDataException("cubemap faces must be square");
        }
        ArgumentNullException.ThrowIfNull(texture.DecodedSamplerState);
        ArgumentNullException.ThrowIfNull(texture.RsxTextureCommandState);
        if (texture.DecodedSamplerState.RawState != texture.SamplerState)
        {
            throw new InvalidDataException(
                "texture sampler byte does not match the decoded sampler state");
        }
        ArgumentNullException.ThrowIfNull(texture.PixelBytes);
        ArgumentNullException.ThrowIfNull(texture.MipLevels);
        ArgumentNullException.ThrowIfNull(texture.EffectiveAuthoredSubresources);
    }

    private static void ValidatePixelPayload(
        byte[] payload,
        int width,
        int height,
        string label)
    {
        int expectedLength = checked(width * height * 4);
        if (payload.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"{label} has {payload.Length} decoded pixel bytes; expected {expectedLength}");
        }
    }

    private static RenderNormalCameraDrawSnapshot
        AppendNormalCameraDrawResources(
            MapRenderScene scene,
            bool includeAllStaticLodDrawResources,
            bool preferProvenAuthoredTexturePayloads)
    {
        var vertexLayouts = new List<RenderVertexLayoutDescriptor>();
        var instanceLayouts = new List<RenderInstanceLayoutDescriptor>();
        var geometries = new List<RenderGeometryDescriptor>();
        var instances = new List<RenderInstanceDescriptor>();
        var textures = new List<RenderTextureDescriptor>();
        var samplers = new List<RenderSamplerDescriptor>();
        var prepared = new List<RenderNormalCameraPreparedPassSnapshot>();
        var omissions =
            new List<RenderNormalCameraDrawOmissionSnapshot>();
        var groups = new List<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>>();
        var textureCache = new NormalCameraTextureResourceCache(
            preferProvenAuthoredTexturePayloads);

        MapRenderTexturedBatch[] worldBatches;
        if (scene.TexturedBatches is null)
        {
            worldBatches = [];
            omissions.Add(new RenderNormalCameraDrawOmissionSnapshot(
                RenderNormalCameraDrawSourceKind.World,
                sourceOrdinal: null,
                collectionOrdinal: null,
                [RenderNormalCameraDrawOmissionCode.SourceCollectionMissing]));
        }
        else
        {
            worldBatches = scene.TexturedBatches.ToArray();
        }

        bool usesAllStaticLods = includeAllStaticLodDrawResources &&
            CanUseAllStaticLodBatches(scene);
        IReadOnlyList<MapRenderInstancedTexturedBatch>? staticSource =
            usesAllStaticLods
                ? scene.StaticModelLodTexturedBatches
                : scene.InstancedTexturedBatches;
        MapRenderInstancedTexturedBatch[] staticBatches;
        if (staticSource is null)
        {
            staticBatches = [];
            omissions.Add(new RenderNormalCameraDrawOmissionSnapshot(
                RenderNormalCameraDrawSourceKind.StaticModel,
                sourceOrdinal: null,
                collectionOrdinal: null,
                [RenderNormalCameraDrawOmissionCode.SourceCollectionMissing]));
        }
        else
        {
            staticBatches = staticSource.ToArray();
        }

        var worldCollections = new List<NormalCameraWorldCollection>
        {
            new(worldBatches, ReceiverVariant: null)
        };
        var staticCollections = new List<NormalCameraStaticCollection>
        {
            new(staticBatches, ReceiverVariant: null)
        };
        if (scene.ReceiverVariants is { } receiverVariants)
        {
            MapRenderWorldSurfacePageMembership[] worldPages =
            [
                MapRenderWorldSurfacePageMembership.PageZero,
                MapRenderWorldSurfacePageMembership.PageOne
            ];
            MapRenderStaticModelReceiverPage[] staticPages =
            [
                MapRenderStaticModelReceiverPage.StaticModelRigidPage2,
                MapRenderStaticModelReceiverPage
                    .StaticModelRigidNoSunShadowPage3
            ];
            MapRenderTechniqueVariantAllocation[] allocations =
            [
                MapRenderTechniqueVariantAllocation.Unshadowed,
                MapRenderTechniqueVariantAllocation.ShadowMapAllocated
            ];
            foreach (MapRenderWorldSurfacePageMembership page in worldPages)
            foreach (MapRenderTechniqueVariantAllocation allocation in
                     allocations)
            {
                var key = new MapRenderWorldReceiverVariantKey(
                    page,
                    allocation);
                worldCollections.Add(new(
                    receiverVariants.GetWorldBatches(page, allocation),
                    key));
            }
            foreach (MapRenderStaticModelReceiverPage page in staticPages)
            foreach (MapRenderTechniqueVariantAllocation allocation in
                     allocations)
            {
                var key = new MapRenderStaticModelReceiverVariantKey(
                    page,
                    allocation);
                staticCollections.Add(new(
                    receiverVariants.GetStaticModelBatches(page, allocation),
                    key));
            }
        }

        int worldSourceCount = worldCollections.Sum(collection =>
            collection.Batches.Count);
        int staticSourceCount = staticCollections.Sum(collection =>
            collection.Batches.Count);
        int worldCollectionOffset = 0;
        foreach (NormalCameraWorldCollection collection in worldCollections)
        {
            AppendNormalCameraWorldGroups(
                collection.Batches,
                worldCollectionOffset,
                collection.ReceiverVariant,
                textureCache,
                prepared,
                omissions,
                groups,
                vertexLayouts,
                instanceLayouts,
                geometries,
                instances,
                textures,
                samplers);
            worldCollectionOffset = checked(
                worldCollectionOffset + collection.Batches.Count);
        }

        int staticCollectionOffset = 0;
        long staticOutputOrdinal = 0;
        foreach (NormalCameraStaticCollection collection in staticCollections)
        {
            AppendNormalCameraStaticGroups(
                collection.Batches,
                worldSourceCount,
                staticCollectionOffset,
                ref staticOutputOrdinal,
                collection.ReceiverVariant,
                textureCache,
                prepared,
                omissions,
                groups,
                vertexLayouts,
                instanceLayouts,
                geometries,
                instances,
                textures,
                samplers);
            staticCollectionOffset = checked(
                staticCollectionOffset + collection.Batches.Count);
        }

        return new RenderNormalCameraDrawSnapshot(
            usesAllStaticLods
                ? RenderNormalCameraDrawCoverage
                    .PreparedWorldAndAllStaticLodBatchesWithoutDpvsSelection
                : RenderNormalCameraDrawCoverage
                    .PreparedWorldAndCurrentStaticBatchesWithoutDynamicLodOrDpvs,
            new RenderResourceSnapshot(
                vertexLayouts,
                instanceLayouts,
                geometries,
                instances,
                textures,
                samplers),
            worldSourceCount,
            staticSourceCount,
            prepared,
            omissions
                .OrderBy(value => value.SourceOrdinal ?? int.MaxValue)
                .ThenBy(value => value.CollectionOrdinal ?? int.MaxValue),
            groups.OrderBy(value => value.SourceOrdinal));
    }

    /// <summary>
    /// Exact shared source-selection rule formerly owned by OpenGL. Every
    /// all-LOD object must either have scheduling metadata or retain its
    /// prepared fallback LOD in the all-LOD collection.
    /// </summary>
    internal static bool CanUseAllStaticLodBatches(MapRenderScene scene)
    {
        IReadOnlyList<MapRenderInstancedTexturedBatch>? allLodBatches =
            scene.StaticModelLodTexturedBatches;
        if (allLodBatches is not { Count: > 0 })
            return false;

        var preparedStaticObjectLods = new Dictionary<int, int>();
        foreach (MapRenderInstancedTexturedBatch batch in
                 scene.InstancedTexturedBatches)
        {
            foreach (MapRenderStaticModelInstance instance in batch.Instances)
            {
                if ((uint)batch.LodIndex >= 32u ||
                    preparedStaticObjectLods.TryGetValue(
                        instance.ObjectIndex,
                        out int existingLod) &&
                    existingLod != batch.LodIndex)
                {
                    return false;
                }
                preparedStaticObjectLods.TryAdd(
                    instance.ObjectIndex,
                    batch.LodIndex);
            }
        }

        HashSet<(int ObjectIndex, int LodIndex)> allLodObjectRows =
            allLodBatches
                .SelectMany(batch => batch.Instances.Select(instance => (
                    instance.ObjectIndex,
                    batch.LodIndex)))
                .ToHashSet();
        return preparedStaticObjectLods.All(pair =>
                   allLodObjectRows.Contains((pair.Key, pair.Value))) &&
            allLodBatches.All(batch =>
                (uint)batch.LodIndex < 32u &&
                batch.Instances.All(instance =>
                    preparedStaticObjectLods.TryGetValue(
                        instance.ObjectIndex,
                        out int fallbackLod) &&
                    fallbackLod >= 0));
    }

    private static void AppendNormalCameraWorldGroups(
        IReadOnlyList<MapRenderTexturedBatch> sourceBatches,
        int collectionOrdinalOffset,
        MapRenderWorldReceiverVariantKey? receiverVariant,
        NormalCameraTextureResourceCache textureCache,
        ICollection<RenderNormalCameraPreparedPassSnapshot> prepared,
        ICollection<RenderNormalCameraDrawOmissionSnapshot> omissions,
        ICollection<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> groups,
        ICollection<RenderVertexLayoutDescriptor> vertexLayouts,
        ICollection<RenderInstanceLayoutDescriptor> instanceLayouts,
        ICollection<RenderGeometryDescriptor> geometries,
        ICollection<RenderInstanceDescriptor> instances,
        ICollection<RenderTextureDescriptor> textures,
        ICollection<RenderSamplerDescriptor> samplers)
    {
        NormalCameraWorldSourceEntry[] entries = sourceBatches
            .Select((batch, collectionOrdinal) =>
                new NormalCameraWorldSourceEntry(
                    checked(collectionOrdinalOffset + collectionOrdinal),
                    batch,
                    CreateNormalCameraWorldGroupKey(
                        batch,
                        checked(collectionOrdinalOffset +
                            collectionOrdinal))))
            .ToArray();

        foreach (IGrouping<NormalCameraWorldGroupKey,
                     NormalCameraWorldSourceEntry> sourceGroup in entries
                 .GroupBy(value => value.GroupKey)
                 .OrderBy(group => group.Min(value =>
                     value.CollectionOrdinal)))
        {
            NormalCameraWorldSourceEntry[] orderedEntries = sourceGroup
                .OrderBy(value => value.Batch?.Pass?.TechniquePass.PassIndex ??
                    int.MaxValue)
                .ThenBy(value => value.CollectionOrdinal)
                .ToArray();
            int authoredSourceOrdinal = orderedEntries.Min(value =>
                value.CollectionOrdinal);
            var candidates = new List<NormalCameraDrawCandidate>(
                orderedEntries.Length);
            var failures = new Dictionary<int,
                IReadOnlyList<RenderNormalCameraDrawOmissionCode>>();
            foreach (NormalCameraWorldSourceEntry entry in orderedEntries)
            {
                IReadOnlyList<RenderNormalCameraDrawOmissionCode> codes =
                    ValidateNormalCameraWorldSource(entry.Batch);
                if (codes.Count != 0)
                {
                    failures.Add(entry.CollectionOrdinal, codes);
                    continue;
                }

                try
                {
                    candidates.Add(CreateNormalCameraWorldCandidate(
                        entry.Batch!,
                        receiverVariant,
                        authoredSourceOrdinal,
                        entry.CollectionOrdinal,
                        textureCache));
                }
                catch (Exception exception) when (
                    exception is ArgumentException or
                    InvalidDataException or
                    InvalidOperationException or
                    OverflowException)
                {
                    failures.Add(
                        entry.CollectionOrdinal,
                        [RenderNormalCameraDrawOmissionCode
                            .ResourceSnapshotCreationFailed]);
                }
            }

            if (failures.Count != 0)
            {
                foreach (NormalCameraWorldSourceEntry entry in orderedEntries)
                {
                    IReadOnlyList<RenderNormalCameraDrawOmissionCode> codes =
                        failures.TryGetValue(
                            entry.CollectionOrdinal,
                            out IReadOnlyList<
                                RenderNormalCameraDrawOmissionCode>? failure)
                            ? failure
                            : [RenderNormalCameraDrawOmissionCode
                                .AuthoredPassGroupIncomplete];
                    omissions.Add(new RenderNormalCameraDrawOmissionSnapshot(
                        RenderNormalCameraDrawSourceKind.World,
                        authoredSourceOrdinal,
                        entry.CollectionOrdinal,
                        codes,
                        worldReceiverVariant: receiverVariant));
                }
                continue;
            }

            MapRenderEditorDrawBucketClassification classification =
                MapRenderEditorDrawBucketClassifier.Classify(
                    candidates.Select(value =>
                        value.Pass.SourceState).ToArray());
            RenderBounds bounds = candidates.Aggregate(
                RenderBounds.Empty,
                (current, value) => IncludeNormalCameraBounds(
                    current,
                    value.Pass.LocalBounds));
            if (!bounds.IsValid)
            {
                foreach (NormalCameraDrawCandidate candidate in candidates)
                {
                    omissions.Add(new RenderNormalCameraDrawOmissionSnapshot(
                        RenderNormalCameraDrawSourceKind.World,
                        authoredSourceOrdinal,
                        candidate.Pass.CollectionOrdinal,
                        [RenderNormalCameraDrawOmissionCode
                            .GeometryMissingOrMalformed],
                        worldReceiverVariant: receiverVariant));
                }
                continue;
            }

            RenderNormalCameraDrawSubmissionSnapshot[] submissions = candidates
                .Select(candidate =>
                    CreateNormalCameraSubmission(
                        candidate.Pass,
                        authoredSourceOrdinal,
                        staticInstanceIndex: null))
                .ToArray();
            foreach (NormalCameraDrawCandidate candidate in candidates)
            {
                CommitNormalCameraCandidate(
                    candidate,
                    textureCache,
                    prepared,
                    vertexLayouts,
                    instanceLayouts,
                    geometries,
                    instances,
                    textures,
                    samplers);
            }
            groups.Add(MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot>.FromBounds(
                    authoredSourceOrdinal,
                    classification,
                    submissions,
                    bounds));
        }
    }

    private static void AppendNormalCameraStaticGroups(
        IReadOnlyList<MapRenderInstancedTexturedBatch> sourceBatches,
        int worldSourceCount,
        int collectionOrdinalOffset,
        ref long staticOutputOrdinal,
        MapRenderStaticModelReceiverVariantKey? receiverVariant,
        NormalCameraTextureResourceCache textureCache,
        ICollection<RenderNormalCameraPreparedPassSnapshot> prepared,
        ICollection<RenderNormalCameraDrawOmissionSnapshot> omissions,
        ICollection<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> groups,
        ICollection<RenderVertexLayoutDescriptor> vertexLayouts,
        ICollection<RenderInstanceLayoutDescriptor> instanceLayouts,
        ICollection<RenderGeometryDescriptor> geometries,
        ICollection<RenderInstanceDescriptor> instances,
        ICollection<RenderTextureDescriptor> textures,
        ICollection<RenderSamplerDescriptor> samplers)
    {
        NormalCameraStaticSourceEntry[] entries = sourceBatches
            .Select((batch, collectionOrdinal) =>
                new NormalCameraStaticSourceEntry(
                    checked(collectionOrdinalOffset + collectionOrdinal),
                    batch,
                    batch?.EditorDrawGroupId >= 0
                        ? batch.EditorDrawGroupId
                        : int.MaxValue - checked(
                            collectionOrdinalOffset + collectionOrdinal)))
            .ToArray();
        foreach (IGrouping<int, NormalCameraStaticSourceEntry> sourceGroup in
                 entries.GroupBy(value => value.DrawGroupId)
                     .OrderBy(group => group.Min(value =>
                         value.CollectionOrdinal)))
        {
            NormalCameraStaticSourceEntry[] orderedEntries = sourceGroup
                .OrderBy(value => value.Batch?.Pass?.TechniquePass.PassIndex ??
                    int.MaxValue)
                .ThenBy(value => value.CollectionOrdinal)
                .ToArray();
            int authoredSourceOrdinal = checked(
                worldSourceCount + orderedEntries.Min(value =>
                    value.CollectionOrdinal));
            var candidates = new List<NormalCameraDrawCandidate>(
                orderedEntries.Length);
            var failures = new Dictionary<int,
                IReadOnlyList<RenderNormalCameraDrawOmissionCode>>();
            foreach (NormalCameraStaticSourceEntry entry in orderedEntries)
            {
                IReadOnlyList<RenderNormalCameraDrawOmissionCode> codes =
                    ValidateNormalCameraStaticSource(entry.Batch);
                if (codes.Count != 0)
                {
                    failures.Add(entry.CollectionOrdinal, codes);
                    continue;
                }

                try
                {
                    candidates.Add(CreateNormalCameraStaticCandidate(
                        entry.Batch!,
                        receiverVariant,
                        authoredSourceOrdinal,
                        entry.CollectionOrdinal,
                        textureCache));
                }
                catch (Exception exception) when (
                    exception is ArgumentException or
                    InvalidDataException or
                    InvalidOperationException or
                    OverflowException)
                {
                    failures.Add(
                        entry.CollectionOrdinal,
                        [RenderNormalCameraDrawOmissionCode
                            .ResourceSnapshotCreationFailed]);
                }
            }

            if (failures.Count != 0)
            {
                foreach (NormalCameraStaticSourceEntry entry in orderedEntries)
                {
                    IReadOnlyList<RenderNormalCameraDrawOmissionCode> codes =
                        failures.TryGetValue(
                            entry.CollectionOrdinal,
                            out IReadOnlyList<
                                RenderNormalCameraDrawOmissionCode>? failure)
                            ? failure
                            : [RenderNormalCameraDrawOmissionCode
                                .AuthoredPassGroupIncomplete];
                    omissions.Add(new RenderNormalCameraDrawOmissionSnapshot(
                        RenderNormalCameraDrawSourceKind.StaticModel,
                        authoredSourceOrdinal,
                        entry.CollectionOrdinal,
                        codes,
                        staticReceiverVariant: receiverVariant));
                }
                continue;
            }

            NormalCameraDrawCandidate first = candidates[0];
            if (candidates.Skip(1).Any(value =>
                    !value.Pass.Geometry.VertexPayload.SequenceEqual(
                        first.Pass.Geometry.VertexPayload) ||
                    !value.Pass.Geometry.IndexPayload.SequenceEqual(
                        first.Pass.Geometry.IndexPayload)))
            {
                OmitNormalCameraStaticGroup(
                    candidates,
                    authoredSourceOrdinal,
                    RenderNormalCameraDrawOmissionCode
                        .StaticGroupGeometryMismatch,
                    omissions);
                continue;
            }
            if (candidates.Skip(1).Any(value =>
                    !value.Pass.StaticInstances.SequenceEqual(
                        first.Pass.StaticInstances)))
            {
                OmitNormalCameraStaticGroup(
                    candidates,
                    authoredSourceOrdinal,
                    RenderNormalCameraDrawOmissionCode
                        .StaticGroupInstanceOwnershipMismatch,
                    omissions);
                continue;
            }

            IReadOnlyList<MapRenderEditorStaticDrawPlan> plans;
            try
            {
                plans = MapRenderEditorStaticDrawPlanner.Create(
                    candidates.Select(value =>
                        new MapRenderEditorStaticPassBatch(
                            value.Pass.CollectionOrdinal,
                            sourceGroup.Key,
                            value.Pass.SourcePass.PassIndex,
                            value.Pass.StaticInstances.Length,
                            value.Pass.SourceState)).ToArray());
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException or
                OverflowException)
            {
                OmitNormalCameraStaticGroup(
                    candidates,
                    authoredSourceOrdinal,
                    RenderNormalCameraDrawOmissionCode
                        .StaticGroupPlanningFailed,
                    omissions);
                continue;
            }

            if (MapRenderOpenGlStaticCameraRegionPolicy
                .SuppressNormalCameraGroup(
                    candidates.Select(value =>
                        value.Pass.StaticCameraRegion).ToArray()))
            {
                OmitNormalCameraStaticGroup(
                    candidates,
                    authoredSourceOrdinal,
                    RenderNormalCameraDrawOmissionCode
                        .AuxiliaryCameraRegionOnly,
                    omissions);
                continue;
            }
            if (plans.Count == 0)
            {
                OmitNormalCameraStaticGroup(
                    candidates,
                    authoredSourceOrdinal,
                    RenderNormalCameraDrawOmissionCode
                        .StaticGroupPlanningFailed,
                    omissions);
                continue;
            }

            var candidateByCollectionOrdinal = candidates.ToDictionary(
                value => value.Pass.CollectionOrdinal);
            var pendingGroups = new List<MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot>>(plans.Count);
            bool groupFailed = false;
            foreach (MapRenderEditorStaticDrawPlan plan in plans)
            {
                long scheduledSourceOrdinal = checked(
                    worldSourceCount + staticOutputOrdinal);
                RenderNormalCameraDrawSubmissionSnapshot[] submissions = plan
                    .PassSourceOrdinals
                    .Select(collectionOrdinal =>
                        CreateNormalCameraSubmission(
                            candidateByCollectionOrdinal[collectionOrdinal]
                                .Pass,
                            scheduledSourceOrdinal,
                            plan.InstanceIndex))
                    .ToArray();
                RenderBounds bounds = CalculateNormalCameraStaticBounds(
                    first.Pass.LocalBounds,
                    first.Pass.StaticInstances,
                    plan.InstanceIndex);
                if (!bounds.IsValid)
                {
                    groupFailed = true;
                    break;
                }
                pendingGroups.Add(MapRenderEditorDrawGroup<
                    RenderNormalCameraDrawSubmissionSnapshot>.FromBounds(
                        scheduledSourceOrdinal,
                        plan.Classification,
                        submissions,
                        bounds));
                staticOutputOrdinal++;
            }
            if (groupFailed)
            {
                staticOutputOrdinal -= pendingGroups.Count;
                OmitNormalCameraStaticGroup(
                    candidates,
                    authoredSourceOrdinal,
                    RenderNormalCameraDrawOmissionCode
                        .GeometryMissingOrMalformed,
                    omissions);
                continue;
            }

            foreach (NormalCameraDrawCandidate candidate in candidates)
            {
                CommitNormalCameraCandidate(
                    candidate,
                    textureCache,
                    prepared,
                    vertexLayouts,
                    instanceLayouts,
                    geometries,
                    instances,
                    textures,
                    samplers);
            }
            foreach (MapRenderEditorDrawGroup<
                         RenderNormalCameraDrawSubmissionSnapshot> group in
                     pendingGroups)
            {
                groups.Add(group);
            }
        }
    }

    private static NormalCameraDrawCandidate
        CreateNormalCameraWorldCandidate(
            MapRenderTexturedBatch source,
            MapRenderWorldReceiverVariantKey? receiverVariant,
            int authoredSourceOrdinal,
            int collectionOrdinal,
            NormalCameraTextureResourceCache textureCache) =>
        CreateNormalCameraCandidate(
            RenderNormalCameraDrawSourceKind.World,
            receiverVariant,
            staticReceiverVariant: null,
            source.Pass,
            source.PrimarySampler,
            source.Texture,
            source.LightmapTexture,
            source.ColorLayers,
            source.MaterialSamplers.Select(binding => binding.Binding).ToArray(),
            source.MaterialSamplers
                .Select(binding => binding.RuntimeTextureIdentity)
                .ToArray(),
            source.ShaderExecution,
            source.ShaderExecutionStatus,
            source.UvRoute,
            source.State,
            source.SceneLightIndex,
            source.UnresolvedCodeSamplerCount,
            source.PickRanges,
            source.Vertices,
            source.RsxVertexInputs,
            source.Indices,
            staticInstances: [],
            editorDrawGroupId: null,
            lodIndex: null,
            depthPrepass: source.EditorDepthPrepass,
            depthPrepassShader: source.DepthPrepassShaderExecution,
            vegetationAnimation: null,
            authoredSourceOrdinal,
            collectionOrdinal,
            textureCache);

    private static NormalCameraDrawCandidate
        CreateNormalCameraStaticCandidate(
            MapRenderInstancedTexturedBatch source,
            MapRenderStaticModelReceiverVariantKey? receiverVariant,
            int authoredSourceOrdinal,
            int collectionOrdinal,
            NormalCameraTextureResourceCache textureCache) =>
        CreateNormalCameraCandidate(
            RenderNormalCameraDrawSourceKind.StaticModel,
            worldReceiverVariant: null,
            receiverVariant,
            source.Pass,
            source.PrimarySampler,
            source.Texture,
            lightmapTexture: null,
            source.ColorLayers,
            source.MaterialSamplers.Select(binding => binding.Binding).ToArray(),
            source.MaterialSamplers
                .Select(binding => binding.RuntimeTextureIdentity)
                .ToArray(),
            source.ShaderExecution,
            source.ShaderExecution.ProgramExecutionStatus,
            source.UvRoute,
            source.State,
            source.SceneLightIndex,
            source.UnresolvedCodeSamplerCount,
            pickRanges: [],
            source.Vertices,
            source.RsxVertexInputs,
            source.Indices,
            source.Instances,
            source.EditorDrawGroupId,
            source.LodIndex,
            source.EditorDepthPrepass,
            source.DepthPrepassShaderExecution,
            source.EditorVegetationAnimation,
            authoredSourceOrdinal,
            collectionOrdinal,
            textureCache);

    private static NormalCameraDrawCandidate CreateNormalCameraCandidate(
        RenderNormalCameraDrawSourceKind sourceKind,
        MapRenderWorldReceiverVariantKey? worldReceiverVariant,
        MapRenderStaticModelReceiverVariantKey? staticReceiverVariant,
        MaterialPassIdentity pass,
        MaterialSamplerIdentity primarySampler,
        Texture baseTexture,
        Texture? lightmapTexture,
        IReadOnlyList<MaterialColorLayer> colorLayers,
        IReadOnlyList<MaterialSamplerBinding> materialSamplers,
        IReadOnlyList<MapRenderWorldRuntimeTextureIdentity?>
            runtimeTextureIdentities,
        ShaderExecutionContract shader,
        string shaderExecutionStatus,
        UvRoute uvRoute,
        RenderState state,
        byte sceneLightIndex,
        int unresolvedCodeSamplerCount,
        IReadOnlyList<MapRenderPickRange> pickRanges,
        float[] vertices,
        float[] rsxVertexInputs,
        uint[] indices,
        IReadOnlyList<MapRenderStaticModelInstance> staticInstances,
        int? editorDrawGroupId,
        int? lodIndex,
        MapRenderEditorDepthPrepassPlan? depthPrepass,
        ShaderExecutionContract? depthPrepassShader,
        MapRenderEditorVegetationAnimationPlan? vegetationAnimation,
        int authoredSourceOrdinal,
        int collectionOrdinal,
        NormalCameraTextureResourceCache textureCache)
    {
        string prefix = string.Concat(
            "scene.normal-camera.",
            sourceKind == RenderNormalCameraDrawSourceKind.World
                ? "world."
                : "static.",
            collectionOrdinal.ToString("D8", CultureInfo.InvariantCulture));
        NormalCameraGeometryResource geometryResource =
            textureCache.GetOrCreateGeometry(vertices, indices);
        RenderVertexLayoutDescriptor vertexLayout = geometryResource.Layout;
        RenderGeometryDescriptor geometry = geometryResource.Geometry;
        NormalCameraRsxVertexInputsResource rsxInputsResource =
            textureCache.GetOrCreateRsxVertexInputs(rsxVertexInputs);

        RenderInstanceLayoutDescriptor? instanceLayout = null;
        RenderInstanceDescriptor? instanceDescriptor = null;
        ImmutableArray<MapRenderStaticModelInstance> frozenStaticInstances =
            ImmutableArray<MapRenderStaticModelInstance>.Empty;
        string staticInstancesContentDigest =
            textureCache.EmptyStaticInstancesContentDigest;
        GfxCameraRegionType? staticCameraRegion = null;
        if (sourceKind == RenderNormalCameraDrawSourceKind.StaticModel)
        {
            NormalCameraStaticInstancesResource staticResource =
                textureCache.GetOrCreateStaticInstances(staticInstances);
            frozenStaticInstances = staticResource.Instances;
            staticInstancesContentDigest = staticResource.ContentDigest;
            staticCameraRegion = staticResource.CameraRegion;
            instanceLayout = staticResource.Layout;
            instanceDescriptor = staticResource.Descriptor;
        }

        var sourceTextures = new List<Texture>();
        var textureOrdinals = new Dictionary<Texture, int>(
            ReferenceEqualityComparer.Instance);
        AddTexture(baseTexture);
        AddTexture(lightmapTexture);
        foreach (MaterialColorLayer layer in colorLayers)
            AddTexture(layer.Texture);
        foreach (MaterialSamplerBinding sampler in materialSamplers)
            AddTexture(sampler.Texture);

        var textureResources =
            new List<RenderNormalCameraTextureResourceSnapshot>(
                sourceTextures.Count);
        for (int ordinal = 0; ordinal < sourceTextures.Count; ordinal++)
        {
            Texture sourceTexture = sourceTextures[ordinal];
            textureResources.Add(textureCache.GetOrCreate(sourceTexture));
        }

        RenderNormalCameraTextureResourceSnapshot Resource(
            Texture texture) =>
            textureResources[textureOrdinals[texture]];

        RenderNormalCameraTextureResourceSnapshot baseResource =
            Resource(baseTexture);
        RenderNormalCameraTextureResourceSnapshot? lightmapResource =
            lightmapTexture is null ? null : Resource(lightmapTexture);
        RenderNormalCameraColorLayerSnapshot[] frozenLayers = colorLayers
            .Select(layer => new RenderNormalCameraColorLayerSnapshot(
                layer,
                Resource(layer.Texture)))
            .ToArray();
        if (runtimeTextureIdentities.Count != materialSamplers.Count)
        {
            throw new ArgumentException(
                "Normal-camera sampler identities must remain aligned with bindings.",
                nameof(runtimeTextureIdentities));
        }
        RenderNormalCameraMaterialSamplerSnapshot[] frozenSamplers =
            materialSamplers
                .Select((sampler, index) =>
                    new RenderNormalCameraMaterialSamplerSnapshot(
                        sampler,
                        runtimeTextureIdentities[index],
                        sampler.Texture is null
                            ? null
                            : Resource(sampler.Texture)))
                .ToArray();
        RenderWorldShaderProvenanceSnapshot? depthShaderProvenance =
            depthPrepassShader is null
                ? null
                : new RenderWorldShaderProvenanceSnapshot(
                    depthPrepassShader,
                    depthPrepassShader.ProgramExecutionStatus);
        RenderBounds localBounds = geometryResource.LocalBounds;
        var preparedPass = new RenderNormalCameraPreparedPassSnapshot(
            sourceKind,
            worldReceiverVariant,
            staticReceiverVariant,
            authoredSourceOrdinal,
            collectionOrdinal,
            editorDrawGroupId,
            lodIndex,
            new RenderMaterialPassProvenanceSnapshot(
                pass,
                primarySampler,
                (byte)uvRoute.TexCoordSource),
            new RenderMaterialUvRouteSnapshot(uvRoute),
            state,
            sceneLightIndex,
            unresolvedCodeSamplerCount,
            new RenderWorldShaderProvenanceSnapshot(
                shader,
                shaderExecutionStatus),
            MapRenderGenericMaterialFallbackContract.Create(
                sourceKind,
                shader,
                colorLayers),
            depthPrepass,
            depthShaderProvenance,
            vegetationAnimation,
            frozenLayers,
            frozenSamplers,
            pickRanges.Select(range =>
                new RenderMaterialPickRangeSnapshot(range)),
            frozenStaticInstances,
            staticInstancesContentDigest,
            staticCameraRegion,
            textureResources,
            baseResource.TextureIdentity,
            baseResource.SamplerIdentity,
            lightmapResource?.TextureIdentity,
            lightmapResource?.SamplerIdentity,
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Draw,
                prefix + ".source-draw"),
            vertexLayout,
            geometry,
            instanceLayout,
            instanceDescriptor,
            rsxInputsResource.Values,
            rsxInputsResource.ContentDigest,
            localBounds);
        return new NormalCameraDrawCandidate(
            preparedPass,
            vertexLayout,
            geometry,
            instanceLayout,
            instanceDescriptor,
            textureResources);

        void AddTexture(Texture? texture)
        {
            if (texture is null || textureOrdinals.ContainsKey(texture))
                return;
            textureOrdinals.Add(texture, sourceTextures.Count);
            sourceTextures.Add(texture);
        }
    }

    private static RenderNormalCameraDrawSubmissionSnapshot
        CreateNormalCameraSubmission(
            RenderNormalCameraPreparedPassSnapshot pass,
            long scheduledSourceOrdinal,
            int? staticInstanceIndex)
    {
        int firstInstance = staticInstanceIndex ?? 0;
        int instanceCount = pass.SourceKind ==
            RenderNormalCameraDrawSourceKind.World
                ? 1
                : staticInstanceIndex.HasValue
                    ? 1
                    : pass.StaticInstances.Length;
        return new RenderNormalCameraDrawSubmissionSnapshot(
            new RenderSemanticIdentity(
                RenderSemanticResourceKind.Draw,
                string.Concat(
                    "scene.normal-camera.scheduled.",
                    scheduledSourceOrdinal.ToString(
                        "D8",
                        CultureInfo.InvariantCulture),
                    ".pass.",
                    pass.CollectionOrdinal.ToString(
                        "D8",
                        CultureInfo.InvariantCulture),
                    staticInstanceIndex is { } instanceIndex
                        ? ".instance." + instanceIndex.ToString(
                            "D8",
                            CultureInfo.InvariantCulture)
                        : string.Empty)),
            pass,
            new RenderDrawRange(
                firstIndex: 0,
                pass.Geometry.IndexCount,
                baseVertex: 0,
                firstInstance,
                instanceCount),
            staticInstanceIndex);
    }

    private static IReadOnlyList<RenderNormalCameraDrawOmissionCode>
        ValidateNormalCameraWorldSource(MapRenderTexturedBatch? source)
    {
        var codes = new List<RenderNormalCameraDrawOmissionCode>();
        ValidateNormalCameraCommonSource(
            source?.Pass,
            source?.Texture,
            source?.ColorLayers,
            source?.MaterialSamplers?.Select(binding => binding.Binding).ToArray(),
            source?.ShaderExecution,
            source?.UvRoute,
            source?.Vertices,
            source?.RsxVertexInputs,
            source?.Indices,
            codes,
            source is null);
        if (source?.PickRanges is null)
        {
            Add(RenderNormalCameraDrawOmissionCode
                .MaterialBindingCollectionMissing);
        }
        else if (source is not null && source.PickRanges.Any(range =>
                     range.FirstIndex < 0 || range.IndexCount <= 0 ||
                     (long)range.FirstIndex + range.IndexCount >
                         source.Indices.Length ||
                     string.IsNullOrEmpty(range.Name) ||
                     range.AuthoredMaterialName is null))
        {
            Add(RenderNormalCameraDrawOmissionCode
                .GeometryMissingOrMalformed);
        }
        return codes;

        void Add(RenderNormalCameraDrawOmissionCode code)
        {
            if (!codes.Contains(code))
                codes.Add(code);
        }
    }

    private static IReadOnlyList<RenderNormalCameraDrawOmissionCode>
        ValidateNormalCameraStaticSource(
            MapRenderInstancedTexturedBatch? source)
    {
        var codes = new List<RenderNormalCameraDrawOmissionCode>();
        ValidateNormalCameraCommonSource(
            source?.Pass,
            source?.Texture,
            source?.ColorLayers,
            source?.MaterialSamplers?.Select(binding => binding.Binding).ToArray(),
            source?.ShaderExecution,
            source?.UvRoute,
            source?.Vertices,
            source?.RsxVertexInputs,
            source?.Indices,
            codes,
            source is null);
        if (source?.Instances is not { Count: > 0 } staticInstances ||
            staticInstances.Any(instance =>
                instance.ObjectIndex < 0 ||
                instance.SurfaceIndex < 0 ||
                string.IsNullOrWhiteSpace(instance.Name) ||
                string.IsNullOrWhiteSpace(instance.AuthoredMaterialName) ||
                !Finite(instance.TransformRow0) ||
                !Finite(instance.TransformRow1) ||
                !Finite(instance.TransformRow2) ||
                !Finite(instance.BaseLightingCoords) ||
                !Finite(instance.LightProbeAmbient)))
        {
            Add(RenderNormalCameraDrawOmissionCode
                .StaticInstancesMissingOrMalformed);
        }
        return codes;

        void Add(RenderNormalCameraDrawOmissionCode code)
        {
            if (!codes.Contains(code))
                codes.Add(code);
        }
    }

    private static void ValidateNormalCameraCommonSource(
        MaterialPassIdentity? pass,
        Texture? texture,
        IReadOnlyList<MaterialColorLayer>? colorLayers,
        IReadOnlyList<MaterialSamplerBinding>? materialSamplers,
        ShaderExecutionContract? shader,
        UvRoute? uvRoute,
        float[]? vertices,
        float[]? rsxVertexInputs,
        uint[]? indices,
        ICollection<RenderNormalCameraDrawOmissionCode> codes,
        bool isNull)
    {
        void Add(RenderNormalCameraDrawOmissionCode code)
        {
            if (!codes.Contains(code))
                codes.Add(code);
        }

        if (isNull)
        {
            Add(RenderNormalCameraDrawOmissionCode.NullBatch);
            return;
        }
        if (pass is null)
            Add(RenderNormalCameraDrawOmissionCode.PassMissing);
        if (texture is null)
            Add(RenderNormalCameraDrawOmissionCode.TextureMissing);
        if (uvRoute is null)
            Add(RenderNormalCameraDrawOmissionCode.UvRouteMissing);
        if (shader is null)
            Add(RenderNormalCameraDrawOmissionCode.ShaderExecutionMissing);
        if (colorLayers is null || materialSamplers is null ||
            colorLayers.Any(layer =>
                layer is null || layer.Texture is null ||
                layer.UvRoute is null) ||
            materialSamplers.Any(sampler => sampler is null))
        {
            Add(RenderNormalCameraDrawOmissionCode
                .MaterialBindingCollectionMissing);
        }
        if (!HasValidNormalCameraGeometry(vertices, indices))
        {
            Add(RenderNormalCameraDrawOmissionCode
                .GeometryMissingOrMalformed);
        }
        if (!HasValidNormalCameraRsxVertexInputs(
                vertices,
                rsxVertexInputs))
        {
            Add(RenderNormalCameraDrawOmissionCode
                .RsxVertexInputPayloadMalformed);
        }
    }

    private static bool HasValidNormalCameraGeometry(
        float[]? vertices,
        uint[]? indices)
    {
        if (vertices is not { Length: > 0 } ||
            indices is not { Length: > 0 } ||
            vertices.Length % MapRenderScene.TexturedVertexFloatCount != 0 ||
            indices.Length % 3 != 0 ||
            vertices.Any(value => !float.IsFinite(value)))
        {
            return false;
        }
        int vertexCount = vertices.Length /
            MapRenderScene.TexturedVertexFloatCount;
        return indices.All(index => index < vertexCount) &&
            IncludeNormalCameraVertexBounds(
                RenderBounds.Empty,
                vertices).IsValid;
    }

    private static bool HasValidNormalCameraRsxVertexInputs(
        float[]? vertices,
        float[]? rsxVertexInputs)
    {
        if (rsxVertexInputs is null)
            return false;
        if (rsxVertexInputs.Length == 0)
            return true;
        if (vertices is null)
            return false;
        int vertexCount = vertices.Length /
            MapRenderScene.TexturedVertexFloatCount;
        return rsxVertexInputs.Length == checked(
            vertexCount *
            RenderWorldDrawPacketSnapshot.RsxVertexInputFloatStride);
    }

    private static NormalCameraWorldGroupKey
        CreateNormalCameraWorldGroupKey(
            MapRenderTexturedBatch? batch,
            int collectionOrdinal)
    {
        MaterialPassIdentity? pass = batch?.Pass;
        if (pass is null)
        {
            return new NormalCameraWorldGroupKey(
                "<invalid>",
                "<invalid>",
                int.MinValue,
                "<invalid>",
                byte.MaxValue,
                $"<invalid:{checked(int.MinValue + collectionOrdinal)}>");
        }
        return new NormalCameraWorldGroupKey(
            pass.MaterialName ?? "<null>",
            pass.TechniquePass.TechniqueSetName ?? "<null>",
            pass.TechniquePass.TechniqueSlot,
            pass.TechniquePass.TechniqueName ?? "<null>",
            batch!.SceneLightIndex,
            CreateNormalCameraWorldSurfaceIdentity(
                batch!,
                collectionOrdinal));
    }

    /// <summary>
    /// Exact backend-neutral world ownership used to pair the authored passes
    /// of one normal-camera group. World batching can split one material and
    /// light into multiple resource batches, so a single-surface sentinel
    /// would merge unrelated submissions and invalidate receiver routing.
    /// </summary>
    private static string CreateNormalCameraWorldSurfaceIdentity(
        MapRenderTexturedBatch batch,
        int collectionOrdinal)
    {
        MapRenderPickRange[] surfaceRanges = batch.PickRanges
            .Where(range => range.Kind == MapRenderPickKind.GfxSurface)
            .ToArray();
        if (surfaceRanges.Length != 0)
        {
            return string.Join(
                ',',
                surfaceRanges.Select(range => string.Concat(
                    range.SurfaceIndex.ToString(
                        CultureInfo.InvariantCulture),
                    ":",
                    range.IndexCount.ToString(
                        CultureInfo.InvariantCulture))));
        }

        MapRenderPickRange[] brushModelRanges = batch.PickRanges
            .Where(range =>
                range.Kind == MapRenderPickKind.GfxBrushModelSurface)
            .ToArray();
        return brushModelRanges.Length == 0
            ? $"<invalid:{collectionOrdinal}>"
            : string.Join(
                ',',
                brushModelRanges.Select(range => string.Concat(
                    ((int)range.Kind).ToString(
                        CultureInfo.InvariantCulture),
                    ":",
                    range.ObjectIndex.ToString(
                        CultureInfo.InvariantCulture),
                    ":",
                    range.SurfaceIndex.ToString(
                        CultureInfo.InvariantCulture),
                    ":",
                    range.IndexCount.ToString(
                        CultureInfo.InvariantCulture))));
    }

    private static void CommitNormalCameraCandidate(
        NormalCameraDrawCandidate candidate,
        NormalCameraTextureResourceCache textureCache,
        ICollection<RenderNormalCameraPreparedPassSnapshot> prepared,
        ICollection<RenderVertexLayoutDescriptor> vertexLayouts,
        ICollection<RenderInstanceLayoutDescriptor> instanceLayouts,
        ICollection<RenderGeometryDescriptor> geometries,
        ICollection<RenderInstanceDescriptor> instances,
        ICollection<RenderTextureDescriptor> textures,
        ICollection<RenderSamplerDescriptor> samplers)
    {
        prepared.Add(candidate.Pass);
        textureCache.CommitGeometry(
            candidate.VertexLayout,
            candidate.Geometry,
            vertexLayouts,
            geometries);
        if (candidate.InstanceLayout is not null &&
            candidate.Instances is not null)
        {
            textureCache.CommitInstances(
                candidate.InstanceLayout,
                candidate.Instances,
                instanceLayouts,
                instances);
        }
        foreach (RenderNormalCameraTextureResourceSnapshot resource in
                 candidate.TextureResources)
        {
            textureCache.CommitTexture(resource, textures, samplers);
        }
    }

    private static void OmitNormalCameraStaticGroup(
        IEnumerable<NormalCameraDrawCandidate> candidates,
        int authoredSourceOrdinal,
        RenderNormalCameraDrawOmissionCode code,
        ICollection<RenderNormalCameraDrawOmissionSnapshot> omissions)
    {
        foreach (NormalCameraDrawCandidate candidate in candidates)
        {
            omissions.Add(new RenderNormalCameraDrawOmissionSnapshot(
                RenderNormalCameraDrawSourceKind.StaticModel,
                authoredSourceOrdinal,
                candidate.Pass.CollectionOrdinal,
                [code],
                staticReceiverVariant:
                    candidate.Pass.StaticReceiverVariant));
        }
    }

    private static RenderBounds IncludeNormalCameraBounds(
        RenderBounds current,
        RenderBounds added) =>
        added.IsValid
            ? current.Include(added.Min).Include(added.Max)
            : current;

    private static RenderBounds IncludeNormalCameraVertexBounds(
        RenderBounds bounds,
        IReadOnlyList<float> vertices)
    {
        for (int offset = 0;
             offset + 2 < vertices.Count;
             offset += MapRenderScene.TexturedVertexFloatCount)
        {
            var position = new Vector3(
                vertices[offset],
                vertices[offset + 1],
                vertices[offset + 2]);
            if (Finite(position))
                bounds = bounds.Include(position);
        }
        return bounds;
    }

    private static RenderBounds CalculateNormalCameraStaticBounds(
        RenderBounds localBounds,
        IReadOnlyList<MapRenderStaticModelInstance> instances,
        int? selectedInstanceIndex)
    {
        if (!localBounds.IsValid)
            return RenderBounds.Empty;
        RenderBounds result = RenderBounds.Empty;
        if (selectedInstanceIndex is { } instanceIndex)
        {
            if ((uint)instanceIndex >= (uint)instances.Count)
                return RenderBounds.Empty;
            return IncludeNormalCameraTransformedBounds(
                result,
                localBounds,
                instances[instanceIndex]);
        }
        foreach (MapRenderStaticModelInstance instance in instances)
        {
            result = IncludeNormalCameraTransformedBounds(
                result,
                localBounds,
                instance);
        }
        return result;
    }

    private static RenderBounds IncludeNormalCameraTransformedBounds(
        RenderBounds result,
        RenderBounds localBounds,
        MapRenderStaticModelInstance instance)
    {
        for (int corner = 0; corner < 8; corner++)
        {
            var local = new Vector4(
                (corner & 1) == 0
                    ? localBounds.Min.X
                    : localBounds.Max.X,
                (corner & 2) == 0
                    ? localBounds.Min.Y
                    : localBounds.Max.Y,
                (corner & 4) == 0
                    ? localBounds.Min.Z
                    : localBounds.Max.Z,
                1f);
            var world = new Vector3(
                Vector4.Dot(instance.TransformRow0, local),
                Vector4.Dot(instance.TransformRow1, local),
                Vector4.Dot(instance.TransformRow2, local));
            if (Finite(world))
                result = result.Include(world);
        }
        return result;
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool Finite(Vector4 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private sealed record NormalCameraDrawCandidate(
        RenderNormalCameraPreparedPassSnapshot Pass,
        RenderVertexLayoutDescriptor VertexLayout,
        RenderGeometryDescriptor Geometry,
        RenderInstanceLayoutDescriptor? InstanceLayout,
        RenderInstanceDescriptor? Instances,
        IReadOnlyList<RenderNormalCameraTextureResourceSnapshot>
            TextureResources);

    private sealed record NormalCameraGeometryResource(
        RenderVertexLayoutDescriptor Layout,
        RenderGeometryDescriptor Geometry,
        RenderBounds LocalBounds);

    private sealed record NormalCameraRsxVertexInputsResource(
        ImmutableArray<float> Values,
        string ContentDigest);

    private sealed record NormalCameraStaticInstancesResource(
        ImmutableArray<MapRenderStaticModelInstance> Instances,
        string ContentDigest,
        GfxCameraRegionType? CameraRegion,
        RenderInstanceLayoutDescriptor Layout,
        RenderInstanceDescriptor Descriptor);

    private readonly record struct NormalCameraGeometryPayloadKey(
        float[] Vertices,
        uint[] Indices);

    /// <summary>
    /// Scene-local identity cache for immutable normal-camera payloads.
    /// Material passes and fixed receiver alternatives share the exact scene
    /// arrays and textures; freezing and digesting them once avoids multiplying
    /// geometry, RSX, instance, and image work by every authored technique
    /// channel. Resources are committed only when a candidate group is
    /// admitted, so typed omissions do not leave orphaned catalog entries.
    /// </summary>
    private sealed class NormalCameraTextureResourceCache
    {
        private readonly bool _preferProvenAuthoredPayloads;
        private readonly Dictionary<Texture,
            RenderNormalCameraTextureResourceSnapshot> _resources = new(
                ReferenceEqualityComparer.Instance);
        private readonly Dictionary<NormalCameraGeometryPayloadKey,
            NormalCameraGeometryResource> _geometryResources = [];
        private readonly Dictionary<float[],
            NormalCameraRsxVertexInputsResource> _rsxVertexInputs = new(
                ReferenceEqualityComparer.Instance);
        private readonly Dictionary<IReadOnlyList<
                MapRenderStaticModelInstance>,
            NormalCameraStaticInstancesResource>
            _staticInstancesByReference = new(
                ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int,
            List<NormalCameraStaticInstancesResource>>
            _staticInstancesByContentHash = [];
        private readonly HashSet<RenderSemanticIdentity>
            _committedTextures = [];
        private readonly HashSet<RenderSemanticIdentity>
            _committedVertexLayouts = [];
        private readonly HashSet<RenderSemanticIdentity>
            _committedGeometries = [];
        private readonly HashSet<RenderSemanticIdentity>
            _committedInstanceLayouts = [];
        private readonly HashSet<RenderSemanticIdentity>
            _committedInstances = [];
        private RenderVertexLayoutDescriptor? _vertexLayout;
        private RenderInstanceLayoutDescriptor? _instanceLayout;
        private int _nextResourceOrdinal;
        private int _nextGeometryOrdinal;
        private int _nextInstanceOrdinal;

        internal NormalCameraTextureResourceCache(
            bool preferProvenAuthoredPayloads)
        {
            _preferProvenAuthoredPayloads =
                preferProvenAuthoredPayloads;
            ImmutableArray<MapRenderStaticModelInstance> empty = [];
            EmptyStaticInstancesContentDigest =
                RenderNormalCameraPreparedPassSnapshot
                    .ComputeStaticInstancesContentDigest(empty);
        }

        internal string EmptyStaticInstancesContentDigest { get; }

        internal NormalCameraGeometryResource GetOrCreateGeometry(
            float[] vertices,
            uint[] indices)
        {
            ArgumentNullException.ThrowIfNull(vertices);
            ArgumentNullException.ThrowIfNull(indices);
            var key = new NormalCameraGeometryPayloadKey(vertices, indices);
            if (_geometryResources.TryGetValue(
                    key,
                    out NormalCameraGeometryResource? existing))
            {
                return existing;
            }

            RenderVertexLayoutDescriptor layout = _vertexLayout ??=
                new RenderVertexLayoutDescriptor(
                    new RenderSemanticIdentity(
                        RenderSemanticResourceKind.VertexLayout,
                        "scene.normal-camera.vertex-layout.position-uv0-f32.stride-88"),
                    MapRenderScene.TexturedVertexFloatCount * sizeof(float),
                    [
                        new RenderVertexElementDescriptor(
                            RenderVertexSemantic.Position,
                            0,
                            RenderVertexElementFormat.Float32x3,
                            0),
                        new RenderVertexElementDescriptor(
                            RenderVertexSemantic.TextureCoordinate,
                            0,
                            RenderVertexElementFormat.Float32x2,
                            3 * sizeof(float))
                    ]);
            int ordinal = _nextGeometryOrdinal;
            var geometry = new RenderGeometryDescriptor(
                new RenderSemanticIdentity(
                    RenderSemanticResourceKind.Geometry,
                    "scene.normal-camera.geometry." + ordinal.ToString(
                        "D8",
                        CultureInfo.InvariantCulture)),
                layout,
                RenderGeometryCoordinateSpace.Render,
                RenderPrimitiveTopology.TriangleList,
                RenderIndexFormat.Unsigned32,
                vertices.Length / MapRenderScene.TexturedVertexFloatCount,
                indices.Length,
                EncodeSingles(vertices),
                EncodeUInt32(indices));
            var resource = new NormalCameraGeometryResource(
                layout,
                geometry,
                IncludeNormalCameraVertexBounds(
                    RenderBounds.Empty,
                    vertices));
            _geometryResources.Add(key, resource);
            _nextGeometryOrdinal = checked(ordinal + 1);
            return resource;
        }

        internal NormalCameraRsxVertexInputsResource
            GetOrCreateRsxVertexInputs(float[] source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (_rsxVertexInputs.TryGetValue(
                    source,
                    out NormalCameraRsxVertexInputsResource? existing))
            {
                return existing;
            }

            ImmutableArray<float> values =
                ImmutableArray.CreateRange(source);
            var resource = new NormalCameraRsxVertexInputsResource(
                values,
                RenderNormalCameraPreparedPassSnapshot
                    .ComputeRsxVertexInputsContentDigest(values));
            _rsxVertexInputs.Add(source, resource);
            return resource;
        }

        internal NormalCameraStaticInstancesResource
            GetOrCreateStaticInstances(
                IReadOnlyList<MapRenderStaticModelInstance> source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (_staticInstancesByReference.TryGetValue(
                    source,
                    out NormalCameraStaticInstancesResource? existing))
            {
                return existing;
            }

            var hash = new HashCode();
            hash.Add(source.Count);
            for (int index = 0; index < source.Count; index++)
                hash.Add(source[index]);
            int contentHash = hash.ToHashCode();
            if (_staticInstancesByContentHash.TryGetValue(
                    contentHash,
                    out List<NormalCameraStaticInstancesResource>?
                        candidates))
            {
                foreach (NormalCameraStaticInstancesResource candidate in
                         candidates)
                {
                    if (StaticInstancesEqual(candidate.Instances, source))
                    {
                        _staticInstancesByReference.Add(source, candidate);
                        return candidate;
                    }
                }
            }
            else
            {
                candidates = [];
                _staticInstancesByContentHash.Add(contentHash, candidates);
            }

            ImmutableArray<MapRenderStaticModelInstance> instances =
                ImmutableArray.CreateRange(source);
            RenderInstanceLayoutDescriptor layout = _instanceLayout ??=
                new RenderInstanceLayoutDescriptor(
                    new RenderSemanticIdentity(
                        RenderSemanticResourceKind.InstanceLayout,
                        "scene.normal-camera.instance-layout.transform-rows-f32.stride-48"),
                    MapRenderStaticInstanceBufferPacker
                        .PlacementOnlyFloatStride * sizeof(float),
                    [
                        new RenderInstanceElementDescriptor(
                            RenderInstanceSemantic.TransformRow,
                            0,
                            RenderVertexElementFormat.Float32x4,
                            0),
                        new RenderInstanceElementDescriptor(
                            RenderInstanceSemantic.TransformRow,
                            1,
                            RenderVertexElementFormat.Float32x4,
                            4 * sizeof(float)),
                        new RenderInstanceElementDescriptor(
                            RenderInstanceSemantic.TransformRow,
                            2,
                            RenderVertexElementFormat.Float32x4,
                            8 * sizeof(float))
                    ]);
            var packed = new float[checked(
                instances.Length *
                MapRenderStaticInstanceBufferPacker
                    .PlacementOnlyFloatStride)];
            MapRenderStaticInstanceBufferPacker.PackAll(
                instances,
                MapRenderStaticInstanceLightingPayload.None,
                packed);
            int ordinal = _nextInstanceOrdinal;
            var descriptor = new RenderInstanceDescriptor(
                new RenderSemanticIdentity(
                    RenderSemanticResourceKind.Instances,
                    "scene.normal-camera.instances." + ordinal.ToString(
                        "D8",
                        CultureInfo.InvariantCulture)),
                layout,
                instances.Length,
                EncodeSingles(packed),
                RenderPayloadByteOrder.LittleEndian);
            var resource = new NormalCameraStaticInstancesResource(
                instances,
                RenderNormalCameraPreparedPassSnapshot
                    .ComputeStaticInstancesContentDigest(instances),
                MapRenderOpenGlStaticCameraRegionPolicy.ResolveUniformRegion(
                    instances),
                layout,
                descriptor);
            candidates.Add(resource);
            _staticInstancesByReference.Add(source, resource);
            _nextInstanceOrdinal = checked(ordinal + 1);
            return resource;
        }

        internal RenderNormalCameraTextureResourceSnapshot GetOrCreate(
            Texture source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (_resources.TryGetValue(source, out
                    RenderNormalCameraTextureResourceSnapshot? existing))
            {
                return existing;
            }

            int ordinal = _nextResourceOrdinal;
            string ordinalText = ordinal.ToString(
                "D8",
                CultureInfo.InvariantCulture);
            RenderTextureDescriptor texture = CreateTextureDescriptor(
                source,
                new RenderSemanticIdentity(
                    RenderSemanticResourceKind.Texture,
                    "scene.normal-camera.texture." + ordinalText),
                _preferProvenAuthoredPayloads);
            var sampler = new RenderSamplerDescriptor(
                new RenderSemanticIdentity(
                    RenderSemanticResourceKind.Sampler,
                    "scene.normal-camera.sampler." + ordinalText),
                source.DecodedSamplerState);
            var resource = new RenderNormalCameraTextureResourceSnapshot(
                ordinal,
                texture,
                sampler);
            _resources.Add(source, resource);
            _nextResourceOrdinal = checked(ordinal + 1);
            return resource;
        }

        internal void CommitTexture(
            RenderNormalCameraTextureResourceSnapshot resource,
            ICollection<RenderTextureDescriptor> textures,
            ICollection<RenderSamplerDescriptor> samplers)
        {
            ArgumentNullException.ThrowIfNull(resource);
            if (!_committedTextures.Add(resource.TextureIdentity))
                return;
            textures.Add(resource.Texture);
            samplers.Add(resource.Sampler);
        }

        internal void CommitGeometry(
            RenderVertexLayoutDescriptor layout,
            RenderGeometryDescriptor geometry,
            ICollection<RenderVertexLayoutDescriptor> vertexLayouts,
            ICollection<RenderGeometryDescriptor> geometries)
        {
            if (_committedVertexLayouts.Add(layout.Identity))
                vertexLayouts.Add(layout);
            if (_committedGeometries.Add(geometry.Identity))
                geometries.Add(geometry);
        }

        internal void CommitInstances(
            RenderInstanceLayoutDescriptor layout,
            RenderInstanceDescriptor instances,
            ICollection<RenderInstanceLayoutDescriptor> instanceLayouts,
            ICollection<RenderInstanceDescriptor> instanceResources)
        {
            if (_committedInstanceLayouts.Add(layout.Identity))
                instanceLayouts.Add(layout);
            if (_committedInstances.Add(instances.Identity))
                instanceResources.Add(instances);
        }

        private static bool StaticInstancesEqual(
            ImmutableArray<MapRenderStaticModelInstance> left,
            IReadOnlyList<MapRenderStaticModelInstance> right)
        {
            if (left.Length != right.Count)
                return false;
            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                    return false;
            }
            return true;
        }
    }

    private readonly record struct NormalCameraWorldSourceEntry(
        int CollectionOrdinal,
        MapRenderTexturedBatch? Batch,
        NormalCameraWorldGroupKey GroupKey);

    private readonly record struct NormalCameraWorldCollection(
        IReadOnlyList<MapRenderTexturedBatch> Batches,
        MapRenderWorldReceiverVariantKey? ReceiverVariant);

    private readonly record struct NormalCameraStaticSourceEntry(
        int CollectionOrdinal,
        MapRenderInstancedTexturedBatch? Batch,
        int DrawGroupId);

    private readonly record struct NormalCameraStaticCollection(
        IReadOnlyList<MapRenderInstancedTexturedBatch> Batches,
        MapRenderStaticModelReceiverVariantKey? ReceiverVariant);

    private readonly record struct NormalCameraWorldGroupKey(
        string MaterialName,
        string TechniqueSetName,
        int TechniqueSlot,
        string TechniqueName,
        byte SceneLightIndex,
        string SurfaceIdentity);

    private sealed record DecodedTextureSubresource(
        int Width,
        int Height,
        byte[] PixelBytes);

    private static byte[] EncodeSingles(float[] source)
    {
        var payload = new byte[checked(source.Length * sizeof(float))];
        for (int index = 0; index < source.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(index * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(source[index]));
        }
        return payload;
    }

    private static byte[] EncodeUInt32(uint[] source)
    {
        var payload = new byte[checked(source.Length * sizeof(uint))];
        for (int index = 0; index < source.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                payload.AsSpan(index * sizeof(uint), sizeof(uint)),
                source[index]);
        }
        return payload;
    }

    private static RenderSemanticIdentity Identity(
        RenderSemanticResourceKind kind,
        string prefix,
        int ordinal,
        string? suffix = null)
    {
        string value = string.Concat(
            prefix,
            ".",
            ordinal.ToString("D8", CultureInfo.InvariantCulture),
            suffix is null ? string.Empty : "." + suffix);
        return new RenderSemanticIdentity(kind, value);
    }

    private static InvalidDataException InvalidSky(
        int ordinal,
        string reason,
        Exception? innerException = null) =>
        new(
            $"Sky submission at scene ordinal {ordinal} is invalid: {reason}",
            innerException);

    private readonly record struct TextureResources(
        RenderTextureDescriptor Texture,
        RenderSamplerDescriptor Sampler);
}
