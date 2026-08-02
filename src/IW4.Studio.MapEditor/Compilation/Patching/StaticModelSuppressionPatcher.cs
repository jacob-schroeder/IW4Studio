using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Compilation.StaticModels;
using IW4.Studio.MapEditor.Compilation.Validation;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.Patching;

public sealed class StaticModelSuppressionPatch
{
    private readonly IReadOnlyList<SourceBindingId> _sourceBindings;

    internal StaticModelSuppressionPatch(
        StaticModelCompilationRelationship relationship,
        IEnumerable<SourceBindingId> sourceBindings,
        float tombstoneZ)
    {
        Relationship = relationship ??
            throw new ArgumentNullException(nameof(relationship));
        ArgumentNullException.ThrowIfNull(sourceBindings);
        SourceBindingId[] bindings = sourceBindings
            .Distinct()
            .ToArray();
        if (bindings.Length == 0 ||
            bindings.Any(value => value.Value == Guid.Empty))
        {
            throw new ArgumentException(
                "A suppression patch requires exact compiled-field bindings.",
                nameof(sourceBindings));
        }
        if (!float.IsFinite(tombstoneZ))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tombstoneZ));
        }

        _sourceBindings =
            new ReadOnlyCollection<SourceBindingId>(bindings);
        TombstoneZ = tombstoneZ;
    }

    public StaticModelCompilationRelationship Relationship { get; }
    public MapObjectId RenderObjectId => Relationship.RenderObjectId;
    public MapObjectId CollisionObjectId =>
        Relationship.CollisionObjectId;
    public int GfxSourceOrdinal =>
        Relationship.GfxSourceOrdinal;
    public int ClipSourceOrdinal =>
        Relationship.ClipSourceOrdinal;
    public MapAssetKind CollisionAssetKind =>
        Relationship.CollisionAssetKind;
    public IReadOnlyList<SourceBindingId> SourceBindings =>
        _sourceBindings;
    public float TombstoneZ { get; }
}

internal sealed class StaticModelSuppressionPatchCandidate
{
    public StaticModelSuppressionPatchCandidate(
        CompiledMapAssetDescriptor? gfxDescriptor,
        CompiledMapAssetDescriptor? clipDescriptor,
        GfxWorldBuildData? gfxBaseline,
        ClipMapBuildData? clipBaseline,
        GfxWorldBuildData? gfxBuildData,
        ClipMapBuildData? clipBuildData,
        IEnumerable<StaticModelSuppressionPatch> patches,
        string? gfxBaselineSemanticDigest,
        string? clipBaselineSemanticDigest,
        MapPatchValidation validation)
    {
        ArgumentNullException.ThrowIfNull(patches);
        ArgumentNullException.ThrowIfNull(validation);

        GfxDescriptor = gfxDescriptor;
        ClipDescriptor = clipDescriptor;
        GfxBaseline = gfxBaseline;
        ClipBaseline = clipBaseline;
        GfxBuildData = gfxBuildData;
        ClipBuildData = clipBuildData;
        Patches = new ReadOnlyCollection<StaticModelSuppressionPatch>(
            patches.ToArray());
        GfxBaselineSemanticDigest = gfxBaselineSemanticDigest;
        ClipBaselineSemanticDigest = clipBaselineSemanticDigest;
        Validation = validation;
    }

    public CompiledMapAssetDescriptor? GfxDescriptor { get; }
    public CompiledMapAssetDescriptor? ClipDescriptor { get; }
    public GfxWorldBuildData? GfxBaseline { get; }
    public ClipMapBuildData? ClipBaseline { get; }
    public GfxWorldBuildData? GfxBuildData { get; }
    public ClipMapBuildData? ClipBuildData { get; }
    public IReadOnlyList<StaticModelSuppressionPatch> Patches { get; }
    public string? GfxBaselineSemanticDigest { get; }
    public string? ClipBaselineSemanticDigest { get; }
    public MapPatchValidation Validation { get; }
}

