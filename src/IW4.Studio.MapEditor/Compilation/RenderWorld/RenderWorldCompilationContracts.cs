using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.RenderWorld;

/// <summary>
/// The only vertex profile admitted by the bounded M3 compiler.
/// It mirrors Event20 backend row 5 without claiming material compatibility.
/// </summary>
public enum RenderWorldStructuralVertexProfile
{
    Tex1Nrm1StructuralV1 = 0
}

public static class RenderWorldStructuralProfile
{
    public const string CompilerIdentity =
        "iw4-studio.gfxworld.structural-tex1-nrm1@1";
    public const string VertexFormatLabel =
        "MTL_WORLDVERT_TEX_1_NRM_1/backendRow5";
    public const string SymbolicMaterialSurfaceOrderingPolicyId =
        "iw4-studio.gfxworld.symbolic-material-source-window@1";
    public const int PositionStride = 0x10;
    public const int VertexLayerStride = 0x1C;

    // The native SrfTriangles.VertexCount field is UInt16. Keeping the
    // detached rows directly representable is a stricter bound than the
    // 65,536 values addressable by an isolated UInt16 index.
    public const int MaximumVerticesPerSurface = ushort.MaxValue;
    public const int MaximumTrianglesPerSurface = ushort.MaxValue;
    public const int MaximumSurfaceCount = ushort.MaxValue + 1;

}

public readonly record struct RenderWorldRange
{
    public RenderWorldRange(int start, int count)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        Start = start;
        Count = count;
        EndExclusive = checked(start + count);
    }

    public int Start { get; }
    public int Count { get; }
    public int EndExclusive { get; }
    public bool IsEmpty => Count == 0;
}

/// <summary>
/// Exact finite endpoint bounds derived from canonical source positions.
/// M3 uses these for detached GfxBrushModel BoundsMins/BoundsMaxs candidates.
/// They are not GfxSurfaceBounds or writable runtime culling bounds.
/// </summary>
public readonly record struct RenderWorldSourceBounds
{
    public RenderWorldSourceBounds(
        MapVector3 minimum,
        MapVector3 maximum)
    {
        if (!minimum.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                "Render source-bound minima must be finite.");
        }
        if (!maximum.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum),
                "Render source-bound maxima must be finite.");
        }
        if (minimum.X > maximum.X ||
            minimum.Y > maximum.Y ||
            minimum.Z > maximum.Z)
        {
            throw new ArgumentException(
                "Render source-bound minima cannot exceed maxima.");
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    public MapVector3 Minimum { get; }
    public MapVector3 Maximum { get; }

    /// <summary>
    /// Smallest float radius, rounded outward, that encloses these bounds
    /// around the Gfx brush-model local origin (0,0,0).
    /// </summary>
    public float LocalOriginRadius
    {
        get
        {
            double x = Math.Max(
                Math.Abs((double)Minimum.X),
                Math.Abs((double)Maximum.X));
            double y = Math.Max(
                Math.Abs((double)Minimum.Y),
                Math.Abs((double)Maximum.Y));
            double z = Math.Max(
                Math.Abs((double)Minimum.Z),
                Math.Abs((double)Maximum.Z));
            double exact = Math.Sqrt(x * x + y * y + z * z);
            if (!double.IsFinite(exact) || exact > float.MaxValue)
            {
                throw new OverflowException(
                    "Render brush-model radius exceeds the finite float " +
                    "range.");
            }

            float rounded = (float)exact;
            if ((double)rounded < exact)
                rounded = MathF.BitIncrement(rounded);
            if (!float.IsFinite(rounded))
            {
                throw new OverflowException(
                    "Render brush-model radius cannot be rounded outward " +
                    "to a finite float.");
            }
            return rounded;
        }
    }

    internal static RenderWorldSourceBounds EmptyAtLocalOrigin =>
        new(new MapVector3(0, 0, 0), new MapVector3(0, 0, 0));

    internal static RenderWorldSourceBounds From(
        IReadOnlyList<AuthoredRenderVertex> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Count == 0)
        {
            throw new ArgumentException(
                "Render source bounds require at least one vertex.",
                nameof(vertices));
        }

        MapVector3 first = vertices[0].Position;
        float minX = first.X;
        float minY = first.Y;
        float minZ = first.Z;
        float maxX = first.X;
        float maxY = first.Y;
        float maxZ = first.Z;
        for (int index = 1; index < vertices.Count; index++)
        {
            MapVector3 position = vertices[index].Position;
            minX = MathF.Min(minX, position.X);
            minY = MathF.Min(minY, position.Y);
            minZ = MathF.Min(minZ, position.Z);
            maxX = MathF.Max(maxX, position.X);
            maxY = MathF.Max(maxY, position.Y);
            maxZ = MathF.Max(maxZ, position.Z);
        }

        return new RenderWorldSourceBounds(
            new MapVector3(
                CanonicalizeZero(minX),
                CanonicalizeZero(minY),
                CanonicalizeZero(minZ)),
            new MapVector3(
                CanonicalizeZero(maxX),
                CanonicalizeZero(maxY),
                CanonicalizeZero(maxZ)));
    }

    internal RenderWorldSourceBounds Include(
        RenderWorldSourceBounds other) =>
        new(
            new MapVector3(
                MathF.Min(Minimum.X, other.Minimum.X),
                MathF.Min(Minimum.Y, other.Minimum.Y),
                MathF.Min(Minimum.Z, other.Minimum.Z)),
            new MapVector3(
                MathF.Max(Maximum.X, other.Maximum.X),
                MathF.Max(Maximum.Y, other.Maximum.Y),
                MathF.Max(Maximum.Z, other.Maximum.Z)));

    private static float CanonicalizeZero(float value) =>
        value == 0f ? 0f : value;
}

