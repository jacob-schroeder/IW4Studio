using IW4.Render.Techniques;
using System.Collections.Immutable;

using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Textures;

namespace IW4.Render.Resources;

/// <summary>
/// Exact source-pass facts retained independently from the executable builtin
/// material used by the first generic material-preview renderer slice.
/// Negative selector values are meaningful source provenance and are never
/// normalized into executable pass identities here.
/// </summary>
public sealed class RenderMaterialPassProvenanceSnapshot
{
    internal RenderMaterialPassProvenanceSnapshot(
        MaterialPassIdentity source,
        MaterialSamplerIdentity primarySampler,
        byte texCoordSource)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.MaterialName);
        ArgumentNullException.ThrowIfNull(
            source.TechniquePass.TechniqueSetName);
        ArgumentNullException.ThrowIfNull(source.TechniquePass.TechniqueName);
        ArgumentNullException.ThrowIfNull(source.TechniquePass.PassClass);

        MaterialName = source.MaterialName;
        TechniqueSetName = source.TechniquePass.TechniqueSetName;
        TechniqueSlot = source.TechniquePass.TechniqueSlot;
        TechniqueName = source.TechniquePass.TechniqueName;
        PassClass = source.TechniquePass.PassClass;
        PassIndex = source.TechniquePass.PassIndex;
        SamplerArgIndex = primarySampler.SamplerArgIndex;
        SamplerDest = primarySampler.SamplerDest;
        SamplerHash = primarySampler.SamplerHash;
        TextureSemantic = primarySampler.TextureSemantic;
        TexCoordSource = texCoordSource;
        CustomSamplerFlags = source.TechniquePass.CustomSamplerFlags;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public string MaterialName { get; }

    public string TechniqueSetName { get; }

    public int TechniqueSlot { get; }

    public string TechniqueName { get; }

    public string PassClass { get; }

    public int PassIndex { get; }

    public int SamplerArgIndex { get; }

    public ushort SamplerDest { get; }

    public uint SamplerHash { get; }

    public byte TextureSemantic { get; }

    public byte TexCoordSource { get; }

    public byte CustomSamplerFlags { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-material-pass-provenance/v1");
        writer.WriteString(MaterialName);
        writer.WriteString(TechniqueSetName);
        writer.WriteInt32(TechniqueSlot);
        writer.WriteString(TechniqueName);
        writer.WriteString(PassClass);
        writer.WriteInt32(PassIndex);
        writer.WriteInt32(SamplerArgIndex);
        writer.WriteInt32(SamplerDest);
        writer.WriteUInt32(SamplerHash);
        writer.WriteByte(TextureSemantic);
        writer.WriteByte(TexCoordSource);
        writer.WriteByte(CustomSamplerFlags);
    }
}

/// <summary>
/// Frozen source UV routing facts. This is the authored/decoded route that
/// produced UV0 in the retained stride-88 vertex payload, not a backend
/// vertex-input description.
/// </summary>
public sealed class RenderMaterialUvRouteSnapshot
{
    internal RenderMaterialUvRouteSnapshot(UvRoute source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Label);
        ArgumentNullException.ThrowIfNull(source.WorldVertexFormat);

        Label = source.Label;
        WorldVertexFormat = source.WorldVertexFormat;
        TexCoordSource = source.TexCoordSource;
        StreamIndex = source.StreamIndex;
        Stride = source.Stride;
        Offset = source.Offset;
        FormatByte0 = source.FormatByte0;
        FormatByte1 = source.FormatByte1;
        BaseMode = source.BaseMode;
        ComponentA = source.ComponentA;
        ComponentB = source.ComponentB;
        ScaleU = source.ScaleU;
        ScaleV = source.ScaleV;
        AddU = source.AddU;
        AddV = source.AddV;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public string Label { get; }

    public string WorldVertexFormat { get; }

    public byte TexCoordSource { get; }

    public byte StreamIndex { get; }

    public int Stride { get; }

    public int Offset { get; }

    public byte FormatByte0 { get; }

    public byte FormatByte1 { get; }

    public UvBaseMode BaseMode { get; }

    public int ComponentA { get; }

    public int ComponentB { get; }

    public float ScaleU { get; }

    public float ScaleV { get; }

    public float AddU { get; }

