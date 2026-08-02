using System.Collections.ObjectModel;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Compilation.Import;
using IW4.Studio.MapEditor.Compilation.Patching;
using IW4.Studio.MapEditor.Compilation.SavePlanning;
using IW4.Studio.MapEditor.Compilation.Validation;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.Persistence;

public enum MapEditorSaveAsStatus
{
    Succeeded,
    Rejected,
    Failed,
    Cancelled
}

/// <summary>
/// Result of a compiled-map Save As. A successful compiled-map save always
/// requires reopening the output because the imported compiled baseline is
/// immutable and is intentionally never rebased in place.
/// </summary>
public sealed class MapEditorSaveAsResult
{
    private readonly IReadOnlyList<string> _diagnostics;

    internal MapEditorSaveAsResult(
        MapEditorSaveAsStatus status,
        string? destinationPath,
        long? savedDocumentRevision,
        MapSavePlan? plan,
        SaveAsResult? transactionalResult,
        IEnumerable<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Status = status;
        DestinationPath = destinationPath;
        SavedDocumentRevision = savedDocumentRevision;
        Plan = plan;
        TransactionalResult = transactionalResult;
        _diagnostics = new ReadOnlyCollection<string>(
            diagnostics
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    public MapEditorSaveAsStatus Status { get; }
    public bool Succeeded => Status == MapEditorSaveAsStatus.Succeeded;
    public bool Cancelled => Status == MapEditorSaveAsStatus.Cancelled;
    public bool RequiresReopen => Succeeded;
    public string? DestinationPath { get; }
    public long? SavedDocumentRevision { get; }
    public MapSavePlan? Plan { get; }
    public SaveAsResult? TransactionalResult { get; }
    public IReadOnlyList<string> Diagnostics => _diagnostics;
}

/// <summary>
/// Caller-acquirable lease over both mutable inputs to a compiled-map save.
/// Desktop may acquire it synchronously before yielding to background work;
/// the Save As overload that accepts it owns commit and disposal.
/// </summary>
public sealed class MapEditorSaveLease : IDisposable
{
    private readonly EditorMapDocument _document;
    private readonly FastFileEditingSession _editingSession;
    private readonly MapCompiledSaveLease _documentLease;
    private readonly FastFileCompiledMapSaveLease _sourceLease;
    private int _disposed;

    internal MapEditorSaveLease(
        EditorMapDocument document,
        FastFileEditingSession editingSession,
        MapCompiledSaveLease documentLease,
        FastFileCompiledMapSaveLease sourceLease)
    {
        _document =
            document ?? throw new ArgumentNullException(nameof(document));
        _editingSession =
            editingSession ??
            throw new ArgumentNullException(nameof(editingSession));
        _documentLease =
            documentLease ??
            throw new ArgumentNullException(nameof(documentLease));
        _sourceLease =
            sourceLease ??
            throw new ArgumentNullException(nameof(sourceLease));
    }

    public long DocumentRevision =>
        _documentLease.DocumentRevision;

    public long SourceEditingSessionRevision =>
        _sourceLease.Revision;

    public bool IsActive =>
        Volatile.Read(ref _disposed) == 0 &&
        _documentLease.IsActive &&
        _sourceLease.IsActive;

    internal bool Owns(
        EditorMapDocument document,
        FastFileEditingSession editingSession) =>
        ReferenceEquals(_document, document) &&
        ReferenceEquals(_editingSession, editingSession) &&
        _documentLease.DocumentId == document.Id &&
        _sourceLease.DocumentId ==
            editingSession.Document.DocumentId;

    internal FastFileEditingSaveSnapshot CaptureSourceSnapshot()
    {
        ThrowIfDisposed();
        return _sourceLease.CaptureForSave();
    }

    internal void MarkCommitted(
        FastFileEditingSaveSnapshot sourceSnapshot,
        string destinationPath)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(sourceSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        // The semantic document must fail closed to RequiresReopen even if a
        // later in-memory Studio acknowledgement unexpectedly fails.
        _documentLease.MarkCommitted();
        _sourceLease.MarkRevisionSaved(
            sourceSnapshot,
            new SavedDocumentState(destinationPath));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            _sourceLease.Dispose();
        }
        finally
        {
            _documentLease.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (!IsActive)
            throw new ObjectDisposedException(nameof(MapEditorSaveLease));
    }
}

/// <summary>
/// Existing compiled-map Save As coordinator. Its production capability
/// catalog contains narrow, independently validated patches for existing
/// ComMap primary-light Color components, Exponent bytes, and bounded type-2
/// inner-cone falloff values; proven MapEnt property/cardinality content;
/// existing FxGlassDef HalfThickness scalars; plus conservative suppression,
/// proof-gated translation, removal, and constrained duplication of
/// artifact-local, mutually unique Gfx/Col static-model pairs.
/// </summary>
public sealed class MapEditorSaveAsService
{
    private readonly TransactionalSaveAsService _transactionalSave;
    private readonly AssetAuthoringAdapterRegistry _adapters;

    public MapEditorSaveAsService(
        TransactionalSaveAsService? transactionalSave = null,
        AssetAuthoringAdapterRegistry? adapters = null)
    {
        _transactionalSave =
            transactionalSave ?? new TransactionalSaveAsService();
        _adapters =
            adapters ?? AssetAuthoringAdapterRegistry.CreateDefault();
    }

    public MapPreservationCoverage PrimaryLightColorPreservationCoverage =>
        ComWorldPrimaryLightPropertyPatcher.ColorPreservationCoverage;

    public MapPreservationCoverage PrimaryLightExponentPreservationCoverage =>
        ComWorldPrimaryLightPropertyPatcher.ExponentPreservationCoverage;

    public MapPreservationCoverage
        PrimaryLightSpotFalloffPreservationCoverage =>
        ComWorldPrimaryLightPropertyPatcher.SpotFalloffPreservationCoverage;

    public MapPreservationCoverage
        FxGlassDefinitionHalfThicknessPreservationCoverage =>
        FxWorldGlassDefinitionPropertyPatcher
            .HalfThicknessPreservationCoverage;

    public MapPreservationCoverage
        FxGlassDefinitionColorPreservationCoverage =>
        FxWorldGlassDefinitionPropertyPatcher
            .ColorPreservationCoverage;

    public MapPreservationCoverage MapEntsPropertyPreservationCoverage =>
        MapEntsEntityStringPatcher.PreservationCoverage;

    public MapPreservationCoverage
        StaticModelGfxSuppressionPreservationCoverage =>
        StaticModelSuppressionPatcher.GfxPreservationCoverage;

    public MapPreservationCoverage
        StaticModelCollisionSuppressionPreservationCoverage(
            MapAssetKind collisionAssetKind) =>
        StaticModelSuppressionPatcher.CollisionPreservationCoverage(
            collisionAssetKind);

    public MapPreservationCoverage
        StaticModelGfxTranslationPreservationCoverage =>
        StaticModelTranslationPatcher.GfxPreservationCoverage;

    public MapPreservationCoverage
        StaticModelCollisionTranslationPreservationCoverage(
            MapAssetKind collisionAssetKind) =>
        StaticModelTranslationPatcher.CollisionPreservationCoverage(
            collisionAssetKind);

    public MapPreservationCoverage
        StaticModelGfxRemovalPreservationCoverage =>
        StaticModelRemovalPatcher.GfxPreservationCoverage;

    public MapPreservationCoverage
        StaticModelCollisionRemovalPreservationCoverage(
            MapAssetKind collisionAssetKind) =>
        StaticModelRemovalPatcher.CollisionPreservationCoverage(
            collisionAssetKind);

    public MapPreservationCoverage
        StaticModelGfxDuplicationPreservationCoverage =>
        StaticModelDuplicationPatcher.GfxPreservationCoverage;

    public MapPreservationCoverage
        StaticModelCollisionDuplicationPreservationCoverage(
            MapAssetKind collisionAssetKind) =>
        StaticModelDuplicationPatcher.CollisionPreservationCoverage(
            collisionAssetKind);

    public MapEditorSaveLease AcquireSaveLease(
        ExistingMapImportResult imported,
        FastFileEditingSession editingSession)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(editingSession);
        var diagnostics = new List<string>();
        ValidateWorkspaceOwnership(
            imported,
            editingSession,
            diagnostics);
        if (diagnostics.Count != 0)
        {
            throw new InvalidOperationException(
                string.Join("; ", diagnostics));
        }

        MapCompiledSaveLease documentLease =
            imported.Document.AcquireCompiledSaveLease();
        try
        {
            FastFileCompiledMapSaveLease sourceLease =
                editingSession.AcquireCompiledMapSaveLease();
            return new MapEditorSaveLease(
                imported.Document,
                editingSession,
                documentLease,
                sourceLease);
        }
        catch
        {
            documentLease.Dispose();
            throw;
        }
    }

    public MapEditorSaveAsResult SaveAs(
        ExistingMapImportResult imported,
        FastFileEditingSession editingSession,
        SaveAsRequest request,
        IProgress<SaveAsProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(editingSession);
        ArgumentNullException.ThrowIfNull(request);

        MapEditorSaveLease saveLease;
        try
        {
            saveLease = AcquireSaveLease(
                imported,
                editingSession);
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException)
        {
            return Rejected(
                plan: null,
                $"{exception.GetType().Name}: {exception.Message}");
        }

        return SaveAs(
            imported,
            editingSession,
            saveLease,
            request,
            progress,
            cancellationToken);
    }

    public MapEditorSaveAsResult SaveAs(
        ExistingMapImportResult imported,
        FastFileEditingSession editingSession,
        MapEditorSaveLease saveLease,
        SaveAsRequest request,
        IProgress<SaveAsProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(editingSession);
        ArgumentNullException.ThrowIfNull(saveLease);
        ArgumentNullException.ThrowIfNull(request);
        if (!saveLease.IsActive ||
            !saveLease.Owns(imported.Document, editingSession))
        {
            saveLease.Dispose();
            return Rejected(
                plan: null,
                "The supplied compiled-map save lease is inactive or belongs " +
                "to another map/session pair.");
        }

        using MapEditorSaveLease ownedSaveLease =
            saveLease;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EditorMapDocument document = imported.Document;
            if (document.RequiresReopen)
            {
                return Rejected(
                    plan: null,
                    "This imported compiled-map baseline has already produced " +
                    "a verified Save As. Reopen that output before another " +
                    "compiled-map edit or save.");
            }

            long capturedDocumentRevision =
                saveLease.DocumentRevision;
            MapPendingEdit[] capturedPendingEdits =
                document.History.SerializedPendingEdits.ToArray();
            MapPendingEdit[] pendingEdits = capturedPendingEdits;
            bool hasPrimaryLightColorEdits = pendingEdits.Any(
                value => value.Kind == MapEditKind.PrimaryLightColor);
            bool hasPrimaryLightExponentEdits = pendingEdits.Any(
                value => value.Kind == MapEditKind.PrimaryLightExponent);
            bool hasPrimaryLightSpotFalloffEdits = pendingEdits.Any(
                value => value.Kind == MapEditKind.PrimaryLightSpotFalloff);
            bool hasPrimaryLightPropertyEdits =
                hasPrimaryLightColorEdits ||
                hasPrimaryLightExponentEdits ||
                hasPrimaryLightSpotFalloffEdits;
            bool hasFxGlassDefinitionHalfThicknessEdits =
                pendingEdits.Any(value =>
                    value.Kind ==
                        MapEditKind
                            .FxGlassDefinitionHalfThickness);
            bool hasFxGlassDefinitionColorEdits =
                pendingEdits.Any(value =>
                    value.Kind ==
                        MapEditKind.FxGlassDefinitionColor);
            bool hasMapEntsEdits = pendingEdits.Any(
                value => value.Kind is (
                    MapEditKind.MapEntityKeyValue or
                    MapEditKind.MapEntityCardinality));
            bool hasStaticModelSuppressionEdits = pendingEdits.Any(
                value =>
                    value.Kind == MapEditKind.StaticModelVisibility);
            bool hasStaticModelTranslationEdits = pendingEdits.Any(
                value =>
                    value.Kind == MapEditKind.StaticModelTransform);
            bool hasStaticModelRemovalEdits = pendingEdits.Any(
                value =>
                    value.Kind == MapEditKind.StaticModelCardinality);
            bool hasStaticModelDuplicationEdits = pendingEdits.Any(
                value =>
                    value.Kind == MapEditKind.StaticModelDuplication);

            var primaryLightPatcher =
                new ComWorldPrimaryLightPropertyPatcher();
            ComWorldPrimaryLightPropertyPatchCandidate? primaryLightCandidate =
                hasPrimaryLightPropertyEdits
                    ? primaryLightPatcher.Prepare(
                        document,
                        imported.Bundle,
                        imported.SourceBindings)
                    : null;
            var fxGlassDefinitionPropertyPatcher =
                new FxWorldGlassDefinitionPropertyPatcher();
            FxWorldGlassDefinitionPropertyPatchCandidate?
                fxGlassDefinitionPropertyCandidate =
                    hasFxGlassDefinitionHalfThicknessEdits ||
                    hasFxGlassDefinitionColorEdits
                        ? fxGlassDefinitionPropertyPatcher.Prepare(
                            document,
                            imported.Bundle,
                            imported.SourceBindings,
                            cancellationToken)
                        : null;
            var mapEntsPatcher = new MapEntsEntityStringPatcher();
            MapEntsEntityStringPatchCandidate? mapEntsCandidate =
                hasMapEntsEdits
                    ? mapEntsPatcher.Prepare(
                        document,
                        imported.Bundle,
                        imported.SourceBindings,
                        cancellationToken)
                    : null;
            var staticModelPatcher =
                new StaticModelSuppressionPatcher();
            StaticModelSuppressionPatchCandidate? staticModelCandidate =
                hasStaticModelSuppressionEdits
                    ? staticModelPatcher.Prepare(
                        document,
                        imported.Bundle,
                        imported.SourceBindings,
                        cancellationToken)
                    : null;
            var staticModelTranslationPatcher =
                new StaticModelTranslationPatcher();
            StaticModelTranslationPatchCandidate?
                staticModelTranslationCandidate =
                    hasStaticModelTranslationEdits
                        ? staticModelTranslationPatcher.Prepare(
                            document,
                            imported.Bundle,
                            imported.SourceBindings,
                            cancellationToken)
                        : null;
            var staticModelRemovalPatcher =
                new StaticModelRemovalPatcher();
            StaticModelRemovalPatchCandidate?
                staticModelRemovalCandidate =
                    hasStaticModelRemovalEdits
                        ? staticModelRemovalPatcher.Prepare(
                            document,
                            imported.Bundle,
                            imported.SourceBindings,
                            cancellationToken)
                        : null;
            var staticModelDuplicationPatcher =
                new StaticModelDuplicationPatcher();
            StaticModelDuplicationPatchCandidate?
                staticModelDuplicationCandidate =
                    hasStaticModelDuplicationEdits
                        ? staticModelDuplicationPatcher.Prepare(
                            document,
                            imported.Bundle,
                            imported.SourceBindings,
                            cancellationToken)
                        : null;
            MapEntsEntityStringPatchCandidate?
                verifiedNetZeroMapEntsCandidate = null;
            if (mapEntsCandidate?.CanOmitAsVerifiedNetZero == true)
            {
                // Keep the semantic undo journal intact, but do not let a
                // structurally valid MapEnt append/remove or replacement/
                // reversion pair block an independent compiled-map edit.
                // A MapEnt candidate is omitted only after its complete
                // replay and owner preservation checks prove exact baseline
                // bytes.
                verifiedNetZeroMapEntsCandidate = mapEntsCandidate;
                mapEntsCandidate = null;
                pendingEdits = pendingEdits
                    .Where(edit => edit.Kind is not (
                        MapEditKind.MapEntityKeyValue or
                        MapEditKind.MapEntityCardinality))
                    .ToArray();
                hasPrimaryLightColorEdits = pendingEdits.Any(
                    value =>
                        value.Kind == MapEditKind.PrimaryLightColor);
                hasPrimaryLightExponentEdits = pendingEdits.Any(
                    value =>
                        value.Kind == MapEditKind.PrimaryLightExponent);
                hasPrimaryLightSpotFalloffEdits = pendingEdits.Any(
                    value =>
                        value.Kind ==
                            MapEditKind.PrimaryLightSpotFalloff);
                hasPrimaryLightPropertyEdits =
                    hasPrimaryLightColorEdits ||
                    hasPrimaryLightExponentEdits ||
                    hasPrimaryLightSpotFalloffEdits;
                hasMapEntsEdits = false;
            }
            long observedDocumentRevision = document.Revision;
            if (observedDocumentRevision != capturedDocumentRevision)
            {
                return Rejected(
                    plan: null,
                    $"Map document revision changed from " +
                    $"{capturedDocumentRevision} to " +
                    $"{observedDocumentRevision} while capturing the save " +
                    "candidate; discard it and retry.");
            }

            var preflight = new List<string>();
            ValidateWorkspaceOwnership(
                imported,
                editingSession,
                preflight);
            ValidatePatchJournal(
                pendingEdits,
                primaryLightCandidate,
                fxGlassDefinitionPropertyCandidate,
                mapEntsCandidate,
                staticModelCandidate,
                staticModelTranslationCandidate,
                staticModelRemovalCandidate,
                staticModelDuplicationCandidate,
                preflight);
            if (primaryLightCandidate is not null)
            {
                preflight.AddRange(
                    primaryLightCandidate.Validation.Diagnostics);
                if (hasPrimaryLightColorEdits &&
                    !ComWorldPrimaryLightPropertyPatcher
                        .ColorPreservationCoverage.IsProven)
                {
                    preflight.Add(
                        "ComMap primary-light Color preservation coverage is " +
                        "not proven.");
                }
                if (hasPrimaryLightExponentEdits &&
                    !ComWorldPrimaryLightPropertyPatcher
                        .ExponentPreservationCoverage.IsProven)
                {
                    preflight.Add(
                        "ComMap primary-light Exponent preservation coverage " +
                        "is not proven.");
                }
                if (hasPrimaryLightSpotFalloffEdits &&
                    !ComWorldPrimaryLightPropertyPatcher
                        .SpotFalloffPreservationCoverage.IsProven)
                {
                    preflight.Add(
                        "ComMap primary-light type-2 inner-cone falloff " +
                        "preservation coverage is not proven.");
                }
                if (primaryLightCandidate.ColorPatches.Count == 0 &&
                    primaryLightCandidate.ExponentPatches.Count == 0 &&
                    primaryLightCandidate.SpotFalloffPatches.Count == 0)
                {
                    preflight.Add(
                        "The semantic document contains no effective existing " +
                        "primary-light Color, Exponent, or type-2 inner-cone " +
                        "falloff change to persist.");
                }
            }
            if (fxGlassDefinitionPropertyCandidate is not null)
            {
                preflight.AddRange(
                    fxGlassDefinitionPropertyCandidate
                        .Validation.Diagnostics);
                if (hasFxGlassDefinitionHalfThicknessEdits &&
                    !FxWorldGlassDefinitionPropertyPatcher
                        .HalfThicknessPreservationCoverage.IsProven)
                {
                    preflight.Add(
                        "FxMap glass-definition HalfThickness preservation " +
                        "coverage is not proven.");
                }
                if (hasFxGlassDefinitionColorEdits &&
                    !FxWorldGlassDefinitionPropertyPatcher
                        .ColorPreservationCoverage.IsProven)
                {
                    preflight.Add(
                        "FxMap glass-definition Color preservation coverage " +
                        "is not proven.");
                }
                if (fxGlassDefinitionPropertyCandidate
                        .HalfThicknessPatches.Count == 0 &&
                    fxGlassDefinitionPropertyCandidate
                        .ColorPatches.Count == 0)
                {
                    preflight.Add(
                        "The semantic document contains no effective " +
                        "existing FxGlassDef property change to " +
                        "persist.");
                }
            }
            if (mapEntsCandidate is not null)
            {
                preflight.AddRange(
                    mapEntsCandidate.Validation.Diagnostics);
                if (!MapEntsEntityStringPatcher
                        .PreservationCoverage.IsProven)
                {
                    preflight.Add(
                        "MapEnts property/cardinality preservation coverage " +
                        "is not proven.");
                }
                if (mapEntsCandidate.Patches.Count == 0 &&
                    mapEntsCandidate.CardinalityPatches.Count == 0)
                {
                    preflight.Add(
                        "The semantic document contains no effective supported " +
                        "MapEnt property or cardinality change to persist.");
                }
            }
            if (staticModelCandidate is not null)
            {
                preflight.AddRange(
                    staticModelCandidate.Validation.Diagnostics);
                if (!StaticModelSuppressionPatcher
                        .GfxPreservationCoverage.IsProven ||
                    staticModelCandidate.ClipDescriptor is null ||
                    !StaticModelSuppressionPatcher
                        .CollisionPreservationCoverage(
                            staticModelCandidate.ClipDescriptor.Kind)
                        .IsProven)
                {
                    preflight.Add(
                        "Atomic Gfx/Col static-model suppression " +
                        "preservation coverage is not proven.");
                }
                if (staticModelCandidate.Patches.Count == 0)
                {
                    preflight.Add(
                        "The semantic document contains no effective " +
                        "uniquely paired static-model suppression.");
                }
                if (mapEntsCandidate is
                    {
                        Descriptor.IsNested: true
                    })
                {
                    preflight.Add(
                        "A nested MapEnts patch and static-model suppression " +
                        "both replace the ColMap owner. Their composite " +
                        "owner validator is not implemented, so this save " +
                        "must be split across reopened outputs.");
                }
            }
            if (staticModelTranslationCandidate is not null)
            {
                preflight.AddRange(
                    staticModelTranslationCandidate.Validation.Diagnostics);
                if (!StaticModelTranslationPatcher
                        .GfxPreservationCoverage.IsProven ||
                    staticModelTranslationCandidate.ClipDescriptor is null ||
                    !StaticModelTranslationPatcher
                        .CollisionPreservationCoverage(
                            staticModelTranslationCandidate
                                .ClipDescriptor.Kind)
                        .IsProven)
                {
                    preflight.Add(
                        "Atomic Gfx/Col static-model translation " +
                        "preservation coverage is not proven.");
                }
                if (staticModelTranslationCandidate.Patches.Count == 0)
                {
                    preflight.Add(
                        "The semantic document contains no effective " +
                        "proof-gated static-model translation.");
                }
                if (mapEntsCandidate is
                    {
                        Descriptor.IsNested: true
                    })
                {
                    preflight.Add(
                        "A nested MapEnts patch and static-model translation " +
                        "both replace the ColMap owner. Their composite " +
                        "owner validator is not implemented, so this save " +
                        "must be split across reopened outputs.");
                }
            }
            if (staticModelRemovalCandidate is not null)
            {
                preflight.AddRange(
                    staticModelRemovalCandidate.Validation.Diagnostics);
                if (!StaticModelRemovalPatcher
                        .GfxPreservationCoverage.IsProven ||
                    staticModelRemovalCandidate.ClipDescriptor is null ||
                    !StaticModelRemovalPatcher
                        .CollisionPreservationCoverage(
                            staticModelRemovalCandidate
                                .ClipDescriptor.Kind)
                        .IsProven)
                {
                    preflight.Add(
                        "Atomic Gfx/Col static-model removal preservation " +
                        "coverage is not proven.");
                }
                if (staticModelRemovalCandidate.Patches.Count == 0)
                {
                    preflight.Add(
                        "The semantic document contains no effective " +
                        "proof-gated static-model removal.");
                }
                if (mapEntsCandidate is
                    {
                        Descriptor.IsNested: true
                    })
                {
                    preflight.Add(
                        "A nested MapEnts patch and static-model removal " +
                        "both replace the ColMap owner. Their composite " +
                        "owner validator is not implemented, so this save " +
                        "must be split across reopened outputs.");
                }
            }
            if (staticModelDuplicationCandidate is not null)
            {
                preflight.AddRange(
                    staticModelDuplicationCandidate.Validation.Diagnostics);
                if (!StaticModelDuplicationPatcher
                        .GfxPreservationCoverage.IsProven ||
                    staticModelDuplicationCandidate.ClipDescriptor is null ||
                    !StaticModelDuplicationPatcher
                        .CollisionPreservationCoverage(
                            staticModelDuplicationCandidate
                                .ClipDescriptor.Kind)
                        .IsProven)
                {
                    preflight.Add(
                        "Atomic Gfx/Col static-model duplication " +
                        "preservation coverage is not proven.");
                }
                if (staticModelDuplicationCandidate.Patches.Count != 1)
                {
                    preflight.Add(
                        "The semantic document must contain exactly one " +
                        "effective proof-gated authored duplicate pair.");
                }
            }
            if (staticModelCandidate is not null &&
                staticModelTranslationCandidate is not null)
            {
                preflight.Add(
                    "Static-model suppression and translation both replace " +
                    "the GfxMap and ColMap owners. Their composite builder " +
                    "is not implemented, so this save must be split across " +
                    "reopened outputs.");
            }
            if (staticModelRemovalCandidate is not null &&
                (staticModelCandidate is not null ||
                 staticModelTranslationCandidate is not null))
            {
                preflight.Add(
                    "Static-model removal cannot share a candidate with " +
                    "suppression or translation because each operation " +
                    "replaces the GfxMap and ColMap owners. Split the save " +
                    "across reopened outputs.");
            }
            preflight.AddRange(
                StaticModelDuplicationSaveCompositionPolicy.Validate(
                    staticModelDuplicationCandidate is not null,
                    staticModelCandidate is not null,
                    staticModelTranslationCandidate is not null,
                    staticModelRemovalCandidate is not null,
                    mapEntsCandidate is
                    {
                        Descriptor.IsNested: true
                    }));
            if (!hasPrimaryLightPropertyEdits &&
                !hasFxGlassDefinitionHalfThicknessEdits &&
                !hasFxGlassDefinitionColorEdits &&
                !hasMapEntsEdits &&
                !hasStaticModelSuppressionEdits &&
                !hasStaticModelTranslationEdits &&
                !hasStaticModelRemovalEdits &&
                !hasStaticModelDuplicationEdits)
            {
                preflight.Add(
                    "The semantic document contains no supported serialized " +
                    "compiled-map edit to persist.");
            }
            if (preflight.Count != 0)
                return Rejected(plan: null, preflight);

            cancellationToken.ThrowIfCancellationRequested();
            FastFileEditingSaveSnapshot sourceCapture =
                saveLease.CaptureSourceSnapshot();
            long sourceSessionRevision = sourceCapture.Revision;
            if (request.ExpectedEditingSessionRevision is
                    { } requestedSourceRevision &&
                requestedSourceRevision != sourceSessionRevision)
            {
                return Rejected(
                    plan: null,
                    $"The requested editing-session revision " +
                    $"{requestedSourceRevision} does not match the current " +
                    $"source revision {sourceSessionRevision}.");
            }
            IReadOnlyList<string> compositionDiagnostics =
                StudioDraftCompositionPolicy.Validate(
                    imported.Bundle,
                    sourceCapture);
            if (compositionDiagnostics.Count != 0)
            {
                return Rejected(
                    plan: null,
                    compositionDiagnostics);
            }
            long sourcePoolRevision =
                editingSession.Workspace.Runtime.AssetPool.Revision;
            string currentBaselineDigest =
                imported.Bundle.ComputeCurrentBaselineDigest(
                    cancellationToken);
            MapPendingEdit[] evidencedEdits = capturedPendingEdits
                .Select(AttachCompilerEvidence)
                .ToArray();
            MapSavePlanNormalization[] normalizations =
                verifiedNetZeroMapEntsCandidate is null
                    ? []
                    :
                    [
                        new MapSavePlanNormalization(
                            MapSavePlanNormalizationKind
                                .VerifiedNetZeroOmission,
                            MapAssetKind.MapEnts,
                            evidencedEdits.Where(edit => edit.Kind is (
                                MapEditKind.MapEntityKeyValue or
                                MapEditKind.MapEntityCardinality)),
                            verifiedNetZeroMapEntsCandidate
                                .BaselineEntityStringDigest,
                            verifiedNetZeroMapEntsCandidate
                                .CandidateEntityStringDigest,
                            "Complete ordered MapEnt replay, semantic " +
                            "projection, preservation, and nested-owner " +
                            "validation succeeded; candidate entity-string " +
                            "bytes equal the immutable baseline.")
                    ];
            string? verifiedNetZeroDiagnostic =
                normalizations.SingleOrDefault() is { } normalization
                    ? "VerifiedNetZero: omitted " +
                      $"{normalization.Edits.Count} MapEnt journal " +
                      "transition(s) from candidate staging after proving " +
                      "the baseline and candidate entity-string SHA-256 " +
                      $"digest {normalization.BaselineContentDigest} equal."
                    : null;
            string[] normalizationDiagnostics =
                verifiedNetZeroDiagnostic is null
                    ? []
                    : [verifiedNetZeroDiagnostic];
            var availablePatchers = new List<MapEditKind>(9);
            if (primaryLightCandidate is not null)
            {
                if (hasPrimaryLightColorEdits)
                {
                    availablePatchers.Add(
                        MapEditKind.PrimaryLightColor);
                }
                if (hasPrimaryLightExponentEdits)
                {
                    availablePatchers.Add(
                        MapEditKind.PrimaryLightExponent);
                }
                if (hasPrimaryLightSpotFalloffEdits)
                {
                    availablePatchers.Add(
                        MapEditKind.PrimaryLightSpotFalloff);
                }
            }
            if (fxGlassDefinitionPropertyCandidate is not null)
            {
                if (hasFxGlassDefinitionHalfThicknessEdits)
                {
                    availablePatchers.Add(
                        MapEditKind.FxGlassDefinitionHalfThickness);
                }
                if (hasFxGlassDefinitionColorEdits)
                {
                    availablePatchers.Add(
                        MapEditKind.FxGlassDefinitionColor);
                }
            }
            if (mapEntsCandidate is not null ||
                verifiedNetZeroMapEntsCandidate is not null)
            {
                availablePatchers.Add(MapEditKind.MapEntityKeyValue);
                availablePatchers.Add(MapEditKind.MapEntityCardinality);
            }
            if (staticModelCandidate is not null)
            {
                availablePatchers.Add(
                    MapEditKind.StaticModelVisibility);
            }
            if (staticModelTranslationCandidate is not null)
            {
                availablePatchers.Add(
                    MapEditKind.StaticModelTransform);
            }
            if (staticModelRemovalCandidate is not null)
            {
                availablePatchers.Add(
                    MapEditKind.StaticModelCardinality);
            }
            if (staticModelDuplicationCandidate is not null)
            {
                availablePatchers.Add(
                    MapEditKind.StaticModelDuplication);
            }
            var planner = new MapSavePlanner(
                imported.SourceBindings,
                availablePatchers);
            MapSavePlan plan = planner.PlanComposed(
                document,
                imported.Bundle,
                capturedDocumentRevision,
                sourcePoolRevision,
                sourceCapture,
                currentBaselineDigest,
                evidencedEdits,
                normalizations);
            if (!plan.CanSave)
                return Rejected(plan, plan.Blockers);

            cancellationToken.ThrowIfCancellationRequested();
            // Start from the exact captured Studio drafts, then layer narrow
            // map patches onto that isolated transaction. A failed candidate
            // leaves the shared drafts and revision untouched.
            using FastFileEditingSession stagingSession =
                editingSession.CreateStagingSession(sourceCapture);
            if (primaryLightCandidate is not null)
            {
                CompiledMapAssetDescriptor comDescriptor =
                    imported.Bundle.RequireAsset(MapAssetKind.ComMap);
                IAssetAuthoringAdapter comAdapter =
                    _adapters.RequireAdapter(
                        IW4.FastFiles.Zone.XAssetType.ComMap);
                _ = stagingSession.MutateAuthoredDraftAtRevision(
                    stagingSession.Revision,
                    comDescriptor.OwnerRow,
                    comAdapter,
                    draft => primaryLightPatcher.ApplyValidatedCandidate(
                        RequireDraft<ComWorldDraft>(
                            draft,
                            "ComMap"),
                        primaryLightCandidate));
            }
            if (fxGlassDefinitionPropertyCandidate is not null)
            {
                StageFxGlassDefinitionPropertyCandidate(
                    stagingSession,
                    fxGlassDefinitionPropertyCandidate,
                    fxGlassDefinitionPropertyPatcher,
                    _adapters);
            }
            if (mapEntsCandidate is not null)
            {
                StageMapEntsCandidate(
                    stagingSession,
                    mapEntsCandidate,
                    mapEntsPatcher,
                    _adapters,
                    cancellationToken);
            }
            if (staticModelCandidate is not null)
            {
                StageStaticModelSuppressionCandidate(
                    stagingSession,
                    staticModelCandidate,
                    staticModelPatcher,
                    _adapters);
            }
            if (staticModelTranslationCandidate is not null)
            {
                StageStaticModelTranslationCandidate(
                    stagingSession,
                    staticModelTranslationCandidate,
                    staticModelTranslationPatcher,
                    _adapters);
            }
            if (staticModelRemovalCandidate is not null)
            {
                StageStaticModelRemovalCandidate(
                    stagingSession,
                    staticModelRemovalCandidate,
                    staticModelRemovalPatcher,
                    _adapters);
            }
            if (staticModelDuplicationCandidate is not null)
            {
                StageStaticModelDuplicationCandidate(
                    stagingSession,
                    staticModelDuplicationCandidate,
                    staticModelDuplicationPatcher,
                    _adapters);
            }

            FastFileEditingSaveSnapshot stagingCapture =
                stagingSession.CaptureForSave();
            ZoneBuildSnapshot stagingSnapshot =
                new ZoneBuildSnapshotBuilder(_adapters).Capture(
                    stagingSession,
                    stagingCapture);
            CompiledMapCandidateExpectation expectation =
                CompiledMapCandidateExpectation.Create(
                    imported.Bundle,
                    stagingSnapshot,
                    cancellationToken);
            var validators =
                new List<ITransactionalSaveCandidateValidator>(3);
            validators.Add(
                new CompiledMapCandidateValidator(
                    editingSession.Workspace,
                    editingSession,
                    sourceSessionRevision,
                    sourcePoolRevision,
                    currentBaselineDigest,
                    imported.Bundle,
                    expectation,
                    _adapters));
            if (request.CandidateValidator is not null)
                validators.Add(request.CandidateValidator);
            // Deliberately last: a caller validator cannot mutate either
            // source authority after built-in checks and still commit.
            validators.Add(
                new MapSaveRevisionCandidateValidator(
                    document,
                    capturedDocumentRevision,
                    editingSession,
                    sourceSessionRevision,
                    sourcePoolRevision,
                    imported.Bundle,
                    currentBaselineDigest));
            ITransactionalSaveCandidateValidator candidateValidator =
                new CompositeCandidateValidator(
                    validators.ToArray());
            SaveAsRequest stagingRequest = request with
            {
                ExpectedEditingSessionRevision =
                    stagingSession.Revision,
                CandidateValidator = candidateValidator
            };

            SaveAsResult saved = _transactionalSave.SaveAs(
                stagingSession,
                stagingRequest,
                progress,
                cancellationToken);
            if (!saved.Succeeded)
            {
                return new MapEditorSaveAsResult(
                    saved.Cancelled
                        ? MapEditorSaveAsStatus.Cancelled
                        : MapEditorSaveAsStatus.Failed,
                    destinationPath: null,
                    savedDocumentRevision: null,
                    plan,
                    saved,
                    saved.Diagnostics);
            }

            // Candidate reopen verification has already succeeded inside the
            // transactional pre-commit boundary. Acknowledge the exact shared
            // Studio capture and semantic map lease only after durable commit.
            try
            {
                saveLease.MarkCommitted(
                    sourceCapture,
                    saved.DestinationPath!);
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException)
            {
                return new MapEditorSaveAsResult(
                    MapEditorSaveAsStatus.Succeeded,
                    saved.DestinationPath,
                    savedDocumentRevision: null,
                    plan,
                    saved,
                    [
                        .. saved.Diagnostics,
                        .. normalizationDiagnostics,
                        "The verified fastfile was committed, but the in-memory " +
                        "Studio/map authoring state could not acknowledge " +
                        $"revision {capturedDocumentRevision}: " +
                        exception.Message,
                        "Reopen the saved fastfile before continuing."
                    ]);
            }
            return new MapEditorSaveAsResult(
                MapEditorSaveAsStatus.Succeeded,
                saved.DestinationPath,
                capturedDocumentRevision,
                plan,
                saved,
                [
                    .. saved.Diagnostics,
                    .. normalizationDiagnostics,
                    "The candidate was reopened and verified before commit.",
                    "Reopen the saved fastfile before another compiled-map " +
                    "save; the imported baseline is intentionally immutable."
                ]);
        }
        catch (OperationCanceledException)
        {
            return new MapEditorSaveAsResult(
                MapEditorSaveAsStatus.Cancelled,
                destinationPath: null,
                savedDocumentRevision: null,
                plan: null,
                transactionalResult: null,
                ["Map Save As was cancelled before commit."]);
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException)
        {
            return new MapEditorSaveAsResult(
                MapEditorSaveAsStatus.Failed,
                destinationPath: null,
                savedDocumentRevision: null,
                plan: null,
                transactionalResult: null,
                [$"{exception.GetType().Name}: {exception.Message}"]);
        }
    }