/// <summary>
/// Detached SrfTriangles-shaped range plus its unresolved semantic material
/// and source identity. It contains no pointer, MaterialAsset, or runtime
/// buffer handle.
/// </summary>
public sealed class RenderWorldCompiledSurface
{
    private readonly IReadOnlyList<int> _sourceVertexIndices;

    internal RenderWorldCompiledSurface(
        int ordinal,
        MapObjectId sourceObjectId,
        int sourceSurfaceOrdinal,
        RenderMeshOwnershipKind ownershipKind,
        MapObjectId? inlineBrushModelObjectId,
        ushort modelOrdinal,
        string symbolicMaterialName,
        RenderTriangleWinding triangleWinding,
        RenderWorldRange vertexRange,
        RenderWorldRange vertexLayerByteRange,
        RenderWorldRange indexRange,
        int triangleCount,
        IEnumerable<int> sourceVertexIndices,
        RenderWorldRange sourceTriangleRange)
    {
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (sourceObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceObjectId));
        if (sourceSurfaceOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceSurfaceOrdinal));
        }
        if (!Enum.IsDefined(ownershipKind))
            throw new ArgumentOutOfRangeException(nameof(ownershipKind));
        if (ownershipKind == RenderMeshOwnershipKind.StandaloneWorld &&
            inlineBrushModelObjectId is not null)
        {
            throw new ArgumentException(
                "Standalone surfaces cannot carry an inline-model identity.",
                nameof(inlineBrushModelObjectId));
        }
        if (ownershipKind == RenderMeshOwnershipKind.InlineBrushModel &&
            inlineBrushModelObjectId is null)
        {
            throw new ArgumentException(
                "Inline surfaces require an inline-model identity.",
                nameof(inlineBrushModelObjectId));
        }
        if (inlineBrushModelObjectId is { } inlineModelId &&
            inlineModelId.Value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inlineBrushModelObjectId));
        }
        if (ownershipKind == RenderMeshOwnershipKind.StandaloneWorld &&
            modelOrdinal != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modelOrdinal),
                "Standalone world surfaces require model ordinal zero.");
        }
        if (ownershipKind == RenderMeshOwnershipKind.InlineBrushModel &&
            modelOrdinal == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modelOrdinal),
                "Inline surfaces cannot claim world model ordinal zero.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolicMaterialName);
        if (!Enum.IsDefined(triangleWinding))
            throw new ArgumentOutOfRangeException(nameof(triangleWinding));
        if (vertexRange.Count is <= 0 or >
            RenderWorldStructuralProfile.MaximumVerticesPerSurface)
        {
            throw new ArgumentOutOfRangeException(nameof(vertexRange));
        }
        if (vertexLayerByteRange.Count !=
            checked(
                vertexRange.Count *
                RenderWorldStructuralProfile.VertexLayerStride))
        {
            throw new ArgumentException(
                "A vertex-layer byte range must contain one exact profile " +
                "row per surface vertex.",
                nameof(vertexLayerByteRange));
        }
        if (triangleCount is <= 0 or >
            RenderWorldStructuralProfile.MaximumTrianglesPerSurface)
        {
            throw new ArgumentOutOfRangeException(nameof(triangleCount));
        }
        if (indexRange.Count != checked(triangleCount * 3))
        {
            throw new ArgumentException(
                "A compiled triangle surface requires exactly three " +
                "indices per triangle.",
                nameof(indexRange));
        }
        ArgumentNullException.ThrowIfNull(sourceVertexIndices);
        int[] sourceVertexIndexCopy = sourceVertexIndices.ToArray();
        if (sourceVertexIndexCopy.Length != vertexRange.Count ||
            sourceVertexIndexCopy.Any(value => value < 0) ||
            sourceVertexIndexCopy.Distinct().Count() !=
                sourceVertexIndexCopy.Length)
        {
            throw new ArgumentException(
                "A surface window requires one distinct nonnegative " +
                "canonical source index per packed vertex.",
                nameof(sourceVertexIndices));
        }
        if (sourceTriangleRange.Count != triangleCount)
        {
            throw new ArgumentException(
                "A surface window's source-triangle range must match its " +
                "compiled triangle count.",
                nameof(sourceTriangleRange));
        }

        Ordinal = ordinal;
        SourceObjectId = sourceObjectId;
        SourceSurfaceOrdinal = sourceSurfaceOrdinal;
        OwnershipKind = ownershipKind;
        InlineBrushModelObjectId = inlineBrushModelObjectId;
        ModelOrdinal = modelOrdinal;
        SymbolicMaterialName = symbolicMaterialName;
        TriangleWinding = triangleWinding;
        VertexRange = vertexRange;
        VertexLayerByteRange = vertexLayerByteRange;
        IndexRange = indexRange;
        TriangleCount = triangleCount;
        _sourceVertexIndices =
            new ReadOnlyCollection<int>(sourceVertexIndexCopy);
        SourceTriangleRange = sourceTriangleRange;
    }

    public int Ordinal { get; }
    public MapObjectId SourceObjectId { get; }
    public int SourceSurfaceOrdinal { get; }
    public RenderMeshOwnershipKind OwnershipKind { get; }
    public MapObjectId? InlineBrushModelObjectId { get; }
    public ushort ModelOrdinal { get; }
    public string SymbolicMaterialName { get; }
    public RenderTriangleWinding TriangleWinding { get; }
    public RenderWorldRange VertexRange { get; }
    public RenderWorldRange VertexLayerByteRange { get; }
    public RenderWorldRange IndexRange { get; }
    public int TriangleCount { get; }
    public IReadOnlyList<int> SourceVertexIndices =>
        _sourceVertexIndices;
    public RenderWorldRange SourceTriangleRange { get; }

    public int BaseVertex => VertexRange.Start;
    public uint MinVertexIndex => 0;
    public int VertexCount => VertexRange.Count;
    public int VertexLayerData => VertexLayerByteRange.Start;
    public int BaseIndex => IndexRange.Start;
    public int IndexCount => IndexRange.Count;
}

