using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Math;

namespace IW4.Studio.Documents;

/// <summary>
/// A stable, machine-readable reason why an imported GfxWorld static-model
/// translation cannot preserve its compiled spatial memberships.
/// </summary>
public enum GfxStaticModelTranslationSpatialIssueKind
{
    StaticModelCardinalityMismatch,
    StaticModelOrdinalOutOfRange,
    InvalidStaticModelPlacement,
    InvalidStaticModelBounds,
    InvalidStaticModelLightingOrigin,
    TranslationOverflow,
    DpvsCellCountInvalid,
    DpvsPlaneCardinalityMismatch,
    DpvsNodeCardinalityMismatch,
    DpvsMissingRootNode,
    DpvsInvalidPlane,
    DpvsInvalidPlaneIndex,
    DpvsInvalidChildOffset,
    DpvsTraversalCycle,
    DpvsNodeStorageOverlap,
    DpvsOrphanNodeStorage,
    CellTreeCardinalityMismatch,
    CellTreeRowCardinalityMismatch,
    CellTreeInvalidBounds,
    CellTreeIndexCardinalityMismatch,
    CellTreeInvalidChildOffset,
    CellTreeInvalidChildRange,
    CellTreeMultipleParents,
    CellTreeTraversalCycle,
    CellTreeOrphanRow,
    CellTreeInvalidStaticModelIndex,
    CellTreeDuplicateStaticModelOwnership,
    CellTreeInternalIndexConcatenationMismatch,
    CellTreeLeafExcludesStaticModel,
    StaticModelHasNoLeafOwner,
    SourceCellMembershipMismatch,
    CandidateCellMembershipMismatch,
    CandidateEscapesOwningLeaf
}

/// <summary>
/// One precise failure in the compiled GfxWorld spatial invariants.
/// </summary>
public sealed record GfxStaticModelTranslationSpatialIssue(
    GfxStaticModelTranslationSpatialIssueKind Kind,
    string Detail,
    int? CellIndex = null,
    int? TreeIndex = null,
    int? NodeOffset = null,
    int? StaticModelIndex = null);

/// <summary>
/// Result of proving that a translation preserves the exact imported DPVS
/// cell set and every serialized static-model leaf membership.
/// </summary>
public sealed class GfxStaticModelTranslationSpatialAssessment
{
    private readonly GfxWorldBuildData _source;

    internal GfxStaticModelTranslationSpatialAssessment(
        GfxWorldBuildData source,
        StaticModelTranslationEdit edit,
        IReadOnlyList<int> sourceCellIndexes,
        IReadOnlyList<int> owningCellIndexes,
        IReadOnlyList<GfxStaticModelTranslationSpatialIssue> issues)
    {
        _source = source;
        Edit = edit;
        SourceCellIndexes = sourceCellIndexes;
        OwningCellIndexes = owningCellIndexes;
        Issues = issues;
    }

    public StaticModelTranslationEdit Edit { get; }
    public bool IsEligible => Issues.Count == 0;
    public IReadOnlyList<int> SourceCellIndexes { get; }
    public IReadOnlyList<int> OwningCellIndexes { get; }
    public IReadOnlyList<GfxStaticModelTranslationSpatialIssue> Issues { get; }

    internal bool IsFor(GfxWorldBuildData source) =>
        ReferenceEquals(_source, source);
}

/// <summary>
/// Exact PS3-compatible spatial eligibility gate for membership-preserving
/// translations of imported GfxWorld static models.
/// </summary>
public static class GfxStaticModelTranslationSpatialAssessor
{
    private const int AabbTreeRowSize = GfxAabbTree.SerializedSize;
    private const float BoxPlaneEpsilon = 0.001f;