    private static void ValidateWorkspaceOwnership(
        ExistingMapImportResult imported,
        FastFileEditingSession editingSession,
        ICollection<string> diagnostics)
    {
        Guid documentId = editingSession.Document.DocumentId;
        foreach (CompiledMapAssetDescriptor asset in imported.Bundle.Assets)
        {
            if (asset.OwnerRow.DocumentId != documentId)
            {
                diagnostics.Add(
                    $"Compiled {asset.Kind} owner row does not belong to the " +
                    "supplied Studio editing session.");
            }
        }

        if (!string.Equals(
                editingSession.Workspace.TargetSource.PhysicalPath,
                editingSession.SavedDocumentState.PhysicalPath,
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(
                "The Studio editing session has already saved or rebased to " +
                "another path. Reopen that output before map persistence.");
        }
    }

    private static void ValidatePatchJournal(
        IEnumerable<MapPendingEdit> pendingEdits,
        ComWorldPrimaryLightPropertyPatchCandidate? primaryLightCandidate,
        FxWorldGlassDefinitionPropertyPatchCandidate?
            fxGlassDefinitionPropertyCandidate,
        MapEntsEntityStringPatchCandidate? mapEntsCandidate,
        StaticModelSuppressionPatchCandidate? staticModelCandidate,
        StaticModelTranslationPatchCandidate?
            staticModelTranslationCandidate,
        StaticModelRemovalPatchCandidate?
            staticModelRemovalCandidate,
        StaticModelDuplicationPatchCandidate?
            staticModelDuplicationCandidate,
        ICollection<string> diagnostics)
    {
        MapPendingEdit[] serialized = pendingEdits
            .Where(edit => edit.Kind != MapEditKind.EditorOnly)
            .ToArray();
        foreach (MapPendingEdit edit in serialized)
        {
            if (edit.Kind is not (
                    MapEditKind.PrimaryLightColor or
                    MapEditKind.PrimaryLightExponent or
                    MapEditKind.PrimaryLightSpotFalloff or
                    MapEditKind.FxGlassDefinitionHalfThickness or
                    MapEditKind.FxGlassDefinitionColor or
                    MapEditKind.MapEntityKeyValue or
                    MapEditKind.MapEntityCardinality or
                    MapEditKind.StaticModelVisibility or
                    MapEditKind.StaticModelTransform or
                    MapEditKind.StaticModelCardinality or
                    MapEditKind.StaticModelDuplication))
            {
                diagnostics.Add(
                    $"Pending serialized edit '{edit.Description}' is " +
                    $"{edit.Kind}; no compiled patcher is registered for it.");
            }
            if (edit.Kind == MapEditKind.StaticModelCardinality)
            {
                if (edit.SourceBindings.Count is not (2 or 3))
                {
                    diagnostics.Add(
                        $"Compiled-map edit '{edit.Description}' must carry " +
                        "exactly two removal bindings, or three when a Gfx " +
                        "provider receiver is carried forward.");
                }
                continue;
            }
            if (edit.Kind == MapEditKind.StaticModelDuplication)
            {
                if (edit.SourceBindings.Count != 2)
                {
                    diagnostics.Add(
                        $"Compiled-map edit '{edit.Description}' must carry " +
                        "exactly the two imported Gfx/Col template-record " +
                        "bindings.");
                }
                continue;
            }
            int expectedBindingCount = edit.Kind switch
                {
                    MapEditKind.StaticModelVisibility => 7,
                    MapEditKind.StaticModelTransform => 5,
                    _ => 1
                };
            if (edit.SourceBindings.Count != expectedBindingCount)
            {
                diagnostics.Add(
                    $"Compiled-map edit '{edit.Description}' must carry " +
                    $"exactly {expectedBindingCount} exact edited-field " +
                    "binding(s).");
            }
        }

        HashSet<SourceBindingId> lightJournalBindings = serialized
            .Where(edit => edit.Kind == MapEditKind.PrimaryLightColor)
            .SelectMany(edit => edit.SourceBindings)
            .ToHashSet();
        HashSet<SourceBindingId> lightPatchBindings =
            primaryLightCandidate?.ColorPatches
                .Select(patch => patch.SourceBinding)
                .ToHashSet() ?? [];
        if (!lightJournalBindings.SetEquals(lightPatchBindings))
        {
            diagnostics.Add(
                "The effective ComMap Color differences do not match the " +
                "active semantic command journal.");
        }

        HashSet<SourceBindingId> exponentJournalBindings = serialized
            .Where(edit =>
                edit.Kind == MapEditKind.PrimaryLightExponent)
            .SelectMany(edit => edit.SourceBindings)
            .ToHashSet();
        HashSet<SourceBindingId> exponentPatchBindings =
            primaryLightCandidate?.ExponentPatches
                .Select(patch => patch.SourceBinding)
                .ToHashSet() ?? [];
        if (!exponentJournalBindings.SetEquals(
                exponentPatchBindings))
        {
            diagnostics.Add(
                "The effective ComMap Exponent differences do not match the " +
                "active semantic command journal.");
        }

        HashSet<SourceBindingId> spotFalloffJournalBindings = serialized
            .Where(edit =>
                edit.Kind == MapEditKind.PrimaryLightSpotFalloff)
            .SelectMany(edit => edit.SourceBindings)
            .ToHashSet();
        HashSet<SourceBindingId> spotFalloffPatchBindings =
            primaryLightCandidate?.SpotFalloffPatches
                .Select(patch => patch.SourceBinding)
                .ToHashSet() ?? [];
        if (!spotFalloffJournalBindings.SetEquals(
                spotFalloffPatchBindings))
        {
            diagnostics.Add(
                "The effective ComMap type-2 inner-cone falloff differences " +
                "do not match the active semantic command journal.");
        }

        HashSet<SourceBindingId> fxGlassJournalBindings = serialized
            .Where(edit =>
                edit.Kind ==
                    MapEditKind.FxGlassDefinitionHalfThickness)
            .SelectMany(edit => edit.SourceBindings)
            .ToHashSet();
        HashSet<SourceBindingId> fxGlassPatchBindings =
            fxGlassDefinitionPropertyCandidate?.HalfThicknessPatches
                .Select(patch => patch.SourceBinding)
                .ToHashSet() ?? [];
        if (!fxGlassJournalBindings.SetEquals(
                fxGlassPatchBindings))
        {
            diagnostics.Add(
                "The effective FxGlassDef HalfThickness differences do not " +
                "match the active semantic command journal.");
        }

        HashSet<SourceBindingId> fxGlassColorJournalBindings = serialized
            .Where(edit =>
                edit.Kind == MapEditKind.FxGlassDefinitionColor)
            .SelectMany(edit => edit.SourceBindings)
            .ToHashSet();
        HashSet<SourceBindingId> fxGlassColorPatchBindings =
            fxGlassDefinitionPropertyCandidate?.ColorPatches
                .Select(patch => patch.SourceBinding)
                .ToHashSet() ?? [];
        if (!fxGlassColorJournalBindings.SetEquals(
                fxGlassColorPatchBindings))
        {
            diagnostics.Add(
                "The effective FxGlassDef Color differences do not match " +
                "the active semantic command journal.");
        }

        HashSet<SourceBindingId> mapEntJournalBindings = serialized
            .Where(edit => edit.Kind == MapEditKind.MapEntityKeyValue)
            .SelectMany(edit => edit.SourceBindings)
            .ToHashSet();
        HashSet<SourceBindingId> mapEntPatchBindings =
            mapEntsCandidate?.Patches
                .Select(patch => patch.SourceBinding)
                .ToHashSet() ?? [];
        if (!mapEntJournalBindings.SetEquals(mapEntPatchBindings))
        {
            diagnostics.Add(
                "The effective MapEnt property differences do not match the " +
                "active semantic command journal.");
        }

        HashSet<SourceBindingId> cardinalityJournalBindings = serialized
            .Where(edit =>
                edit.Kind == MapEditKind.MapEntityCardinality)
            .SelectMany(edit => edit.SourceBindings)
            .ToHashSet();
        HashSet<SourceBindingId> cardinalityPatchBindings =
            mapEntsCandidate?.CardinalityPatches
                .Select(patch => patch.SourceBinding)
                .ToHashSet() ?? [];
        if (!cardinalityJournalBindings.SetEquals(
                cardinalityPatchBindings))
        {
            diagnostics.Add(
                "The effective MapEnt cardinality differences do not match " +
                "the active semantic command journal.");
        }

        HashSet<SourceBindingId> staticModelJournalBindings = serialized
            .Where(edit =>
                edit.Kind == MapEditKind.StaticModelVisibility)
            .SelectMany(edit => edit.SourceBindings)
            .ToHashSet();
        HashSet<SourceBindingId> staticModelPatchBindings =
            staticModelCandidate?.Patches
                .SelectMany(patch => patch.SourceBindings)
                .ToHashSet() ?? [];
        if (!staticModelJournalBindings.SetEquals(
                staticModelPatchBindings))
        {
            diagnostics.Add(
                "The effective atomic static-model suppression differences " +
                "do not match the active semantic command journal.");
        }

        HashSet<SourceBindingId> translationJournalBindings = serialized
            .Where(edit =>
                edit.Kind == MapEditKind.StaticModelTransform)
            .SelectMany(edit => edit.SourceBindings)
            .ToHashSet();
        HashSet<SourceBindingId> translationPatchBindings =
            staticModelTranslationCandidate?.Patches
                .SelectMany(patch => patch.SourceBindings)
                .ToHashSet() ?? [];
        if (!translationJournalBindings.SetEquals(
                translationPatchBindings))
        {
            diagnostics.Add(
                "The effective proof-gated static-model translation " +
                "differences do not match the active semantic command " +
                "journal.");
        }

        MapPendingEdit[] removalJournalEdits = serialized
            .Where(edit =>
                edit.Kind == MapEditKind.StaticModelCardinality)
            .ToArray();
        var unmatchedRemovalPatches =
            staticModelRemovalCandidate?.Patches.ToList() ?? [];
        foreach (MapPendingEdit edit in removalJournalEdits)
        {
            HashSet<SourceBindingId> journalBindings =
                edit.SourceBindings.ToHashSet();
            StaticModelRemovalPatch[] matches =
                unmatchedRemovalPatches.Where(patch =>
                        journalBindings.SetEquals(
                            patch.SourceBindings))
                    .ToArray();
            if (matches.Length != 1)
            {
                diagnostics.Add(
                    "A proof-gated static-model removal journal entry does " +
                    "not bijectively match one exact compiled patch and its " +
                    "binding roles.");
                continue;
            }
            unmatchedRemovalPatches.Remove(matches[0]);
        }
        if (unmatchedRemovalPatches.Count != 0)
        {
            diagnostics.Add(
                "The effective proof-gated static-model removal patches " +
                "contain no matching active semantic command journal entry.");
        }

        MapPendingEdit[] duplicationJournalEdits = serialized
            .Where(edit =>
                edit.Kind == MapEditKind.StaticModelDuplication)
            .ToArray();
        var unmatchedDuplicationPatches =
            staticModelDuplicationCandidate?.Patches.ToList() ?? [];
        foreach (MapPendingEdit edit in duplicationJournalEdits)
        {
            HashSet<SourceBindingId> journalBindings =
                edit.SourceBindings.ToHashSet();
            StaticModelDuplicationPatch[] matches =
                unmatchedDuplicationPatches.Where(patch =>
                        journalBindings.SetEquals(
                            patch.SourceBindings))
                    .ToArray();
            if (matches.Length != 1)
            {
                diagnostics.Add(
                    "A proof-gated static-model duplication journal entry " +
                    "does not bijectively match one exact compiled patch and " +
                    "its two imported template authorities.");
                continue;
            }
            unmatchedDuplicationPatches.Remove(matches[0]);
        }
        if (unmatchedDuplicationPatches.Count != 0)
        {
            diagnostics.Add(
                "The effective proof-gated static-model duplication patch " +
                "has no matching active semantic command journal entry.");
        }
    }

    private static MapPendingEdit AttachCompilerEvidence(
        MapPendingEdit edit)
    {
        bool proven = edit.Kind switch
        {
            MapEditKind.PrimaryLightColor =>
                ComWorldPrimaryLightPropertyPatcher
                    .ColorPreservationCoverage.IsProven,
            MapEditKind.PrimaryLightExponent =>
                ComWorldPrimaryLightPropertyPatcher
                    .ExponentPreservationCoverage.IsProven,
            MapEditKind.PrimaryLightSpotFalloff =>
                ComWorldPrimaryLightPropertyPatcher
                    .SpotFalloffPreservationCoverage.IsProven,
            MapEditKind.FxGlassDefinitionHalfThickness =>
                FxWorldGlassDefinitionPropertyPatcher
                    .HalfThicknessPreservationCoverage.IsProven,
            MapEditKind.FxGlassDefinitionColor =>
                FxWorldGlassDefinitionPropertyPatcher
                    .ColorPreservationCoverage.IsProven,
            MapEditKind.MapEntityKeyValue =>
                MapEntsEntityStringPatcher
                    .PreservationCoverage.IsProven,
            MapEditKind.MapEntityCardinality =>
                MapEntsEntityStringPatcher
                    .PreservationCoverage.IsProven,
            MapEditKind.StaticModelVisibility =>
                StaticModelSuppressionPatcher
                    .GfxPreservationCoverage.IsProven,
            MapEditKind.StaticModelTransform =>
                StaticModelTranslationPatcher
                    .GfxPreservationCoverage.IsProven,
            MapEditKind.StaticModelCardinality =>
                StaticModelRemovalPatcher
                    .GfxPreservationCoverage.IsProven,
            MapEditKind.StaticModelDuplication =>
                StaticModelDuplicationPatcher
                    .GfxPreservationCoverage.IsProven,
            _ => false
        };
        return proven
            ? new MapPendingEdit(
                edit.Description,
                edit.Kind,
                edit.SourceBindings,
                preservationCoverageProven: true,
                hasRequiredBuilder: true)
            : edit;
    }

    private static void StageMapEntsCandidate(
        FastFileEditingSession stagingSession,
        MapEntsEntityStringPatchCandidate candidate,
        MapEntsEntityStringPatcher patcher,
        AssetAuthoringAdapterRegistry adapters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!candidate.Descriptor.IsNested)
        {
            IAssetAuthoringAdapter adapter =
                adapters.RequireAdapter(
                    IW4.FastFiles.Zone.XAssetType.MapEnts);
            _ = stagingSession.MutateAuthoredDraftAtRevision(
                stagingSession.Revision,
                candidate.Descriptor.OwnerRow,
                adapter,
                draft => patcher.ApplyValidatedCandidate(
                    RequireDraft<MapEntsDraft>(
                        draft,
                        "MapEnts"),
                    candidate));
            return;
        }

        CompiledMapAssetDescriptor owner =
            candidate.NestedOwnerDescriptor ??
            throw new InvalidOperationException(
                "A nested MapEnts candidate has no ColMap owner descriptor.");
        IAssetAuthoringAdapter clipAdapter =
            adapters.RequireAdapter(owner.SerializedType);
        _ = stagingSession.MutateAuthoredDraftAtRevision(
            stagingSession.Revision,
            owner.OwnerRow,
            clipAdapter,
            draft => patcher.ApplyValidatedCandidate(
                RequireDraft<ClipMapDraft>(
                    draft,
                    owner.SerializedType.ToString()),
                candidate,
                cancellationToken));
    }

