using IW4.Render.Techniques;
using System.Collections.Immutable;
using System.Numerics;

using IW4.Assets.Assets.Material;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Scheduling;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.SceneBuilding;
using IW4.Render.Textures;

namespace IW4.Render.Resources;

/// <summary>
/// One immutable sky draw and its semantic scene-lifetime resource bindings.
/// SceneOrdinal is the source MapRenderScene.Skies ordinal and is never
/// compacted or renumbered.
/// </summary>
public sealed class RenderSkySubmissionSnapshot
{
    internal RenderSkySubmissionSnapshot(
        int sceneOrdinal,
        int? worldSkyIndex,
        MapRenderSkySource source,
        IEnumerable<int> skyStartSurfPositions,
        IEnumerable<int> surfaceIndices,
        RenderSemanticIdentity drawIdentity,
        RenderSemanticIdentity geometryIdentity,
        RenderSemanticIdentity vertexLayoutIdentity,
        RenderSemanticIdentity textureIdentity,
        RenderSemanticIdentity samplerIdentity,
        MaterialPassIdentity? shaderPass = null,
        MaterialSamplerIdentity? shaderPrimarySampler = null,
        byte shaderTexCoordSource = 0,
        ShaderExecutionContract? shaderExecution = null)
    {
        if (sceneOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sceneOrdinal));
        if (worldSkyIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(worldSkyIndex));
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source));
        RenderVertexLayoutDescriptor.RequireIdentity(
            drawIdentity,
            RenderSemanticResourceKind.Draw);
        RenderVertexLayoutDescriptor.RequireIdentity(
            geometryIdentity,
            RenderSemanticResourceKind.Geometry);
        RenderVertexLayoutDescriptor.RequireIdentity(
            vertexLayoutIdentity,
            RenderSemanticResourceKind.VertexLayout);
        RenderVertexLayoutDescriptor.RequireIdentity(
            textureIdentity,
            RenderSemanticResourceKind.Texture);
        RenderVertexLayoutDescriptor.RequireIdentity(
            samplerIdentity,
            RenderSemanticResourceKind.Sampler);

        ImmutableArray<int> frozenStarts = RenderSnapshotCollections.Freeze(
            skyStartSurfPositions,
            nameof(skyStartSurfPositions));
        ImmutableArray<int> frozenSurfaces = RenderSnapshotCollections.Freeze(
            surfaceIndices,
            nameof(surfaceIndices));
        if (frozenStarts.IsEmpty || frozenStarts.Length != frozenSurfaces.Length)
        {
            throw new ArgumentException(
                "Sky surface positions and resolved surfaces must be non-empty and have equal lengths.");
        }
        if (frozenStarts.Any(value => value < 0) ||
            frozenSurfaces.Any(value => value < 0))
        {
            throw new ArgumentException(
                "Sky surface positions and resolved surfaces cannot be negative.");
        }

        SceneOrdinal = sceneOrdinal;
        WorldSkyIndex = worldSkyIndex;
        Source = source;
        SkyStartSurfPositions = frozenStarts;
        SurfaceIndices = frozenSurfaces;
        DrawIdentity = drawIdentity;
        GeometryIdentity = geometryIdentity;
        VertexLayoutIdentity = vertexLayoutIdentity;
        TextureIdentity = textureIdentity;
        SamplerIdentity = samplerIdentity;
        if ((shaderPass is null) != (shaderPrimarySampler is null))
        {
            throw new ArgumentException(
                "Sky source-pass and primary-sampler provenance must be retained together.");
        }
        ShaderPassProvenance = shaderPass is null ||
            shaderPrimarySampler is not { } primarySampler
                ? null
                : new RenderMaterialPassProvenanceSnapshot(
                    shaderPass,
                    primarySampler,
                    shaderTexCoordSource);
        ShaderProvenance = shaderExecution is null
            ? null
            : new RenderWorldShaderProvenanceSnapshot(
                shaderExecution,
                shaderExecution.ProgramExecutionStatus);
        if ((ShaderPassProvenance is null) != (ShaderProvenance is null))
        {
            throw new ArgumentException(
                "Sky source-pass and shader provenance must be retained together.");
        }
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public int SceneOrdinal { get; }

    public int? WorldSkyIndex { get; }

    public MapRenderSkySource Source { get; }

    public ImmutableArray<int> SkyStartSurfPositions { get; }

    public ImmutableArray<int> SurfaceIndices { get; }

    public RenderSemanticIdentity DrawIdentity { get; }

    public RenderSemanticIdentity GeometryIdentity { get; }

    public RenderSemanticIdentity VertexLayoutIdentity { get; }

    public RenderSemanticIdentity TextureIdentity { get; }

    public RenderSemanticIdentity SamplerIdentity { get; }

    internal RenderMaterialPassProvenanceSnapshot? ShaderPassProvenance
        { get; }

    internal RenderWorldShaderProvenanceSnapshot? ShaderProvenance { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-sky-submission/v2");
        writer.WriteInt32(SceneOrdinal);
        writer.WriteNullableInt32(WorldSkyIndex);
        writer.WriteInt32((int)Source);
        writer.WriteInt32(SkyStartSurfPositions.Length);
        foreach (int value in SkyStartSurfPositions)
            writer.WriteInt32(value);
        writer.WriteInt32(SurfaceIndices.Length);
        foreach (int value in SurfaceIndices)
            writer.WriteInt32(value);
        writer.WriteIdentity(DrawIdentity);
        writer.WriteIdentity(GeometryIdentity);
        writer.WriteIdentity(VertexLayoutIdentity);
        writer.WriteIdentity(TextureIdentity);
        writer.WriteIdentity(SamplerIdentity);
        writer.WriteBoolean(ShaderPassProvenance is not null);
        ShaderPassProvenance?.AppendContent(writer);
        ShaderProvenance?.AppendContent(writer);
    }
}