    public float AddV { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-material-uv-route/v1");
        writer.WriteString(Label);
        writer.WriteString(WorldVertexFormat);
        writer.WriteByte(TexCoordSource);
        writer.WriteByte(StreamIndex);
        writer.WriteInt32(Stride);
        writer.WriteInt32(Offset);
        writer.WriteByte(FormatByte0);
        writer.WriteByte(FormatByte1);
        writer.WriteInt32((int)BaseMode);
        writer.WriteInt32(ComponentA);
        writer.WriteInt32(ComponentB);
        writer.WriteSingle(ScaleU);
        writer.WriteSingle(ScaleV);
        writer.WriteSingle(AddU);
        writer.WriteSingle(AddV);
    }
}

/// <summary>
/// Source material-table and selected-color-layer provenance for the one
/// texture binding admitted into the generic opaque preview packet.
/// </summary>
public sealed class RenderMaterialTextureBindingProvenanceSnapshot
{
    internal RenderMaterialTextureBindingProvenanceSnapshot(
        MaterialColorLayer layer,
        MaterialSamplerBinding sampler,
        MapRenderWorldRuntimeTextureIdentity? runtimeTextureIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(sampler);
        ArgumentNullException.ThrowIfNull(sampler.TextureName);

        LayerIndex = layer.LayerIndex;
        SamplerArgIndex = layer.Identity.SamplerArgIndex;
        SamplerDest = layer.Identity.SamplerDest;
        SamplerHash = layer.Identity.SamplerHash;
        TextureSemantic = layer.Identity.TextureSemantic;
        BlendWeightComponent = layer.BlendWeightComponent;
        TextureName = sampler.TextureName;
        WorldRuntimeTextureIdentity = runtimeTextureIdentity;
        EditorTextureRole = sampler.EditorTextureRole;
        TextureTableOrdinal = sampler.TextureTableOrdinal;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public int LayerIndex { get; }

    public int SamplerArgIndex { get; }

    public ushort SamplerDest { get; }

    public uint SamplerHash { get; }

    public byte TextureSemantic { get; }

    public int BlendWeightComponent { get; }

    public string TextureName { get; }

    public MapRenderWorldRuntimeTextureIdentity? WorldRuntimeTextureIdentity
        { get; }

    public EditorMaterialTextureRole EditorTextureRole { get; }

    public int TextureTableOrdinal { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-material-texture-binding-provenance/v1");
        writer.WriteInt32(LayerIndex);
        writer.WriteInt32(SamplerArgIndex);
        writer.WriteInt32(SamplerDest);
        writer.WriteUInt32(SamplerHash);
        writer.WriteByte(TextureSemantic);
        writer.WriteInt32(BlendWeightComponent);
        writer.WriteString(TextureName);
        writer.WriteBoolean(WorldRuntimeTextureIdentity.HasValue);
        if (WorldRuntimeTextureIdentity is { } runtimeIdentity)
        {
            writer.WriteInt32((int)runtimeIdentity.Kind);
            writer.WriteByte(runtimeIdentity.Ordinal);
        }
        writer.WriteInt32((int)EditorTextureRole);
        writer.WriteInt32(TextureTableOrdinal);
    }
}

/// <summary>
/// Immutable source ownership metadata for one range in a textured batch.
/// These are exact scene-builder facts, not a backend picking identity or a
/// visibility result.
/// </summary>
public sealed class RenderMaterialPickRangeSnapshot
{
    internal RenderMaterialPickRangeSnapshot(MapRenderPickRange source)
    {
        ArgumentNullException.ThrowIfNull(source.Name);
        ArgumentNullException.ThrowIfNull(source.AuthoredMaterialName);

        Kind = source.Kind;
        ObjectIndex = source.ObjectIndex;
        SurfaceIndex = source.SurfaceIndex;
        FirstIndex = source.FirstIndex;
        IndexCount = source.IndexCount;
        Name = source.Name;
        AuthoredMaterialName = source.AuthoredMaterialName;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public MapRenderPickKind Kind { get; }

    public int ObjectIndex { get; }

    public int SurfaceIndex { get; }

    public int FirstIndex { get; }

    public int IndexCount { get; }

    public string Name { get; }

    public string AuthoredMaterialName { get; }

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-material-pick-range/v1");
        writer.WriteInt32((int)Kind);
        writer.WriteInt32(ObjectIndex);
        writer.WriteInt32(SurfaceIndex);
        writer.WriteInt32(FirstIndex);
        writer.WriteInt32(IndexCount);
        writer.WriteString(Name);
        writer.WriteString(AuthoredMaterialName);
    }
}

/// <summary>
/// One immutable, backend-neutral generic opaque material draw. Its source
/// provenance remains separate from the builtin executable preview program.
/// </summary>
public sealed class RenderMaterialDrawPacketSnapshot
{
    public const int VertexStrideBytes =
        MapRenderScene.TexturedVertexFloatCount * sizeof(float);

