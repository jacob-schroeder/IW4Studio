using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Math;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.StaticModels;

/// <summary>
/// Proves GfxWorld/ClipMap static-model correspondence for one immutable
/// imported bundle. Matching is value-based and fail-closed: source ordinals
/// are outputs of the proof, never matching evidence.
/// </summary>
public static class StaticModelCompilationRelationshipResolver
{
    /// <summary>
    /// Maximum accepted difference between corresponding midpoint/half-size
    /// components. This covers the observed compiler quantization boundary
    /// without admitting proximity-based matching.
    /// </summary>
    public const float BoundsAbsoluteTolerance = 1f / 512f;

    /// <summary>
    /// Maximum residual when the decoded scaled Gfx matrix and the Clip
    /// inverse-scaled matrix are multiplied in either order. The bound is
    /// derived from the 10/11-bit packed-axis precision.
    /// </summary>
    public const float AxisIdentityTolerance = 1f / 128f;

    public static StaticModelCorrespondenceCatalog Resolve(
        CompiledMapBundle bundle,
        EditorMapDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var issues = new List<StaticModelCorrespondenceIssue>();
        if (document.Id != bundle.DocumentId ||
            !string.Equals(
                document.MapIdentity,
                bundle.MapIdentity,
                StringComparison.Ordinal))
        {
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind.DocumentIdentityMismatch,
                null,
                null,
                "The semantic document does not belong to the exact " +
                "compiled bundle supplied to correspondence resolution."));
            return InvalidCatalog(
                bundle,
                document,
                collisionAssetKind: null,
                issues);
        }

        if (!bundle.TryGetBaseline(
                MapAssetKind.GfxMap,
                out GfxWorldBuildData? gfx) ||
            gfx is null)
        {
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind.MissingGfxWorldAuthority,
                StaticModelRepresentation.Render,
                null,
                "The bundle has no detached GfxWorld authority."));
            return InvalidCatalog(
                bundle,
                document,
                collisionAssetKind: null,
                issues);
        }

        bool hasMp = bundle.TryGetBaseline(
            MapAssetKind.ColMapMp,
            out ClipMapBuildData? clipMp) &&
            clipMp is not null;
        bool hasSp = bundle.TryGetBaseline(
            MapAssetKind.ColMapSp,
            out ClipMapBuildData? clipSp) &&
            clipSp is not null;
        if (hasMp && hasSp)
        {
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind
                    .ConflictingCollisionAuthorities,
                StaticModelRepresentation.Collision,
                null,
                "The bundle contains both ColMapMp and ColMapSp authority; " +
                "the collision owner is not unique."));
            return InvalidCatalog(
                bundle,
                document,
                collisionAssetKind: null,
                issues);
        }
        if (!hasMp && !hasSp)
        {
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind
                    .MissingCollisionAuthority,
                StaticModelRepresentation.Collision,
                null,
                "The bundle has no detached ColMapMp or ColMapSp authority."));
            return InvalidCatalog(
                bundle,
                document,
                collisionAssetKind: null,
                issues);
        }

        MapAssetKind collisionAssetKind = hasMp
            ? MapAssetKind.ColMapMp
            : MapAssetKind.ColMapSp;
        ClipMapBuildData clip = hasMp ? clipMp! : clipSp!;
        cancellationToken.ThrowIfCancellationRequested();

        if (!ValidateAuthorityCardinality(
                gfx,
                clip,
                collisionAssetKind,
                issues))
        {
            return InvalidCatalog(
                bundle,
                document,
                collisionAssetKind,
                issues);
        }

        int renderCount = checked((int)gfx.Definition.Dpvs.SModelCount);
        int collisionCount = clip.Definition.NumStaticModels;
        CompiledMapAssetDescriptor gfxDescriptor =
            bundle.RequireAsset(MapAssetKind.GfxMap);
        CompiledMapAssetDescriptor clipDescriptor =
            bundle.RequireAsset(collisionAssetKind);

        if (!TryIndexSemanticRows(
                document,
                gfxDescriptor,
                clipDescriptor,
                renderCount,
                collisionCount,
                issues,
                out EditorStaticModel[] renderSemanticRows,
                out EditorStaticModel[] collisionSemanticRows))
        {
            return InvalidCatalog(
                bundle,
                document,
                collisionAssetKind,
                issues);
        }

        RenderRow[] renderRows = new RenderRow[renderCount];
        for (int ordinal = 0; ordinal < renderRows.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            renderRows[ordinal] = BuildRenderRow(
                ordinal,
                gfx,
                renderSemanticRows[ordinal],
                issues);
        }

        CollisionRow[] collisionRows = new CollisionRow[collisionCount];
        for (int ordinal = 0; ordinal < collisionRows.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            collisionRows[ordinal] = BuildCollisionRow(
                ordinal,
                clip,
                collisionSemanticRows[ordinal],
                issues);
        }

        Dictionary<StaticModelExactMatchKey, CollisionRow[]> collisionByKey =
            collisionRows
                .Where(value => value.IsValid)
                .GroupBy(MatchKey)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray());
        var evaluations = new List<PairEvaluation>();
        foreach (RenderRow render in renderRows.Where(value => value.IsValid))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!collisionByKey.TryGetValue(
                    MatchKey(render),
                    out CollisionRow[]? candidates))
            {
                continue;
            }

            foreach (CollisionRow collision in candidates)
            {
                StaticModelBoundsCorrespondenceEvidence bounds =
                    CompareBounds(render.Bounds, collision.Bounds);
                StaticModelAxisScaleCorrespondenceEvidence axisScale =
                    CompareAxisScale(
                        render.Scale,
                        render.ScaledAxis,
                        collision.InverseScaledAxis);
                evaluations.Add(new PairEvaluation(
                    render,
                    collision,
                    bounds,
                    axisScale,
                    bounds.MaximumAbsoluteDelta <=
                        bounds.AbsoluteTolerance,
                    axisScale.MaximumIdentityResidual <=
                        axisScale.IdentityTolerance));
            }
        }

        var evaluationsByRender = evaluations
            .GroupBy(value => value.Render.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());
        var evaluationsByCollision = evaluations
            .GroupBy(value => value.Collision.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());
        PairEvaluation[] accepted = evaluations
            .Where(value => value.IsAccepted)
            .ToArray();
        var acceptedByRender = accepted
            .GroupBy(value => value.Render.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());
        var acceptedByCollision = accepted
            .GroupBy(value => value.Collision.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());

        var relationships =
            new List<StaticModelCompilationRelationship>();
        foreach (RenderRow render in renderRows.Where(value => value.IsValid))
        {
            cancellationToken.ThrowIfCancellationRequested();
            PairEvaluation[] renderAccepted =
                acceptedByRender.GetValueOrDefault(render.Ordinal) ?? [];
            if (renderAccepted.Length != 1)
                continue;

            PairEvaluation candidate = renderAccepted[0];
            PairEvaluation[] collisionAccepted =
                acceptedByCollision.GetValueOrDefault(
                    candidate.Collision.Ordinal) ?? [];
            if (collisionAccepted.Length != 1)
                continue;

            SymbolicXAssetReference model = render.ModelReference!;
            relationships.Add(new StaticModelCompilationRelationship(
                render.Semantic.Id,
                candidate.Collision.Semantic.Id,
                render.Ordinal,
                candidate.Collision.Ordinal,
                collisionAssetKind,
                model.AssetType,
                model.OriginalSerializedName,
                ExactOriginEvidence(render.Origin),
                candidate.Bounds,
                candidate.AxisScale,
                "Exact imported-bundle relationship: the serialized XModel " +
                "reference and IEEE-754 origin bits are identical, bounds " +
                $"differ by at most " +
                $"{candidate.Bounds.MaximumAbsoluteDelta:R} (limit " +
                $"{BoundsAbsoluteTolerance:R}), and both scaled-axis " +
                $"identity products have residual at most " +
                $"{candidate.AxisScale.MaximumIdentityResidual:R} (limit " +
                $"{AxisIdentityTolerance:R}). The match is mutual and " +
                "one-to-one; no ordinal or universal IW4 relationship is " +
                "inferred."));
        }

        HashSet<int> exactRenderOrdinals = relationships
            .Select(value => value.GfxSourceOrdinal)
            .ToHashSet();
        HashSet<int> exactCollisionOrdinals = relationships
            .Select(value => value.ClipSourceOrdinal)
            .ToHashSet();
        var assessments =
            new List<StaticModelCorrespondenceAssessment>(
                renderRows.Length + collisionRows.Length);
        foreach (RenderRow render in renderRows)
        {
            assessments.Add(AssessRender(
                render,
                evaluationsByRender,
                acceptedByRender,
                acceptedByCollision,
                exactRenderOrdinals,
                issues));
        }
        foreach (CollisionRow collision in collisionRows)
        {
            assessments.Add(AssessCollision(
                collision,
                evaluationsByCollision,
                acceptedByRender,
                acceptedByCollision,
                exactCollisionOrdinals,
                issues));
        }

        return new StaticModelCorrespondenceCatalog(
            document.Id,
            bundle.MapIdentity,
            bundle.BaselineDigest,
            collisionAssetKind,
            authoritiesValid: true,
            relationships,
            assessments,
            issues);
    }

    private static bool ValidateAuthorityCardinality(
        GfxWorldBuildData gfx,
        ClipMapBuildData clip,
        MapAssetKind collisionAssetKind,
        ICollection<StaticModelCorrespondenceIssue> issues)
    {
        GfxWorldDpvsStatic dpvs = gfx.Definition.Dpvs;
        bool valid = true;
        if (dpvs.SModelCount > int.MaxValue ||
            dpvs.SModelDrawInsts.Count != dpvs.SModelCount ||
            dpvs.SModelInsts.Count != dpvs.SModelCount ||
            gfx.References.StaticModelDrawInsts.Count != dpvs.SModelCount)
        {
            valid = false;
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind
                    .AuthorityCardinalityMismatch,
                StaticModelRepresentation.Render,
                null,
                $"GfxWorld declares {dpvs.SModelCount} static models and " +
                $"retains {dpvs.SModelDrawInsts.Count} draw rows, " +
                $"{dpvs.SModelInsts.Count} instance rows, and " +
                $"{gfx.References.StaticModelDrawInsts.Count} symbolic " +
                "XModel references."));
        }

        ClipMapAsset definition = clip.Definition;
        if (definition.NumStaticModels < 0 ||
            definition.StaticModelList.Count !=
                definition.NumStaticModels ||
            clip.References.StaticModels.Count !=
                definition.NumStaticModels)
        {
            valid = false;
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind
                    .AuthorityCardinalityMismatch,
                StaticModelRepresentation.Collision,
                null,
                $"{collisionAssetKind} declares " +
                $"{definition.NumStaticModels} static models and retains " +
                $"{definition.StaticModelList.Count} rows and " +
                $"{clip.References.StaticModels.Count} symbolic XModel " +
                "references."));
        }

        return valid;
    }

    private static bool TryIndexSemanticRows(
        EditorMapDocument document,
        CompiledMapAssetDescriptor gfxDescriptor,
        CompiledMapAssetDescriptor clipDescriptor,
        int renderCount,
        int collisionCount,
        ICollection<StaticModelCorrespondenceIssue> issues,
        out EditorStaticModel[] renderRows,
        out EditorStaticModel[] collisionRows)
    {
        renderRows = new EditorStaticModel[renderCount];
        collisionRows = new EditorStaticModel[collisionCount];
        bool renderValid = TryIndexSemanticRepresentation(
            document,
            StaticModelRepresentation.Render,
            gfxDescriptor,
            "render-static-model",
            renderRows,
            issues);
        bool collisionValid = TryIndexSemanticRepresentation(
            document,
            StaticModelRepresentation.Collision,
            clipDescriptor,
            "collision-static-model",
            collisionRows,
            issues);
        return renderValid && collisionValid;
    }

    private static bool TryIndexSemanticRepresentation(
        EditorMapDocument document,
        StaticModelRepresentation representation,
        CompiledMapAssetDescriptor descriptor,
        string semanticRole,
        EditorStaticModel[] destination,
        ICollection<StaticModelCorrespondenceIssue> issues)
    {
        EditorStaticModel[] source = document.StaticModels
            .Where(value =>
                value.IsImported &&
                value.Representation == representation)
            .ToArray();
        bool valid = true;
        if (source.Length != destination.Length)
        {
            valid = false;
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind
                    .SemanticProjectionMismatch,
                representation,
                null,
                $"Semantic " +
                $"{representation.ToString().ToLowerInvariant()} " +
                $"cardinality {source.Length} does not match compiled " +
                $"authority cardinality {destination.Length}."));
        }

        foreach (EditorStaticModel model in source)
        {
            int ordinal = model.SourceOrdinal.Value;
            if ((uint)ordinal >= (uint)destination.Length)
            {
                valid = false;
                issues.Add(new StaticModelCorrespondenceIssue(
                    StaticModelCorrespondenceIssueKind
                        .SemanticProjectionMismatch,
                    representation,
                    ordinal,
                    $"Semantic object {model.Id} claims out-of-range source " +
                    $"ordinal {ordinal}."));
                continue;
            }
            if (destination[ordinal] is not null)
            {
                valid = false;
                issues.Add(new StaticModelCorrespondenceIssue(
                    StaticModelCorrespondenceIssueKind
                        .SemanticProjectionMismatch,
                    representation,
                    ordinal,
                    $"More than one semantic object claims source ordinal " +
                    $"{ordinal}."));
                continue;
            }

            MapObjectId expectedId = DeterministicMapIdentity.Object(
                document.MapIdentity,
                descriptor.SerializedType.ToString(),
                descriptor.AssetName,
                semanticRole,
                ordinal);
            if (model.Id != expectedId)
            {
                valid = false;
                issues.Add(new StaticModelCorrespondenceIssue(
                    StaticModelCorrespondenceIssueKind
                        .SemanticProjectionMismatch,
                    representation,
                    ordinal,
                    $"Semantic object {model.Id} does not have the exact " +
                    $"deterministic bundle identity {expectedId}."));
                continue;
            }

            destination[ordinal] = model;
        }

        if (destination.Any(value => value is null))
            valid = false;
        return valid;
    }

    private static RenderRow BuildRenderRow(
        int ordinal,
        GfxWorldBuildData gfx,
        EditorStaticModel semantic,
        ICollection<StaticModelCorrespondenceIssue> issues)
    {
        GfxStaticModelDrawInst draw =
            gfx.Definition.Dpvs.SModelDrawInsts[ordinal];
        GfxStaticModelInst instance =
            gfx.Definition.Dpvs.SModelInsts[ordinal];
        SymbolicXAssetReference? model =
            gfx.References.StaticModelDrawInsts[ordinal];
        var failures = new List<string>();

        if (!ValidModelReference(model))
        {
            failures.Add(
                "The Gfx row has no exact serialized XModel reference.");
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind.InvalidModelReference,
                StaticModelRepresentation.Render,
                ordinal,
                failures[^1]));
        }

        GfxPackedPlacement placement = draw.Placement;
        bool transformValid =
            placement.Origin.Count == 3 &&
            placement.Origin.All(float.IsFinite) &&
            placement.PackedAxis.Count == 3 &&
            float.IsFinite(placement.Scale) &&
            placement.Scale > 0 &&
            ValidBounds(instance.Bounds);
        MapVector3 origin = placement.Origin.Count == 3
            ? new MapVector3(
                placement.Origin[0],
                placement.Origin[1],
                placement.Origin[2])
            : default;
        MapBounds bounds = ToMapBounds(instance.Bounds);
        StaticModelMatrix3x3 scaledAxis =
            placement.PackedAxis.Count == 3 &&
            float.IsFinite(placement.Scale)
                ? DecodeScaledAxis(
                    placement.PackedAxis,
                    placement.Scale)
                : default;
        if (transformValid &&
            MathF.Abs(Determinant(scaledAxis)) <= 1e-8f)
        {
            transformValid = false;
        }
        if (!transformValid)
        {
            failures.Add(
                "The Gfx origin, positive scale, packed axis, or bounds are " +
                "malformed, non-finite, or singular.");
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind.InvalidTransform,
                StaticModelRepresentation.Render,
                ordinal,
                failures[^1]));
        }

        if (model is not null &&
            !SemanticProjectionMatches(
                semantic,
                model,
                origin,
                placement.Scale,
                bounds))
        {
            failures.Add(
                "The render semantic row is not an exact projection of its " +
                "imported Gfx row.");
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind
                    .SemanticProjectionMismatch,
                StaticModelRepresentation.Render,
                ordinal,
                failures[^1]));
        }

        return new RenderRow(
            ordinal,
            semantic,
            model,
            origin,
            bounds,
            placement.Scale,
            scaledAxis,
            failures.Count == 0,
            failures.Count == 0
                ? "The Gfx row is structurally valid."
                : string.Join(" ", failures));
    }

    private static CollisionRow BuildCollisionRow(
        int ordinal,
        ClipMapBuildData clip,
        EditorStaticModel semantic,
        ICollection<StaticModelCorrespondenceIssue> issues)
    {
        ClipStaticModel source =
            clip.Definition.StaticModelList[ordinal];
        SymbolicXAssetReference? model =
            clip.References.StaticModels[ordinal];
        var failures = new List<string>();

        if (!ValidModelReference(model))
        {
            failures.Add(
                "The Clip row has no exact serialized XModel reference.");
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind.InvalidModelReference,
                StaticModelRepresentation.Collision,
                ordinal,
                failures[^1]));
        }

        MapVector3 origin = ToMapVector(source.Origin);
        MapBounds bounds = new(
            ToMapVector(source.AbsMin),
            ToMapVector(source.AbsMax));
        bool transformValid =
            origin.IsFinite &&
            bounds.IsFinite &&
            ValidHalfSize(bounds.HalfSize) &&
            source.InvScaledAxis.Count == 3 &&
            source.InvScaledAxis.All(ValidVector);
        StaticModelMatrix3x3 inverseScaledAxis =
            source.InvScaledAxis.Count == 3
                ? ToMatrix(source.InvScaledAxis)
                : default;
        if (transformValid &&
            MathF.Abs(Determinant(inverseScaledAxis)) <= 1e-8f)
        {
            transformValid = false;
        }
        if (!transformValid)
        {
            failures.Add(
                "The Clip origin, inverse-scaled axis, or decoded " +
                "midpoint/half-size bounds are malformed, non-finite, or " +
                "singular.");
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind.InvalidTransform,
                StaticModelRepresentation.Collision,
                ordinal,
                failures[^1]));
        }

        if (model is not null &&
            !SemanticProjectionMatches(
                semantic,
                model,
                origin,
                scale: null,
                bounds))
        {
            failures.Add(
                "The collision semantic row is not an exact projection of " +
                "its imported Clip row.");
            issues.Add(new StaticModelCorrespondenceIssue(
                StaticModelCorrespondenceIssueKind
                    .SemanticProjectionMismatch,
                StaticModelRepresentation.Collision,
                ordinal,
                failures[^1]));
        }

        return new CollisionRow(
            ordinal,
            semantic,
            model,
            origin,
            bounds,
            inverseScaledAxis,
            failures.Count == 0,
            failures.Count == 0
                ? "The Clip row is structurally valid."
                : string.Join(" ", failures));
    }

    private static StaticModelCorrespondenceAssessment AssessRender(
        RenderRow row,
        IReadOnlyDictionary<int, PairEvaluation[]> evaluationsByRender,
        IReadOnlyDictionary<int, PairEvaluation[]> acceptedByRender,
        IReadOnlyDictionary<int, PairEvaluation[]> acceptedByCollision,
        IReadOnlySet<int> exactOrdinals,
        ICollection<StaticModelCorrespondenceIssue> issues)
    {
        if (!row.IsValid)
        {
            return Assessment(
                row,
                StaticModelCorrespondenceStatus.Invalid,
                [],
                row.InvalidEvidence);
        }

        PairEvaluation[] exactKey =
            evaluationsByRender.GetValueOrDefault(row.Ordinal) ?? [];
        PairEvaluation[] accepted =
            acceptedByRender.GetValueOrDefault(row.Ordinal) ?? [];
        int[] candidates = exactKey
            .Select(value => value.Collision.Ordinal)
            .ToArray();
        if (exactOrdinals.Contains(row.Ordinal))
        {
            return Assessment(
                row,
                StaticModelCorrespondenceStatus.ExactBundleUnique,
                candidates,
                "One Clip row is a mutual one-to-one match in this exact " +
                "imported bundle.");
        }
        if (accepted.Length > 1)
        {
            string evidence =
                $"{accepted.Length} Clip rows pass every correspondence gate.";
            issues.Add(Issue(
                StaticModelCorrespondenceIssueKind.AmbiguousForwardMatch,
                row,
                evidence));
            return Assessment(
                row,
                StaticModelCorrespondenceStatus.Ambiguous,
                candidates,
                evidence);
        }
        if (accepted.Length == 1 &&
            (acceptedByCollision.GetValueOrDefault(
                accepted[0].Collision.Ordinal)?.Length ?? 0) > 1)
        {
            string evidence =
                "The sole passing Clip row is also claimed by another " +
                "passing Gfx row.";
            issues.Add(Issue(
                StaticModelCorrespondenceIssueKind.AmbiguousReverseMatch,
                row,
                evidence));
            return Assessment(
                row,
                StaticModelCorrespondenceStatus.Ambiguous,
                candidates,
                evidence);
        }

        return AssessRejectedRender(row, exactKey, candidates, issues);
    }

    private static StaticModelCorrespondenceAssessment AssessCollision(
        CollisionRow row,
        IReadOnlyDictionary<int, PairEvaluation[]> evaluationsByCollision,
        IReadOnlyDictionary<int, PairEvaluation[]> acceptedByRender,
        IReadOnlyDictionary<int, PairEvaluation[]> acceptedByCollision,
        IReadOnlySet<int> exactOrdinals,
        ICollection<StaticModelCorrespondenceIssue> issues)
    {
        if (!row.IsValid)
        {
            return Assessment(
                row,
                StaticModelCorrespondenceStatus.Invalid,
                [],
                row.InvalidEvidence);
        }

        PairEvaluation[] exactKey =
            evaluationsByCollision.GetValueOrDefault(row.Ordinal) ?? [];
        PairEvaluation[] accepted =
            acceptedByCollision.GetValueOrDefault(row.Ordinal) ?? [];
        int[] candidates = exactKey
            .Select(value => value.Render.Ordinal)
            .ToArray();
        if (exactOrdinals.Contains(row.Ordinal))
        {
            return Assessment(
                row,
                StaticModelCorrespondenceStatus.ExactBundleUnique,
                candidates,
                "One Gfx row is a mutual one-to-one match in this exact " +
                "imported bundle.");
        }
        if (accepted.Length > 1)
        {
            string evidence =
                $"{accepted.Length} Gfx rows pass every correspondence gate.";
            issues.Add(Issue(
                StaticModelCorrespondenceIssueKind.AmbiguousReverseMatch,
                row,
                evidence));
            return Assessment(
                row,
                StaticModelCorrespondenceStatus.Ambiguous,
                candidates,
                evidence);
        }
        if (accepted.Length == 1 &&
            (acceptedByRender.GetValueOrDefault(
                accepted[0].Render.Ordinal)?.Length ?? 0) > 1)
        {
            string evidence =
                "The sole passing Gfx row also accepts another Clip row.";
            issues.Add(Issue(
                StaticModelCorrespondenceIssueKind.AmbiguousForwardMatch,
                row,
                evidence));
            return Assessment(
                row,
                StaticModelCorrespondenceStatus.Ambiguous,
                candidates,
                evidence);
        }

        return AssessRejectedCollision(row, exactKey, candidates, issues);
    }

    private static StaticModelCorrespondenceAssessment AssessRejectedRender(
        RenderRow row,
        PairEvaluation[] exactKey,
        int[] candidates,
        ICollection<StaticModelCorrespondenceIssue> issues)
    {
        if (exactKey.Length == 0)
        {
            string evidence =
                "No Clip row has both the exact serialized XModel reference " +
                "and exact IEEE-754 origin bits.";
            issues.Add(Issue(
                StaticModelCorrespondenceIssueKind
                    .NoExactModelOriginCandidate,
                row,
                evidence));
            return Assessment(
                row,
                StaticModelCorrespondenceStatus.Unmatched,
                candidates,
                evidence);
        }

        string rejection = AddShapeMismatchIssues(row, exactKey, issues);
        return Assessment(
            row,
            StaticModelCorrespondenceStatus.Inconsistent,
            candidates,
            rejection);
    }

    private static StaticModelCorrespondenceAssessment
        AssessRejectedCollision(
            CollisionRow row,
            PairEvaluation[] exactKey,
            int[] candidates,
            ICollection<StaticModelCorrespondenceIssue> issues)
    {
        if (exactKey.Length == 0)
        {
            string evidence =
                "No Gfx row has both the exact serialized XModel reference " +
                "and exact IEEE-754 origin bits.";
            issues.Add(Issue(
                StaticModelCorrespondenceIssueKind
                    .NoExactModelOriginCandidate,
                row,
                evidence));
            return Assessment(
                row,
                StaticModelCorrespondenceStatus.Unmatched,
                candidates,
                evidence);
        }

        string rejection = AddShapeMismatchIssues(row, exactKey, issues);
        return Assessment(
            row,
            StaticModelCorrespondenceStatus.Inconsistent,
            candidates,
            rejection);
    }

    private static string AddShapeMismatchIssues(
        StaticModelRow row,
        IEnumerable<PairEvaluation> evaluations,
        ICollection<StaticModelCorrespondenceIssue> issues)
    {
        PairEvaluation[] copy = evaluations.ToArray();
        var reasons = new List<string>();
        if (copy.Any(value => !value.BoundsMatch))
        {
            float maximum = copy.Max(
                value => value.Bounds.MaximumAbsoluteDelta);
            string evidence =
                $"Exact model/origin candidates exceed the bounds tolerance " +
                $"{BoundsAbsoluteTolerance:R}; maximum observed delta is " +
                $"{maximum:R}.";
            reasons.Add(evidence);
            issues.Add(Issue(
                StaticModelCorrespondenceIssueKind.BoundsMismatch,
                row,
                evidence));
        }
        if (copy.Any(value => !value.AxisScaleMatch))
        {
            float maximum = copy.Max(
                value => value.AxisScale.MaximumIdentityResidual);
            string evidence =
                "Exact model/origin candidates fail scaled-axis/inverse-axis " +
                $"consistency; maximum identity residual is {maximum:R} " +
                $"with limit {AxisIdentityTolerance:R}.";
            reasons.Add(evidence);
            issues.Add(Issue(
                StaticModelCorrespondenceIssueKind.AxisScaleMismatch,
                row,
                evidence));
        }

        return reasons.Count == 0
            ? "No exact model/origin candidate passed every relationship gate."
            : string.Join(" ", reasons);
    }

    private static StaticModelCorrespondenceAssessment Assessment(
        StaticModelRow row,
        StaticModelCorrespondenceStatus status,
        IEnumerable<int> candidates,
        string evidence) =>
        new(
            row.Semantic.Id,
            row.Representation,
            row.Ordinal,
            status,
            candidates,
            evidence);

    private static StaticModelCorrespondenceIssue Issue(
        StaticModelCorrespondenceIssueKind kind,
        StaticModelRow row,
        string evidence) =>
        new(
            kind,
            row.Representation,
            row.Ordinal,
            evidence);

    private static StaticModelCorrespondenceCatalog InvalidCatalog(
        CompiledMapBundle bundle,
        EditorMapDocument document,
        MapAssetKind? collisionAssetKind,
        IEnumerable<StaticModelCorrespondenceIssue> issues)
    {
        StaticModelCorrespondenceIssue[] issueCopy = issues.ToArray();
        string evidence = issueCopy.Length == 0
            ? "The exact-bundle static-model authority is invalid."
            : "The exact-bundle static-model authority is invalid: " +
              string.Join(
                  " ",
                  issueCopy.Select(value => value.Evidence));
        StaticModelCorrespondenceAssessment[] assessments =
            document.StaticModels
                .Where(value => value.IsImported)
                .Select(value =>
                new StaticModelCorrespondenceAssessment(
                    value.Id,
                    value.Representation,
                    value.SourceOrdinal.Value,
                    StaticModelCorrespondenceStatus.Invalid,
                    [],
                    evidence))
                .ToArray();
        return new StaticModelCorrespondenceCatalog(
            document.Id,
            bundle.MapIdentity,
            bundle.BaselineDigest,
            collisionAssetKind,
            authoritiesValid: false,
            relationships: [],
            assessments,
            issueCopy);
    }

    private static bool SemanticProjectionMatches(
        EditorStaticModel semantic,
        SymbolicXAssetReference model,
        MapVector3 origin,
        float? scale,
        MapBounds bounds)
    {
        string expectedModel =
            XAssetStableIdentity.GetLookupSpelling(
                model.OriginalSerializedName);
        EditorStaticModelTransformState imported =
            semantic.ImportedTransform;
        return string.Equals(
                   semantic.ModelName.Value,
                   expectedModel,
                   StringComparison.Ordinal) &&
               VectorBitsEqual(imported.Origin, origin) &&
               NullableFloatBitsEqual(imported.Scale, scale) &&
               imported.Bounds is { } importedBounds &&
               BoundsBitsEqual(importedBounds, bounds);
    }

    private static bool ValidModelReference(
        SymbolicXAssetReference? value) =>
        value is
        {
            AssetType: XAssetType.XModel,
            OriginalSerializedName.Length: > 0
        };

    private static bool ValidBounds(Bounds value) =>
        ValidVector(value.MidPoint) &&
        ValidVector(value.HalfSize) &&
        value.HalfSize.X >= 0 &&
        value.HalfSize.Y >= 0 &&
        value.HalfSize.Z >= 0;

    private static bool ValidHalfSize(MapVector3 value) =>
        value.IsFinite &&
        value.X >= 0 &&
        value.Y >= 0 &&
        value.Z >= 0;

    private static bool ValidVector(Vec3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static StaticModelBoundsCorrespondenceEvidence CompareBounds(
        MapBounds gfx,
        MapBounds clip)
    {
        float maximum = new[]
        {
            Difference(gfx.MidPoint.X, clip.MidPoint.X),
            Difference(gfx.MidPoint.Y, clip.MidPoint.Y),
            Difference(gfx.MidPoint.Z, clip.MidPoint.Z),
            Difference(gfx.HalfSize.X, clip.HalfSize.X),
            Difference(gfx.HalfSize.Y, clip.HalfSize.Y),
            Difference(gfx.HalfSize.Z, clip.HalfSize.Z)
        }.Max();
        return new StaticModelBoundsCorrespondenceEvidence(
            gfx,
            clip,
            maximum,
            BoundsAbsoluteTolerance);
    }

    private static StaticModelAxisScaleCorrespondenceEvidence
        CompareAxisScale(
            float scale,
            StaticModelMatrix3x3 scaledAxis,
            StaticModelMatrix3x3 inverseScaledAxis)
    {
        StaticModelMatrix3x3 forwardProduct =
            Multiply(scaledAxis, inverseScaledAxis);
        StaticModelMatrix3x3 reverseProduct =
            Multiply(inverseScaledAxis, scaledAxis);
        float residual = MathF.Max(
            IdentityResidual(forwardProduct),
            IdentityResidual(reverseProduct));
        return new StaticModelAxisScaleCorrespondenceEvidence(
            scale,
            scaledAxis,
            inverseScaledAxis,
            residual,
            AxisIdentityTolerance);
    }

    private static StaticModelMatrix3x3 DecodeScaledAxis(
        IReadOnlyList<uint> packedAxis,
        float scale) =>
        new(
            Scale(DecodePackedAxis(packedAxis[0]), scale),
            Scale(DecodePackedAxis(packedAxis[1]), scale),
            Scale(DecodePackedAxis(packedAxis[2]), scale));

    private static MapVector3 DecodePackedAxis(uint packed) =>
        new(
            SignExtend((int)(packed & 0x7ff), 11) / 1023f,
            SignExtend((int)((packed >> 11) & 0x7ff), 11) / 1023f,
            SignExtend((int)((packed >> 22) & 0x3ff), 10) / 511f);

    private static int SignExtend(int value, int bits)
    {
        int sign = 1 << (bits - 1);
        return (value ^ sign) - sign;
    }

    private static StaticModelMatrix3x3 ToMatrix(
        IReadOnlyList<Vec3> rows) =>
        new(
            ToMapVector(rows[0]),
            ToMapVector(rows[1]),
            ToMapVector(rows[2]));

    private static StaticModelMatrix3x3 Multiply(
        StaticModelMatrix3x3 left,
        StaticModelMatrix3x3 right) =>
        new(
            new MapVector3(
                DotRowColumn(left.Row0, right, 0),
                DotRowColumn(left.Row0, right, 1),
                DotRowColumn(left.Row0, right, 2)),
            new MapVector3(
                DotRowColumn(left.Row1, right, 0),
                DotRowColumn(left.Row1, right, 1),
                DotRowColumn(left.Row1, right, 2)),
            new MapVector3(
                DotRowColumn(left.Row2, right, 0),
                DotRowColumn(left.Row2, right, 1),
                DotRowColumn(left.Row2, right, 2)));

    private static float DotRowColumn(
        MapVector3 row,
        StaticModelMatrix3x3 right,
        int column)
    {
        float x = Component(right.Row0, column);
        float y = Component(right.Row1, column);
        float z = Component(right.Row2, column);
        return row.X * x + row.Y * y + row.Z * z;
    }

    private static float Component(MapVector3 value, int column) =>
        column switch
        {
            0 => value.X,
            1 => value.Y,
            2 => value.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(column))
        };

    private static float IdentityResidual(StaticModelMatrix3x3 value) =>
        new[]
        {
            Difference(value.Row0.X, 1),
            Difference(value.Row0.Y, 0),
            Difference(value.Row0.Z, 0),
            Difference(value.Row1.X, 0),
            Difference(value.Row1.Y, 1),
            Difference(value.Row1.Z, 0),
            Difference(value.Row2.X, 0),
            Difference(value.Row2.Y, 0),
            Difference(value.Row2.Z, 1)
        }.Max();

    private static float Determinant(StaticModelMatrix3x3 value) =>
        value.Row0.X *
        (value.Row1.Y * value.Row2.Z - value.Row1.Z * value.Row2.Y) -
        value.Row0.Y *
        (value.Row1.X * value.Row2.Z - value.Row1.Z * value.Row2.X) +
        value.Row0.Z *
        (value.Row1.X * value.Row2.Y - value.Row1.Y * value.Row2.X);

    private static MapVector3 Scale(MapVector3 value, float scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    private static float Difference(float left, float right) =>
        MathF.Abs(left - right);

    private static StaticModelExactMatchKey MatchKey(StaticModelRow row)
    {
        SymbolicXAssetReference model = row.ModelReference ??
            throw new InvalidOperationException(
                "Only structurally valid static-model rows may be indexed.");
        return StaticModelExactMatchKey.Create(
            model.AssetType,
            model.OriginalSerializedName,
            row.Origin);
    }

    private static bool VectorBitsEqual(
        MapVector3 left,
        MapVector3 right) =>
        FloatBitsEqual(left.X, right.X) &&
        FloatBitsEqual(left.Y, right.Y) &&
        FloatBitsEqual(left.Z, right.Z);

    private static bool BoundsBitsEqual(MapBounds left, MapBounds right) =>
        VectorBitsEqual(left.MidPoint, right.MidPoint) &&
        VectorBitsEqual(left.HalfSize, right.HalfSize);

    private static bool NullableFloatBitsEqual(
        float? left,
        float? right) =>
        left.HasValue == right.HasValue &&
        (!left.HasValue || FloatBitsEqual(left.Value, right!.Value));

    private static bool FloatBitsEqual(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);

    private static StaticModelExactOriginEvidence ExactOriginEvidence(
        MapVector3 value) =>
        new(
            value,
            BitConverter.SingleToInt32Bits(value.X),
            BitConverter.SingleToInt32Bits(value.Y),
            BitConverter.SingleToInt32Bits(value.Z));

    private static MapVector3 ToMapVector(Vec3 value) =>
        new(value.X, value.Y, value.Z);

    private static MapBounds ToMapBounds(Bounds value) =>
        new(ToMapVector(value.MidPoint), ToMapVector(value.HalfSize));

    private abstract record StaticModelRow(
        int Ordinal,
        EditorStaticModel Semantic,
        SymbolicXAssetReference? ModelReference,
        MapVector3 Origin,
        MapBounds Bounds,
        bool IsValid,
        string InvalidEvidence)
    {
        public abstract StaticModelRepresentation Representation { get; }
    }

    private sealed record RenderRow(
        int Ordinal,
        EditorStaticModel Semantic,
        SymbolicXAssetReference? ModelReference,
        MapVector3 Origin,
        MapBounds Bounds,
        float Scale,
        StaticModelMatrix3x3 ScaledAxis,
        bool IsValid,
        string InvalidEvidence)
        : StaticModelRow(
            Ordinal,
            Semantic,
            ModelReference,
            Origin,
            Bounds,
            IsValid,
            InvalidEvidence)
    {
        public override StaticModelRepresentation Representation =>
            StaticModelRepresentation.Render;
    }

    private sealed record CollisionRow(
        int Ordinal,
        EditorStaticModel Semantic,
        SymbolicXAssetReference? ModelReference,
        MapVector3 Origin,
        MapBounds Bounds,
        StaticModelMatrix3x3 InverseScaledAxis,
        bool IsValid,
        string InvalidEvidence)
        : StaticModelRow(
            Ordinal,
            Semantic,
            ModelReference,
            Origin,
            Bounds,
            IsValid,
            InvalidEvidence)
    {
        public override StaticModelRepresentation Representation =>
            StaticModelRepresentation.Collision;
    }

    private sealed record PairEvaluation(
        RenderRow Render,
        CollisionRow Collision,
        StaticModelBoundsCorrespondenceEvidence Bounds,
        StaticModelAxisScaleCorrespondenceEvidence AxisScale,
        bool BoundsMatch,
        bool AxisScaleMatch)
    {
        public bool IsAccepted => BoundsMatch && AxisScaleMatch;
    }

}