    public static GfxStaticModelTranslationSpatialAssessment Assess(
        GfxWorldBuildData source,
        StaticModelTranslationEdit edit)
    {
        ArgumentNullException.ThrowIfNull(source);
        GfxWorldAsset world = source.Definition;
        GfxWorldDpvsStatic dpvs = world.Dpvs;
        var issues = new List<GfxStaticModelTranslationSpatialIssue>();
        int staticModelCount;
        if (dpvs.SModelCount > int.MaxValue ||
            dpvs.SModelDrawInsts.Count != (int)dpvs.SModelCount ||
            dpvs.SModelInsts.Count != (int)dpvs.SModelCount)
        {
            issues.Add(Issue(
                GfxStaticModelTranslationSpatialIssueKind.StaticModelCardinalityMismatch,
                "The materialized static-model placement and bounds tables do not match GfxWorld.dpvs.smodelCount."));
            return Failed(source, edit, issues);
        }
        staticModelCount = (int)dpvs.SModelCount;
        if ((uint)edit.SourceOrdinal >= (uint)staticModelCount)
        {
            issues.Add(Issue(
                GfxStaticModelTranslationSpatialIssueKind.StaticModelOrdinalOutOfRange,
                $"Static-model ordinal {edit.SourceOrdinal} escapes the {staticModelCount}-row DPVS table.",
                staticModelIndex: edit.SourceOrdinal));
            return Failed(source, edit, issues);
        }

        GfxStaticModelDrawInst draw = dpvs.SModelDrawInsts[edit.SourceOrdinal];
        if (draw.Placement.Origin.Count != 3 ||
            draw.Placement.Origin.Any(value => !float.IsFinite(value)))
        {
            issues.Add(Issue(
                GfxStaticModelTranslationSpatialIssueKind.InvalidStaticModelPlacement,
                $"Static-model ordinal {edit.SourceOrdinal} does not have a finite three-coordinate placement.",
                staticModelIndex: edit.SourceOrdinal));
            return Failed(source, edit, issues);
        }

        GfxStaticModelInst instance = dpvs.SModelInsts[edit.SourceOrdinal];
        if (!TryValidateBounds(instance.Bounds))
        {
            issues.Add(Issue(
                GfxStaticModelTranslationSpatialIssueKind.InvalidStaticModelBounds,
                $"Static-model ordinal {edit.SourceOrdinal} has an invalid source AABB.",
                staticModelIndex: edit.SourceOrdinal));
            return Failed(source, edit, issues);
        }
        if (!IsFinite(instance.LightingOrigin))
        {
            issues.Add(Issue(
                GfxStaticModelTranslationSpatialIssueKind.InvalidStaticModelLightingOrigin,
                $"Static-model ordinal {edit.SourceOrdinal} has a non-finite lighting origin.",
                staticModelIndex: edit.SourceOrdinal));
            return Failed(source, edit, issues);
        }

        if (!TryBuildCandidate(
                draw,
                instance,
                edit,
                out Bounds candidateBounds,
                out _))
        {
            issues.Add(Issue(
                GfxStaticModelTranslationSpatialIssueKind.TranslationOverflow,
                $"Static-model ordinal {edit.SourceOrdinal} cannot be translated within the IW4 float domain.",
                staticModelIndex: edit.SourceOrdinal));
            return Failed(source, edit, issues);
        }

        var bsp = new DpvsBoxClassifier(world);
        if (!bsp.TryValidate(out GfxStaticModelTranslationSpatialIssue? bspIssue))
        {
            issues.Add(bspIssue!);
            return Failed(source, edit, issues);
        }

        var cells = new CellTreeCatalog(world, staticModelCount);
        if (!cells.TryValidate(out GfxStaticModelTranslationSpatialIssue? cellIssue))
        {
            issues.Add(cellIssue!);
            return Failed(source, edit, issues);
        }

        IReadOnlyList<LeafOwner> owners =
            cells.Owners(edit.SourceOrdinal);
        if (owners.Count == 0)
        {
            issues.Add(Issue(
                GfxStaticModelTranslationSpatialIssueKind.StaticModelHasNoLeafOwner,
                $"Static-model ordinal {edit.SourceOrdinal} is not owned by any Gfx cell-tree leaf.",
                staticModelIndex: edit.SourceOrdinal));
            return Failed(source, edit, issues);
        }

        int[] ownerCells = owners
            .Select(value => value.CellIndex)
            .Distinct()
            .Order()
            .ToArray();
        int[] sourceCells = bsp.Classify(instance.Bounds)
            .Order()
            .ToArray();
        if (!sourceCells.SequenceEqual(ownerCells))
        {
            issues.Add(Issue(
                GfxStaticModelTranslationSpatialIssueKind.SourceCellMembershipMismatch,
                $"Static-model ordinal {edit.SourceOrdinal} classifies to cells {Format(sourceCells)}, but its imported leaves own it in cells {Format(ownerCells)}.",
                staticModelIndex: edit.SourceOrdinal));
            return new(
                source,
                edit,
                sourceCells,
                ownerCells,
                issues.ToArray());
        }

        int[] candidateCells = bsp.Classify(candidateBounds)
            .Order()
            .ToArray();
        if (!candidateCells.SequenceEqual(sourceCells))
        {
            issues.Add(Issue(
                GfxStaticModelTranslationSpatialIssueKind.CandidateCellMembershipMismatch,
                $"The translated AABB classifies to cells {Format(candidateCells)} instead of the imported set {Format(sourceCells)}.",
                staticModelIndex: edit.SourceOrdinal));
            return new(
                source,
                edit,
                sourceCells,
                ownerCells,
                issues.ToArray());
        }

        LeafOwner? escapedOwner = owners.FirstOrDefault(owner =>
            !StaticModelSpatialEnvelope.ContainsImported(
                owner.Bounds,
                candidateBounds));
        if (escapedOwner is not null)
        {
            issues.Add(Issue(
                GfxStaticModelTranslationSpatialIssueKind.CandidateEscapesOwningLeaf,
                $"The translated AABB escapes cell {escapedOwner.CellIndex}, leaf row {escapedOwner.TreeIndex}.",
                escapedOwner.CellIndex,
                escapedOwner.TreeIndex,
                staticModelIndex: edit.SourceOrdinal));
        }

        return new(
            source,
            edit,
            sourceCells,
            ownerCells,
            issues.ToArray());
    }

    internal static GfxWorldBuildData Rewrite(
        GfxWorldBuildData source,
        GfxStaticModelTranslationSpatialAssessment assessment) =>
        Rewrite(
            source,
            [assessment]);