/// <summary>
/// Immutable scene-lifetime resource snapshot used as input to frame planning.
/// It owns every array and payload reachable from its public surface.
/// </summary>
public sealed class RenderSceneSnapshot
{
    internal RenderSceneSnapshot(
        string name,
        long revision,
        RenderResourceSnapshot resources,
        IEnumerable<RenderSkySubmissionSnapshot> skies,
        IEnumerable<RenderDiagnosticSubmissionSnapshot>? diagnostics = null,
        RenderWireframeSubmissionSnapshot? wireframe = null,
        RenderMaterialDrawPacketAdmission? materialDrawPacketAdmission = null,
        RenderWorldDrawPacketAdmission? worldDrawPacketAdmission = null,
        RenderNormalCameraDrawSnapshot? normalCameraDraws = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentNullException.ThrowIfNull(resources);
        ImmutableArray<RenderSkySubmissionSnapshot> frozenSkies =
            RenderSnapshotCollections.Freeze(skies, nameof(skies));
        if (frozenSkies.Any(sky => sky is null))
            throw new ArgumentException("A sky snapshot cannot be null.", nameof(skies));
        ImmutableArray<RenderDiagnosticSubmissionSnapshot>
            frozenDiagnostics = RenderSnapshotCollections.Freeze(
                diagnostics ?? [],
                nameof(diagnostics));
        if (frozenDiagnostics.Any(diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "A diagnostic snapshot cannot be null.",
                nameof(diagnostics));
        }

        var drawIdentities = new HashSet<RenderSemanticIdentity>();
        for (int ordinal = 0; ordinal < frozenSkies.Length; ordinal++)
        {
            RenderSkySubmissionSnapshot sky = frozenSkies[ordinal];
            if (sky.SceneOrdinal != ordinal)
            {
                throw new ArgumentException(
                    "Sky scene ordinals must preserve source order without compaction.",
                    nameof(skies));
            }
            if (!drawIdentities.Add(sky.DrawIdentity))
                throw new ArgumentException("Sky draw identities must be unique.", nameof(skies));

            RenderGeometryDescriptor geometry =
                resources.RequireGeometry(sky.GeometryIdentity);
            if (geometry.VertexLayout != sky.VertexLayoutIdentity)
            {
                throw new ArgumentException(
                    "Sky geometry and vertex-layout identities do not match.",
                    nameof(skies));
            }
            resources.RequireVertexLayout(sky.VertexLayoutIdentity);
            resources.RequireTexture(sky.TextureIdentity);
            resources.RequireSampler(sky.SamplerIdentity);
        }

        var sourceOrdinals = new HashSet<int>();
        foreach (RenderDiagnosticSubmissionSnapshot diagnostic in
                 frozenDiagnostics)
        {
            if (!sourceOrdinals.Add(diagnostic.SourceOrdinal))
            {
                throw new ArgumentException(
                    "Diagnostic source ordinals must be unique.",
                    nameof(diagnostics));
            }
            if (!drawIdentities.Add(diagnostic.DrawIdentity))
            {
                throw new ArgumentException(
                    "Scene draw identities must be unique.",
                    nameof(diagnostics));
            }

            RenderGeometryDescriptor geometry =
                resources.RequireGeometry(diagnostic.GeometryIdentity);
            if (geometry.VertexLayout !=
                diagnostic.VertexLayoutIdentity)
            {
                throw new ArgumentException(
                    "Diagnostic geometry and vertex-layout identities do not match.",
                    nameof(diagnostics));
            }
            resources.RequireVertexLayout(
                diagnostic.VertexLayoutIdentity);
            if (diagnostic.InstancesIdentity is { } instances &&
                diagnostic.InstanceLayoutIdentity is { } instanceLayout)
            {
                RenderInstanceDescriptor descriptor =
                    resources.RequireInstances(instances);
                if (descriptor.Layout != instanceLayout)
                {
                    throw new ArgumentException(
                        "Diagnostic instance resource and layout identities do not match.",
                        nameof(diagnostics));
                }
                resources.RequireInstanceLayout(instanceLayout);
            }
            else if (diagnostic.InstancesIdentity.HasValue ||
                     diagnostic.InstanceLayoutIdentity.HasValue)
            {
                throw new ArgumentException(
                    "Diagnostic instance resource and layout must be specified together.",
                    nameof(diagnostics));
            }
        }

        for (int index = 1; index < frozenDiagnostics.Length; index++)
        {
            if (frozenDiagnostics[index - 1].SourceOrdinal >=
                frozenDiagnostics[index].SourceOrdinal)
            {
                throw new ArgumentException(
                    "Diagnostic submissions must preserve ascending source order.",
                    nameof(diagnostics));
            }
        }

        if (wireframe is not null)
        {
            if (!drawIdentities.Add(wireframe.DrawIdentity))
            {
                throw new ArgumentException(
                    "Scene draw identities must be unique.",
                    nameof(wireframe));
            }

            RenderGeometryDescriptor geometry =
                resources.RequireGeometry(wireframe.GeometryIdentity);
            if (geometry.VertexLayout != wireframe.VertexLayoutIdentity)
            {
                throw new ArgumentException(
                    "Wireframe geometry and vertex-layout identities do not match.",
                    nameof(wireframe));
            }
            RenderVertexLayoutDescriptor layout =
                resources.RequireVertexLayout(
                    wireframe.VertexLayoutIdentity);
            ValidateWireframeGeometry(geometry, layout);
        }

        materialDrawPacketAdmission ??=
            new RenderMaterialDrawPacketAdmission(
                packet: null,
                rejections: [],
                RenderMaterialDrawPacketAdmissionFailure.NoSourceBatches,
                "NO_TEXTURED_BATCHES");
        if (materialDrawPacketAdmission.Packet is { } materialPacket)
        {
            if (!drawIdentities.Add(materialPacket.DrawIdentity))
            {
                throw new ArgumentException(
                    "Scene draw identities must be unique.",
                    nameof(materialDrawPacketAdmission));
            }

            RenderVertexLayoutDescriptor layout =
                resources.RequireVertexLayout(
                    materialPacket.VertexLayoutIdentity);
            RenderGeometryDescriptor geometry =
                resources.RequireGeometry(materialPacket.GeometryIdentity);
            RenderTextureDescriptor texture =
                resources.RequireTexture(materialPacket.TextureIdentity);
            RenderSamplerDescriptor sampler =
                resources.RequireSampler(materialPacket.SamplerIdentity);
            if (!string.Equals(
                    layout.ContentDigest,
                    materialPacket.VertexLayout.ContentDigest,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    geometry.ContentDigest,
                    materialPacket.Geometry.ContentDigest,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    texture.ContentDigest,
                    materialPacket.Texture.ContentDigest,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    sampler.ContentDigest,
                    materialPacket.Sampler.ContentDigest,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Material draw packet descriptors do not match scene resources.",
                    nameof(materialDrawPacketAdmission));
            }
        }

        worldDrawPacketAdmission ??= new RenderWorldDrawPacketAdmission(
            packet: null,
            rejections: [],
            RenderWorldDrawPacketAdmissionFailure.NoSourceBatches,
            "NO_LOADED_CAMERA_COLOR_TEXTURED_BATCHES");
        if (worldDrawPacketAdmission.Packet is { } worldPacket)
        {
            if (!drawIdentities.Add(worldPacket.FullBatchDrawIdentity))
            {
                throw new ArgumentException(
                    "Scene draw identities must be unique.",
                    nameof(worldDrawPacketAdmission));
            }

            RenderVertexLayoutDescriptor layout =
                resources.RequireVertexLayout(
                    worldPacket.VertexLayoutIdentity);
            RenderGeometryDescriptor geometry = resources.RequireGeometry(
                worldPacket.GeometryIdentity);
            RenderTextureDescriptor texture = resources.RequireTexture(
                worldPacket.TextureIdentity);
            RenderSamplerDescriptor sampler = resources.RequireSampler(
                worldPacket.SamplerIdentity);
            if (!string.Equals(
                    layout.ContentDigest,
                    worldPacket.VertexLayout.ContentDigest,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    geometry.ContentDigest,
                    worldPacket.Geometry.ContentDigest,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    texture.ContentDigest,
                    worldPacket.Texture.ContentDigest,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    sampler.ContentDigest,
                    worldPacket.Sampler.ContentDigest,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Loaded world draw packet descriptors do not match scene resources.",
                    nameof(worldDrawPacketAdmission));
            }
        }

        normalCameraDraws ??= RenderNormalCameraDrawSnapshot.Empty;

        Name = name;
        Revision = revision;
        Resources = resources;
        Skies = frozenSkies;
        Diagnostics = frozenDiagnostics;
        Wireframe = wireframe;
        MaterialDrawPacketAdmission = materialDrawPacketAdmission;
        WorldSurfaceAdmission = RenderWorldSurfaceAdmission.Create(
            materialDrawPacketAdmission);
        LoadedCameraColorWorldDrawPacketAdmission =
            worldDrawPacketAdmission;
        NormalCameraDraws = normalCameraDraws;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public string Name { get; }

    public long Revision { get; }

    public RenderResourceSnapshot Resources { get; }

    public ImmutableArray<RenderSkySubmissionSnapshot> Skies { get; }

    public ImmutableArray<RenderDiagnosticSubmissionSnapshot> Diagnostics
        { get; }

    public RenderWireframeSubmissionSnapshot? Wireframe { get; }

    public RenderMaterialDrawPacketAdmission MaterialDrawPacketAdmission
        { get; }

    public RenderWorldSurfaceAdmission WorldSurfaceAdmission { get; }

    /// <summary>
    /// Bounded loaded CameraColor base-texture compatibility admission. This
    /// is separate from the synthetic/generic material-preview admission and
    /// carries no DPVS or full-map claim.
    /// </summary>
    public RenderWorldDrawPacketAdmission
        LoadedCameraColorWorldDrawPacketAdmission { get; }

    /// <summary>
    /// Complete prepared normal-camera textured-draw seam. Construction is
    /// assembly-owned; both backends publicly consume the same immutable
    /// source and camera-ordering contract.
    /// </summary>
    public RenderNormalCameraDrawSnapshot NormalCameraDraws { get; }

    public string ContentDigest { get; }

    private void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-scene-snapshot/v12");
        writer.WriteString(Name);
        writer.WriteInt64(Revision);
        writer.WriteString(Resources.ContentDigest);
        writer.WriteInt32(Skies.Length);
        foreach (RenderSkySubmissionSnapshot sky in Skies)
            sky.AppendContent(writer);
        writer.WriteInt32(Diagnostics.Length);
        foreach (RenderDiagnosticSubmissionSnapshot diagnostic in Diagnostics)
            diagnostic.AppendContent(writer);
        writer.WriteBoolean(Wireframe is not null);
        Wireframe?.AppendContent(writer);
        MaterialDrawPacketAdmission.AppendContent(writer);
        WorldSurfaceAdmission.AppendContent(writer);
        LoadedCameraColorWorldDrawPacketAdmission.AppendContent(writer);
        writer.WriteString(NormalCameraDraws.ContentDigest);
    }

    private static void ValidateWireframeGeometry(
        RenderGeometryDescriptor geometry,
        RenderVertexLayoutDescriptor layout)
    {
        if (geometry.CoordinateSpace != RenderGeometryCoordinateSpace.Render ||
            geometry.Topology != RenderPrimitiveTopology.LineList ||
            geometry.IndexFormat != RenderIndexFormat.Unsigned32)
        {
            throw new ArgumentException(
                "Wireframe geometry must use render coordinates, line-list topology, and unsigned 32-bit indices.",
                nameof(geometry));
        }

        if (layout.StrideBytes != 6 * sizeof(float) ||
            layout.Elements.Length != 2 ||
            !IsWireframeElement(
                layout.Elements[0],
                RenderVertexSemantic.Position,
                offsetBytes: 0) ||
            !IsWireframeElement(
                layout.Elements[1],
                RenderVertexSemantic.Color,
                offsetBytes: 3 * sizeof(float)))
        {
            throw new ArgumentException(
                "Wireframe geometry requires the exact position/color float3 layout with a 24-byte stride.",
                nameof(layout));
        }
    }

    private static bool IsWireframeElement(
        RenderVertexElementDescriptor element,
        RenderVertexSemantic semantic,
        int offsetBytes) =>
        element.Semantic == semantic &&
        element.SemanticIndex == 0 &&
        element.Format == RenderVertexElementFormat.Float32x3 &&
        element.OffsetBytes == offsetBytes;
}