/// <summary>
/// Produces the narrow conservative tombstone candidate already supported by
/// the detached Gfx/Col authoring models. It requires a mutual exact-bundle
/// relationship and always stages both asset owners.
/// </summary>
internal sealed class StaticModelSuppressionPatcher
{
    public const float TombstoneZ = -65536f;

    private static readonly GfxWorldBodyEmitter GfxEmitter = new();

    public static MapPreservationCoverage GfxPreservationCoverage { get; } =
        new(
            MapAssetKind.GfxMap,
            "Existing uniquely paired static-model suppression",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "GfxWorld row identity, root scalars, counts and checksums",
                "Every unselected Gfx static-model draw/instance row",
                "Selected placement X/Y, packed axis, and scale",
                "Selected model link and nested definition provenance",
                "Selected lighting handle, probe/light indices, material skin, and ground lighting",
                "Selected instance bounds half-size",
                "All Gfx cell/AABB/DPVS membership and visibility tables",
                "All shadow-geometry static-model index lists",
                "All dependencies and imported pointer source forms"
            ],
            mutableFields:
            [
                "$.definition.dpvs.sModelDrawInsts[i].placement.origin.z",
                "$.definition.dpvs.sModelDrawInsts[i].cullDist",
                "$.definition.dpvs.sModelDrawInsts[i].flags bit 0",
                "$.definition.dpvs.sModelInsts[i].bounds midpoint.z",
                "$.definition.dpvs.sModelInsts[i].lightingOrigin.z"
            ]);

    public static MapPreservationCoverage CollisionPreservationCoverage(
        MapAssetKind collisionAssetKind)
    {
        if (collisionAssetKind is not (
                MapAssetKind.ColMapMp or
                MapAssetKind.ColMapSp))
        {
            throw new ArgumentOutOfRangeException(
                nameof(collisionAssetKind));
        }

        return new MapPreservationCoverage(
            collisionAssetKind,
            "Existing uniquely paired static-model suppression",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "ColMap row identity, serialized Sp/Mp type, root scalars and counts",
                "Every unselected ClipStaticModel row",
                "Selected XModel link and pointer source form",
                "Selected inverse-scaled axis and bounds half-size",
                "All SModelAabbNode rows and child/index topology",
                "All collision geometry, dynamic entities, stages, MapEnts and dependencies"
            ],
            mutableFields:
            [
                "$.definition.staticModelList[i].origin.z",
                "$.definition.staticModelList[i].absMin.z (decoded bounds midpoint)"
            ]);
    }

    public StaticModelSuppressionPatchCandidate Prepare(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        IEnumerable<CompiledSourceBinding> sourceBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(sourceBindings);
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<string>();
        if (!bundle.TryGetBaseline(
                MapAssetKind.GfxMap,
                out GfxWorldBuildData? gfxBaseline) ||
            gfxBaseline is null)
        {
            diagnostics.Add(
                "The compiled map bundle has no detached GfxMap baseline.");
            return InvalidCandidate(diagnostics);
        }
        if (!TryGetCollisionBaseline(
                bundle,
                out MapAssetKind collisionKind,
                out ClipMapBuildData? clipBaseline) ||
            clipBaseline is null)
        {
            diagnostics.Add(
                "The compiled map bundle must have exactly one detached " +
                "ColMapMp or ColMapSp baseline.");
            return InvalidCandidate(diagnostics);
        }

        CompiledMapAssetDescriptor gfxDescriptor =
            bundle.RequireAsset(MapAssetKind.GfxMap);
        CompiledMapAssetDescriptor clipDescriptor =
            bundle.RequireAsset(collisionKind);
        StaticModelCorrespondenceCatalog relationships =
            StaticModelCompilationRelationshipResolver.Resolve(
                bundle,
                document,
                cancellationToken);
        if (!relationships.AuthoritiesValid)
        {
            diagnostics.Add(
                "Static-model correspondence authorities are invalid: " +
                string.Join(
                    "; ",
                    relationships.Issues.Select(value => value.Evidence)));
        }

        Dictionary<SourceBindingId, CompiledSourceBinding> bindingCatalog =
            BuildBindingCatalog(sourceBindings, diagnostics);
        EditorStaticModel[] suppressed = document.StaticModels
            .Where(value =>
                value.IsImported &&
                value.CompiledDisposition ==
                StaticModelCompiledDisposition.Suppressed)
            .ToArray();
        var patches = new List<StaticModelSuppressionPatch>();
        var consumed = new HashSet<MapObjectId>();
        foreach (EditorStaticModel render in suppressed
                     .Where(value =>
                         value.Representation ==
                         StaticModelRepresentation.Render)
                     .OrderBy(value => value.SourceOrdinal.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!relationships.TryGetByRenderObjectId(
                    render.Id,
                    out StaticModelCompilationRelationship? relationship) ||
                relationship is null)
            {
                diagnostics.Add(
                    $"Suppressed render static model {render.Id} has no " +
                    "mutual exact-bundle collision relationship.");
                continue;
            }
            EditorStaticModel? collision = document.StaticModels
                .SingleOrDefault(value =>
                    value.IsImported &&
                    value.Id == relationship.CollisionObjectId);
            if (collision is null ||
                collision.CompiledDisposition !=
                    StaticModelCompiledDisposition.Suppressed)
            {
                diagnostics.Add(
                    $"Suppressed render static model {render.Id} does not " +
                    "have its exact collision counterpart suppressed.");
                continue;
            }
            if (relationship.CollisionAssetKind != collisionKind)
            {
                diagnostics.Add(
                    $"Static-model relationship {render.Id} targets " +
                    $"{relationship.CollisionAssetKind}, not the owned " +
                    $"{collisionKind} baseline.");
                continue;
            }
            if (!render.HasTransform(render.ImportedTransform) ||
                !collision.HasTransform(collision.ImportedTransform))
            {
                diagnostics.Add(
                    $"Static-model pair {render.Id} includes an authored " +
                    "transform; suppression cannot persist that transform.");
                continue;
            }

            SourceBindingId[] mutableBindings =
                GetMutableBindings(render, collision, diagnostics);
            ValidateBindings(
                bundle,
                gfxDescriptor,
                clipDescriptor,
                relationship,
                mutableBindings,
                bindingCatalog,
                diagnostics);
            if (!consumed.Add(render.Id) ||
                !consumed.Add(collision.Id))
            {
                diagnostics.Add(
                    $"Static-model relationship {render.Id} is not " +
                    "one-to-one in the suppressed semantic state.");
                continue;
            }
            patches.Add(new StaticModelSuppressionPatch(
                relationship,
                mutableBindings,
                TombstoneZ));
        }

        foreach (EditorStaticModel orphan in suppressed.Where(value =>
                     !consumed.Contains(value.Id)))
        {
            diagnostics.Add(
                $"Suppressed {orphan.Representation.ToString().ToLowerInvariant()} " +
                $"static model {orphan.Id} is not part of an authorized " +
                "atomic pair.");
        }

        GfxWorldBuildData gfxCandidate = gfxBaseline;
        ClipMapBuildData clipCandidate = clipBaseline;
        string gfxBaselineDigest =
            RelocationInvariantAssetSemanticDigest.Compute(
                gfxBaseline,
                cancellationToken);
        string clipBaselineDigest =
            RelocationInvariantAssetSemanticDigest.Compute(
                clipBaseline,
                cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        gfxCandidate = gfxCandidate.WithSuppressedStaticModels(
            patches.Select(value => value.GfxSourceOrdinal),
            TombstoneZ);
        clipCandidate = clipCandidate.WithSuppressedStaticModels(
            patches.Select(value => value.ClipSourceOrdinal),
            TombstoneZ);

        diagnostics.AddRange(
            ValidatePreservation(
                    gfxBaseline,
                    clipBaseline,
                    gfxCandidate,
                    clipCandidate,
                    patches,
                    cancellationToken)
                .Diagnostics);
        if (!string.Equals(
                gfxBaselineDigest,
                RelocationInvariantAssetSemanticDigest.Compute(
                    gfxBaseline,
                    cancellationToken),
                StringComparison.Ordinal) ||
            !string.Equals(
                clipBaselineDigest,
                RelocationInvariantAssetSemanticDigest.Compute(
                    clipBaseline,
                    cancellationToken),
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                "Preparing static-model suppression mutated an immutable " +
                "compiled baseline.");
        }

        return new StaticModelSuppressionPatchCandidate(
            gfxDescriptor,
            clipDescriptor,
            gfxBaseline,
            clipBaseline,
            gfxCandidate,
            clipCandidate,
            patches,
            gfxBaselineDigest,
            clipBaselineDigest,
            new MapPatchValidation(diagnostics));
    }

    public MapPatchValidation ValidatePreservation(
        GfxWorldBuildData gfxBaseline,
        ClipMapBuildData clipBaseline,
        GfxWorldBuildData gfxCandidate,
        ClipMapBuildData clipCandidate,
        IEnumerable<StaticModelSuppressionPatch> patches,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gfxBaseline);
        ArgumentNullException.ThrowIfNull(clipBaseline);
        ArgumentNullException.ThrowIfNull(gfxCandidate);
        ArgumentNullException.ThrowIfNull(clipCandidate);
        ArgumentNullException.ThrowIfNull(patches);

        var diagnostics = new List<string>();
        StaticModelSuppressionPatch[] patchCopy = patches.ToArray();
        if (patchCopy.Select(value => value.GfxSourceOrdinal)
                .Distinct().Count() != patchCopy.Length ||
            patchCopy.Select(value => value.ClipSourceOrdinal)
                .Distinct().Count() != patchCopy.Length)
        {
            diagnostics.Add(
                "Static-model suppression patches are not one-to-one.");
        }
        if (patchCopy.Any(value =>
                !SameBits(value.TombstoneZ, TombstoneZ)))
        {
            diagnostics.Add(
                "Static-model suppression patches must use the canonical " +
                "compiled tombstone coordinate.");
        }
        if (gfxBaseline.Definition.Dpvs.SModelCount !=
                gfxCandidate.Definition.Dpvs.SModelCount ||
            gfxBaseline.Definition.Dpvs.SModelDrawInsts.Count !=
                gfxCandidate.Definition.Dpvs.SModelDrawInsts.Count ||
            gfxBaseline.Definition.Dpvs.SModelInsts.Count !=
                gfxCandidate.Definition.Dpvs.SModelInsts.Count)
        {
            diagnostics.Add(
                "Gfx static-model count or parallel table cardinality changed.");
        }
        if (clipBaseline.SerializedType != clipCandidate.SerializedType ||
            clipBaseline.Definition.NumStaticModels !=
                clipCandidate.Definition.NumStaticModels ||
            clipBaseline.Definition.StaticModelList.Count !=
                clipCandidate.Definition.StaticModelList.Count ||
            clipBaseline.Definition.SModelNodeCount !=
                clipCandidate.Definition.SModelNodeCount ||
            clipBaseline.Definition.SModelNodes.Count !=
                clipCandidate.Definition.SModelNodes.Count)
        {
            diagnostics.Add(
                "Collision static-model or spatial-node topology changed.");
        }

        HashSet<int> gfxPatched = patchCopy
            .Select(value => value.GfxSourceOrdinal)
            .ToHashSet();
        HashSet<int> clipPatched = patchCopy
            .Select(value => value.ClipSourceOrdinal)
            .ToHashSet();
        ValidateGfxRows(
            gfxBaseline,
            gfxCandidate,
            patchCopy,
            gfxPatched,
            diagnostics);
        ValidateClipRows(
            clipBaseline,
            clipCandidate,
            patchCopy,
            clipPatched,
            diagnostics);

        GfxWorldBuildData expectedGfx = gfxBaseline;
        ClipMapBuildData expectedClip = clipBaseline;
        cancellationToken.ThrowIfCancellationRequested();
        expectedGfx = expectedGfx.WithSuppressedStaticModels(
            patchCopy.Select(value => value.GfxSourceOrdinal),
            TombstoneZ);
        expectedClip = expectedClip.WithSuppressedStaticModels(
            patchCopy.Select(value => value.ClipSourceOrdinal),
            TombstoneZ);
        if (!SameSemantic(
                expectedGfx,
                gfxCandidate,
                cancellationToken))
        {
            diagnostics.Add(
                "Gfx candidate differs outside the canonical conservative " +
                "suppression transformation.");
        }
        if (!SameSemantic(
                expectedClip,
                clipCandidate,
                cancellationToken))
        {
            diagnostics.Add(
                "ColMap candidate differs outside the canonical conservative " +
                "suppression transformation.");
        }

        diagnostics.AddRange(
            GfxEmitter.Validate(gfxCandidate)
                .Select(value =>
                    $"GfxMap emitter validation failed at {value.Path}: " +
                    value.Message));
        var clipEmitter =
            new ClipMapBodyEmitter(clipCandidate.SerializedType);
        diagnostics.AddRange(
            clipEmitter.Validate(clipCandidate)
                .Select(value =>
                    $"ColMap emitter validation failed at {value.Path}: " +
                    value.Message));
        return new MapPatchValidation(diagnostics);
    }

    public void ApplyValidatedGfxCandidate(
        GfxWorldDraft draft,
        StaticModelSuppressionPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireValidCandidate(candidate);
        if (!SameDigest(
                draft.Data,
                candidate.GfxBaselineSemanticDigest!))
        {
            throw new InvalidOperationException(
                "The staged GfxMap draft no longer matches the exact " +
                "imported suppression baseline.");
        }
        draft.Replace(candidate.GfxBuildData!);
    }

    public void ApplyValidatedCollisionCandidate(
        ClipMapDraft draft,
        StaticModelSuppressionPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireValidCandidate(candidate);
        if (!SameDigest(
                draft.Data,
                candidate.ClipBaselineSemanticDigest!))
        {
            throw new InvalidOperationException(
                "The staged ColMap draft no longer matches the exact " +
                "imported suppression baseline.");
        }
        draft.Replace(candidate.ClipBuildData!);
    }

    private static void RequireValidCandidate(
        StaticModelSuppressionPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!GfxPreservationCoverage.IsProven ||
            candidate.ClipDescriptor is null ||
            !CollisionPreservationCoverage(
                candidate.ClipDescriptor.Kind).IsProven ||
            !candidate.Validation.IsValid ||
            candidate.Patches.Count == 0 ||
            candidate.GfxBuildData is null ||
            candidate.ClipBuildData is null)
        {
            throw new InvalidOperationException(
                "An invalid, empty, or coverage-incomplete static-model " +
                "suppression candidate cannot replace staged drafts.");
        }
    }

    private static void ValidateGfxRows(
        GfxWorldBuildData baseline,
        GfxWorldBuildData candidate,
        IReadOnlyList<StaticModelSuppressionPatch> patches,
        IReadOnlySet<int> patched,
        ICollection<string> diagnostics)
    {
        IReadOnlyList<GfxStaticModelDrawInst> sourceDraws =
            baseline.Definition.Dpvs.SModelDrawInsts;
        IReadOnlyList<GfxStaticModelDrawInst> editedDraws =
            candidate.Definition.Dpvs.SModelDrawInsts;
        IReadOnlyList<GfxStaticModelInst> sourceInstances =
            baseline.Definition.Dpvs.SModelInsts;
        IReadOnlyList<GfxStaticModelInst> editedInstances =
            candidate.Definition.Dpvs.SModelInsts;
        int count = Math.Min(
            Math.Min(sourceDraws.Count, editedDraws.Count),
            Math.Min(sourceInstances.Count, editedInstances.Count));
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            if (!patched.Contains(ordinal))
            {
                if (!SameJson(sourceDraws[ordinal], editedDraws[ordinal]) ||
                    !SameJson(
                        sourceInstances[ordinal],
                        editedInstances[ordinal]))
                {
                    diagnostics.Add(
                        $"Unselected Gfx static-model row {ordinal} changed.");
                }
                continue;
            }

            StaticModelSuppressionPatch patch = patches.Single(value =>
                value.GfxSourceOrdinal == ordinal);
            GfxStaticModelDrawInst sourceDraw = sourceDraws[ordinal];
            GfxStaticModelDrawInst editedDraw = editedDraws[ordinal];
            GfxStaticModelInst sourceInstance =
                sourceInstances[ordinal];
            GfxStaticModelInst editedInstance =
                editedInstances[ordinal];
            float deltaZ =
                patch.TombstoneZ - sourceDraw.Placement.Origin[2];
            if (!SameBits(
                    editedDraw.Placement.Origin[0],
                    sourceDraw.Placement.Origin[0]) ||
                !SameBits(
                    editedDraw.Placement.Origin[1],
                    sourceDraw.Placement.Origin[1]) ||
                !SameBits(
                    editedDraw.Placement.Origin[2],
                    patch.TombstoneZ) ||
                editedDraw.CullDist != 1 ||
                editedDraw.Flags != (byte)(sourceDraw.Flags | 1) ||
                !sourceDraw.Placement.PackedAxis.SequenceEqual(
                    editedDraw.Placement.PackedAxis) ||
                !SameBits(
                    sourceDraw.Placement.Scale,
                    editedDraw.Placement.Scale) ||
                !SameVec(
                    editedInstance.Bounds.MidPoint,
                    sourceInstance.Bounds.MidPoint,
                    deltaZ) ||
                !SameVec(
                    sourceInstance.Bounds.HalfSize,
                    editedInstance.Bounds.HalfSize) ||
                !SameVec(
                    editedInstance.LightingOrigin,
                    sourceInstance.LightingOrigin,
                    deltaZ))
            {
                diagnostics.Add(
                    $"Gfx static-model row {ordinal} does not match its " +
                    "authorized tombstone fields.");
            }
        }
    }

    private static void ValidateClipRows(
        ClipMapBuildData baseline,
        ClipMapBuildData candidate,
        IReadOnlyList<StaticModelSuppressionPatch> patches,
        IReadOnlySet<int> patched,
        ICollection<string> diagnostics)
    {
        IReadOnlyList<ClipStaticModel> sourceRows =
            baseline.Definition.StaticModelList;
        IReadOnlyList<ClipStaticModel> editedRows =
            candidate.Definition.StaticModelList;
        int count = Math.Min(sourceRows.Count, editedRows.Count);
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            if (!patched.Contains(ordinal))
            {
                if (!SameJson(sourceRows[ordinal], editedRows[ordinal]))
                {
                    diagnostics.Add(
                        $"Unselected collision static-model row {ordinal} " +
                        "changed.");
                }
                continue;
            }

            StaticModelSuppressionPatch patch = patches.Single(value =>
                value.ClipSourceOrdinal == ordinal);
            ClipStaticModel source = sourceRows[ordinal];
            ClipStaticModel edited = editedRows[ordinal];
            float deltaZ = patch.TombstoneZ - source.Origin.Z;
            if (!SameBits(edited.Origin.X, source.Origin.X) ||
                !SameBits(edited.Origin.Y, source.Origin.Y) ||
                !SameBits(edited.Origin.Z, patch.TombstoneZ) ||
                !SameVec(edited.AbsMin, source.AbsMin, deltaZ) ||
                !SameVec(source.AbsMax, edited.AbsMax) ||
                source.InvScaledAxis.Count !=
                    edited.InvScaledAxis.Count ||
                !source.InvScaledAxis.Zip(edited.InvScaledAxis)
                    .All(value => SameVec(value.First, value.Second)))
            {
                diagnostics.Add(
                    $"Collision static-model row {ordinal} does not match " +
                    "its authorized tombstone fields.");
            }
        }
    }

    private static void ValidateBindings(
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor gfxDescriptor,
        CompiledMapAssetDescriptor clipDescriptor,
        StaticModelCompilationRelationship relationship,
        IEnumerable<SourceBindingId> bindingIds,
        IReadOnlyDictionary<SourceBindingId, CompiledSourceBinding> catalog,
        ICollection<string> diagnostics)
    {
        int gfx = relationship.GfxSourceOrdinal;
        int clip = relationship.ClipSourceOrdinal;
        var expected = new Dictionary<string, CompiledMapAssetDescriptor>
        {
            [$"$.definition.dpvs.sModelDrawInsts[{gfx}].placement.origin"] =
                gfxDescriptor,
            [$"$.definition.dpvs.sModelDrawInsts[{gfx}].cullDist"] =
                gfxDescriptor,
            [$"$.definition.dpvs.sModelDrawInsts[{gfx}].flags"] =
                gfxDescriptor,
            [$"$.definition.dpvs.sModelInsts[{gfx}].bounds"] =
                gfxDescriptor,
            [$"$.definition.dpvs.sModelInsts[{gfx}].lightingOrigin"] =
                gfxDescriptor,
            [$"$.definition.staticModelList[{clip}].origin"] =
                clipDescriptor,
            [$"$.definition.staticModelList[{clip}].absMin"] =
                clipDescriptor
        };
        CompiledSourceBinding[] bindings = bindingIds
            .Select(id => catalog.TryGetValue(
                    id,
                    out CompiledSourceBinding? value)
                ? value
                : null)
            .Where(value => value is not null)
            .Cast<CompiledSourceBinding>()
            .ToArray();
        foreach (SourceBindingId missing in bindingIds.Where(
                     id => !catalog.ContainsKey(id)))
        {
            diagnostics.Add(
                $"Static-model suppression binding {missing} is absent " +
                "from the imported catalog.");
        }
        if (!expected.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(bindings.Select(value => value.FieldPath)))
        {
            diagnostics.Add(
                "Static-model suppression does not carry the exact seven " +
                "mutable Gfx/Col compiled-field bindings.");
        }
        foreach (CompiledSourceBinding binding in bindings)
        {
            if (!expected.TryGetValue(
                    binding.FieldPath,
                    out CompiledMapAssetDescriptor? descriptor))
            {
                continue;
            }
            int ordinal = descriptor.Kind == MapAssetKind.GfxMap
                ? gfx
                : clip;
            SourceBindingId expectedId =
                DeterministicMapIdentity.Binding(
                    bundle.MapIdentity,
                    descriptor.SerializedType.ToString(),
                    descriptor.AssetName,
                    binding.FieldPath,
                    ordinal);
            if (binding.Id != expectedId ||
                binding.AssetType != descriptor.SerializedType ||
                binding.OwnerRow != descriptor.OwnerRow ||
                binding.SourceOrdinal != ordinal ||
                !string.Equals(
                    binding.AssetName,
                    descriptor.AssetName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    binding.BaselineDigest,
                    descriptor.BaselineDigest,
                    StringComparison.Ordinal) ||
                binding.Provenance is not (
                    MapValueProvenance.ExactSerialized or
                    MapValueProvenance.ExactDecodedRuntime))
            {
                diagnostics.Add(
                    $"Static-model suppression binding {binding.Id} is " +
                    "not exact authority for '{binding.FieldPath}'.");
            }
        }
    }

    private static SourceBindingId[] GetMutableBindings(
        EditorStaticModel render,
        EditorStaticModel collision,
        ICollection<string> diagnostics)
    {
        StaticModelCompiledFieldBindings gfx =
            render.CompiledFieldBindings;
        StaticModelCompiledFieldBindings clip =
            collision.CompiledFieldBindings;
        if (!gfx.HasCompleteSuppressionBindings ||
            !clip.HasCompleteSuppressionBindings ||
            gfx.CullDistanceBinding is not { } cull ||
            gfx.FlagsBinding is not { } flags ||
            gfx.LightingOriginBinding is not { } lighting)
        {
            diagnostics.Add(
                $"Static-model pair {render.Id} lacks complete exact " +
                "compiled suppression bindings.");
            return [];
        }

        return
        [
            gfx.OriginBinding,
            cull,
            flags,
            gfx.BoundsMidpointBinding,
            lighting,
            clip.OriginBinding,
            clip.BoundsMidpointBinding
        ];
    }

    private static Dictionary<SourceBindingId, CompiledSourceBinding>
        BuildBindingCatalog(
            IEnumerable<CompiledSourceBinding> sourceBindings,
            ICollection<string> diagnostics)
    {
        var result =
            new Dictionary<SourceBindingId, CompiledSourceBinding>();
        foreach (CompiledSourceBinding binding in sourceBindings)
        {
            if (binding is null || !result.TryAdd(binding.Id, binding))
            {
                diagnostics.Add(
                    "The imported compiled-binding catalog contains a null " +
                    "or duplicate entry.");
            }
        }
        return result;
    }

    private static bool TryGetCollisionBaseline(
        CompiledMapBundle bundle,
        out MapAssetKind kind,
        out ClipMapBuildData? baseline)
    {
        bool hasMp = bundle.TryGetBaseline(
            MapAssetKind.ColMapMp,
            out ClipMapBuildData? mp) &&
            mp is not null;
        bool hasSp = bundle.TryGetBaseline(
            MapAssetKind.ColMapSp,
            out ClipMapBuildData? sp) &&
            sp is not null;
        if (hasMp == hasSp)
        {
            kind = default;
            baseline = null;
            return false;
        }

        kind = hasMp
            ? MapAssetKind.ColMapMp
            : MapAssetKind.ColMapSp;
        baseline = hasMp ? mp : sp;
        return true;
    }

    private static StaticModelSuppressionPatchCandidate InvalidCandidate(
        IEnumerable<string> diagnostics) =>
        new(
            gfxDescriptor: null,
            clipDescriptor: null,
            gfxBaseline: null,
            clipBaseline: null,
            gfxBuildData: null,
            clipBuildData: null,
            patches: [],
            gfxBaselineSemanticDigest: null,
            clipBaselineSemanticDigest: null,
            new MapPatchValidation(diagnostics));

    private static bool SameSemantic(
        IXAssetBuildData left,
        IXAssetBuildData right,
        CancellationToken cancellationToken) =>
        string.Equals(
            RelocationInvariantAssetSemanticDigest.Compute(
                left,
                cancellationToken),
            RelocationInvariantAssetSemanticDigest.Compute(
                right,
                cancellationToken),
            StringComparison.Ordinal);

    private static bool SameDigest(
        IXAssetBuildData value,
        string expectedDigest) =>
        string.Equals(
            RelocationInvariantAssetSemanticDigest.Compute(value),
            expectedDigest,
            StringComparison.Ordinal);

    private static bool SameJson<T>(T left, T right) =>
        System.Text.Json.JsonSerializer.Serialize(left) ==
        System.Text.Json.JsonSerializer.Serialize(right);

    private static bool SameVec(
        IW4.Assets.Math.Vec3 left,
        IW4.Assets.Math.Vec3 right) =>
        SameBits(left.X, right.X) &&
        SameBits(left.Y, right.Y) &&
        SameBits(left.Z, right.Z);

    private static bool SameVec(
        IW4.Assets.Math.Vec3 edited,
        IW4.Assets.Math.Vec3 source,
        float deltaZ) =>
        SameBits(edited.X, source.X) &&
        SameBits(edited.Y, source.Y) &&
        SameBits(edited.Z, source.Z + deltaZ);

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);
}