public sealed class RenderWorldSourceSurfaceMapping
{
    internal RenderWorldSourceSurfaceMapping(
        MapObjectId sourceObjectId,
        RenderMeshOwnershipKind ownershipKind,
        MapObjectId? inlineBrushModelObjectId,
        ushort modelOrdinal,
        string symbolicMaterialName,
        RenderTriangleWinding triangleWinding,
        RenderWorldRange surfaceRange,
        RenderWorldSourceBounds sourceBounds)
    {
        if (sourceObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceObjectId));
        if (!Enum.IsDefined(ownershipKind))
            throw new ArgumentOutOfRangeException(nameof(ownershipKind));
        if (ownershipKind == RenderMeshOwnershipKind.StandaloneWorld &&
            inlineBrushModelObjectId is not null)
        {
            throw new ArgumentException(
                "Standalone source mappings cannot carry an inline-model " +
                "identity.",
                nameof(inlineBrushModelObjectId));
        }
        if (ownershipKind == RenderMeshOwnershipKind.InlineBrushModel &&
            inlineBrushModelObjectId is null)
        {
            throw new ArgumentException(
                "Inline source mappings require an inline-model identity.",
                nameof(inlineBrushModelObjectId));
        }
        if (inlineBrushModelObjectId is { } inlineId &&
            inlineId.Value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inlineBrushModelObjectId));
        }
        if (ownershipKind == RenderMeshOwnershipKind.StandaloneWorld &&
            modelOrdinal != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modelOrdinal));
        }
        if (ownershipKind == RenderMeshOwnershipKind.InlineBrushModel &&
            modelOrdinal == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modelOrdinal));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolicMaterialName);
        if (!Enum.IsDefined(triangleWinding))
            throw new ArgumentOutOfRangeException(nameof(triangleWinding));
        if (surfaceRange.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(surfaceRange));

        SourceObjectId = sourceObjectId;
        OwnershipKind = ownershipKind;
        InlineBrushModelObjectId = inlineBrushModelObjectId;
        ModelOrdinal = modelOrdinal;
        SymbolicMaterialName = symbolicMaterialName;
        TriangleWinding = triangleWinding;
        SurfaceRange = surfaceRange;
        SourceBounds = sourceBounds;
    }

    public MapObjectId SourceObjectId { get; }
    public RenderMeshOwnershipKind OwnershipKind { get; }
    public MapObjectId? InlineBrushModelObjectId { get; }
    public ushort ModelOrdinal { get; }
    public string SymbolicMaterialName { get; }
    public RenderTriangleWinding TriangleWinding { get; }
    public RenderWorldRange SurfaceRange { get; }
    public RenderWorldSourceBounds SourceBounds { get; }
}