    private static void StageFxGlassDefinitionPropertyCandidate(
        FastFileEditingSession stagingSession,
        FxWorldGlassDefinitionPropertyPatchCandidate candidate,
        FxWorldGlassDefinitionPropertyPatcher patcher,
        AssetAuthoringAdapterRegistry adapters)
    {
        ArgumentNullException.ThrowIfNull(stagingSession);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(patcher);
        ArgumentNullException.ThrowIfNull(adapters);
        CompiledMapAssetDescriptor descriptor =
            candidate.Descriptor ??
            throw new InvalidOperationException(
                "The FX glass candidate has no FxMap owner.");
        IAssetAuthoringAdapter adapter =
            adapters.RequireAdapter(
                IW4.FastFiles.Zone.XAssetType.FxMap);
        _ = stagingSession.MutateAuthoredDraftAtRevision(
            stagingSession.Revision,
            descriptor.OwnerRow,
            adapter,
            draft => patcher.ApplyValidatedCandidate(
                RequireDraft<FxWorldDraft>(
                    draft,
                    "FxMap"),
                candidate));
    }

    private static void StageStaticModelSuppressionCandidate(
        FastFileEditingSession stagingSession,
        StaticModelSuppressionPatchCandidate candidate,
        StaticModelSuppressionPatcher patcher,
        AssetAuthoringAdapterRegistry adapters)
    {
        ArgumentNullException.ThrowIfNull(stagingSession);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(patcher);
        ArgumentNullException.ThrowIfNull(adapters);
        CompiledMapAssetDescriptor gfxDescriptor =
            candidate.GfxDescriptor ??
            throw new InvalidOperationException(
                "The suppression candidate has no GfxMap owner.");
        CompiledMapAssetDescriptor clipDescriptor =
            candidate.ClipDescriptor ??
            throw new InvalidOperationException(
                "The suppression candidate has no ColMap owner.");

        IAssetAuthoringAdapter gfxAdapter =
            adapters.RequireAdapter(gfxDescriptor.SerializedType);
        _ = stagingSession.MutateAuthoredDraftAtRevision(
            stagingSession.Revision,
            gfxDescriptor.OwnerRow,
            gfxAdapter,
            draft => patcher.ApplyValidatedGfxCandidate(
                RequireDraft<GfxWorldDraft>(
                    draft,
                    "GfxMap"),
                candidate));

        IAssetAuthoringAdapter clipAdapter =
            adapters.RequireAdapter(clipDescriptor.SerializedType);
        _ = stagingSession.MutateAuthoredDraftAtRevision(
            stagingSession.Revision,
            clipDescriptor.OwnerRow,
            clipAdapter,
            draft => patcher.ApplyValidatedCollisionCandidate(
                RequireDraft<ClipMapDraft>(
                    draft,
                    clipDescriptor.SerializedType.ToString()),
                candidate));
    }

