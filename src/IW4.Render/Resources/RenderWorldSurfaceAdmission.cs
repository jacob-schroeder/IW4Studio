using IW4.Render.Geometry;
using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.Resources;

public enum RenderWorldSurfaceAdmissionFailure
{
    None,
    MaterialPacketNotAdmitted,
    UnsupportedGeometryContract,
    PickRangeCountNotOne,
    PickRangeKindNotGfxSurface,
    NegativeSurfaceOrObjectIndex,
    FirstIndexNotZero,
    IndexCountNotPositive,
    IndexCountNotTriangleAligned,
    IndexCountDoesNotCoverGeometry
}

/// <summary>
/// Typed admission for the deliberately narrow first world-geometry slice.
/// Failure remains immutable scene data so document-only and non-world scenes
/// are valid snapshots rather than exceptional render inputs.
/// </summary>
public sealed class RenderWorldSurfaceAdmission
{
    private RenderWorldSurfaceAdmission(
        RenderMaterialDrawPacketSnapshot? materialPacket,
        RenderMaterialPickRangeSnapshot? candidateRange,
        RenderWorldSurfaceAdmissionFailure failure,
        string? rejectionReason)
    {
        if (!Enum.IsDefined(failure))
            throw new ArgumentOutOfRangeException(nameof(failure));
        bool admitted = failure == RenderWorldSurfaceAdmissionFailure.None;
        if ((admitted &&
             (materialPacket is null || candidateRange is null)) ||
            (admitted && rejectionReason is not null) ||
            (!admitted && string.IsNullOrWhiteSpace(rejectionReason)))
        {
            throw new ArgumentException(
                "World-surface packet, range, failure, and reason do not form a valid admission result.");
        }
        if (failure ==
                RenderWorldSurfaceAdmissionFailure.MaterialPacketNotAdmitted &&
            (materialPacket is not null || candidateRange is not null))
        {
            throw new ArgumentException(
                "A missing material packet cannot retain world-surface candidates.");
        }

        MaterialPacket = materialPacket;
        CandidateRange = candidateRange;
        Failure = failure;
        RejectionReason = rejectionReason;
        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public RenderMaterialDrawPacketSnapshot? MaterialPacket { get; }

    /// <summary>
    /// The sole source range when exactly one was present. It remains visible
    /// on a failed admission so malformed world ownership is diagnosable.
    /// </summary>
    public RenderMaterialPickRangeSnapshot? CandidateRange { get; }

    public RenderMaterialPickRangeSnapshot? SurfaceRange =>
        IsAdmitted ? CandidateRange : null;

    public RenderWorldSurfaceAdmissionFailure Failure { get; }

    public string? RejectionReason { get; }

    public bool IsAdmitted =>
        Failure == RenderWorldSurfaceAdmissionFailure.None;

    public string ContentDigest { get; }

    internal static RenderWorldSurfaceAdmission Create(
        RenderMaterialDrawPacketAdmission materialAdmission)
    {
        ArgumentNullException.ThrowIfNull(materialAdmission);
        if (materialAdmission.Packet is not { } packet)
        {
            return Rejected(
                materialPacket: null,
                candidateRange: null,
                RenderWorldSurfaceAdmissionFailure.MaterialPacketNotAdmitted,
                string.Concat(
                    "MATERIAL_DRAW_PACKET_NOT_ADMITTED; failure=",
                    materialAdmission.Failure));
        }

        if (packet.Geometry.Topology != RenderPrimitiveTopology.TriangleList ||
            packet.Geometry.IndexFormat != RenderIndexFormat.Unsigned32)
        {
            return Rejected(
                packet,
                candidateRange: null,
                RenderWorldSurfaceAdmissionFailure.UnsupportedGeometryContract,
                "WORLD_SURFACE_GEOMETRY_MUST_BE_U32_TRIANGLE_LIST");
        }

        if (packet.PickRanges.Length != 1)
        {
            return Rejected(
                packet,
                candidateRange: null,
                RenderWorldSurfaceAdmissionFailure.PickRangeCountNotOne,
                string.Concat(
                    "WORLD_SURFACE_PICK_RANGE_COUNT_NOT_ONE; count=",
                    packet.PickRanges.Length));
        }

        RenderMaterialPickRangeSnapshot range = packet.PickRanges[0];
        if (range.Kind != MapRenderPickKind.GfxSurface)
        {
            return Rejected(
                packet,
                range,
                RenderWorldSurfaceAdmissionFailure.PickRangeKindNotGfxSurface,
                string.Concat(
                    "WORLD_SURFACE_PICK_RANGE_KIND_NOT_GFX_SURFACE; kind=",
                    range.Kind));
        }
        if (range.ObjectIndex < 0 || range.SurfaceIndex < 0)
        {
            return Rejected(
                packet,
                range,
                RenderWorldSurfaceAdmissionFailure.NegativeSurfaceOrObjectIndex,
                "WORLD_SURFACE_PICK_RANGE_INDICES_MUST_BE_NONNEGATIVE");
        }
        if (range.FirstIndex != 0)
        {
            return Rejected(
                packet,
                range,
                RenderWorldSurfaceAdmissionFailure.FirstIndexNotZero,
                "WORLD_SURFACE_PICK_RANGE_MUST_START_AT_ZERO");
        }
        if (range.IndexCount <= 0)
        {
            return Rejected(
                packet,
                range,
                RenderWorldSurfaceAdmissionFailure.IndexCountNotPositive,
                "WORLD_SURFACE_PICK_RANGE_INDEX_COUNT_MUST_BE_POSITIVE");
        }
        if (range.IndexCount % 3 != 0)
        {
            return Rejected(
                packet,
                range,
                RenderWorldSurfaceAdmissionFailure.IndexCountNotTriangleAligned,
                "WORLD_SURFACE_PICK_RANGE_INDEX_COUNT_MUST_BE_DIVISIBLE_BY_THREE");
        }
        if (range.IndexCount != packet.Geometry.IndexCount)
        {
            return Rejected(
                packet,
                range,
                RenderWorldSurfaceAdmissionFailure.IndexCountDoesNotCoverGeometry,
                string.Concat(
                    "WORLD_SURFACE_PICK_RANGE_DOES_NOT_COVER_GEOMETRY; range=",
                    range.IndexCount,
                    "; geometry=",
                    packet.Geometry.IndexCount));
        }

        return new RenderWorldSurfaceAdmission(
            packet,
            range,
            RenderWorldSurfaceAdmissionFailure.None,
            rejectionReason: null);
    }

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-world-surface-admission/v1");
        writer.WriteInt32((int)Failure);
        writer.WriteBoolean(RejectionReason is not null);
        if (RejectionReason is not null)
            writer.WriteString(RejectionReason);
        writer.WriteBoolean(MaterialPacket is not null);
        if (MaterialPacket is not null)
            writer.WriteString(MaterialPacket.ContentDigest);
        writer.WriteBoolean(CandidateRange is not null);
        CandidateRange?.AppendContent(writer);
    }

    private static RenderWorldSurfaceAdmission Rejected(
        RenderMaterialDrawPacketSnapshot? materialPacket,
        RenderMaterialPickRangeSnapshot? candidateRange,
        RenderWorldSurfaceAdmissionFailure failure,
        string rejectionReason) =>
        new(
            materialPacket,
            candidateRange,
            failure,
            rejectionReason);
}