    public static RenderState RequiredEffectiveState { get; } =
        RenderState.Default with { HasState = true };

    internal RenderMaterialDrawPacketSnapshot(
        int sourceOrdinal,
        RenderMaterialPassProvenanceSnapshot sourcePass,
        RenderMaterialTextureBindingProvenanceSnapshot baseTextureBinding,
        RenderMaterialUvRouteSnapshot uvRoute,
        RenderState effectiveState,
        byte sceneLightIndex,
        string shaderExecutionStatus,
        IEnumerable<MapRenderPickRange> pickRanges,
        RenderSemanticIdentity drawIdentity,
        RenderVertexLayoutDescriptor vertexLayout,
        RenderGeometryDescriptor geometry,
        RenderTextureDescriptor texture,
        RenderSamplerDescriptor sampler)
    {
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        ArgumentNullException.ThrowIfNull(sourcePass);
        ArgumentNullException.ThrowIfNull(baseTextureBinding);
        ArgumentNullException.ThrowIfNull(uvRoute);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderExecutionStatus);
        ArgumentNullException.ThrowIfNull(pickRanges);
        ImmutableArray<RenderMaterialPickRangeSnapshot> frozenPickRanges =
            RenderSnapshotCollections.Freeze(
                pickRanges.Select(range =>
                    new RenderMaterialPickRangeSnapshot(range)),
                nameof(pickRanges));
        ArgumentNullException.ThrowIfNull(vertexLayout);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(sampler);
        RenderVertexLayoutDescriptor.RequireIdentity(
            drawIdentity,
            RenderSemanticResourceKind.Draw);
        ValidateSource(sourcePass, baseTextureBinding, uvRoute, effectiveState);
        ValidateResources(vertexLayout, geometry, texture, sampler);

        SourceOrdinal = sourceOrdinal;
        SourcePass = sourcePass;
        BaseTextureBinding = baseTextureBinding;
        UvRoute = uvRoute;
        EffectiveState = effectiveState;
        SceneLightIndex = sceneLightIndex;
        ShaderExecutionStatus = shaderExecutionStatus;
        PickRanges = frozenPickRanges;
        DrawIdentity = drawIdentity;
        VertexLayout = vertexLayout;
        Geometry = geometry;
        Texture = texture;
        Sampler = sampler;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public int SourceOrdinal { get; }

    public RenderMaterialPassProvenanceSnapshot SourcePass { get; }

    public RenderMaterialTextureBindingProvenanceSnapshot BaseTextureBinding
        { get; }

    public RenderMaterialUvRouteSnapshot UvRoute { get; }

    public RenderState EffectiveState { get; }

    public byte SceneLightIndex { get; }

    public string ShaderExecutionStatus { get; }

    public ImmutableArray<RenderMaterialPickRangeSnapshot> PickRanges
        { get; }

    public RenderSemanticIdentity DrawIdentity { get; }

    public RenderVertexLayoutDescriptor VertexLayout { get; }

    public RenderGeometryDescriptor Geometry { get; }

    public RenderTextureDescriptor Texture { get; }

    public RenderSamplerDescriptor Sampler { get; }

    public RenderSemanticIdentity VertexLayoutIdentity =>
        VertexLayout.Identity;

    public RenderSemanticIdentity GeometryIdentity => Geometry.Identity;

    public RenderSemanticIdentity TextureIdentity => Texture.Identity;

    public RenderSemanticIdentity SamplerIdentity => Sampler.Identity;

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-material-draw-packet/v2");
        writer.WriteInt32(SourceOrdinal);
        SourcePass.AppendContent(writer);
        BaseTextureBinding.AppendContent(writer);
        UvRoute.AppendContent(writer);
        writer.AppendRenderStateV1(EffectiveState);
        writer.WriteByte(SceneLightIndex);
        writer.WriteString(ShaderExecutionStatus);
        writer.WriteInt32(PickRanges.Length);
        foreach (RenderMaterialPickRangeSnapshot range in PickRanges)
            range.AppendContent(writer);
        writer.WriteIdentity(DrawIdentity);
        writer.WriteIdentity(VertexLayout.Identity);
        writer.WriteString(VertexLayout.ContentDigest);
        writer.WriteIdentity(Geometry.Identity);
        writer.WriteString(Geometry.ContentDigest);
        writer.WriteIdentity(Texture.Identity);
        writer.WriteString(Texture.ContentDigest);
        writer.WriteIdentity(Sampler.Identity);
        writer.WriteString(Sampler.ContentDigest);
    }