    internal static GfxWorldBuildData Rewrite(
        GfxWorldBuildData source,
        IEnumerable<GfxStaticModelTranslationSpatialAssessment> assessments)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(assessments);
        GfxStaticModelTranslationSpatialAssessment[] requested =
            assessments.ToArray();
        if (requested.Any(assessment => assessment is null))
        {
            throw new ArgumentException(
                "A Gfx static-model translation assessment cannot be null.",
                nameof(assessments));
        }
        int? duplicateOrdinal = requested
            .GroupBy(assessment => assessment.Edit.SourceOrdinal)
            .Where(group => group.Count() > 1)
            .Select(group => (int?)group.Key)
            .FirstOrDefault();
        if (duplicateOrdinal is not null)
        {
            throw new ArgumentException(
                $"Static-model ordinal {duplicateOrdinal.Value} has more than one translation assessment.",
                nameof(assessments));
        }

        foreach (GfxStaticModelTranslationSpatialAssessment assessment
                 in requested)
        {
            if (!assessment.IsFor(source))
            {
                throw new InvalidOperationException(
                    "A Gfx static-model spatial assessment can only authorize the exact detached source snapshot it inspected.");
            }
            if (!assessment.IsEligible)
            {
                throw new InvalidOperationException(
                    $"The ineligible Gfx static-model translation for ordinal {assessment.Edit.SourceOrdinal} cannot be applied.");
            }

            // Re-run each proof before cloning so a mutable collection
            // supplied by a caller cannot make an earlier assessment stale.
            GfxStaticModelTranslationSpatialAssessment current =
                Assess(source, assessment.Edit);
            if (!current.IsEligible)
            {
                throw new InvalidOperationException(
                    $"The Gfx static-model spatial assessment for ordinal {assessment.Edit.SourceOrdinal} became stale: " +
                    current.Issues[0].Detail);
            }
        }

        GfxWorldBuildData edited = source.Copy();
        GfxWorldDpvsStatic dpvs = edited.Definition.Dpvs;
        GfxStaticModelDrawInst[] draws =
            dpvs.SModelDrawInsts.ToArray();
        GfxStaticModelInst[] instances =
            dpvs.SModelInsts.ToArray();
        foreach (GfxStaticModelTranslationSpatialAssessment assessment
                 in requested)
        {
            int index = assessment.Edit.SourceOrdinal;
            GfxStaticModelDrawInst draw = draws[index];
            GfxStaticModelInst instance = instances[index];
            if (!TryBuildCandidate(
                    draw,
                    instance,
                    assessment.Edit,
                    out Bounds candidateBounds,
                    out Vec3 candidateLightingOrigin))
            {
                throw new InvalidOperationException(
                    $"The proven Gfx static-model translation for ordinal {index} no longer fits the IW4 float domain.");
            }

            draws[index] = new GfxStaticModelDrawInst
            {
                Placement = new GfxPackedPlacement
                {
                    Origin =
                    [
                        assessment.Edit.X,
                        assessment.Edit.Y,
                        assessment.Edit.Z
                    ],
                    PackedAxis =
                        draw.Placement.PackedAxis.ToArray(),
                    Scale = draw.Placement.Scale
                },
                ModelPointer = draw.ModelPointer,
                Model = draw.Model,
                ModelIncomingDefinition =
                    draw.ModelIncomingDefinition,
                CullDist = draw.CullDist,
                LightingHandle = draw.LightingHandle,
                ReflectionProbeIndex =
                    draw.ReflectionProbeIndex,
                PrimaryLightIndex =
                    draw.PrimaryLightIndex,
                Flags = draw.Flags,
                FirstMaterialSkinIndex =
                    draw.FirstMaterialSkinIndex,
                GroundLighting = draw.GroundLighting
            };
            instances[index] = new GfxStaticModelInst
            {
                Bounds = candidateBounds,
                LightingOrigin = candidateLightingOrigin
            };
        }