/// <summary>
/// GfxBrushModel-shaped detached ownership for one shared MapEnt inline-model
/// ordinal. A plan row with no authored render mesh is retained as an explicit
/// empty range at the local origin.
/// </summary>
public sealed class RenderWorldInlineModelSurfaceRange
{
    private readonly IReadOnlyList<MapObjectId> _sourceObjectIds;

    internal RenderWorldInlineModelSurfaceRange(
        ushort modelOrdinal,
        MapObjectId inlineBrushModelObjectId,
        RenderWorldRange surfaceRange,
        IEnumerable<MapObjectId> sourceObjectIds,
        RenderWorldSourceBounds sourceBounds)
    {
        if (modelOrdinal == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modelOrdinal),
                "Inline brush models cannot claim world model ordinal zero.");
        }
        if (inlineBrushModelObjectId.Value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inlineBrushModelObjectId));
        }
        ArgumentNullException.ThrowIfNull(sourceObjectIds);
        MapObjectId[] sourceIdCopy = sourceObjectIds.ToArray();
        if (sourceIdCopy.Any(value => value.Value == Guid.Empty) ||
            sourceIdCopy.Distinct().Count() != sourceIdCopy.Length)
        {
            throw new ArgumentException(
                "An inline model requires distinct nonempty source " +
                "identities.",
                nameof(sourceObjectIds));
        }
        if (surfaceRange.IsEmpty != (sourceIdCopy.Length == 0))
        {
            throw new ArgumentException(
                "An empty inline surface range requires no render sources; " +
                "a nonempty range requires at least one source.",
                nameof(sourceObjectIds));
        }

        ModelOrdinal = modelOrdinal;
        InlineBrushModelObjectId = inlineBrushModelObjectId;
        SurfaceRange = surfaceRange;
        _sourceObjectIds =
            new ReadOnlyCollection<MapObjectId>(sourceIdCopy);
        SourceBounds = sourceBounds;
    }

    public ushort ModelOrdinal { get; }
    public MapObjectId InlineBrushModelObjectId { get; }
    public RenderWorldRange SurfaceRange { get; }
    public IReadOnlyList<MapObjectId> SourceObjectIds =>
        _sourceObjectIds;
    public RenderWorldSourceBounds SourceBounds { get; }
    public MapVector3 BoundsMinimum => SourceBounds.Minimum;
    public MapVector3 BoundsMaximum => SourceBounds.Maximum;
    public float LocalOriginRadius => SourceBounds.LocalOriginRadius;
    public float SourceRadius => LocalOriginRadius;
    public ushort SurfaceCount => checked((ushort)SurfaceRange.Count);
    public ushort StartSurfIndex =>
        checked((ushort)SurfaceRange.Start);
    public bool HasSurfaceGeometry => !SurfaceRange.IsEmpty;
}