    private static void ValidateSource(
        RenderMaterialPassProvenanceSnapshot sourcePass,
        RenderMaterialTextureBindingProvenanceSnapshot binding,
        RenderMaterialUvRouteSnapshot uvRoute,
        RenderState effectiveState)
    {
        if (sourcePass.TechniqueSlot != -1 ||
            sourcePass.PassIndex != -1 ||
            !string.Equals(
                sourcePass.TechniqueName,
                "material.texture[semantic=0x02]",
                StringComparison.Ordinal) ||
            !string.Equals(
                sourcePass.PassClass,
                "MaterialColor",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A material draw packet requires the exact generic MaterialColor source pass.",
                nameof(sourcePass));
        }
        if (effectiveState != RequiredEffectiveState)
        {
            throw new ArgumentException(
                "A material draw packet requires the exact generic opaque state.",
                nameof(effectiveState));
        }
        if (binding.LayerIndex != 0 || binding.BlendWeightComponent != -1 ||
            binding.SamplerArgIndex != sourcePass.SamplerArgIndex ||
            binding.SamplerDest != sourcePass.SamplerDest ||
            binding.SamplerHash != sourcePass.SamplerHash ||
            binding.TextureSemantic != sourcePass.TextureSemantic ||
            uvRoute.TexCoordSource != sourcePass.TexCoordSource)
        {
            throw new ArgumentException(
                "Material pass, base texture binding, and UV provenance do not match.");
        }
    }

    private static void ValidateResources(
        RenderVertexLayoutDescriptor vertexLayout,
        RenderGeometryDescriptor geometry,
        RenderTextureDescriptor texture,
        RenderSamplerDescriptor sampler)
    {
        if (vertexLayout.StrideBytes != VertexStrideBytes ||
            vertexLayout.Elements.Length != 2 ||
            vertexLayout.Elements[0] != new RenderVertexElementDescriptor(
                RenderVertexSemantic.Position,
                0,
                RenderVertexElementFormat.Float32x3,
                0) ||
            vertexLayout.Elements[1] != new RenderVertexElementDescriptor(
                RenderVertexSemantic.TextureCoordinate,
                0,
                RenderVertexElementFormat.Float32x2,
                3 * sizeof(float)))
        {
            throw new ArgumentException(
                "Material preview geometry requires position/UV0 stride-88 vertices.",
                nameof(vertexLayout));
        }
        if (geometry.VertexLayout != vertexLayout.Identity ||
            !string.Equals(
                geometry.VertexLayoutContentDigest,
                vertexLayout.ContentDigest,
                StringComparison.Ordinal) ||
            geometry.VertexStrideBytes != VertexStrideBytes ||
            geometry.CoordinateSpace != RenderGeometryCoordinateSpace.Render ||
            geometry.Topology != RenderPrimitiveTopology.TriangleList ||
            geometry.IndexFormat != RenderIndexFormat.Unsigned32)
        {
            throw new ArgumentException(
                "Material preview geometry must be render-space indexed triangle-list U32 geometry.",
                nameof(geometry));
        }
        if (texture.Dimension != RenderTextureDimension.Texture2D ||
            texture.ArrayLayerCount != 1 ||
            texture.FaceCount != 1 ||
            texture.Subresources.Any(subresource =>
                !subresource.Payloads.Any(payload =>
                    payload.Kind == RenderTexturePayloadKind.DecodedRgba8)))
        {
            throw new ArgumentException(
                "Material preview texture requires a complete decoded RGBA8 2D mip chain.",
                nameof(texture));
        }
        RenderVertexLayoutDescriptor.RequireIdentity(
            sampler.Identity,
            RenderSemanticResourceKind.Sampler);
    }

}

public enum RenderMaterialDrawPacketCandidateRejectionCode
{
    NullBatch,
    MissingPass,
    MissingMaterialIdentity,
    UnsupportedSourcePass,
    LightmapPresent,
    ColorLayerCountNotOne,
    MaterialSamplerCountNotOne,
    BaseTextureBindingMismatch,
    UnresolvedCodeSamplers,
    DepthPrepassPresent,
    UnsupportedGenericOpaqueState,
    GeometryMissingOrMalformed,
    TextureNotTwoDimensional,
    DecodedRgbaMipChainIncomplete,
    ResourceSnapshotCreationFailed
}