/// <summary>
/// Exact prepared collection that owns one normal-camera textured source.
/// </summary>
public enum RenderNormalCameraDrawSourceKind : byte
{
    World,
    StaticModel
}

/// <summary>
/// Deliberately bounded coverage for the first complete shared draw seam.
/// Dynamic LOD, DPVS, receiver-page selection, and auxiliary targets remain
/// later frame-scheduling responsibilities.
/// </summary>
public enum RenderNormalCameraDrawCoverage : byte
{
    PreparedWorldAndCurrentStaticBatchesWithoutDynamicLodOrDpvs = 0,

    /// <summary>
    /// The complete validated all-LOD static collection is frozen beside the
    /// world collection. Camera LOD and DPVS selection remain frame/backend
    /// scheduling inputs and are not claimed by this scene inventory.
    /// </summary>
    PreparedWorldAndAllStaticLodBatchesWithoutDpvsSelection
}

/// <summary>
/// Typed reason that an enumerated source cannot enter the normal-camera
/// draw contract. Omissions never authorize fallback shader behavior.
/// </summary>
public enum RenderNormalCameraDrawOmissionCode : byte
{
    SourceCollectionMissing,
    NullBatch,
    PassMissing,
    TextureMissing,
    UvRouteMissing,
    ShaderExecutionMissing,
    MaterialBindingCollectionMissing,
    GeometryMissingOrMalformed,
    RsxVertexInputPayloadMalformed,
    StaticInstancesMissingOrMalformed,
    ResourceSnapshotCreationFailed,
    AuthoredPassGroupIncomplete,
    StaticGroupGeometryMismatch,
    StaticGroupInstanceOwnershipMismatch,
    StaticGroupPlanningFailed,
    AuxiliaryCameraRegionOnly
}

/// <summary>
/// Immutable fail-closed accounting row for one pass row or one missing source
/// collection. SourceOrdinal owns the authored group; CollectionOrdinal
/// preserves the exact pass row in its world or static collection.
/// </summary>
public sealed class RenderNormalCameraDrawOmissionSnapshot
{
    internal RenderNormalCameraDrawOmissionSnapshot(
        RenderNormalCameraDrawSourceKind sourceKind,
        int? sourceOrdinal,
        int? collectionOrdinal,
        IEnumerable<RenderNormalCameraDrawOmissionCode> codes,
        MapRenderWorldReceiverVariantKey? worldReceiverVariant = null,
        MapRenderStaticModelReceiverVariantKey? staticReceiverVariant = null)
    {
        if (!Enum.IsDefined(sourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        if (collectionOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(collectionOrdinal));
        if (sourceOrdinal.HasValue != collectionOrdinal.HasValue)
        {
            throw new ArgumentException(
                "Source and collection ordinals must either both exist or both be absent.");
        }
        if (worldReceiverVariant.HasValue &&
            staticReceiverVariant.HasValue ||
            worldReceiverVariant.HasValue &&
            sourceKind != RenderNormalCameraDrawSourceKind.World ||
            staticReceiverVariant.HasValue &&
            sourceKind != RenderNormalCameraDrawSourceKind.StaticModel)
        {
            throw new ArgumentException(
                "Receiver-variant omission metadata must be mutually exclusive and match the source kind.");
        }

        ImmutableArray<RenderNormalCameraDrawOmissionCode> frozenCodes =
            RenderSnapshotCollections.Freeze(codes, nameof(codes));
        if (frozenCodes.IsEmpty ||
            frozenCodes.Any(code => !Enum.IsDefined(code)) ||
            frozenCodes.Distinct().Count() != frozenCodes.Length)
        {
            throw new ArgumentException(
                "Normal-camera omission codes must be non-empty, defined, and unique.",
                nameof(codes));
        }
        if (!sourceOrdinal.HasValue &&
            !frozenCodes.Contains(
                RenderNormalCameraDrawOmissionCode.SourceCollectionMissing))
        {
            throw new ArgumentException(
                "A collection-level omission must identify a missing source collection.",
                nameof(codes));
        }

        SourceKind = sourceKind;
        WorldReceiverVariant = worldReceiverVariant;
        StaticReceiverVariant = staticReceiverVariant;
        SourceOrdinal = sourceOrdinal;
        CollectionOrdinal = collectionOrdinal;
        Codes = frozenCodes;
        Reason = string.Join(",", frozenCodes.Select(code => code.ToString()));
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderNormalCameraDrawSourceKind SourceKind { get; }

    public MapRenderWorldReceiverVariantKey? WorldReceiverVariant { get; }

    public MapRenderStaticModelReceiverVariantKey? StaticReceiverVariant
        { get; }

    public int? SourceOrdinal { get; }

    public int? CollectionOrdinal { get; }

    public ImmutableArray<RenderNormalCameraDrawOmissionCode> Codes { get; }

    public string Reason { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-normal-camera-draw-omission/v2");
        writer.WriteInt32((int)SourceKind);
        writer.WriteBoolean(WorldReceiverVariant.HasValue);
        if (WorldReceiverVariant is { } worldReceiver)
        {
            writer.WriteInt32((int)worldReceiver.Page);
            writer.WriteInt32((int)worldReceiver.Allocation);
        }
        writer.WriteBoolean(StaticReceiverVariant.HasValue);
        if (StaticReceiverVariant is { } staticReceiver)
        {
            writer.WriteInt32((int)staticReceiver.Page);
            writer.WriteInt32((int)staticReceiver.Allocation);
        }
        writer.WriteNullableInt32(SourceOrdinal);
        writer.WriteNullableInt32(CollectionOrdinal);
        writer.WriteInt32(Codes.Length);
        foreach (RenderNormalCameraDrawOmissionCode code in Codes)
            writer.WriteInt32((int)code);
    }
}

/// <summary>
/// One exact texture and sampler pair reachable from a prepared pass. The
/// descriptors own their bytes; no source Texture is retained.
/// </summary>
public sealed class RenderNormalCameraTextureResourceSnapshot
{
    internal RenderNormalCameraTextureResourceSnapshot(
        int resourceOrdinal,
        RenderTextureDescriptor texture,
        RenderSamplerDescriptor sampler)
    {
        if (resourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(resourceOrdinal));
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(sampler);

        ResourceOrdinal = resourceOrdinal;
        Texture = texture;
        Sampler = sampler;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public int ResourceOrdinal { get; }

    public RenderTextureDescriptor Texture { get; }

    public RenderSamplerDescriptor Sampler { get; }

    public RenderSemanticIdentity TextureIdentity => Texture.Identity;

    public RenderSemanticIdentity SamplerIdentity => Sampler.Identity;

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-normal-camera-texture-resource/v1");
        writer.WriteInt32(ResourceOrdinal);
        writer.WriteIdentity(TextureIdentity);
        writer.WriteString(Texture.ContentDigest);
        writer.WriteIdentity(SamplerIdentity);
        writer.WriteString(Sampler.ContentDigest);
    }
}

/// <summary>
/// Frozen color-layer composition and exact texture-resource ownership.
/// </summary>
public sealed class RenderNormalCameraColorLayerSnapshot
{
    internal RenderNormalCameraColorLayerSnapshot(
        MaterialColorLayer source,
        RenderNormalCameraTextureResourceSnapshot resource)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(source.UvRoute);

        LayerIndex = source.LayerIndex;
        SamplerArgIndex = source.Identity.SamplerArgIndex;
        SamplerDest = source.Identity.SamplerDest;
        SamplerHash = source.Identity.SamplerHash;
        TextureSemantic = (byte)source.Identity.TextureSemantic;
        UvRoute = new RenderMaterialUvRouteSnapshot(source.UvRoute);
        BlendWeightComponent = source.BlendWeightComponent;
        TextureIdentity = resource.TextureIdentity;
        SamplerIdentity = resource.SamplerIdentity;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public int LayerIndex { get; }
    public int SamplerArgIndex { get; }
    public ushort SamplerDest { get; }
    public uint SamplerHash { get; }
    public byte TextureSemantic { get; }
    public RenderMaterialUvRouteSnapshot UvRoute { get; }
    public int BlendWeightComponent { get; }
    public RenderSemanticIdentity TextureIdentity { get; }
    public RenderSemanticIdentity SamplerIdentity { get; }
    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-normal-camera-color-layer/v1");
        writer.WriteInt32(LayerIndex);
        writer.WriteInt32(SamplerArgIndex);
        writer.WriteInt32(SamplerDest);
        writer.WriteUInt32(SamplerHash);
        writer.WriteByte(TextureSemantic);
        UvRoute.AppendContent(writer);
        writer.WriteInt32(BlendWeightComponent);
        writer.WriteIdentity(TextureIdentity);
        writer.WriteIdentity(SamplerIdentity);
    }
}

/// <summary>
/// Frozen material-table sampler row. Null resource identities preserve an
/// unresolved authored binding instead of inventing a backend resource.
/// </summary>
public sealed class RenderNormalCameraMaterialSamplerSnapshot
{
    internal RenderNormalCameraMaterialSamplerSnapshot(
        MaterialSamplerBinding source,
        MapRenderWorldRuntimeTextureIdentity? runtimeTextureIdentity,
        RenderNormalCameraTextureResourceSnapshot? resource)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.TextureName);

        SamplerArgIndex = source.Identity.SamplerArgIndex;
        SamplerDest = source.Identity.SamplerDest;
        SamplerHash = source.Identity.SamplerHash;
        TextureSemantic = (byte)source.Identity.TextureSemantic;
        TextureName = source.TextureName;
        WorldRuntimeTextureIdentity = runtimeTextureIdentity;
        EditorTextureRole = source.EditorTextureRole;
        TextureTableOrdinal = source.TextureTableOrdinal;
        UvRoute = source.UvRoute is null
            ? null
            : new RenderMaterialUvRouteSnapshot(source.UvRoute);
        TextureIdentity = resource?.TextureIdentity;
        SamplerIdentity = resource?.SamplerIdentity;
        if (source.Texture is null != !TextureIdentity.HasValue)
        {
            throw new ArgumentException(
                "Material sampler resource ownership does not match its source texture.",
                nameof(resource));
        }
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public int SamplerArgIndex { get; }
    public ushort SamplerDest { get; }
    public uint SamplerHash { get; }
    public byte TextureSemantic { get; }
    public string TextureName { get; }
    public MapRenderWorldRuntimeTextureIdentity? WorldRuntimeTextureIdentity
        { get; }
    public EditorMaterialTextureRole EditorTextureRole { get; }
    public int TextureTableOrdinal { get; }
    public RenderMaterialUvRouteSnapshot? UvRoute { get; }
    public RenderSemanticIdentity? TextureIdentity { get; }
    public RenderSemanticIdentity? SamplerIdentity { get; }
    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-normal-camera-material-sampler/v1");
        writer.WriteInt32(SamplerArgIndex);
        writer.WriteInt32(SamplerDest);
        writer.WriteUInt32(SamplerHash);
        writer.WriteByte(TextureSemantic);
        writer.WriteString(TextureName);
        writer.WriteBoolean(WorldRuntimeTextureIdentity.HasValue);
        if (WorldRuntimeTextureIdentity is { } runtime)
        {
            writer.WriteInt32((int)runtime.Kind);
            writer.WriteByte(runtime.Ordinal);
        }
        writer.WriteInt32((int)EditorTextureRole);
        writer.WriteInt32(TextureTableOrdinal);
        writer.WriteBoolean(UvRoute is not null);
        UvRoute?.AppendContent(writer);
        writer.WriteBoolean(TextureIdentity.HasValue);
        if (TextureIdentity is { } texture)
            writer.WriteIdentity(texture);
        writer.WriteBoolean(SamplerIdentity.HasValue);
        if (SamplerIdentity is { } sampler)
            writer.WriteIdentity(sampler);
    }
}