/// <summary>
/// Detached GfxBrushModel-shaped world row. Writable runtime bounds are
/// intentionally absent and remain an M4 runtime derivation.
/// </summary>
public sealed class RenderWorldWorldModelSurfaceRange
{
    internal RenderWorldWorldModelSurfaceRange(
        RenderWorldRange surfaceRange,
        RenderWorldSourceBounds sourceBounds)
    {
        if (surfaceRange.Start != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(surfaceRange),
                "The world brush model must begin at surface zero.");
        }
        if (surfaceRange.Count > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(surfaceRange));

        SurfaceRange = surfaceRange;
        SourceBounds = sourceBounds;
    }

    public ushort ModelOrdinal => 0;
    public RenderWorldRange SurfaceRange { get; }
    public RenderWorldSourceBounds SourceBounds { get; }
    public MapVector3 BoundsMinimum => SourceBounds.Minimum;
    public MapVector3 BoundsMaximum => SourceBounds.Maximum;
    public float LocalOriginRadius => SourceBounds.LocalOriginRadius;
    public ushort SurfaceCount => checked((ushort)SurfaceRange.Count);
    public ushort StartSurfIndex => 0;
    public bool HasSurfaceGeometry => !SurfaceRange.IsEmpty;
}

/// <summary>
/// Immutable packed render geometry and structural ownership plan. This is a
/// detached compiler artifact, not a GfxWorldAsset or emission build input.
/// </summary>
public sealed class RenderWorldCompiledGeometry
{
    private readonly IReadOnlyList<AuthoredIndexedRenderMeshSource> _sources;
    private readonly IReadOnlyList<byte> _packedPositionData;
    private readonly IReadOnlyList<byte> _packedVertexLayerData;
    private readonly IReadOnlyList<ushort> _indices;
    private readonly IReadOnlyList<RenderWorldCompiledSurface> _surfaces;
    private readonly IReadOnlyList<RenderWorldSourceSurfaceMapping>
        _sourceMappings;
    private readonly IReadOnlyList<ushort> _sortedWorldSurfaceOrdinals;
    private readonly IReadOnlyList<RenderWorldInlineModelSurfaceRange>
        _inlineModels;
    private readonly GfxMapVertexChecksumAssignment
        _mapVertexChecksumAssignment;

    internal RenderWorldCompiledGeometry(
        IEnumerable<AuthoredIndexedRenderMeshSource> orderedSources,
        byte[] packedPositionData,
        byte[] packedVertexLayerData,
        ushort[] indices,
        RenderWorldCompiledSurface[] surfaces,
        RenderWorldSourceSurfaceMapping[] sourceMappings,
        RenderWorldRange standaloneWorldSurfaceRange,
        ushort[] sortedWorldSurfaceOrdinals,
        RenderWorldWorldModelSurfaceRange worldModel,
        RenderWorldInlineModelSurfaceRange[] inlineModels,
        CollisionInlineModelAllocationPlan inlineModelAllocationPlan,
        GfxMapVertexChecksumAssignment mapVertexChecksumAssignment)
    {
        ArgumentNullException.ThrowIfNull(orderedSources);
        ArgumentNullException.ThrowIfNull(packedPositionData);
        ArgumentNullException.ThrowIfNull(packedVertexLayerData);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(sourceMappings);
        ArgumentNullException.ThrowIfNull(sortedWorldSurfaceOrdinals);
        ArgumentNullException.ThrowIfNull(worldModel);
        ArgumentNullException.ThrowIfNull(inlineModels);
        ArgumentNullException.ThrowIfNull(inlineModelAllocationPlan);
        ArgumentNullException.ThrowIfNull(mapVertexChecksumAssignment);

        _sources =
            new ReadOnlyCollection<AuthoredIndexedRenderMeshSource>(
                orderedSources.ToArray());
        _packedPositionData =
            new ReadOnlyCollection<byte>(packedPositionData.ToArray());
        _packedVertexLayerData =
            new ReadOnlyCollection<byte>(
                packedVertexLayerData.ToArray());
        _indices =
            new ReadOnlyCollection<ushort>(indices.ToArray());
        _surfaces =
            new ReadOnlyCollection<RenderWorldCompiledSurface>(
                surfaces.ToArray());
        _sourceMappings =
            new ReadOnlyCollection<RenderWorldSourceSurfaceMapping>(
                sourceMappings.ToArray());
        StandaloneWorldSurfaceRange = standaloneWorldSurfaceRange;
        _sortedWorldSurfaceOrdinals =
            new ReadOnlyCollection<ushort>(
                sortedWorldSurfaceOrdinals.ToArray());
        _inlineModels =
            new ReadOnlyCollection<RenderWorldInlineModelSurfaceRange>(
                inlineModels.ToArray());
        WorldModel = worldModel;
        InlineModelAllocationPlan = inlineModelAllocationPlan;
        _mapVertexChecksumAssignment = mapVertexChecksumAssignment;
    }