public sealed class RenderMaterialDrawPacketCandidateRejection
{
    internal RenderMaterialDrawPacketCandidateRejection(
        int sourceOrdinal,
        IEnumerable<RenderMaterialDrawPacketCandidateRejectionCode> codes)
    {
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        ImmutableArray<RenderMaterialDrawPacketCandidateRejectionCode>
            frozenCodes = RenderSnapshotCollections.Freeze(
                codes,
                nameof(codes));
        if (frozenCodes.IsEmpty ||
            frozenCodes.Any(code => !Enum.IsDefined(code)) ||
            frozenCodes.Distinct().Count() != frozenCodes.Length)
        {
            throw new ArgumentException(
                "Candidate rejection codes must be non-empty, defined, and unique.",
                nameof(codes));
        }

        SourceOrdinal = sourceOrdinal;
        Codes = frozenCodes;
        Reason = string.Join(",", frozenCodes.Select(code => code.ToString()));
    }

    public int SourceOrdinal { get; }

    public ImmutableArray<RenderMaterialDrawPacketCandidateRejectionCode>
        Codes { get; }

    public string Reason { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteInt32(SourceOrdinal);
        writer.WriteInt32(Codes.Length);
        foreach (RenderMaterialDrawPacketCandidateRejectionCode code in Codes)
            writer.WriteInt32((int)code);
    }
}

public enum RenderMaterialDrawPacketAdmissionFailure
{
    None,
    SourceCollectionMissing,
    NoSourceBatches,
    NoEligibleBatch
}

/// <summary>
/// Optional material-preview admission. Failure is data, not a scene-build
/// exception, so document-only workspaces remain valid without renderable
/// assets.
/// </summary>
public sealed class RenderMaterialDrawPacketAdmission
{
    internal RenderMaterialDrawPacketAdmission(
        RenderMaterialDrawPacketSnapshot? packet,
        IEnumerable<RenderMaterialDrawPacketCandidateRejection> rejections,
        RenderMaterialDrawPacketAdmissionFailure failure,
        string? rejectionReason)
    {
        if (!Enum.IsDefined(failure))
            throw new ArgumentOutOfRangeException(nameof(failure));
        ImmutableArray<RenderMaterialDrawPacketCandidateRejection>
            frozenRejections = RenderSnapshotCollections.Freeze(
                rejections,
                nameof(rejections));
        if (frozenRejections.Any(value => value is null))
        {
            throw new ArgumentException(
                "Candidate rejections cannot contain null entries.",
                nameof(rejections));
        }
        if (frozenRejections.Select(value => value.SourceOrdinal)
                .Distinct().Count() != frozenRejections.Length)
        {
            throw new ArgumentException(
                "Candidate rejection ordinals must be unique.",
                nameof(rejections));
        }
        for (int index = 1; index < frozenRejections.Length; index++)
        {
            if (frozenRejections[index - 1].SourceOrdinal >=
                frozenRejections[index].SourceOrdinal)
            {
                throw new ArgumentException(
                    "Candidate rejections must preserve ascending source order.",
                    nameof(rejections));
            }
        }
        bool admitted = packet is not null;
        if (admitted != (failure == RenderMaterialDrawPacketAdmissionFailure.None) ||
            admitted == !string.IsNullOrWhiteSpace(rejectionReason))
        {
            throw new ArgumentException(
                "Packet, failure, and rejection reason do not form a valid admission result.");
        }
        if (packet is not null &&
            frozenRejections.Any(value =>
                value.SourceOrdinal >= packet.SourceOrdinal))
        {
            throw new ArgumentException(
                "Only candidates preceding the admitted packet may be rejected.",
                nameof(rejections));
        }

        Packet = packet;
        CandidateRejections = frozenRejections;
        Failure = failure;
        RejectionReason = rejectionReason;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderMaterialDrawPacketSnapshot? Packet { get; }

    public ImmutableArray<RenderMaterialDrawPacketCandidateRejection>
        CandidateRejections { get; }

    public RenderMaterialDrawPacketAdmissionFailure Failure { get; }

    public string? RejectionReason { get; }

    public bool IsAdmitted => Packet is not null;

    public string ContentDigest { get; }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-material-draw-packet-admission/v1");
        writer.WriteInt32((int)Failure);
        writer.WriteBoolean(RejectionReason is not null);
        if (RejectionReason is not null)
            writer.WriteString(RejectionReason);
        writer.WriteInt32(CandidateRejections.Length);
        foreach (RenderMaterialDrawPacketCandidateRejection rejection in
                 CandidateRejections)
        {
            rejection.AppendContent(writer);
        }
        writer.WriteBoolean(Packet is not null);
        Packet?.AppendContent(writer);
    }
}