    private static void StageStaticModelTranslationCandidate(
        FastFileEditingSession stagingSession,
        StaticModelTranslationPatchCandidate candidate,
        StaticModelTranslationPatcher patcher,
        AssetAuthoringAdapterRegistry adapters)
    {
        ArgumentNullException.ThrowIfNull(stagingSession);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(patcher);
        ArgumentNullException.ThrowIfNull(adapters);
        CompiledMapAssetDescriptor gfxDescriptor =
            candidate.GfxDescriptor ??
            throw new InvalidOperationException(
                "The translation candidate has no GfxMap owner.");
        CompiledMapAssetDescriptor clipDescriptor =
            candidate.ClipDescriptor ??
            throw new InvalidOperationException(
                "The translation candidate has no ColMap owner.");

        IAssetAuthoringAdapter gfxAdapter =
            adapters.RequireAdapter(gfxDescriptor.SerializedType);
        _ = stagingSession.MutateAuthoredDraftAtRevision(
            stagingSession.Revision,
            gfxDescriptor.OwnerRow,
            gfxAdapter,
            draft => patcher.ApplyValidatedGfxCandidate(
                RequireDraft<GfxWorldDraft>(
                    draft,
                    "GfxMap"),
                candidate));

        IAssetAuthoringAdapter clipAdapter =
            adapters.RequireAdapter(clipDescriptor.SerializedType);
        _ = stagingSession.MutateAuthoredDraftAtRevision(
            stagingSession.Revision,
            clipDescriptor.OwnerRow,
            clipAdapter,
            draft => patcher.ApplyValidatedCollisionCandidate(
                RequireDraft<ClipMapDraft>(
                    draft,
                    clipDescriptor.SerializedType.ToString()),
                candidate));
    }