        edited.ReplaceStaticModelTables(
            draws,
            instances);
        return edited;
    }

    private static bool TryBuildCandidate(
        GfxStaticModelDrawInst draw,
        GfxStaticModelInst instance,
        StaticModelTranslationEdit edit,
        out Bounds bounds,
        out Vec3 lightingOrigin)
    {
        bounds = new();
        lightingOrigin = default;
        Vec3 origin = new()
        {
            X = draw.Placement.Origin[0],
            Y = draw.Placement.Origin[1],
            Z = draw.Placement.Origin[2]
        };
        if (!TrySubtract(edit.ToVec3(), origin, out Vec3 delta) ||
            !TryTranslate(instance.Bounds.MidPoint, delta, out Vec3 midpoint) ||
            !TryTranslate(instance.LightingOrigin, delta, out lightingOrigin))
        {
            return false;
        }
        bounds = new Bounds
        {
            MidPoint = midpoint,
            HalfSize = StaticModelSpatialEnvelope.Copy(
                instance.Bounds.HalfSize)
        };
        return TryValidateBounds(bounds);
    }

    private static bool TrySubtract(
        Vec3 left,
        Vec3 right,
        out Vec3 result)
    {
        result = new()
        {
            X = left.X - right.X,
            Y = left.Y - right.Y,
            Z = left.Z - right.Z
        };
        return IsFinite(result);
    }

    private static bool TryTranslate(
        Vec3 value,
        Vec3 delta,
        out Vec3 result)
    {
        result = new()
        {
            X = value.X + delta.X,
            Y = value.Y + delta.Y,
            Z = value.Z + delta.Z
        };
        return IsFinite(result);
    }

    private static bool TryValidateBounds(Bounds value)
    {
        try
        {
            StaticModelSpatialEnvelope.Validate(value, nameof(value));
        }
        catch (Exception exception)
            when (exception is InvalidDataException or ArgumentNullException)
        {
            return false;
        }

        return float.IsFinite(value.MidPoint.X - value.HalfSize.X) &&
            float.IsFinite(value.MidPoint.Y - value.HalfSize.Y) &&
            float.IsFinite(value.MidPoint.Z - value.HalfSize.Z) &&
            float.IsFinite(value.MidPoint.X + value.HalfSize.X) &&
            float.IsFinite(value.MidPoint.Y + value.HalfSize.Y) &&
            float.IsFinite(value.MidPoint.Z + value.HalfSize.Z);
    }

    private static bool IsFinite(Vec3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static GfxStaticModelTranslationSpatialAssessment Failed(
        GfxWorldBuildData source,
        StaticModelTranslationEdit edit,
        IReadOnlyList<GfxStaticModelTranslationSpatialIssue> issues) =>
        new(source, edit, [], [], issues.ToArray());

    private static GfxStaticModelTranslationSpatialIssue Issue(
        GfxStaticModelTranslationSpatialIssueKind kind,
        string detail,
        int? cellIndex = null,
        int? treeIndex = null,
        int? nodeOffset = null,
        int? staticModelIndex = null) =>
        new(
            kind,
            detail,
            cellIndex,
            treeIndex,
            nodeOffset,
            staticModelIndex);

    private static string Format(IEnumerable<int> values) =>
        $"[{string.Join(", ", values)}]";

    private sealed record LeafOwner(
        int CellIndex,
        int TreeIndex,
        Bounds Bounds);

    private sealed class CellTreeCatalog
    {
        private readonly GfxWorldAsset _world;
        private readonly int _staticModelCount;
        private readonly Dictionary<int, List<LeafOwner>> _owners = [];

        public CellTreeCatalog(
            GfxWorldAsset world,
            int staticModelCount)
        {
            _world = world;
            _staticModelCount = staticModelCount;
        }

        public IReadOnlyList<LeafOwner> Owners(int staticModelIndex) =>
            _owners.TryGetValue(
                staticModelIndex,
                out List<LeafOwner>? owners)
                ? owners
                : [];

        public bool TryValidate(
            out GfxStaticModelTranslationSpatialIssue? issue)
        {
            int cellCount = _world.DpvsPlanes.CellCount;
            if (_world.CellTrees.Count != cellCount ||
                _world.CellTreeCounts.Count != cellCount)
            {
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.CellTreeCardinalityMismatch,
                    $"GfxWorld materializes {_world.CellTrees.Count} cell trees and {_world.CellTreeCounts.Count} count rows for {cellCount} DPVS cells.");
                return false;
            }

            for (int staticModelIndex = 0;
                 staticModelIndex < _staticModelCount;
                 staticModelIndex++)
            {
                if (!TryValidateBounds(
                        _world.Dpvs.SModelInsts[staticModelIndex].Bounds))
                {
                    issue = Issue(
                        GfxStaticModelTranslationSpatialIssueKind.InvalidStaticModelBounds,
                        $"Static-model ordinal {staticModelIndex} has an invalid AABB.",
                        staticModelIndex: staticModelIndex);
                    return false;
                }
            }

            for (int cellIndex = 0;
                 cellIndex < cellCount;
                 cellIndex++)
            {
                if (!TryValidateCell(cellIndex, out issue))
                    return false;
            }

            issue = null;
            return true;
        }

        private bool TryValidateCell(
            int cellIndex,
            out GfxStaticModelTranslationSpatialIssue? issue)
        {
            GfxCellTree cell = _world.CellTrees[cellIndex];
            uint declaredCount =
                _world.CellTreeCounts[cellIndex].AabbTreeCount;
            if (declaredCount > int.MaxValue ||
                cell.AabbTrees.Count != (int)declaredCount)
            {
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.CellTreeRowCardinalityMismatch,
                    $"Cell {cellIndex} declares {declaredCount} AABB rows but materializes {cell.AabbTrees.Count}.",
                    cellIndex);
                return false;
            }
            if (cell.AabbTrees.Count == 0)
            {
                issue = null;
                return true;
            }

            IReadOnlyList<GfxAabbTree> rows = cell.AabbTrees;
            int[] parents = Enumerable.Repeat(-1, rows.Count).ToArray();
            var cellModels = new HashSet<int>();
            for (int treeIndex = 0;
                 treeIndex < rows.Count;
                 treeIndex++)
            {
                GfxAabbTree row = rows[treeIndex];
                if (!TryValidateBounds(row.Bounds))
                {
                    issue = Issue(
                        GfxStaticModelTranslationSpatialIssueKind.CellTreeInvalidBounds,
                        $"Cell {cellIndex}, AABB row {treeIndex} has an invalid bound.",
                        cellIndex,
                        treeIndex);
                    return false;
                }
                if (row.SModelIndexCount != row.SModelIndexes.Count)
                {
                    issue = Issue(
                        GfxStaticModelTranslationSpatialIssueKind.CellTreeIndexCardinalityMismatch,
                        $"Cell {cellIndex}, AABB row {treeIndex} declares {row.SModelIndexCount} static-model indices but materializes {row.SModelIndexes.Count}.",
                        cellIndex,
                        treeIndex);
                    return false;
                }

                if (row.ChildCount == 0)
                {
                    foreach (ushort modelIndex in row.SModelIndexes)
                    {
                        if (modelIndex >= _staticModelCount)
                        {
                            issue = Issue(
                                GfxStaticModelTranslationSpatialIssueKind.CellTreeInvalidStaticModelIndex,
                                $"Cell {cellIndex}, leaf row {treeIndex} references static-model ordinal {modelIndex} outside {_staticModelCount} rows.",
                                cellIndex,
                                treeIndex,
                                staticModelIndex: modelIndex);
                            return false;
                        }
                        if (!cellModels.Add(modelIndex))
                        {
                            issue = Issue(
                                GfxStaticModelTranslationSpatialIssueKind.CellTreeDuplicateStaticModelOwnership,
                                $"Cell {cellIndex} owns static-model ordinal {modelIndex} in more than one leaf entry.",
                                cellIndex,
                                treeIndex,
                                staticModelIndex: modelIndex);
                            return false;
                        }

                        Bounds modelBounds =
                            _world.Dpvs.SModelInsts[modelIndex].Bounds;
                        if (!StaticModelSpatialEnvelope.ContainsImported(
                                row.Bounds,
                                modelBounds))
                        {
                            issue = Issue(
                                GfxStaticModelTranslationSpatialIssueKind.CellTreeLeafExcludesStaticModel,
                                $"Cell {cellIndex}, leaf row {treeIndex} does not contain static-model ordinal {modelIndex}.",
                                cellIndex,
                                treeIndex,
                                staticModelIndex: modelIndex);
                            return false;
                        }
                        if (!_owners.TryGetValue(
                                modelIndex,
                                out List<LeafOwner>? owners))
                        {
                            owners = [];
                            _owners.Add(modelIndex, owners);
                        }
                        owners.Add(
                            new LeafOwner(
                                cellIndex,
                                treeIndex,
                                StaticModelSpatialEnvelope.Copy(
                                    row.Bounds)));
                    }
                    continue;
                }

                if (row.ChildrenOffset <= 0 ||
                    row.ChildrenOffset % AabbTreeRowSize != 0)
                {
                    issue = Issue(
                        GfxStaticModelTranslationSpatialIssueKind.CellTreeInvalidChildOffset,
                        $"Cell {cellIndex}, internal AABB row {treeIndex} has non-forward or unaligned child byte offset {row.ChildrenOffset}.",
                        cellIndex,
                        treeIndex);
                    return false;
                }
                int firstChild;
                int childEnd;
                try
                {
                    firstChild = checked(
                        treeIndex +
                        row.ChildrenOffset / AabbTreeRowSize);
                    childEnd = checked(firstChild + row.ChildCount);
                }
                catch (OverflowException)
                {
                    issue = Issue(
                        GfxStaticModelTranslationSpatialIssueKind.CellTreeInvalidChildRange,
                        $"Cell {cellIndex}, internal AABB row {treeIndex} has a child range outside the host integer domain.",
                        cellIndex,
                        treeIndex);
                    return false;
                }
                if (firstChild <= treeIndex ||
                    childEnd > rows.Count)
                {
                    issue = Issue(
                        GfxStaticModelTranslationSpatialIssueKind.CellTreeInvalidChildRange,
                        $"Cell {cellIndex}, internal AABB row {treeIndex} selects child rows [{firstChild}, {childEnd}) outside {rows.Count} rows.",
                        cellIndex,
                        treeIndex);
                    return false;
                }
                for (int child = firstChild;
                     child < childEnd;
                     child++)
                {
                    if (parents[child] != -1)
                    {
                        issue = Issue(
                            GfxStaticModelTranslationSpatialIssueKind.CellTreeMultipleParents,
                            $"Cell {cellIndex}, AABB row {child} is owned by both rows {parents[child]} and {treeIndex}.",
                            cellIndex,
                            child);
                        return false;
                    }
                    parents[child] = treeIndex;
                }
            }

            if (parents[0] != -1)
            {
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.CellTreeMultipleParents,
                    $"Cell {cellIndex} root AABB row 0 is owned by row {parents[0]}.",
                    cellIndex,
                    0);
                return false;
            }
            for (int treeIndex = 1;
                 treeIndex < rows.Count;
                 treeIndex++)
            {
                if (parents[treeIndex] == -1)
                {
                    issue = Issue(
                        GfxStaticModelTranslationSpatialIssueKind.CellTreeOrphanRow,
                        $"Cell {cellIndex}, AABB row {treeIndex} is not owned by root row 0.",
                        cellIndex,
                        treeIndex);
                    return false;
                }
            }

            var active = new HashSet<int>();
            var visited = new HashSet<int>();
            if (!VisitCellTree(
                    cellIndex,
                    rows,
                    0,
                    active,
                    visited,
                    out issue))
            {
                return false;
            }
            if (visited.Count != rows.Count)
            {
                int orphan = Enumerable.Range(0, rows.Count)
                    .First(index => !visited.Contains(index));
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.CellTreeOrphanRow,
                    $"Cell {cellIndex}, AABB row {orphan} is unreachable from root row 0.",
                    cellIndex,
                    orphan);
                return false;
            }

            for (int treeIndex = 0;
                 treeIndex < rows.Count;
                 treeIndex++)
            {
                GfxAabbTree row = rows[treeIndex];
                if (row.ChildCount == 0)
                    continue;
                int firstChild =
                    treeIndex +
                    row.ChildrenOffset / AabbTreeRowSize;
                ushort[] expected = Enumerable
                    .Range(firstChild, row.ChildCount)
                    .SelectMany(index =>
                        rows[index].SModelIndexes)
                    .ToArray();
                if (!row.SModelIndexes.SequenceEqual(expected))
                {
                    issue = Issue(
                        GfxStaticModelTranslationSpatialIssueKind.CellTreeInternalIndexConcatenationMismatch,
                        $"Cell {cellIndex}, internal AABB row {treeIndex} does not contain the exact direct-child static-model index concatenation.",
                        cellIndex,
                        treeIndex);
                    return false;
                }
            }

            issue = null;
            return true;
        }

        private static bool VisitCellTree(
            int cellIndex,
            IReadOnlyList<GfxAabbTree> rows,
            int treeIndex,
            HashSet<int> active,
            HashSet<int> visited,
            out GfxStaticModelTranslationSpatialIssue? issue)
        {
            if (!active.Add(treeIndex))
            {
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.CellTreeTraversalCycle,
                    $"Cell {cellIndex} revisits active AABB row {treeIndex}.",
                    cellIndex,
                    treeIndex);
                return false;
            }
            if (!visited.Add(treeIndex))
            {
                active.Remove(treeIndex);
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.CellTreeMultipleParents,
                    $"Cell {cellIndex} reaches AABB row {treeIndex} more than once.",
                    cellIndex,
                    treeIndex);
                return false;
            }

            GfxAabbTree row = rows[treeIndex];
            if (row.ChildCount != 0)
            {
                int firstChild =
                    treeIndex +
                    row.ChildrenOffset / AabbTreeRowSize;
                for (int child = firstChild;
                     child < firstChild + row.ChildCount;
                     child++)
                {
                    if (!VisitCellTree(
                            cellIndex,
                            rows,
                            child,
                            active,
                            visited,
                            out issue))
                    {
                        return false;
                    }
                }
            }

            active.Remove(treeIndex);
            issue = null;
            return true;
        }
    }

    private sealed class DpvsBoxClassifier
    {
        private readonly GfxWorldAsset _world;
        private readonly IReadOnlyList<ushort> _nodes;
        private readonly int _internalBase;

        public DpvsBoxClassifier(GfxWorldAsset world)
        {
            _world = world;
            _nodes = world.DpvsPlanes.Nodes;
            _internalBase = world.DpvsPlanes.CellCount + 1;
        }

        public bool TryValidate(
            out GfxStaticModelTranslationSpatialIssue? issue)
        {
            int cellCount = _world.DpvsPlanes.CellCount;
            if (cellCount <= 0 ||
                cellCount >= ushort.MaxValue)
            {
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsCellCountInvalid,
                    $"PS3 packed DPVS leaves cannot represent cell count {cellCount}.");
                return false;
            }
            if (_world.PlaneCount < 0 ||
                _world.DpvsPlanes.Planes.Count !=
                    _world.PlaneCount)
            {
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsPlaneCardinalityMismatch,
                    $"GfxWorld declares {_world.PlaneCount} DPVS planes but materializes {_world.DpvsPlanes.Planes.Count}.");
                return false;
            }
            if (_world.NodeCount < 0 ||
                _nodes.Count != _world.NodeCount)
            {
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsNodeCardinalityMismatch,
                    $"GfxWorld declares {_world.NodeCount} packed DPVS ushorts but materializes {_nodes.Count}.");
                return false;
            }
            if (_nodes.Count == 0)
            {
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsMissingRootNode,
                    "The packed DPVS node table has no root token.");
                return false;
            }
            for (int planeIndex = 0;
                 planeIndex < _world.DpvsPlanes.Planes.Count;
                 planeIndex++)
            {
                DpvsPlane plane =
                    _world.DpvsPlanes.Planes[planeIndex];
                if (!float.IsFinite(plane.NormalX) ||
                    !float.IsFinite(plane.NormalY) ||
                    !float.IsFinite(plane.NormalZ) ||
                    !float.IsFinite(plane.Distance))
                {
                    issue = Issue(
                        GfxStaticModelTranslationSpatialIssueKind.DpvsInvalidPlane,
                        $"DPVS plane {planeIndex} has a non-finite coefficient.",
                        nodeOffset: null);
                    return false;
                }
            }

            var active = new HashSet<int>();
            var nodeStarts = new HashSet<int>();
            var metadata = new HashSet<int>();
            if (!VisitTopology(
                    0,
                    active,
                    nodeStarts,
                    metadata,
                    out issue))
            {
                return false;
            }
            for (int offset = 0;
                 offset < _nodes.Count;
                 offset++)
            {
                bool isNode = nodeStarts.Contains(offset);
                bool isMetadata = metadata.Contains(offset);
                if (isNode && isMetadata)
                {
                    issue = Issue(
                        GfxStaticModelTranslationSpatialIssueKind.DpvsNodeStorageOverlap,
                        $"Packed DPVS ushort offset {offset} is both a node token and right-child metadata.",
                        nodeOffset: offset);
                    return false;
                }
                if (!isNode && !isMetadata)
                {
                    issue = Issue(
                        GfxStaticModelTranslationSpatialIssueKind.DpvsOrphanNodeStorage,
                        $"Packed DPVS ushort offset {offset} is unreachable from root offset 0.",
                        nodeOffset: offset);
                    return false;
                }
            }

            issue = null;
            return true;
        }

        public IReadOnlySet<int> Classify(Bounds bounds)
        {
            var cells = new HashSet<int>();
            Classify(
                0,
                bounds,
                cells);
            return cells;
        }

        private bool VisitTopology(
            int nodeOffset,
            HashSet<int> active,
            HashSet<int> nodeStarts,
            HashSet<int> metadata,
            out GfxStaticModelTranslationSpatialIssue? issue)
        {
            if ((uint)nodeOffset >= (uint)_nodes.Count)
            {
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsInvalidChildOffset,
                    $"Packed DPVS traversal selects ushort offset {nodeOffset} outside {_nodes.Count} entries.",
                    nodeOffset: nodeOffset);
                return false;
            }
            if (metadata.Contains(nodeOffset))
            {
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsNodeStorageOverlap,
                    $"Packed DPVS child selects right-child metadata at ushort offset {nodeOffset}.",
                    nodeOffset: nodeOffset);
                return false;
            }
            if (!active.Add(nodeOffset))
            {
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsTraversalCycle,
                    $"Packed DPVS traversal revisits active ushort offset {nodeOffset}.",
                    nodeOffset: nodeOffset);
                return false;
            }
            if (!nodeStarts.Add(nodeOffset))
            {
                active.Remove(nodeOffset);
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsNodeStorageOverlap,
                    $"Packed DPVS node token at ushort offset {nodeOffset} has more than one parent.",
                    nodeOffset: nodeOffset);
                return false;
            }

            int token = _nodes[nodeOffset];
            if (token < _internalBase)
            {
                active.Remove(nodeOffset);
                issue = null;
                return true;
            }

            int planeIndex = token - _internalBase;
            if ((uint)planeIndex >=
                (uint)_world.DpvsPlanes.Planes.Count)
            {
                active.Remove(nodeOffset);
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsInvalidPlaneIndex,
                    $"Packed DPVS node at ushort offset {nodeOffset} references plane {planeIndex} outside {_world.DpvsPlanes.Planes.Count} rows.",
                    nodeOffset: nodeOffset);
                return false;
            }
            if (nodeOffset + 1 >= _nodes.Count)
            {
                active.Remove(nodeOffset);
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsInvalidChildOffset,
                    $"Packed DPVS internal node at ushort offset {nodeOffset} has no right-child offset word.",
                    nodeOffset: nodeOffset);
                return false;
            }
            int rightOffset = _nodes[nodeOffset + 1];
            if (rightOffset < 3)
            {
                active.Remove(nodeOffset);
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsInvalidChildOffset,
                    $"Packed DPVS internal node at ushort offset {nodeOffset} has invalid right-child delta {rightOffset}.",
                    nodeOffset: nodeOffset);
                return false;
            }
            if (!metadata.Add(nodeOffset + 1))
            {
                active.Remove(nodeOffset);
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsNodeStorageOverlap,
                    $"Packed DPVS ushort offset {nodeOffset + 1} is reused as right-child metadata.",
                    nodeOffset: nodeOffset + 1);
                return false;
            }
            int frontChild = nodeOffset + 2;
            int backChild;
            try
            {
                backChild = checked(nodeOffset + rightOffset);
            }
            catch (OverflowException)
            {
                active.Remove(nodeOffset);
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsInvalidChildOffset,
                    $"Packed DPVS internal node at ushort offset {nodeOffset} overflows its right-child offset.",
                    nodeOffset: nodeOffset);
                return false;
            }
            if ((uint)frontChild >= (uint)_nodes.Count ||
                (uint)backChild >= (uint)_nodes.Count)
            {
                active.Remove(nodeOffset);
                issue = Issue(
                    GfxStaticModelTranslationSpatialIssueKind.DpvsInvalidChildOffset,
                    $"Packed DPVS internal node at ushort offset {nodeOffset} selects children {frontChild} and {backChild} outside {_nodes.Count} entries.",
                    nodeOffset: nodeOffset);
                return false;
            }

            if (!VisitTopology(
                    frontChild,
                    active,
                    nodeStarts,
                    metadata,
                    out issue) ||
                !VisitTopology(
                    backChild,
                    active,
                    nodeStarts,
                    metadata,
                    out issue))
            {
                active.Remove(nodeOffset);
                return false;
            }

            active.Remove(nodeOffset);
            issue = null;
            return true;
        }

        private void Classify(
            int nodeOffset,
            Bounds bounds,
            HashSet<int> cells)
        {
            int token = _nodes[nodeOffset];
            if (token < _internalBase)
            {
                if (token != 0)
                    cells.Add(token - 1);
                return;
            }

            DpvsPlane plane =
                _world.DpvsPlanes.Planes[token - _internalBase];
            float distance =
                bounds.MidPoint.X * plane.NormalX +
                bounds.MidPoint.Y * plane.NormalY +
                bounds.MidPoint.Z * plane.NormalZ -
                plane.Distance;
            float radius =
                bounds.HalfSize.X * MathF.Abs(plane.NormalX) +
                bounds.HalfSize.Y * MathF.Abs(plane.NormalY) +
                bounds.HalfSize.Z * MathF.Abs(plane.NormalZ) -
                BoxPlaneEpsilon;
            int frontChild = nodeOffset + 2;
            int backChild = nodeOffset + _nodes[nodeOffset + 1];
            if (distance > radius)
            {
                Classify(frontChild, bounds, cells);
                return;
            }
            if (distance < -radius)
            {
                Classify(backChild, bounds, cells);
                return;
            }

            if (plane.Type > 2)
            {
                Classify(frontChild, bounds, cells);
                Classify(backChild, bounds, cells);
                return;
            }

            int axis = plane.Type;
            float minimum = Component(bounds.MidPoint, axis) -
                Component(bounds.HalfSize, axis);
            float maximum = Component(bounds.MidPoint, axis) +
                Component(bounds.HalfSize, axis);
            if (maximum > plane.Distance)
            {
                Bounds frontBounds = ClipMinimum(
                    bounds,
                    axis,
                    plane.Distance);
                Classify(frontChild, frontBounds, cells);
            }
            if (minimum <= plane.Distance)
            {
                Bounds backBounds = ClipMaximum(
                    bounds,
                    axis,
                    plane.Distance);
                Classify(backChild, backBounds, cells);
            }
        }

        private static Bounds ClipMinimum(
            Bounds value,
            int axis,
            float minimum)
        {
            Vec3 oldMinimum = new()
            {
                X = value.MidPoint.X - value.HalfSize.X,
                Y = value.MidPoint.Y - value.HalfSize.Y,
                Z = value.MidPoint.Z - value.HalfSize.Z
            };
            Vec3 maximum = new()
            {
                X = value.MidPoint.X + value.HalfSize.X,
                Y = value.MidPoint.Y + value.HalfSize.Y,
                Z = value.MidPoint.Z + value.HalfSize.Z
            };
            SetComponent(ref oldMinimum, axis, minimum);
            return FromEndpoints(oldMinimum, maximum);
        }

        private static Bounds ClipMaximum(
            Bounds value,
            int axis,
            float maximum)
        {
            Vec3 minimum = new()
            {
                X = value.MidPoint.X - value.HalfSize.X,
                Y = value.MidPoint.Y - value.HalfSize.Y,
                Z = value.MidPoint.Z - value.HalfSize.Z
            };
            Vec3 oldMaximum = new()
            {
                X = value.MidPoint.X + value.HalfSize.X,
                Y = value.MidPoint.Y + value.HalfSize.Y,
                Z = value.MidPoint.Z + value.HalfSize.Z
            };
            SetComponent(ref oldMaximum, axis, maximum);
            return FromEndpoints(minimum, oldMaximum);
        }

        private static Bounds FromEndpoints(
            Vec3 minimum,
            Vec3 maximum) =>
            new()
            {
                MidPoint = new Vec3
                {
                    X = (minimum.X + maximum.X) * 0.5f,
                    Y = (minimum.Y + maximum.Y) * 0.5f,
                    Z = (minimum.Z + maximum.Z) * 0.5f
                },
                HalfSize = new Vec3
                {
                    X = (maximum.X - minimum.X) * 0.5f,
                    Y = (maximum.Y - minimum.Y) * 0.5f,
                    Z = (maximum.Z - minimum.Z) * 0.5f
                }
            };

        private static float Component(Vec3 value, int axis) =>
            axis switch
            {
                0 => value.X,
                1 => value.Y,
                2 => value.Z,
                _ => throw new ArgumentOutOfRangeException(nameof(axis))
            };

        private static void SetComponent(
            ref Vec3 value,
            int axis,
            float component)
        {
            switch (axis)
            {
                case 0:
                    value.X = component;
                    break;
                case 1:
                    value.Y = component;
                    break;
                case 2:
                    value.Z = component;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(axis));
            }
        }
    }
}