/// <summary>
/// One immutable source pass before camera sorting. It owns exact semantic
/// resources and provenance but does not claim any executable shader lowering.
/// </summary>
public sealed class RenderNormalCameraPreparedPassSnapshot
{
    internal RenderNormalCameraPreparedPassSnapshot(
        RenderNormalCameraDrawSourceKind sourceKind,
        MapRenderWorldReceiverVariantKey? worldReceiverVariant,
        MapRenderStaticModelReceiverVariantKey? staticReceiverVariant,
        int sourceOrdinal,
        int collectionOrdinal,
        int? editorDrawGroupId,
        int? lodIndex,
        RenderMaterialPassProvenanceSnapshot sourcePass,
        RenderMaterialUvRouteSnapshot uvRoute,
        RenderState sourceState,
        byte sceneLightIndex,
        int unresolvedCodeSamplerCount,
        RenderWorldShaderProvenanceSnapshot shaderProvenance,
        MapRenderEditorDepthPrepassPlan? depthPrepass,
        RenderWorldShaderProvenanceSnapshot? depthPrepassShaderProvenance,
        MapRenderEditorVegetationAnimationPlan? vegetationAnimation,
        IEnumerable<RenderNormalCameraColorLayerSnapshot> colorLayers,
        IEnumerable<RenderNormalCameraMaterialSamplerSnapshot> materialSamplers,
        IEnumerable<RenderMaterialPickRangeSnapshot> pickRanges,
        ImmutableArray<MapRenderStaticModelInstance> staticInstances,
        string staticInstancesContentDigest,
        GfxCameraRegionType? staticCameraRegion,
        IEnumerable<RenderNormalCameraTextureResourceSnapshot> textureResources,
        RenderSemanticIdentity baseTextureIdentity,
        RenderSemanticIdentity baseSamplerIdentity,
        RenderSemanticIdentity? lightmapTextureIdentity,
        RenderSemanticIdentity? lightmapSamplerIdentity,
        RenderSemanticIdentity drawIdentity,
        RenderVertexLayoutDescriptor vertexLayout,
        RenderGeometryDescriptor geometry,
        RenderInstanceLayoutDescriptor? instanceLayout,
        RenderInstanceDescriptor? instances,
        ImmutableArray<float> rsxVertexInputs,
        string rsxVertexInputsContentDigest,
        RenderBounds localBounds)
    {
        if (!Enum.IsDefined(sourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (worldReceiverVariant.HasValue &&
            staticReceiverVariant.HasValue ||
            worldReceiverVariant.HasValue &&
            sourceKind != RenderNormalCameraDrawSourceKind.World ||
            staticReceiverVariant.HasValue &&
            sourceKind != RenderNormalCameraDrawSourceKind.StaticModel)
        {
            throw new ArgumentException(
                "Receiver-variant metadata must be mutually exclusive and match the prepared source kind.");
        }
        if (sourceOrdinal < 0 || collectionOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        if (unresolvedCodeSamplerCount < 0)
            throw new ArgumentOutOfRangeException(nameof(unresolvedCodeSamplerCount));
        ArgumentNullException.ThrowIfNull(sourcePass);
        ArgumentNullException.ThrowIfNull(uvRoute);
        ArgumentNullException.ThrowIfNull(shaderProvenance);
        ArgumentNullException.ThrowIfNull(vertexLayout);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            staticInstancesContentDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            rsxVertexInputsContentDigest);
        if (staticInstances.IsDefault)
            throw new ArgumentException(
                "Static instance storage is uninitialized.",
                nameof(staticInstances));
        if (rsxVertexInputs.IsDefault)
            throw new ArgumentException(
                "RSX vertex input storage is uninitialized.",
                nameof(rsxVertexInputs));
        RenderVertexLayoutDescriptor.RequireIdentity(
            drawIdentity,
            RenderSemanticResourceKind.Draw);
        if (!localBounds.IsValid || !Finite(localBounds.Min) ||
            !Finite(localBounds.Max))
        {
            throw new ArgumentException(
                "A prepared pass requires finite local bounds.",
                nameof(localBounds));
        }

        ImmutableArray<RenderNormalCameraColorLayerSnapshot> frozenLayers =
            RenderSnapshotCollections.Freeze(colorLayers, nameof(colorLayers));
        ImmutableArray<RenderNormalCameraMaterialSamplerSnapshot>
            frozenSamplers = RenderSnapshotCollections.Freeze(
                materialSamplers,
                nameof(materialSamplers));
        ImmutableArray<RenderMaterialPickRangeSnapshot> frozenRanges =
            RenderSnapshotCollections.Freeze(pickRanges, nameof(pickRanges));
        ImmutableArray<RenderNormalCameraTextureResourceSnapshot>
            frozenTextures = RenderSnapshotCollections.Freeze(
                textureResources,
                nameof(textureResources));
        if (frozenLayers.Any(value => value is null) ||
            frozenSamplers.Any(value => value is null) ||
            frozenRanges.Any(value => value is null) ||
            frozenTextures.IsEmpty ||
            frozenTextures.Any(value => value is null) ||
            (rsxVertexInputs.Length != 0 &&
             rsxVertexInputs.Length != checked(
                 geometry.VertexCount *
                 RenderWorldDrawPacketSnapshot.RsxVertexInputFloatStride)))
        {
            throw new ArgumentException(
                "Prepared pass provenance and resource collections must be initialized, and RSX vertex inputs must be empty or retain 16 float4 values per geometry vertex.");
        }
        if (frozenTextures.Select(value => value.ResourceOrdinal)
                .Distinct().Count() != frozenTextures.Length ||
            frozenTextures.Select(value => value.TextureIdentity)
                .Distinct().Count() != frozenTextures.Length ||
            frozenTextures.Select(value => value.SamplerIdentity)
                .Distinct().Count() != frozenTextures.Length)
        {
            throw new ArgumentException(
                "Prepared pass texture resources must have unique ordinals and identities.",
                nameof(textureResources));
        }
        RequireTexturePair(
            frozenTextures,
            baseTextureIdentity,
            baseSamplerIdentity,
            nameof(baseTextureIdentity));
        if (lightmapTextureIdentity.HasValue !=
            lightmapSamplerIdentity.HasValue)
        {
            throw new ArgumentException(
                "Lightmap texture and sampler identities must be specified together.");
        }
        if (lightmapTextureIdentity is { } lightmapTexture &&
            lightmapSamplerIdentity is { } lightmapSampler)
        {
            RequireTexturePair(
                frozenTextures,
                lightmapTexture,
                lightmapSampler,
                nameof(lightmapTextureIdentity));
        }
        foreach (RenderNormalCameraColorLayerSnapshot layer in frozenLayers)
        {
            RequireTexturePair(
                frozenTextures,
                layer.TextureIdentity,
                layer.SamplerIdentity,
                nameof(colorLayers));
        }
        foreach (RenderNormalCameraMaterialSamplerSnapshot sampler in
                 frozenSamplers)
        {
            if (sampler.TextureIdentity.HasValue !=
                sampler.SamplerIdentity.HasValue)
            {
                throw new ArgumentException(
                    "Material sampler texture and sampler identities must be specified together.",
                    nameof(materialSamplers));
            }
            if (sampler.TextureIdentity is { } texture &&
                sampler.SamplerIdentity is { } samplerIdentity)
            {
                RequireTexturePair(
                    frozenTextures,
                    texture,
                    samplerIdentity,
                    nameof(materialSamplers));
            }
        }

        bool hasInstanceResources = instanceLayout is not null && instances is not null;
        if ((instanceLayout is null) != (instances is null))
        {
            throw new ArgumentException(
                "Instance layout and instance payload must be specified together.");
        }
        if (sourceKind == RenderNormalCameraDrawSourceKind.World)
        {
            if (editorDrawGroupId.HasValue || lodIndex.HasValue ||
                hasInstanceResources || !staticInstances.IsEmpty ||
                staticCameraRegion.HasValue || vegetationAnimation is not null)
            {
                throw new ArgumentException(
                    "World pass contains static-model-only provenance.");
            }
            if (worldReceiverVariant.HasValue &&
                (frozenRanges.IsEmpty || frozenRanges.Any(range =>
                    range.Kind != MapRenderPickKind.GfxSurface ||
                    range.SurfaceIndex < 0 ||
                    range.ObjectIndex < 0)))
            {
                throw new ArgumentException(
                    "A world receiver variant requires exact GfxSurface ownership.",
                    nameof(worldReceiverVariant));
            }
        }
        else
        {
            if (!editorDrawGroupId.HasValue || !lodIndex.HasValue ||
                !hasInstanceResources || staticInstances.IsEmpty ||
                instances!.InstanceCount != staticInstances.Length ||
                instances.Layout != instanceLayout!.Identity)
            {
                throw new ArgumentException(
                    "Static pass requires exact group, LOD, placement, and instance-resource ownership.");
            }
            if (staticReceiverVariant is { } receiver &&
                staticInstances.Any(instance =>
                    !MapRenderStaticModelReceiverRouting
                        .CanPrepareAuthoredRegion(
                            receiver.Page,
                            instance.CameraRegion)))
            {
                throw new ArgumentException(
                    "A static receiver variant contains an identity outside its exact camera-region page.",
                    nameof(staticReceiverVariant));
            }
        }
        if (geometry.VertexLayout != vertexLayout.Identity ||
            !string.Equals(
                geometry.VertexLayoutContentDigest,
                vertexLayout.ContentDigest,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Prepared geometry does not own the supplied vertex layout.",
                nameof(geometry));
        }

        SourceKind = sourceKind;
        WorldReceiverVariant = worldReceiverVariant;
        StaticReceiverVariant = staticReceiverVariant;
        SourceOrdinal = sourceOrdinal;
        CollectionOrdinal = collectionOrdinal;
        EditorDrawGroupId = editorDrawGroupId;
        LodIndex = lodIndex;
        SourcePass = sourcePass;
        UvRoute = uvRoute;
        SourceState = sourceState;
        SceneLightIndex = sceneLightIndex;
        UnresolvedCodeSamplerCount = unresolvedCodeSamplerCount;
        ShaderProvenance = shaderProvenance;
        DepthPrepass = depthPrepass is null ? null : depthPrepass with { };
        DepthPrepassShaderProvenance = depthPrepassShaderProvenance;
        VegetationAnimation = vegetationAnimation;
        ColorLayers = frozenLayers;
        MaterialSamplers = frozenSamplers;
        PickRanges = frozenRanges;
        StaticInstances = staticInstances;
        StaticInstancesContentDigest = staticInstancesContentDigest;
        StaticCameraRegion = staticCameraRegion;
        TextureResources = frozenTextures;
        BaseTextureIdentity = baseTextureIdentity;
        BaseSamplerIdentity = baseSamplerIdentity;
        LightmapTextureIdentity = lightmapTextureIdentity;
        LightmapSamplerIdentity = lightmapSamplerIdentity;
        DrawIdentity = drawIdentity;
        VertexLayout = vertexLayout;
        Geometry = geometry;
        InstanceLayout = instanceLayout;
        Instances = instances;
        RsxVertexInputs = rsxVertexInputs;
        RsxVertexInputsContentDigest = rsxVertexInputsContentDigest;
        LocalBounds = localBounds;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderNormalCameraDrawSourceKind SourceKind { get; }

    public MapRenderWorldReceiverVariantKey? WorldReceiverVariant { get; }

    public MapRenderStaticModelReceiverVariantKey? StaticReceiverVariant
        { get; }

    /// <summary>
    /// Stable authored draw-group owner shared by every pass row in that
    /// source. Scheduled per-instance groups retain their own queue ordinal.
    /// </summary>
    public int SourceOrdinal { get; }

    /// <summary>
    /// Exact zero-based row in the original world or current-static batch
    /// collection. This is the unique coverage-accounting identity.
    /// </summary>
    public int CollectionOrdinal { get; }
    public int? EditorDrawGroupId { get; }
    public int? LodIndex { get; }
    public RenderMaterialPassProvenanceSnapshot SourcePass { get; }
    public RenderMaterialUvRouteSnapshot UvRoute { get; }
    public RenderState SourceState { get; }
    public byte SceneLightIndex { get; }
    public int UnresolvedCodeSamplerCount { get; }
    public RenderWorldShaderProvenanceSnapshot ShaderProvenance { get; }
    public MapRenderEditorDepthPrepassPlan? DepthPrepass { get; }
    public RenderWorldShaderProvenanceSnapshot? DepthPrepassShaderProvenance
        { get; }
    public MapRenderEditorVegetationAnimationPlan? VegetationAnimation { get; }
    public ImmutableArray<RenderNormalCameraColorLayerSnapshot> ColorLayers
        { get; }
    public ImmutableArray<RenderNormalCameraMaterialSamplerSnapshot>
        MaterialSamplers { get; }
    public ImmutableArray<RenderMaterialPickRangeSnapshot> PickRanges { get; }
    public ImmutableArray<MapRenderStaticModelInstance> StaticInstances { get; }
    internal string StaticInstancesContentDigest { get; }
    public GfxCameraRegionType? StaticCameraRegion { get; }
    public ImmutableArray<RenderNormalCameraTextureResourceSnapshot>
        TextureResources { get; }
    public RenderSemanticIdentity BaseTextureIdentity { get; }
    public RenderSemanticIdentity BaseSamplerIdentity { get; }
    public RenderSemanticIdentity? LightmapTextureIdentity { get; }
    public RenderSemanticIdentity? LightmapSamplerIdentity { get; }
    public RenderSemanticIdentity DrawIdentity { get; }
    public RenderVertexLayoutDescriptor VertexLayout { get; }
    public RenderGeometryDescriptor Geometry { get; }
    public RenderInstanceLayoutDescriptor? InstanceLayout { get; }
    public RenderInstanceDescriptor? Instances { get; }
    public ImmutableArray<float> RsxVertexInputs { get; }
    internal string RsxVertexInputsContentDigest { get; }
    public RenderBounds LocalBounds { get; }
    public string ContentDigest { get; }

    internal void ValidateResources(RenderResourceSnapshot resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (!ReferenceEquals(
                resources.RequireVertexLayout(VertexLayout.Identity),
                VertexLayout) ||
            !ReferenceEquals(
                resources.RequireGeometry(Geometry.Identity),
                Geometry))
        {
            throw new ArgumentException(
                "Normal-camera geometry must own the exact scene resource descriptors.",
                nameof(resources));
        }
        if (InstanceLayout is not null && Instances is not null &&
            (!ReferenceEquals(
                resources.RequireInstanceLayout(InstanceLayout.Identity),
                InstanceLayout) ||
             !ReferenceEquals(
                resources.RequireInstances(Instances.Identity),
                Instances)))
        {
            throw new ArgumentException(
                "Normal-camera instances must own the exact scene resource descriptors.",
                nameof(resources));
        }
        foreach (RenderNormalCameraTextureResourceSnapshot resource in
                 TextureResources)
        {
            if (!ReferenceEquals(
                    resources.RequireTexture(resource.TextureIdentity),
                    resource.Texture) ||
                !ReferenceEquals(
                    resources.RequireSampler(resource.SamplerIdentity),
                    resource.Sampler))
            {
                throw new ArgumentException(
                    "Normal-camera texture bindings must own the exact scene resource descriptors.",
                    nameof(resources));
            }
        }
    }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-normal-camera-prepared-pass/v3");
        writer.WriteInt32((int)SourceKind);
        writer.WriteBoolean(WorldReceiverVariant.HasValue);
        if (WorldReceiverVariant is { } worldReceiver)
        {
            writer.WriteInt32((int)worldReceiver.Page);
            writer.WriteInt32((int)worldReceiver.Allocation);
        }
        writer.WriteBoolean(StaticReceiverVariant.HasValue);
        if (StaticReceiverVariant is { } staticReceiver)
        {
            writer.WriteInt32((int)staticReceiver.Page);
            writer.WriteInt32((int)staticReceiver.Allocation);
        }
        writer.WriteInt32(SourceOrdinal);
        writer.WriteInt32(CollectionOrdinal);
        writer.WriteNullableInt32(EditorDrawGroupId);
        writer.WriteNullableInt32(LodIndex);
        SourcePass.AppendContent(writer);
        UvRoute.AppendContent(writer);
        writer.AppendRenderStateV1(SourceState);
        writer.WriteByte(SceneLightIndex);
        writer.WriteInt32(UnresolvedCodeSamplerCount);
        ShaderProvenance.AppendContent(writer);
        writer.WriteBoolean(DepthPrepass is not null);
        if (DepthPrepass is { } depth)
        {
            writer.WriteString(depth.MaterialName);
            writer.WriteString(depth.TechniqueSetName);
            writer.WriteInt32(depth.TechniqueSlot);
            writer.WriteString(depth.TechniqueName);
            writer.WriteInt32(depth.PassIndex);
            writer.WriteInt32((int)depth.TechniqueFlags);
            writer.WriteString(depth.VertexProgramName);
            writer.WriteString(depth.PixelProgramName);
            writer.WriteInt32((int)depth.Program);
            writer.AppendRenderStateV1(depth.State);
        }
        writer.WriteBoolean(DepthPrepassShaderProvenance is not null);
        DepthPrepassShaderProvenance?.AppendContent(writer);
        writer.WriteBoolean(VegetationAnimation is not null);
        if (VegetationAnimation is { } vegetation)
        {
            writer.WriteInt32((int)vegetation.Status);
            writer.WriteBoolean(vegetation.IsEnabled);
            writer.WriteSingle(vegetation.Amplitude);
            writer.WriteSingle(vegetation.AngularFrequency);
            writer.WriteSingle(vegetation.SpatialFrequency);
            writer.WriteString(vegetation.Reason);
        }
        writer.WriteInt32(ColorLayers.Length);
        foreach (RenderNormalCameraColorLayerSnapshot layer in ColorLayers)
            layer.AppendContent(writer);
        writer.WriteInt32(MaterialSamplers.Length);
        foreach (RenderNormalCameraMaterialSamplerSnapshot sampler in
                 MaterialSamplers)
        {
            sampler.AppendContent(writer);
        }
        writer.WriteInt32(PickRanges.Length);
        foreach (RenderMaterialPickRangeSnapshot range in PickRanges)
            range.AppendContent(writer);
        writer.WriteInt32(StaticInstances.Length);
        writer.WriteString(StaticInstancesContentDigest);
        writer.WriteBoolean(StaticCameraRegion.HasValue);
        if (StaticCameraRegion is { } cameraRegion)
            writer.WriteByte((byte)cameraRegion);
        writer.WriteInt32(TextureResources.Length);
        foreach (RenderNormalCameraTextureResourceSnapshot resource in
                 TextureResources)
        {
            resource.AppendContent(writer);
        }
        writer.WriteIdentity(BaseTextureIdentity);
        writer.WriteIdentity(BaseSamplerIdentity);
        writer.WriteBoolean(LightmapTextureIdentity.HasValue);
        if (LightmapTextureIdentity is { } lightmapTexture)
            writer.WriteIdentity(lightmapTexture);
        writer.WriteBoolean(LightmapSamplerIdentity.HasValue);
        if (LightmapSamplerIdentity is { } lightmapSampler)
            writer.WriteIdentity(lightmapSampler);
        writer.WriteIdentity(DrawIdentity);
        writer.WriteIdentity(VertexLayout.Identity);
        writer.WriteString(VertexLayout.ContentDigest);
        writer.WriteIdentity(Geometry.Identity);
        writer.WriteString(Geometry.ContentDigest);
        writer.WriteBoolean(InstanceLayout is not null);
        if (InstanceLayout is not null)
        {
            writer.WriteIdentity(InstanceLayout.Identity);
            writer.WriteString(InstanceLayout.ContentDigest);
        }
        writer.WriteBoolean(Instances is not null);
        if (Instances is not null)
        {
            writer.WriteIdentity(Instances.Identity);
            writer.WriteString(Instances.ContentDigest);
        }
        writer.WriteInt32(RsxVertexInputs.Length);
        writer.WriteString(RsxVertexInputsContentDigest);
        AppendBounds(writer, LocalBounds);
    }

    internal static string ComputeStaticInstancesContentDigest(
        ImmutableArray<MapRenderStaticModelInstance> instances)
    {
        if (instances.IsDefault)
        {
            throw new ArgumentException(
                "Static instance storage is uninitialized.",
                nameof(instances));
        }
        return RenderContentDigest.Compute(writer =>
        {
            writer.WriteString("render-normal-camera-static-instances/v1");
            writer.WriteInt32(instances.Length);
            foreach (MapRenderStaticModelInstance instance in instances)
                AppendInstance(writer, instance);
        });
    }

    internal static string ComputeRsxVertexInputsContentDigest(
        ImmutableArray<float> values)
    {
        if (values.IsDefault)
        {
            throw new ArgumentException(
                "RSX vertex input storage is uninitialized.",
                nameof(values));
        }
        return RenderContentDigest.Compute(writer =>
        {
            writer.WriteString("render-normal-camera-rsx-vertex-inputs/v1");
            writer.WriteSingles(values);
        });
    }

    private static void RequireTexturePair(
        ImmutableArray<RenderNormalCameraTextureResourceSnapshot> resources,
        RenderSemanticIdentity texture,
        RenderSemanticIdentity sampler,
        string parameterName)
    {
        bool found = resources.Any(value =>
            value.TextureIdentity == texture &&
            value.SamplerIdentity == sampler);
        if (!found)
        {
            throw new ArgumentException(
                "Texture and sampler identities do not name one owned resource pair.",
                parameterName);
        }
    }

    private static void AppendInstance(
        RenderContentDigestWriter writer,
        MapRenderStaticModelInstance instance)
    {
        AppendVector(writer, instance.TransformRow0);
        AppendVector(writer, instance.TransformRow1);
        AppendVector(writer, instance.TransformRow2);
        writer.WriteInt32(instance.ObjectIndex);
        writer.WriteInt32(instance.SurfaceIndex);
        writer.WriteString(instance.Name);
        writer.WriteString(instance.AuthoredMaterialName);
        writer.WriteByte((byte)instance.CameraRegion);
        writer.WriteInt32(instance.PrimaryLightIndex);
        writer.WriteByte(instance.ReflectionProbeIndex);
        writer.WriteBoolean(instance.AuthoredLightingIdentity.HasValue);
        if (instance.AuthoredLightingIdentity is { } lighting)
        {
            writer.WriteInt32(lighting.LightingHandle);
            writer.WriteUInt32(lighting.GroundLighting.Packed);
            writer.WriteByte((byte)lighting.Flags);
        }
        AppendVector(writer, instance.BaseLightingCoords);
        AppendVector(writer, instance.LightProbeAmbient);
    }

    private static void AppendVector(
        RenderContentDigestWriter writer,
        Vector4 value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        writer.WriteSingle(value.Z);
        writer.WriteSingle(value.W);
    }

    private static void AppendBounds(
        RenderContentDigestWriter writer,
        RenderBounds bounds)
    {
        writer.WriteSingle(bounds.Min.X);
        writer.WriteSingle(bounds.Min.Y);
        writer.WriteSingle(bounds.Min.Z);
        writer.WriteSingle(bounds.Max.X);
        writer.WriteSingle(bounds.Max.Y);
        writer.WriteSingle(bounds.Max.Z);
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

/// <summary>
/// One scheduled authored pass. The draw range selects either the complete
/// source instance resource or exactly one independently sorted translucent
/// instance.
/// </summary>
public sealed class RenderNormalCameraDrawSubmissionSnapshot
{
    internal RenderNormalCameraDrawSubmissionSnapshot(
        RenderSemanticIdentity drawIdentity,
        RenderNormalCameraPreparedPassSnapshot preparedPass,
        RenderDrawRange range,
        int? staticInstanceIndex)
    {
        RenderVertexLayoutDescriptor.RequireIdentity(
            drawIdentity,
            RenderSemanticResourceKind.Draw);
        ArgumentNullException.ThrowIfNull(preparedPass);
        if (preparedPass.SourceKind == RenderNormalCameraDrawSourceKind.World)
        {
            if (staticInstanceIndex.HasValue ||
                range.FirstIndex != 0 ||
                range.IndexCount != preparedPass.Geometry.IndexCount ||
                range.BaseVertex != 0 ||
                range.FirstInstance != 0 ||
                range.InstanceCount != 1)
            {
                throw new ArgumentException(
                    "World submissions must select the complete non-instanced source geometry.",
                    nameof(range));
            }
        }
        else if (staticInstanceIndex is { } instanceIndex)
        {
            if ((uint)instanceIndex >=
                    (uint)preparedPass.StaticInstances.Length ||
                range.FirstInstance != instanceIndex ||
                range.InstanceCount != 1)
            {
                throw new ArgumentException(
                    "Per-instance static submission range does not match its selected instance.",
                    nameof(range));
            }
        }
        else if (range.FirstInstance != 0 ||
                 range.InstanceCount != preparedPass.StaticInstances.Length)
        {
            throw new ArgumentException(
                "Instanced static submission must select the complete prepared instance resource.",
                nameof(range));
        }
        if (range.FirstIndex != 0 ||
            range.IndexCount != preparedPass.Geometry.IndexCount ||
            range.BaseVertex != 0)
        {
            throw new ArgumentException(
                "Normal-camera submissions currently own complete source geometry ranges.",
                nameof(range));
        }

        DrawIdentity = drawIdentity;
        PreparedPass = preparedPass;
        Range = range;
        StaticInstanceIndex = staticInstanceIndex;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderSemanticIdentity DrawIdentity { get; }
    public RenderNormalCameraPreparedPassSnapshot PreparedPass { get; }
    public RenderDrawRange Range { get; }
    public int? StaticInstanceIndex { get; }
    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-normal-camera-draw-submission/v1");
        writer.WriteIdentity(DrawIdentity);
        writer.WriteString(PreparedPass.ContentDigest);
        writer.WriteInt32(Range.FirstIndex);
        writer.WriteInt32(Range.IndexCount);
        writer.WriteInt32(Range.BaseVertex);
        writer.WriteInt32(Range.FirstInstance);
        writer.WriteInt32(Range.InstanceCount);
        writer.WriteNullableInt32(StaticInstanceIndex);
    }
}

/// <summary>
/// Complete immutable scene-lifetime normal-camera textured-draw inventory.
/// Every enumerated source is represented by exactly one prepared pass or one
/// typed omission.
/// </summary>
public sealed class RenderNormalCameraDrawSnapshot
{
    internal static RenderNormalCameraDrawSnapshot Empty { get; } = new(
        RenderNormalCameraDrawCoverage
            .PreparedWorldAndCurrentStaticBatchesWithoutDynamicLodOrDpvs,
        new RenderResourceSnapshot(
            [],
            [],
            [],
            []),
        worldSourceCount: 0,
        staticSourceCount: 0,
        preparedPasses: [],
        omissions: [],
        drawGroups: []);

    internal RenderNormalCameraDrawSnapshot(
        RenderNormalCameraDrawCoverage coverage,
        RenderResourceSnapshot resources,
        int worldSourceCount,
        int staticSourceCount,
        IEnumerable<RenderNormalCameraPreparedPassSnapshot> preparedPasses,
        IEnumerable<RenderNormalCameraDrawOmissionSnapshot> omissions,
        IEnumerable<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> drawGroups)
    {
        if (!Enum.IsDefined(coverage))
            throw new ArgumentOutOfRangeException(nameof(coverage));
        ArgumentNullException.ThrowIfNull(resources);
        if (worldSourceCount < 0 || staticSourceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(worldSourceCount));
        ImmutableArray<RenderNormalCameraPreparedPassSnapshot> frozenPasses =
            RenderSnapshotCollections.Freeze(
                preparedPasses,
                nameof(preparedPasses));
        ImmutableArray<RenderNormalCameraDrawOmissionSnapshot>
            frozenOmissions = RenderSnapshotCollections.Freeze(
                omissions,
                nameof(omissions));
        ImmutableArray<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> frozenGroups =
                RenderSnapshotCollections.Freeze(
                    drawGroups,
                    nameof(drawGroups));
        if (frozenPasses.Any(value => value is null) ||
            frozenOmissions.Any(value => value is null) ||
            frozenGroups.Any(value => value is null))
        {
            throw new ArgumentException(
                "Normal-camera snapshot collections cannot contain null values.");
        }

        int sourceCount = checked(worldSourceCount + staticSourceCount);
        var accounted = new bool[sourceCount];
        foreach (RenderNormalCameraPreparedPassSnapshot pass in frozenPasses)
        {
            int accountingOrdinal = ValidateSourceOrdinal(
                pass.SourceKind,
                pass.SourceOrdinal,
                pass.CollectionOrdinal,
                worldSourceCount,
                staticSourceCount);
            if (accounted[accountingOrdinal])
            {
                throw new ArgumentException(
                    "A normal-camera collection row was accounted more than once.",
                    nameof(preparedPasses));
            }
            accounted[accountingOrdinal] = true;
        }
        foreach (RenderNormalCameraDrawOmissionSnapshot omission in
                 frozenOmissions)
        {
            if (omission.SourceOrdinal is not { } sourceOrdinal ||
                omission.CollectionOrdinal is not { } collectionOrdinal)
            {
                continue;
            }
            int accountingOrdinal = ValidateSourceOrdinal(
                omission.SourceKind,
                sourceOrdinal,
                collectionOrdinal,
                worldSourceCount,
                staticSourceCount);
            if (accounted[accountingOrdinal])
            {
                throw new ArgumentException(
                    "A normal-camera collection row was accounted more than once.",
                    nameof(omissions));
            }
            accounted[accountingOrdinal] = true;
        }
        if (accounted.Any(value => !value))
        {
            throw new ArgumentException(
                "Every enumerated normal-camera source must be prepared or omitted.");
        }
        if (!IsAscending(frozenGroups.Select(value => value.SourceOrdinal)))
        {
            throw new ArgumentException(
                "Draw groups must preserve ascending scheduled source order.");
        }

        var preparedReferences = new HashSet<
            RenderNormalCameraPreparedPassSnapshot>(
                ReferenceEqualityComparer.Instance);
        preparedReferences.UnionWith(frozenPasses);
        var referencedPasses = new HashSet<
            RenderNormalCameraPreparedPassSnapshot>(
                ReferenceEqualityComparer.Instance);
        var drawsByPreparedPass = new Dictionary<
            RenderNormalCameraPreparedPassSnapshot,
            List<RenderNormalCameraDrawSubmissionSnapshot>>(
                ReferenceEqualityComparer.Instance);
        foreach (RenderNormalCameraPreparedPassSnapshot pass in frozenPasses)
        {
            drawsByPreparedPass.Add(
                pass,
                new List<RenderNormalCameraDrawSubmissionSnapshot>());
        }
        var drawIdentities = new HashSet<RenderSemanticIdentity>();
        foreach (MapRenderEditorDrawGroup<
                     RenderNormalCameraDrawSubmissionSnapshot> group in
                 frozenGroups)
        {
            if (group.AuthoredPasses.Select(draw =>
                    draw.PreparedPass.SourceOrdinal).Distinct().Count() != 1)
            {
                throw new ArgumentException(
                    "Every authored pass in one normal-camera group must share one source owner.",
                    nameof(drawGroups));
            }
            for (int passOrdinal = 1;
                 passOrdinal < group.AuthoredPasses.Count;
                 passOrdinal++)
            {
                RenderNormalCameraPreparedPassSnapshot previous =
                    group.AuthoredPasses[passOrdinal - 1].PreparedPass;
                RenderNormalCameraPreparedPassSnapshot current =
                    group.AuthoredPasses[passOrdinal].PreparedPass;
                if (previous.SourcePass.PassIndex >
                        current.SourcePass.PassIndex ||
                    previous.SourcePass.PassIndex ==
                        current.SourcePass.PassIndex &&
                    previous.CollectionOrdinal >= current.CollectionOrdinal)
                {
                    throw new ArgumentException(
                        "Normal-camera authored passes must preserve pass-index then collection order.",
                        nameof(drawGroups));
                }
            }
            MapRenderEditorDrawBucketClassification expectedClassification =
                MapRenderEditorDrawBucketClassifier.Classify(
                    group.AuthoredPasses.Select(draw =>
                        draw.PreparedPass.SourceState).ToArray());
            if (expectedClassification.Bucket != group.Bucket ||
                expectedClassification.UsesOpaqueStateFallback !=
                    group.Classification.UsesOpaqueStateFallback)
            {
                throw new ArgumentException(
                    "Normal-camera draw-group classification does not match its complete authored pass states.",
                    nameof(drawGroups));
            }
            foreach (RenderNormalCameraDrawSubmissionSnapshot draw in
                     group.AuthoredPasses)
            {
                if (!preparedReferences.Contains(draw.PreparedPass))
                {
                    throw new ArgumentException(
                        "A normal-camera draw group references no prepared source pass.",
                        nameof(drawGroups));
                }
                referencedPasses.Add(draw.PreparedPass);
                drawsByPreparedPass[draw.PreparedPass].Add(draw);
                if (!drawIdentities.Add(draw.DrawIdentity))
                {
                    throw new ArgumentException(
                        "Normal-camera scheduled draw identities must be unique.",
                        nameof(drawGroups));
                }
            }
        }
        if (!preparedReferences.SetEquals(referencedPasses))
        {
            throw new ArgumentException(
                "Every prepared normal-camera pass must be owned by at least one draw group.",
                nameof(drawGroups));
        }
        foreach ((RenderNormalCameraPreparedPassSnapshot pass,
                  List<RenderNormalCameraDrawSubmissionSnapshot> draws) in
                 drawsByPreparedPass)
        {
            if (pass.SourceKind == RenderNormalCameraDrawSourceKind.World)
            {
                if (draws.Count != 1)
                {
                    throw new ArgumentException(
                        "Every prepared world pass must be scheduled exactly once.",
                        nameof(drawGroups));
                }
                continue;
            }

            int[] selectedInstances = draws
                .Where(draw => draw.StaticInstanceIndex.HasValue)
                .Select(draw => draw.StaticInstanceIndex!.Value)
                .Order()
                .ToArray();
            if (selectedInstances.Length == 0)
            {
                if (draws.Count != 1 ||
                    draws[0].StaticInstanceIndex.HasValue)
                {
                    throw new ArgumentException(
                        "An instanced static pass must be scheduled exactly once.",
                        nameof(drawGroups));
                }
            }
            else if (selectedInstances.Length != draws.Count ||
                     !selectedInstances.SequenceEqual(
                         Enumerable.Range(
                             0,
                             pass.StaticInstances.Length)))
            {
                throw new ArgumentException(
                    "A translucent static pass must schedule every instance exactly once.",
                    nameof(drawGroups));
            }
        }

        Coverage = coverage;
        Resources = resources;
        WorldSourceCount = worldSourceCount;
        StaticSourceCount = staticSourceCount;
        PreparedPasses = frozenPasses;
        Omissions = frozenOmissions;
        DrawGroups = frozenGroups;
        ValidateResources(resources);
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderNormalCameraDrawCoverage Coverage { get; }
    public RenderResourceSnapshot Resources { get; }
    public int WorldSourceCount { get; }
    public int StaticSourceCount { get; }
    public int SourceCount => checked(WorldSourceCount + StaticSourceCount);
    public ImmutableArray<RenderNormalCameraPreparedPassSnapshot>
        PreparedPasses { get; }
    public ImmutableArray<RenderNormalCameraDrawOmissionSnapshot> Omissions
        { get; }
    public ImmutableArray<MapRenderEditorDrawGroup<
        RenderNormalCameraDrawSubmissionSnapshot>> DrawGroups { get; }
    public string ContentDigest { get; }

    public RenderNormalCameraDrawFramePlan CreateFramePlan(
        Vector3 cameraPosition,
        Vector3 cameraForward) =>
        new(this, cameraPosition, cameraForward);

    /// <summary>
    /// Creates one backend-owned moving-camera ordering workspace. Creation is
    /// a scene/setup operation; after its first order call, repeated calls
    /// reuse the same frame-local view and scratch storage.
    /// </summary>
    public RenderNormalCameraDrawFrameOrderWorkspace
        CreateFrameOrderWorkspace() => new(this);

    internal void ValidateResources(RenderResourceSnapshot resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var sourceDraws = new HashSet<RenderSemanticIdentity>();
        foreach (RenderNormalCameraPreparedPassSnapshot pass in PreparedPasses)
        {
            if (!sourceDraws.Add(pass.DrawIdentity))
            {
                throw new ArgumentException(
                    "Normal-camera prepared draw identities must be unique.",
                    nameof(resources));
            }
            pass.ValidateResources(resources);
        }
    }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-normal-camera-draw-snapshot/v3");
        writer.WriteInt32((int)Coverage);
        writer.WriteString(Resources.ContentDigest);
        writer.WriteInt32(WorldSourceCount);
        writer.WriteInt32(StaticSourceCount);
        writer.WriteInt32(PreparedPasses.Length);
        foreach (RenderNormalCameraPreparedPassSnapshot pass in PreparedPasses)
            writer.WriteString(pass.ContentDigest);
        writer.WriteInt32(Omissions.Length);
        foreach (RenderNormalCameraDrawOmissionSnapshot omission in Omissions)
            writer.WriteString(omission.ContentDigest);
        writer.WriteInt32(DrawGroups.Length);
        foreach (MapRenderEditorDrawGroup<
                     RenderNormalCameraDrawSubmissionSnapshot> group in
                 DrawGroups)
        {
            AppendGroup(writer, group);
        }
    }

    internal static void AppendGroup(
        RenderContentDigestWriter writer,
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot> group)
    {
        writer.WriteInt64(group.SourceOrdinal);
        writer.WriteInt32((int)group.Bucket);
        writer.WriteBoolean(
            group.Classification.UsesOpaqueStateFallback);
        writer.WriteBoolean(group.SortCenter.HasValue);
        if (group.SortCenter is { } center)
        {
            writer.WriteSingle(center.X);
            writer.WriteSingle(center.Y);
            writer.WriteSingle(center.Z);
        }
        writer.WriteBoolean(group.ExplicitDepth.HasValue);
        if (group.ExplicitDepth is { } depth)
            writer.WriteSingle(depth);
        writer.WriteBoolean(group.CameraIndependentSortKey.HasValue);
        if (group.CameraIndependentSortKey is { } sortKey)
            writer.WriteInt64(sortKey);
        writer.WriteInt32(group.AuthoredPasses.Count);
        foreach (RenderNormalCameraDrawSubmissionSnapshot draw in
                 group.AuthoredPasses)
        {
            draw.AppendContent(writer);
        }
    }

    private static int ValidateSourceOrdinal(
        RenderNormalCameraDrawSourceKind sourceKind,
        int sourceOrdinal,
        int collectionOrdinal,
        int worldSourceCount,
        int staticSourceCount)
    {
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        int accountingOrdinal = sourceKind switch
        {
            RenderNormalCameraDrawSourceKind.World => collectionOrdinal,
            RenderNormalCameraDrawSourceKind.StaticModel =>
                checked(worldSourceCount + collectionOrdinal),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind))
        };
        int collectionCount = sourceKind ==
            RenderNormalCameraDrawSourceKind.World
                ? worldSourceCount
                : staticSourceCount;
        if (collectionOrdinal < 0 || collectionOrdinal >= collectionCount ||
            accountingOrdinal < 0)
        {
            throw new ArgumentException(
                "Normal-camera collection ordinal is outside its exact source collection.");
        }
        return accountingOrdinal;
    }

    private static bool IsAscending(IEnumerable<long> values)
    {
        bool first = true;
        long previous = 0;
        foreach (long value in values)
        {
            if (!first && value <= previous)
                return false;
            first = false;
            previous = value;
        }
        return true;
    }
}

/// <summary>
/// Reusable allocation-free moving-camera ordering for one immutable normal-
/// camera draw snapshot. The returned list is frame-local and its contents are
/// overwritten by a later <see cref="Order"/> call on this workspace.
/// Backends that need retained frame state should use
/// <see cref="RenderNormalCameraDrawSnapshot.CreateFramePlan"/> instead.
/// </summary>
public sealed class RenderNormalCameraDrawFrameOrderWorkspace
{
    private readonly MapRenderEditorDrawGroup<
        RenderNormalCameraDrawSubmissionSnapshot>[] _groups;

    internal RenderNormalCameraDrawFrameOrderWorkspace(
        RenderNormalCameraDrawSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        _groups = source.DrawGroups.ToArray();
    }

    public RenderNormalCameraDrawSnapshot Source { get; }

    public int GroupCapacity => _groups.Length;

    public IReadOnlyList<MapRenderEditorDrawGroup<
        RenderNormalCameraDrawSubmissionSnapshot>> Order(
            Vector3 cameraPosition,
            Vector3 cameraForward) =>
        MapRenderEditorDrawQueueSorter.SortImmutableFrame(
            _groups,
            cameraPosition,
            cameraForward);
}

/// <summary>
/// Immutable camera-specific ordering of the complete prepared draw inventory.
/// This is semantic frame-planning data, not a GPU command abstraction.
/// </summary>
public sealed class RenderNormalCameraDrawFramePlan
{
    internal RenderNormalCameraDrawFramePlan(
        RenderNormalCameraDrawSnapshot source,
        Vector3 cameraPosition,
        Vector3 cameraForward)
    {
        ArgumentNullException.ThrowIfNull(source);
        IReadOnlyList<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> ordered =
                MapRenderEditorDrawQueueSorter.Sort(
                    source.DrawGroups,
                    cameraPosition,
                    cameraForward);

        Source = source;
        CameraPosition = cameraPosition;
        CameraForward = cameraForward;
        OrderedGroups = ordered.ToImmutableArray();
        OrderedDraws = OrderedGroups
            .SelectMany(group => group.AuthoredPasses)
            .ToImmutableArray();
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderNormalCameraDrawSnapshot Source { get; }
    public Vector3 CameraPosition { get; }
    public Vector3 CameraForward { get; }
    public ImmutableArray<MapRenderEditorDrawGroup<
        RenderNormalCameraDrawSubmissionSnapshot>> OrderedGroups { get; }
    public ImmutableArray<RenderNormalCameraDrawSubmissionSnapshot>
        OrderedDraws { get; }
    public string ContentDigest { get; }

    private void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-normal-camera-draw-frame-plan/v1");
        writer.WriteString(Source.ContentDigest);
        writer.WriteSingle(CameraPosition.X);
        writer.WriteSingle(CameraPosition.Y);
        writer.WriteSingle(CameraPosition.Z);
        writer.WriteSingle(CameraForward.X);
        writer.WriteSingle(CameraForward.Y);
        writer.WriteSingle(CameraForward.Z);
        writer.WriteInt32(OrderedGroups.Length);
        foreach (MapRenderEditorDrawGroup<
                     RenderNormalCameraDrawSubmissionSnapshot> group in
                 OrderedGroups)
        {
            RenderNormalCameraDrawSnapshot.AppendGroup(writer, group);
        }
    }
}