    private static void StageStaticModelRemovalCandidate(
        FastFileEditingSession stagingSession,
        StaticModelRemovalPatchCandidate candidate,
        StaticModelRemovalPatcher patcher,
        AssetAuthoringAdapterRegistry adapters)
    {
        ArgumentNullException.ThrowIfNull(stagingSession);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(patcher);
        ArgumentNullException.ThrowIfNull(adapters);
        CompiledMapAssetDescriptor gfxDescriptor =
            candidate.GfxDescriptor ??
            throw new InvalidOperationException(
                "The removal candidate has no GfxMap owner.");
        CompiledMapAssetDescriptor clipDescriptor =
            candidate.ClipDescriptor ??
            throw new InvalidOperationException(
                "The removal candidate has no ColMap owner.");

        IAssetAuthoringAdapter gfxAdapter =
            adapters.RequireAdapter(gfxDescriptor.SerializedType);
        _ = stagingSession.MutateAuthoredDraftAtRevision(
            stagingSession.Revision,
            gfxDescriptor.OwnerRow,
            gfxAdapter,
            draft => patcher.ApplyValidatedGfxCandidate(
                RequireDraft<GfxWorldDraft>(
                    draft,
                    "GfxMap"),
                candidate));

        IAssetAuthoringAdapter clipAdapter =
            adapters.RequireAdapter(clipDescriptor.SerializedType);
        _ = stagingSession.MutateAuthoredDraftAtRevision(
            stagingSession.Revision,
            clipDescriptor.OwnerRow,
            clipAdapter,
            draft => patcher.ApplyValidatedCollisionCandidate(
                RequireDraft<ClipMapDraft>(
                    draft,
                    clipDescriptor.SerializedType.ToString()),
                candidate));
    }