public sealed partial class GfxWorldBuildData
{
    /// <summary>
    /// Returns a detached copy containing one translation that has passed the
    /// exact imported GfxWorld spatial-membership proof. No spatial topology,
    /// membership list, lighting assignment, shadow assignment, packed axis,
    /// scale, flags, cull distance, or model reference is rewritten.
    /// </summary>
    public GfxWorldBuildData WithSpatiallyEligibleStaticModelTranslation(
        GfxStaticModelTranslationSpatialAssessment assessment) =>
        GfxStaticModelTranslationSpatialAssessor.Rewrite(
            this,
            assessment);

    /// <summary>
    /// Batch form of the proof-gated Gfx translation rewrite. Every
    /// assessment must belong to this exact baseline and address a distinct
    /// static-model ordinal. The detached world graph is copied once.
    /// </summary>
    public GfxWorldBuildData WithSpatiallyEligibleStaticModelTranslations(
        IEnumerable<GfxStaticModelTranslationSpatialAssessment> assessments) =>
        GfxStaticModelTranslationSpatialAssessor.Rewrite(
            this,
            assessments);

    internal void ReplaceStaticModelTables(
        IReadOnlyList<GfxStaticModelDrawInst> draws,
        IReadOnlyList<GfxStaticModelInst> instances)
    {
        Set(
            Definition.Dpvs,
            nameof(GfxWorldDpvsStatic.SModelDrawInsts),
            draws);
        Set(
            Definition.Dpvs,
            nameof(GfxWorldDpvsStatic.SModelInsts),
            instances);
    }
}