    public RenderWorldStructuralVertexProfile VertexProfile =>
        RenderWorldStructuralVertexProfile.Tex1Nrm1StructuralV1;
    public string CompilerIdentity =>
        RenderWorldStructuralProfile.CompilerIdentity;
    public string VertexFormatLabel =>
        RenderWorldStructuralProfile.VertexFormatLabel;
    public string SymbolicMaterialSurfaceOrderingPolicyId =>
        RenderWorldStructuralProfile
            .SymbolicMaterialSurfaceOrderingPolicyId;
    public int PositionStride =>
        RenderWorldStructuralProfile.PositionStride;
    public int VertexLayerStride =>
        RenderWorldStructuralProfile.VertexLayerStride;
    public GfxMapVertexChecksumAssignment MapVertexChecksumAssignment =>
        _mapVertexChecksumAssignment;
    public uint MapVertexChecksum =>
        MapVertexChecksumAssignment.Checksum.Value;
    public IReadOnlyList<AuthoredIndexedRenderMeshSource> Sources =>
        _sources;
    public IReadOnlyList<byte> PackedPositionData =>
        _packedPositionData;
    public IReadOnlyList<byte> PackedVertexLayerData =>
        _packedVertexLayerData;
    public IReadOnlyList<ushort> Indices => _indices;
    public IReadOnlyList<RenderWorldCompiledSurface> Surfaces =>
        _surfaces;
    public IReadOnlyList<RenderWorldSourceSurfaceMapping> SourceMappings =>
        _sourceMappings;
    public RenderWorldRange StandaloneWorldSurfaceRange { get; }
    public IReadOnlyList<ushort> SortedWorldSurfaceOrdinals =>
        _sortedWorldSurfaceOrdinals;
    public RenderWorldWorldModelSurfaceRange WorldModel { get; }
    public IReadOnlyList<RenderWorldInlineModelSurfaceRange> InlineModels =>
        _inlineModels;
    public CollisionInlineModelAllocationPlan InlineModelAllocationPlan
    {
        get;
    }
    public int VertexCount =>
        PackedPositionData.Count /
        RenderWorldStructuralProfile.PositionStride;
}

public enum RenderWorldDeferredMilestone
{
    M4SpatialTopology = 4,
    M5Lighting = 5,
    M7AssetResolutionAndPersistence = 7
}

public enum RenderWorldStructuralBlockerKind
{
    CellsPortalsAabbAndVisibilityNotCompiled = 0,
    FinalRuntimeBoundsNotCompiled = 1,
    LightingAssignmentsNotCompiled = 2,
    LightingBakesNotCompiled = 3,
    MaterialResolutionNotCompiled = 4,
    CompleteGfxWorldAssemblyNotCompiled = 5,
    LinkingEmissionAndPersistenceNotAuthorized = 6
}

/// <summary>
/// Structured statement of work intentionally absent from an otherwise valid
/// M3 candidate. These blockers are expected and cannot be interpreted as
/// validator defects or silently cleared by a caller.
/// </summary>
public sealed class RenderWorldStructuralBlocker
{
    public RenderWorldStructuralBlocker(
        RenderWorldDeferredMilestone milestone,
        RenderWorldStructuralBlockerKind kind,
        string detail)
    {
        if (!Enum.IsDefined(milestone))
            throw new ArgumentOutOfRangeException(nameof(milestone));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        Milestone = milestone;
        Kind = kind;
        Detail = detail;
    }

    public RenderWorldDeferredMilestone Milestone { get; }
    public RenderWorldStructuralBlockerKind Kind { get; }
    public string Detail { get; }
}