    private static void StageStaticModelDuplicationCandidate(
        FastFileEditingSession stagingSession,
        StaticModelDuplicationPatchCandidate candidate,
        StaticModelDuplicationPatcher patcher,
        AssetAuthoringAdapterRegistry adapters)
    {
        ArgumentNullException.ThrowIfNull(stagingSession);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(patcher);
        ArgumentNullException.ThrowIfNull(adapters);
        CompiledMapAssetDescriptor gfxDescriptor =
            candidate.GfxDescriptor ??
            throw new InvalidOperationException(
                "The duplication candidate has no GfxMap owner.");
        CompiledMapAssetDescriptor clipDescriptor =
            candidate.ClipDescriptor ??
            throw new InvalidOperationException(
                "The duplication candidate has no ColMap owner.");

        IAssetAuthoringAdapter gfxAdapter =
            adapters.RequireAdapter(gfxDescriptor.SerializedType);
        _ = stagingSession.MutateAuthoredDraftAtRevision(
            stagingSession.Revision,
            gfxDescriptor.OwnerRow,
            gfxAdapter,
            draft => patcher.ApplyValidatedGfxCandidate(
                RequireDraft<GfxWorldDraft>(
                    draft,
                    "GfxMap"),
                candidate));

        IAssetAuthoringAdapter clipAdapter =
            adapters.RequireAdapter(clipDescriptor.SerializedType);
        _ = stagingSession.MutateAuthoredDraftAtRevision(
            stagingSession.Revision,
            clipDescriptor.OwnerRow,
            clipAdapter,
            draft => patcher.ApplyValidatedCollisionCandidate(
                RequireDraft<ClipMapDraft>(
                    draft,
                    clipDescriptor.SerializedType.ToString()),
                candidate));
    }

    private static TDraft RequireDraft<TDraft>(
        object value,
        string role)
        where TDraft : class =>
        value as TDraft ??
        throw new InvalidDataException(
            $"{role} staging expected detached draft type " +
            $"'{typeof(TDraft).FullName}', but received " +
            $"'{value?.GetType().FullName ?? "null"}'.");

    private static MapEditorSaveAsResult Rejected(
        MapSavePlan? plan,
        params string[] diagnostics) =>
        Rejected(plan, (IEnumerable<string>)diagnostics);

    private static MapEditorSaveAsResult Rejected(
        MapSavePlan? plan,
        IEnumerable<string> diagnostics) =>
        new(
            MapEditorSaveAsStatus.Rejected,
            destinationPath: null,
            savedDocumentRevision: null,
            plan,
            transactionalResult: null,
            diagnostics);

    private sealed class CompositeCandidateValidator(
        params ITransactionalSaveCandidateValidator[] validators)
        : ITransactionalSaveCandidateValidator
    {
        private readonly ITransactionalSaveCandidateValidator[] _validators =
            validators;

        public IReadOnlyList<string> Validate(
            string candidatePath,
            CancellationToken cancellationToken = default)
        {
            var diagnostics = new List<string>();
            foreach (ITransactionalSaveCandidateValidator validator in
                     _validators)
            {
                cancellationToken.ThrowIfCancellationRequested();
                diagnostics.AddRange(
                    validator.Validate(candidatePath, cancellationToken));
            }
            return diagnostics;
        }
    }

}
