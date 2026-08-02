using System.Collections.ObjectModel;
using System.Security.Cryptography;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Compilation.Validation;
using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Entities;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.Patching;

/// <summary>
/// Reviewable evidence for one existing MapEnt property-content replacement.
/// Entity and property ordinals are stable within the exact immutable byte
/// snapshots identified by the before/after digests.
/// </summary>
internal sealed record MapEntsPropertyPatch(
    MapObjectId ObjectId,
    SourceBindingId SourceBinding,
    MapEntEntityOrdinal EntityOrdinal,
    MapEntPropertyOrdinal PropertyOrdinal,
    MapEntPropertyField Field,
    MapEntSourceSpan OriginalContentSpan,
    string OriginalText,
    string ReplacementText,
    string BeforeContentDigest,
    string AfterContentDigest,
    MapEntityCompilationAssessment BeforeRelationship,
    MapEntityCompilationAssessment AfterRelationship,
    MapEntityPropertyEditAssessment PropertyEditAssessment);

internal sealed class MapEntsCardinalityPatch
{
    private readonly byte[] _entityBytes;

    public MapEntsCardinalityPatch(
        MapObjectId objectId,
        SourceBindingId sourceBinding,
        MapEntityCardinalityOperation operation,
        MapEntEntityOrdinal entityOrdinal,
        ReadOnlySpan<byte> entityBytes,
        string beforeContentDigest,
        string afterContentDigest,
        MapEntityCardinalityAssessment assessment)
    {
        if (objectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        if (sourceBinding.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceBinding));
        if (!Enum.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        ArgumentException.ThrowIfNullOrWhiteSpace(beforeContentDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(afterContentDigest);
        ArgumentNullException.ThrowIfNull(assessment);
        if (entityBytes.IsEmpty)
            throw new ArgumentException(
                "A cardinality patch requires exact entity bytes.",
                nameof(entityBytes));

        ObjectId = objectId;
        SourceBinding = sourceBinding;
        Operation = operation;
        EntityOrdinal = entityOrdinal;
        _entityBytes = entityBytes.ToArray();
        BeforeContentDigest = beforeContentDigest;
        AfterContentDigest = afterContentDigest;
        Assessment = assessment;
    }

    public MapObjectId ObjectId { get; }
    public SourceBindingId SourceBinding { get; }
    public MapEntityCardinalityOperation Operation { get; }
    public MapEntEntityOrdinal EntityOrdinal { get; }
    public byte[] EntityBytes => _entityBytes.ToArray();
    public string BeforeContentDigest { get; }
    public string AfterContentDigest { get; }
    public MapEntityCardinalityAssessment Assessment { get; }
}

internal sealed record MapEntsJournalPatch
{
    private MapEntsJournalPatch(
        MapEntsPropertyPatch? property,
        MapEntsCardinalityPatch? cardinality)
    {
        if ((property is null) == (cardinality is null))
        {
            throw new ArgumentException(
                "A MapEnt journal patch must contain exactly one transition.");
        }
        Property = property;
        Cardinality = cardinality;
    }

    public MapEntsPropertyPatch? Property { get; }
    public MapEntsCardinalityPatch? Cardinality { get; }

    public static MapEntsJournalPatch ForProperty(
        MapEntsPropertyPatch value) =>
        new(
            value ?? throw new ArgumentNullException(nameof(value)),
            cardinality: null);

    public static MapEntsJournalPatch ForCardinality(
        MapEntsCardinalityPatch value) =>
        new(
            property: null,
            value ?? throw new ArgumentNullException(nameof(value)));
}

/// <summary>
/// Fully detached MapEnts candidate. For nested definitions, the candidate
/// also carries the exact expected ColMap owner after replacing only the
/// incoming MapEnts entity-string bytes.
/// </summary>
internal sealed class MapEntsEntityStringPatchCandidate
{
    public MapEntsEntityStringPatchCandidate(
        string mapIdentity,
        CompiledMapAssetDescriptor descriptor,
        MapEntsBuildData baseline,
        MapEntsBuildData buildData,
        IEnumerable<MapEntsPropertyPatch> patches,
        IEnumerable<MapEntsCardinalityPatch>? cardinalityPatches,
        IEnumerable<MapEntsJournalPatch>? orderedPatches,
        MapPatchValidation validation,
        MapPatchValidation structuralValidation,
        MapAssetKind? nestedOwnerKind = null,
        CompiledMapAssetDescriptor? nestedOwnerDescriptor = null,
        ClipMapBuildData? nestedOwnerBaseline = null,
        ClipMapBuildData? nestedOwnerBuildData = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapIdentity);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(buildData);
        ArgumentNullException.ThrowIfNull(patches);
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(structuralValidation);

        bool carriesNestedOwner =
            nestedOwnerKind is not null ||
            nestedOwnerDescriptor is not null ||
            nestedOwnerBaseline is not null ||
            nestedOwnerBuildData is not null;
        if (descriptor.IsNested != carriesNestedOwner ||
            (carriesNestedOwner &&
             (nestedOwnerKind is not (
                  MapAssetKind.ColMapMp or
                  MapAssetKind.ColMapSp) ||
              nestedOwnerDescriptor is null ||
              nestedOwnerBaseline is null ||
              nestedOwnerBuildData is null)))
        {
            throw new ArgumentException(
                "A nested MapEnts candidate must carry one complete ColMap " +
                "owner transition; a top-level candidate must carry none.");
        }

        byte[] baselineEntityStringBytes =
            baseline.GetEntityStringBytesCopy();
        byte[] candidateEntityStringBytes =
            buildData.GetEntityStringBytesCopy();
        MapIdentity = mapIdentity;
        Descriptor = descriptor;
        Baseline = baseline.WithEntityStringBytes(
            baselineEntityStringBytes);
        BuildData = buildData.WithEntityStringBytes(
            candidateEntityStringBytes);
        Patches = new ReadOnlyCollection<MapEntsPropertyPatch>(
            patches.ToArray());
        CardinalityPatches =
            new ReadOnlyCollection<MapEntsCardinalityPatch>(
                (cardinalityPatches ?? []).ToArray());
        OrderedPatches = new ReadOnlyCollection<MapEntsJournalPatch>(
            (orderedPatches ??
             Patches.Select(MapEntsJournalPatch.ForProperty))
                .ToArray());
        Validation = validation;
        StructuralValidation = structuralValidation;
        HasEffectiveEntityStringChange =
            !baselineEntityStringBytes.AsSpan().SequenceEqual(
                candidateEntityStringBytes);
        BaselineEntityStringDigest = Convert.ToHexString(
            SHA256.HashData(baselineEntityStringBytes));
        CandidateEntityStringDigest = Convert.ToHexString(
            SHA256.HashData(candidateEntityStringBytes));
        NestedOwnerKind = nestedOwnerKind;
        NestedOwnerDescriptor = nestedOwnerDescriptor;
        NestedOwnerBaseline = nestedOwnerBaseline is null
            ? null
            : CloneNestedOwner(nestedOwnerBaseline);
        NestedOwnerBuildData = nestedOwnerBuildData is null
            ? null
            : CloneNestedOwner(nestedOwnerBuildData);
    }

    public string MapIdentity { get; }
    public CompiledMapAssetDescriptor Descriptor { get; }
    public MapEntsBuildData Baseline { get; }
    public MapEntsBuildData BuildData { get; }
    public IReadOnlyList<MapEntsPropertyPatch> Patches { get; }
    public IReadOnlyList<MapEntsCardinalityPatch> CardinalityPatches { get; }
    internal IReadOnlyList<MapEntsJournalPatch> OrderedPatches { get; }
    public MapPatchValidation Validation { get; }
    public MapPatchValidation StructuralValidation { get; }
    public bool HasEffectiveEntityStringChange { get; }
    public string BaselineEntityStringDigest { get; }
    public string CandidateEntityStringDigest { get; }
    public bool CanOmitAsVerifiedNetZero =>
        !HasEffectiveEntityStringChange &&
        StructuralValidation.IsValid;
    public MapAssetKind? NestedOwnerKind { get; }
    public CompiledMapAssetDescriptor? NestedOwnerDescriptor { get; }
    public ClipMapBuildData? NestedOwnerBaseline { get; }
    public ClipMapBuildData? NestedOwnerBuildData { get; }

    private static ClipMapBuildData CloneNestedOwner(
        ClipMapBuildData value)
    {
        if (value.References.MapEntsLink?.IncomingDefinition is not
            IMapEntsBuildData mapEnts)
        {
            throw new InvalidDataException(
                "A nested MapEnts candidate owner has no detached incoming " +
                "MapEnts definition.");
        }
        return value.WithNestedMapEntsEntityStringBytes(
            mapEnts.GetEntityStringBytesCopy());
    }
}

/// <summary>
/// Produces a byte-authoritative replacement for existing MapEnt property key
/// or value content. Entity/property cardinality, ordering, duplicate keys,
/// untouched byte slices, MapEnts metadata, and any ColMap owner are preserved.
/// Unknown or compiled-counterpart entities and unproven exact
/// classname/key/operation tuples fail closed before draft mutation.
/// </summary>
internal sealed class MapEntsEntityStringPatcher
{
    private static readonly MapEntsBodyEmitter Emitter = new();
    private readonly MapEntityConsumerCatalog _consumerCatalog;

    public MapEntsEntityStringPatcher(
        MapEntityConsumerCatalog? consumerCatalog = null) =>
        _consumerCatalog =
            consumerCatalog ?? MapEntityConsumerCatalog.ConservativeIw4;

    public static MapPreservationCoverage PreservationCoverage { get; } = new(
        MapAssetKind.MapEnts,
        "Existing MapEnt property content and proven tail script_origin cardinality",
        MapPreservationCoverageStatus.Proven,
        preservedFields:
        [
            "MapEnts name and target-row identity",
            "MapEnts trigger model/hull/slab values, counts, and ordering",
            "MapEnts stages values, count, and ordering",
            "MapEnts 0x29-0x2B tail padding",
            "All pre-existing non-removed entity/property ordering",
            "Duplicate property keys and every untouched byte slice",
            "Optional trailing NUL form",
            "Nested ColMap pointer form, reference identity, collision payload, and all other links"
        ],
        mutableFields:
        [
            "entityStringBytes.entities[existing].properties[existing].key content (authorized tuples only)",
            "entityStringBytes.entities[existing].properties[existing].value content (authorized tuples only)",
            "entityStringBytes tail append of one canonical script_origin row (authorized journal transitions only)",
            "entityStringBytes physical-final script_origin row removal (authorized journal transitions only)"
        ]);

    public MapEntsEntityStringPatchCandidate Prepare(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        IEnumerable<CompiledSourceBinding> sourceBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(sourceBindings);
        cancellationToken.ThrowIfCancellationRequested();

        EditorMapEntitySource source = document.EntitySource ??
            throw new InvalidOperationException(
                "The semantic document has no byte-authoritative MapEnts source.");
        IMapEditCommand[] mapEntCommands = document.History.ActiveCommands
            .Where(command => command.Kind is (
                MapEditKind.MapEntityKeyValue or
                MapEditKind.MapEntityCardinality))
            .ToArray();
        if (mapEntCommands.Any(command =>
                command.Kind == MapEditKind.MapEntityCardinality))
        {
            return PrepareCardinalityAwareJournal(
                document,
                bundle,
                sourceBindings,
                mapEntCommands,
                cancellationToken);
        }

        MapEntsSyntaxDocument replay = MapEntsSyntaxParser.Parse(
            source.GetBaselineBytesCopy(),
            cancellationToken);
        var edits = new List<MapEntsPropertyEdit>();
        foreach (SetMapEntityPropertyCommand command in
                 document.History.ActiveCommands
                     .OfType<SetMapEntityPropertyCommand>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            EditorEntity entity =
                document.Entities.SingleOrDefault(value =>
                    value.Id == command.EntityId) ??
                throw new InvalidOperationException(
                    $"MapEnt command target {command.EntityId} is not in the " +
                    "semantic document.");
            MapEntsPropertyEdit edit = replay.PreparePropertyReplacement(
                entity.SyntaxOrdinal,
                command.PropertyOrdinal,
                command.Field,
                command.Replacement,
                cancellationToken);
            edits.Add(edit);
            replay = edit.After;
        }

        return Prepare(
            document,
            bundle,
            sourceBindings,
            edits,
            cancellationToken);
    }

    private MapEntsEntityStringPatchCandidate
        PrepareCardinalityAwareJournal(
            EditorMapDocument document,
            CompiledMapBundle bundle,
            IEnumerable<CompiledSourceBinding> sourceBindings,
            IReadOnlyList<IMapEditCommand> commands,
            CancellationToken cancellationToken)
    {
        EditorMapEntitySource source = document.EntitySource ??
            throw new InvalidOperationException(
                "The semantic document has no byte-authoritative MapEnts source.");
        if (!bundle.TryGetBaseline(
                MapAssetKind.MapEnts,
                out MapEntsBuildData? baseline) ||
            baseline is null)
        {
            throw new InvalidOperationException(
                "The compiled map bundle has no detached MapEnts build-data " +
                "baseline.");
        }

        CompiledMapAssetDescriptor descriptor =
            bundle.RequireAsset(MapAssetKind.MapEnts);
        MapEntsSyntaxDocument replay = MapEntsSyntaxParser.Parse(
            source.GetBaselineBytesCopy(),
            cancellationToken);
        var identityLedger = replay.Entities
            .Select(entity => DeterministicMapIdentity.Object(
                bundle.MapIdentity,
                XAssetType.MapEnts.ToString(),
                descriptor.AssetName,
                "mapent-entity",
                entity.Ordinal.Value))
            .ToList();
        var transitions = new List<PreparedJournalTransition>();
        foreach (IMapEditCommand command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (command)
            {
                case SetMapEntityPropertyCommand property:
                {
                    int currentOrdinal = identityLedger.IndexOf(
                        property.EntityId);
                    if (currentOrdinal < 0)
                    {
                        throw new InvalidOperationException(
                            $"MapEnt property command target " +
                            $"{property.EntityId} is absent from the ordered " +
                            "identity journal.");
                    }
                    MapEntsPropertyEdit edit =
                        replay.PreparePropertyReplacement(
                            new MapEntEntityOrdinal(currentOrdinal),
                            property.PropertyOrdinal,
                            property.Field,
                            property.Replacement,
                            cancellationToken);
                    if (!string.Equals(
                            edit.OriginalText,
                            property.ExpectedOriginalText,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "MapEnt property command original text does not " +
                            "match exact ordered journal replay.");
                    }
                    transitions.Add(
                        PreparedJournalTransition.ForProperty(
                            property.EntityId,
                            edit));
                    replay = edit.After;
                    break;
                }

                case AppendScriptOriginEntityCommand append:
                {
                    if (identityLedger.Contains(append.EntityId))
                    {
                        throw new InvalidOperationException(
                            $"Authored MapEnt identity {append.EntityId} was " +
                            "already present during ordered journal replay.");
                    }
                    MapEntsCardinalityEdit edit =
                        replay.PrepareScriptOriginAppend(
                            append.Definition.ToCanonicalProperties(),
                            cancellationToken);
                    MapEntityCardinalityAssessment assessment =
                        AppendScriptOriginEntityCommand.Assess(
                            edit,
                            MapEntityCardinalityOperation.Append);
                    if (assessment != append.ExpectedAssessment ||
                        !assessment.IsPatchAuthorized)
                    {
                        throw new InvalidOperationException(
                            "script_origin append evidence changed during " +
                            "ordered journal replay.");
                    }
                    transitions.Add(
                        PreparedJournalTransition.ForCardinality(
                            append.EntityId,
                            edit,
                            assessment));
                    identityLedger.Add(append.EntityId);
                    replay = edit.After;
                    break;
                }

                case RemoveFinalScriptOriginEntityCommand remove:
                {
                    if (identityLedger.Count == 0 ||
                        identityLedger[^1] != remove.EntityId)
                    {
                        throw new InvalidOperationException(
                            "A script_origin removal no longer targets the " +
                            "physical final identity during ordered replay.");
                    }
                    var ordinal = new MapEntEntityOrdinal(
                        identityLedger.Count - 1);
                    MapEntsCardinalityEdit edit =
                        replay.PrepareFinalScriptOriginRemoval(
                            ordinal,
                            cancellationToken);
                    MapEntityCardinalityAssessment assessment =
                        AppendScriptOriginEntityCommand.Assess(
                            edit,
                            MapEntityCardinalityOperation.Remove);
                    if (assessment != remove.ExpectedAssessment ||
                        !assessment.IsPatchAuthorized)
                    {
                        throw new InvalidOperationException(
                            "script_origin removal evidence changed during " +
                            "ordered journal replay.");
                    }
                    transitions.Add(
                        PreparedJournalTransition.ForCardinality(
                            remove.EntityId,
                            edit,
                            assessment));
                    identityLedger.RemoveAt(identityLedger.Count - 1);
                    replay = edit.After;
                    break;
                }

                default:
                    throw new InvalidOperationException(
                        $"MapEnt journal contains unsupported command type " +
                        $"'{command.GetType().FullName}'.");
            }
        }

        if (!replay.HasSameBytes(source.Syntax) ||
            !identityLedger.SequenceEqual(
                document.Entities.Select(value => value.Id)))
        {
            throw new InvalidOperationException(
                "Ordered MapEnt command replay does not reproduce the current " +
                "semantic syntax and identity collection.");
        }

        return PrepareCardinalityAwareCandidate(
            document,
            bundle,
            descriptor,
            baseline,
            sourceBindings,
            transitions,
            identityLedger,
            cancellationToken);
    }

    private MapEntsEntityStringPatchCandidate
        PrepareCardinalityAwareCandidate(
            EditorMapDocument document,
            CompiledMapBundle bundle,
            CompiledMapAssetDescriptor descriptor,
            MapEntsBuildData baseline,
            IEnumerable<CompiledSourceBinding> sourceBindings,
            IReadOnlyList<PreparedJournalTransition> transitions,
            IReadOnlyList<MapObjectId> expectedFinalIdentities,
            CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        ValidateDescriptorShape(descriptor, diagnostics);
        ValidateRawSourceBinding(
            document,
            bundle,
            descriptor,
            baseline,
            sourceBindings,
            diagnostics);

        byte[] baselineBytes = baseline.GetEntityStringBytesCopy();
        MapEntsSyntaxDocument importedSyntax = MapEntsSyntaxParser.Parse(
            baselineBytes,
            cancellationToken);
        MapEntsSyntaxDocument syntax = importedSyntax;
        if (!syntax.CanEdit)
        {
            diagnostics.Add(
                "The imported MapEnts bytes are not safely editable: " +
                FormatSyntaxDiagnostics(syntax));
        }

        var propertyPatches = new List<MapEntsPropertyPatch>();
        var cardinalityPatches = new List<MapEntsCardinalityPatch>();
        var orderedPatches = new List<MapEntsJournalPatch>();
        foreach (PreparedJournalTransition transition in transitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (transition.Property is { } propertyEdit)
            {
                MapEntsSyntaxDocument before = syntax;
                MapEntsSyntaxDocument after = propertyEdit.Apply(before);
                ValidateEditWasPreparedBySyntaxLayer(
                    before,
                    after,
                    propertyEdit,
                    diagnostics,
                    cancellationToken);
                MapEntsSyntaxProperty property = before.GetProperty(
                    propertyEdit.EntityOrdinal,
                    propertyEdit.PropertyOrdinal);
                MapEntityCompilationAssessment beforeRelationship =
                    Classify(before, propertyEdit.EntityOrdinal);
                MapEntityCompilationAssessment afterRelationship =
                    Classify(after, propertyEdit.EntityOrdinal);
                MapEntityPropertyEditAssessment propertyAssessment =
                    ClassifyPropertyEdit(
                        before,
                        after,
                        propertyEdit.EntityOrdinal,
                        propertyEdit.PropertyOrdinal,
                        propertyEdit.Field);
                ValidateRelationships(
                    propertyEdit,
                    beforeRelationship,
                    afterRelationship,
                    propertyAssessment,
                    diagnostics);
                SourceBindingId propertyBinding =
                    ValidateJournalPropertyBinding(
                        bundle,
                        descriptor,
                        transition.ObjectId,
                        propertyEdit,
                        sourceBindings,
                        diagnostics);
                var patch = new MapEntsPropertyPatch(
                    transition.ObjectId,
                    propertyBinding,
                    propertyEdit.EntityOrdinal,
                    propertyEdit.PropertyOrdinal,
                    propertyEdit.Field,
                    propertyEdit.Field == MapEntPropertyField.Key
                        ? property.KeyContentSpan
                        : property.ValueContentSpan,
                    propertyEdit.OriginalText,
                    propertyEdit.ReplacementText,
                    before.ContentDigest,
                    after.ContentDigest,
                    beforeRelationship,
                    afterRelationship,
                    propertyAssessment);
                propertyPatches.Add(patch);
                orderedPatches.Add(
                    MapEntsJournalPatch.ForProperty(patch));
                syntax = after;
                continue;
            }

            MapEntsCardinalityEdit cardinalityEdit =
                transition.Cardinality ??
                throw new InvalidOperationException(
                    "Prepared MapEnt journal transition is empty.");
            MapEntsSyntaxDocument cardinalityBefore = syntax;
            MapEntsSyntaxDocument cardinalityAfter =
                cardinalityEdit.Apply(cardinalityBefore);
            ValidateCardinalityWasPreparedBySyntaxLayer(
                cardinalityBefore,
                cardinalityAfter,
                cardinalityEdit,
                diagnostics,
                cancellationToken);
            MapEntityCardinalityAssessment currentAssessment =
                AppendScriptOriginEntityCommand.Assess(
                    cardinalityEdit,
                    cardinalityEdit.Operation);
            if (currentAssessment != transition.CardinalityAssessment ||
                !currentAssessment.IsPatchAuthorized)
            {
                diagnostics.Add(
                    $"MapEnt {cardinalityEdit.Operation} at entity " +
                    $"{cardinalityEdit.EntityOrdinal} lacks the exact " +
                    "executable-backed script_origin evidence: " +
                    currentAssessment.Evidence);
            }
            var cardinalityPatch = new MapEntsCardinalityPatch(
                transition.ObjectId,
                document.EntitySource!.SourceBinding,
                cardinalityEdit.Operation,
                cardinalityEdit.EntityOrdinal,
                cardinalityEdit.GetEntityBytesCopy(),
                cardinalityBefore.ContentDigest,
                cardinalityAfter.ContentDigest,
                currentAssessment);
            cardinalityPatches.Add(cardinalityPatch);
            orderedPatches.Add(
                MapEntsJournalPatch.ForCardinality(cardinalityPatch));
            syntax = cardinalityAfter;
        }

        ValidateCardinalitySemanticProjection(
            document,
            syntax,
            expectedFinalIdentities,
            diagnostics);
        byte[] replacementBytes = syntax.Serialize();
        var buildData = baseline.WithEntityStringBytes(replacementBytes);
        MapPatchValidation preservation =
            ValidateCardinalityAwarePreservation(
                baseline,
                buildData,
                orderedPatches,
                cancellationToken);
        diagnostics.AddRange(preservation.Diagnostics);
        if (orderedPatches.Count == 0)
        {
            diagnostics.Add(
                "No effective MapEnt journal transition was supplied.");
        }
        MapAssetKind? ownerKind = null;
        CompiledMapAssetDescriptor? ownerDescriptor = null;
        ClipMapBuildData? ownerBaseline = null;
        ClipMapBuildData? ownerBuildData = null;
        if (descriptor.IsNested)
        {
            PrepareNestedOwner(
                bundle,
                descriptor,
                baseline,
                buildData,
                replacementBytes,
                diagnostics,
                out ownerKind,
                out ownerDescriptor,
                out ownerBaseline,
                out ownerBuildData,
                cancellationToken);
        }

        var structuralValidation = new MapPatchValidation(diagnostics);
        if (baselineBytes.AsSpan().SequenceEqual(replacementBytes))
        {
            diagnostics.Add(
                "The MapEnt edit sequence produces the original entity-string bytes.");
        }

        return new MapEntsEntityStringPatchCandidate(
            bundle.MapIdentity,
            descriptor,
            baseline,
            buildData,
            propertyPatches,
            cardinalityPatches,
            orderedPatches,
            new MapPatchValidation(diagnostics),
            structuralValidation,
            ownerKind,
            ownerDescriptor,
            ownerBaseline,
            ownerBuildData);
    }

    public MapEntsEntityStringPatchCandidate Prepare(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        IEnumerable<CompiledSourceBinding> sourceBindings,
        IEnumerable<MapEntsPropertyEdit> edits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(sourceBindings);
        ArgumentNullException.ThrowIfNull(edits);
        cancellationToken.ThrowIfCancellationRequested();

        if (!bundle.TryGetBaseline(
                MapAssetKind.MapEnts,
                out MapEntsBuildData? baseline) ||
            baseline is null)
        {
            throw new InvalidOperationException(
                "The compiled map bundle has no detached MapEnts build-data " +
                "baseline.");
        }

        CompiledMapAssetDescriptor descriptor =
            bundle.RequireAsset(MapAssetKind.MapEnts);
        var diagnostics = new List<string>();
        ValidateDescriptorShape(descriptor, diagnostics);
        ValidateRawSourceBinding(
            document,
            bundle,
            descriptor,
            baseline,
            sourceBindings,
            diagnostics);

        byte[] baselineBytes = baseline.GetEntityStringBytesCopy();
        MapEntsSyntaxDocument syntax = MapEntsSyntaxParser.Parse(
            baselineBytes,
            cancellationToken);
        MapEntsSyntaxDocument importedSyntax = syntax;
        if (!syntax.CanEdit)
        {
            diagnostics.Add(
                "The imported MapEnts bytes are not safely editable: " +
                FormatSyntaxDiagnostics(syntax));
        }

        var patches = new List<MapEntsPropertyPatch>();
        foreach (MapEntsPropertyEdit edit in edits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (edit is null)
            {
                diagnostics.Add(
                    "The MapEnt property-edit sequence contains a null entry.");
                continue;
            }

            MapEntsSyntaxDocument before = syntax;
            if (edit.IsNoChange)
            {
                diagnostics.Add(
                    $"MapEnt entity {edit.EntityOrdinal}, property " +
                    $"{edit.PropertyOrdinal} is a no-op replacement.");
                continue;
            }

            MapEntsSyntaxDocument after;
            try
            {
                after = edit.Apply(before);
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException)
            {
                diagnostics.Add(
                    $"MapEnt edit {patches.Count} is not a contiguous " +
                    $"baseline-to-current byte transition: {exception.Message}");
                continue;
            }

            ValidateEditWasPreparedBySyntaxLayer(
                before,
                after,
                edit,
                diagnostics,
                cancellationToken);
            MapEntsSyntaxProperty property;
            try
            {
                property = before.GetProperty(
                    edit.EntityOrdinal,
                    edit.PropertyOrdinal);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                diagnostics.Add(
                    $"MapEnt edit {patches.Count} targets an unavailable " +
                    $"entity/property ordinal: {exception.Message}");
                syntax = after;
                continue;
            }

            MapEntityCompilationAssessment beforeRelationship =
                Classify(before, edit.EntityOrdinal);
            MapEntityCompilationAssessment afterRelationship =
                Classify(after, edit.EntityOrdinal);
            MapEntityPropertyEditAssessment propertyEditAssessment =
                ClassifyPropertyEdit(
                    before,
                    after,
                    edit.EntityOrdinal,
                    edit.PropertyOrdinal,
                    edit.Field);
            ValidateRelationships(
                edit,
                beforeRelationship,
                afterRelationship,
                propertyEditAssessment,
                diagnostics);
            EditorEntity? semanticEntity = ValidateSemanticEntity(
                document,
                bundle,
                descriptor,
                edit.EntityOrdinal,
                diagnostics);
            SourceBindingId propertyBinding = default;
            if (semanticEntity is not null)
            {
                try
                {
                    EditorEntityProperty semanticProperty =
                        semanticEntity.GetProperty(edit.PropertyOrdinal);
                    propertyBinding = edit.Field switch
                    {
                        MapEntPropertyField.Key =>
                            semanticProperty.KeySourceBinding,
                        MapEntPropertyField.Value =>
                            semanticProperty.ValueSourceBinding,
                        _ => default
                    };
                    ValidatePropertyBinding(
                        bundle,
                        descriptor,
                        edit,
                        semanticProperty,
                        propertyBinding,
                        sourceBindings,
                        diagnostics);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    diagnostics.Add(
                        $"Semantic MapEnt entity {edit.EntityOrdinal} has no " +
                        $"property {edit.PropertyOrdinal}: {exception.Message}");
                }
            }

            patches.Add(new MapEntsPropertyPatch(
                semanticEntity?.Id ?? default,
                propertyBinding,
                edit.EntityOrdinal,
                edit.PropertyOrdinal,
                edit.Field,
                edit.Field == MapEntPropertyField.Key
                    ? property.KeyContentSpan
                    : property.ValueContentSpan,
                edit.OriginalText,
                edit.ReplacementText,
                before.ContentDigest,
                after.ContentDigest,
                beforeRelationship,
                afterRelationship,
                propertyEditAssessment));
            syntax = after;
        }

        ValidateSemanticProjection(
            document,
            bundle,
            descriptor,
            importedSyntax,
            syntax,
            diagnostics);

        byte[] replacementBytes = syntax.Serialize();
        var buildData = baseline.WithEntityStringBytes(replacementBytes);
        MapPatchValidation preservation = ValidatePreservation(
            baseline,
            buildData,
            patches,
            cancellationToken);
        diagnostics.AddRange(preservation.Diagnostics);
        if (patches.Count == 0)
        {
            diagnostics.Add(
                "No effective existing MapEnt property replacement was supplied.");
        }
        MapAssetKind? ownerKind = null;
        CompiledMapAssetDescriptor? ownerDescriptor = null;
        ClipMapBuildData? ownerBaseline = null;
        ClipMapBuildData? ownerBuildData = null;
        if (descriptor.IsNested)
        {
            PrepareNestedOwner(
                bundle,
                descriptor,
                baseline,
                buildData,
                replacementBytes,
                diagnostics,
                out ownerKind,
                out ownerDescriptor,
                out ownerBaseline,
                out ownerBuildData,
                cancellationToken);
        }

        var structuralValidation = new MapPatchValidation(diagnostics);
        if (baselineBytes.AsSpan().SequenceEqual(replacementBytes))
        {
            diagnostics.Add(
                "The MapEnt edit sequence produces the original entity-string bytes.");
        }

        return new MapEntsEntityStringPatchCandidate(
            bundle.MapIdentity,
            descriptor,
            baseline,
            buildData,
            patches,
            cardinalityPatches: null,
            orderedPatches: null,
            new MapPatchValidation(diagnostics),
            structuralValidation,
            ownerKind,
            ownerDescriptor,
            ownerBaseline,
            ownerBuildData);
    }

    public MapPatchValidation ValidatePreservation(
        IMapEntsBuildData baseline,
        IMapEntsBuildData candidate,
        IEnumerable<MapEntsPropertyPatch> patches,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(patches);
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<string>();
        if (baseline.AssetType != XAssetType.MapEnts ||
            candidate.AssetType != XAssetType.MapEnts)
        {
            diagnostics.Add(
                "MapEnts patch inputs must retain the MapEnts asset type.");
        }
        if (!string.Equals(
                baseline.Name,
                candidate.Name,
                StringComparison.Ordinal))
        {
            diagnostics.Add("MapEnts name was not preserved.");
        }
        if (!SameTriggers(baseline.Triggers, candidate.Triggers))
        {
            diagnostics.Add(
                "MapEnts trigger models, hulls, or slabs were not preserved.");
        }
        if (!baseline.Stages.SequenceEqual(candidate.Stages))
            diagnostics.Add("MapEnts stages were not preserved.");
        if (!baseline.GetPad29To2BCopy().AsSpan().SequenceEqual(
                candidate.GetPad29To2BCopy()))
        {
            diagnostics.Add(
                "MapEnts 0x29-0x2B tail padding was not preserved.");
        }

        MapEntsSyntaxDocument current = MapEntsSyntaxParser.Parse(
            baseline.GetEntityStringBytesCopy(),
            cancellationToken);
        if (!current.CanEdit)
        {
            diagnostics.Add(
                "Baseline MapEnts syntax is not safely editable: " +
                FormatSyntaxDiagnostics(current));
        }
        foreach (MapEntsPropertyPatch patch in patches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (patch is null)
            {
                diagnostics.Add(
                    "The MapEnt property patch set contains a null patch.");
                continue;
            }
            if (patch.ObjectId.Value == Guid.Empty ||
                patch.SourceBinding.Value == Guid.Empty)
            {
                diagnostics.Add(
                    $"MapEnt entity {patch.EntityOrdinal}, property " +
                    $"{patch.PropertyOrdinal} has no authorized semantic " +
                    "object or exact source binding.");
            }
            if (!Enum.IsDefined(patch.Field))
            {
                diagnostics.Add(
                    $"MapEnt entity {patch.EntityOrdinal}, property " +
                    $"{patch.PropertyOrdinal} has an invalid replacement field.");
                continue;
            }
            if (!string.Equals(
                    current.ContentDigest,
                    patch.BeforeContentDigest,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"MapEnt entity {patch.EntityOrdinal}, property " +
                    $"{patch.PropertyOrdinal} does not follow the preceding " +
                    "byte snapshot.");
                continue;
            }

            MapEntsSyntaxDocument next;
            try
            {
                MapEntsSyntaxProperty property = current.GetProperty(
                    patch.EntityOrdinal,
                    patch.PropertyOrdinal);
                MapEntSourceSpan contentSpan =
                    patch.Field == MapEntPropertyField.Key
                        ? property.KeyContentSpan
                        : property.ValueContentSpan;
                string originalText =
                    patch.Field == MapEntPropertyField.Key
                        ? property.Key
                        : property.Value;
                if (contentSpan != patch.OriginalContentSpan ||
                    !string.Equals(
                        originalText,
                        patch.OriginalText,
                        StringComparison.Ordinal))
                {
                    diagnostics.Add(
                        $"MapEnt entity {patch.EntityOrdinal}, property " +
                        $"{patch.PropertyOrdinal} no longer matches its " +
                        "authorized original content.");
                }

                next = current.PreparePropertyReplacement(
                    patch.EntityOrdinal,
                    patch.PropertyOrdinal,
                    patch.Field,
                    patch.ReplacementText,
                    cancellationToken).After;
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException)
            {
                diagnostics.Add(
                    $"MapEnt entity {patch.EntityOrdinal}, property " +
                    $"{patch.PropertyOrdinal} could not be replayed: " +
                    exception.Message);
                continue;
            }

            if (!string.Equals(
                    next.ContentDigest,
                    patch.AfterContentDigest,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"MapEnt entity {patch.EntityOrdinal}, property " +
                    $"{patch.PropertyOrdinal} does not produce its authorized " +
                    "after snapshot.");
            }
            MapEntityCompilationAssessment beforeRelationship =
                Classify(current, patch.EntityOrdinal);
            MapEntityCompilationAssessment afterRelationship =
                Classify(next, patch.EntityOrdinal);
            MapEntityPropertyEditAssessment propertyEditAssessment =
                ClassifyPropertyEdit(
                    current,
                    next,
                    patch.EntityOrdinal,
                    patch.PropertyOrdinal,
                    patch.Field);
            if (beforeRelationship != patch.BeforeRelationship ||
                afterRelationship != patch.AfterRelationship ||
                !beforeRelationship.CanPatchExistingProperty ||
                !afterRelationship.CanPatchExistingProperty)
            {
                diagnostics.Add(
                    $"MapEnt entity {patch.EntityOrdinal} relationship " +
                    "evidence is not the authorized MapEnt-only transition.");
            }
            if (propertyEditAssessment !=
                    patch.PropertyEditAssessment ||
                !propertyEditAssessment.IsPatchAuthorized)
            {
                diagnostics.Add(
                    $"MapEnt entity {patch.EntityOrdinal}, property " +
                    $"{patch.PropertyOrdinal} exact property-operation " +
                    "evidence is not authorized: " +
                    propertyEditAssessment.Evidence);
            }
            current = next;
        }

        byte[] candidateBytes = candidate.GetEntityStringBytesCopy();
        if (!current.Serialize().AsSpan().SequenceEqual(candidateBytes))
        {
            diagnostics.Add(
                "MapEnts candidate bytes contain changes outside the declared " +
                "property patch sequence.");
        }
        MapEntsSyntaxDocument candidateSyntax = MapEntsSyntaxParser.Parse(
            candidateBytes,
            cancellationToken);
        if (!candidateSyntax.CanEdit)
        {
            diagnostics.Add(
                "Candidate MapEnts syntax is not valid: " +
                FormatSyntaxDiagnostics(candidateSyntax));
        }

        diagnostics.AddRange(
            Emitter.Validate(candidate)
                .Select(issue =>
                    $"MapEnts emitter validation failed at {issue.Path}: " +
                    issue.Message));
        return new MapPatchValidation(diagnostics);
    }

    public void ApplyValidatedCandidate(
        MapEntsDraft draft,
        MapEntsEntityStringPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Descriptor.IsNested)
        {
            throw new InvalidOperationException(
                "A nested MapEnts candidate must be applied through its " +
                "ColMap owner draft.");
        }
        RequireValidCandidate(candidate);

        if (!string.Equals(
                candidate.Baseline.Name,
                draft.Name,
                StringComparison.Ordinal) ||
            !SameTriggers(
                candidate.Baseline.Triggers,
                draft.Triggers) ||
            !candidate.Baseline.Stages.SequenceEqual(draft.Stages) ||
            !candidate.Baseline.GetPad29To2BCopy().AsSpan().SequenceEqual(
                draft.GetPad29To2BCopy()) ||
            !candidate.Baseline.GetEntityStringBytesCopy().AsSpan()
                .SequenceEqual(draft.GetEntityStringBytesCopy()))
        {
            throw new InvalidOperationException(
                "The MapEnts draft no longer matches the validated immutable " +
                "baseline.");
        }

        draft.ReplaceEntityStringBytes(
            candidate.BuildData.GetEntityStringBytesCopy());
    }

    public void ApplyValidatedCandidate(
        ClipMapDraft draft,
        MapEntsEntityStringPatchCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        if (!candidate.Descriptor.IsNested ||
            candidate.NestedOwnerDescriptor is null ||
            candidate.NestedOwnerBaseline is null ||
            candidate.NestedOwnerBuildData is null)
        {
            throw new InvalidOperationException(
                "A top-level MapEnts candidate cannot replace a ColMap draft.");
        }
        RequireValidCandidate(candidate);
        RequireExactOwner(
            candidate.MapIdentity,
            candidate.NestedOwnerDescriptor,
            candidate.NestedOwnerBaseline,
            draft.Data,
            "The ColMap draft no longer matches the validated immutable baseline",
            cancellationToken);

        draft.ReplaceNestedMapEntsEntityStringBytes(
            candidate.BuildData.GetEntityStringBytesCopy());
        RequireExactOwner(
            candidate.MapIdentity,
            candidate.NestedOwnerDescriptor,
            candidate.NestedOwnerBuildData,
            draft.Data,
            "The staged ColMap draft does not match the exact expected nested MapEnts candidate",
            cancellationToken);
    }

    private static void ValidateDescriptorShape(
        CompiledMapAssetDescriptor descriptor,
        ICollection<string> diagnostics)
    {
        if (descriptor.Kind != MapAssetKind.MapEnts ||
            descriptor.SerializedType != XAssetType.MapEnts)
        {
            diagnostics.Add(
                "The selected compiled descriptor is not a MapEnts asset.");
        }
        string expectedPath = descriptor.IsNested
            ? "references.mapEntsLink.incomingDefinition"
            : "$";
        if (!string.Equals(
                descriptor.SourcePath,
                expectedPath,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                "The MapEnts descriptor has unsupported source ownership " +
                $"path '{descriptor.SourcePath}'.");
        }
    }

    private static void ValidateRawSourceBinding(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor descriptor,
        MapEntsBuildData baseline,
        IEnumerable<CompiledSourceBinding> sourceBindings,
        ICollection<string> diagnostics)
    {
        EditorMapEntitySource? source = document.EntitySource;
        if (source is null)
        {
            diagnostics.Add(
                "The semantic document has no exact MapEnts entity-string source.");
            return;
        }
        byte[] semanticBaselineBytes = source.GetBaselineBytesCopy();
        MapEntsSyntaxDocument semanticBaseline =
            MapEntsSyntaxParser.Parse(semanticBaselineBytes);
        if (!string.Equals(
                source.Name,
                baseline.Name,
                StringComparison.Ordinal) ||
            !semanticBaselineBytes.AsSpan().SequenceEqual(
                baseline.GetEntityStringBytesCopy()))
        {
            diagnostics.Add(
                "The semantic MapEnts source no longer matches the immutable " +
                "compiled baseline.");
        }
        if (!string.Equals(
                source.BaselineDigest,
                semanticBaseline.ContentDigest,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                "The semantic MapEnts baseline digest does not match its " +
                "immutable baseline bytes.");
        }

        CompiledSourceBinding[] matches = sourceBindings
            .Where(value =>
                value is not null &&
                value.Id == source.SourceBinding)
            .ToArray();
        if (matches.Length != 1)
        {
            diagnostics.Add(
                $"The exact MapEnts source binding {source.SourceBinding} " +
                $"resolves to {matches.Length} catalog entries.");
            return;
        }

        CompiledSourceBinding binding = matches[0];
        string expectedPath =
            $"{descriptor.SourcePath}.entityStringBytes";
        SourceBindingId expectedId = DeterministicMapIdentity.Binding(
            bundle.MapIdentity,
            XAssetType.MapEnts.ToString(),
            descriptor.AssetName,
            expectedPath,
            sourceOrdinal: null);
        if (binding.Id != expectedId ||
            binding.AssetType != XAssetType.MapEnts ||
            binding.OwnerRow != descriptor.OwnerRow ||
            binding.SourceOrdinal is not null ||
            binding.Provenance != MapValueProvenance.ExactSerialized ||
            !string.Equals(
                binding.AssetName,
                descriptor.AssetName,
                StringComparison.Ordinal) ||
            !string.Equals(
                binding.FieldPath,
                expectedPath,
                StringComparison.Ordinal) ||
            !string.Equals(
                binding.BaselineDigest,
                descriptor.BaselineDigest,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                "The MapEnts entity-string source does not have the exact " +
                "deterministic compiled-field binding.");
        }
    }

    private static SourceBindingId ValidateJournalPropertyBinding(
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor descriptor,
        MapObjectId objectId,
        MapEntsPropertyEdit edit,
        IEnumerable<CompiledSourceBinding> sourceBindings,
        ICollection<string> diagnostics)
    {
        MapObjectId expectedObjectId = DeterministicMapIdentity.Object(
            bundle.MapIdentity,
            XAssetType.MapEnts.ToString(),
            descriptor.AssetName,
            "mapent-entity",
            edit.EntityOrdinal.Value);
        if (objectId != expectedObjectId)
        {
            diagnostics.Add(
                $"MapEnt entity {edit.EntityOrdinal} property editing after " +
                "authored cardinality is unsupported because it has no " +
                "imported deterministic field binding.");
            return default;
        }

        string fieldName = edit.Field switch
        {
            MapEntPropertyField.Key => "key",
            MapEntPropertyField.Value => "value",
            _ => string.Empty
        };
        string expectedPath =
            $"{descriptor.SourcePath}.entityStringBytes.entities" +
            $"[{edit.EntityOrdinal.Value}].properties" +
            $"[{edit.PropertyOrdinal.Value}].{fieldName}";
        SourceBindingId expectedId = DeterministicMapIdentity.Binding(
            bundle.MapIdentity,
            XAssetType.MapEnts.ToString(),
            descriptor.AssetName,
            expectedPath,
            edit.EntityOrdinal.Value);
        CompiledSourceBinding[] matches = sourceBindings
            .Where(value =>
                value is not null &&
                value.Id == expectedId)
            .ToArray();
        if (matches.Length != 1)
        {
            diagnostics.Add(
                $"MapEnt entity {edit.EntityOrdinal}, property " +
                $"{edit.PropertyOrdinal} exact {fieldName} binding resolves " +
                $"to {matches.Length} catalog entries.");
            return default;
        }

        CompiledSourceBinding binding = matches[0];
        if (binding.AssetType != XAssetType.MapEnts ||
            binding.OwnerRow != descriptor.OwnerRow ||
            binding.SourceOrdinal != edit.EntityOrdinal.Value ||
            binding.Provenance != MapValueProvenance.ExactDecodedRuntime ||
            !string.Equals(
                binding.AssetName,
                descriptor.AssetName,
                StringComparison.Ordinal) ||
            !string.Equals(
                binding.FieldPath,
                expectedPath,
                StringComparison.Ordinal) ||
            !string.Equals(
                binding.BaselineDigest,
                descriptor.BaselineDigest,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                $"MapEnt entity {edit.EntityOrdinal}, property " +
                $"{edit.PropertyOrdinal} does not retain its exact imported " +
                $"{fieldName} binding.");
        }
        return expectedId;
    }

    private static void ValidateCardinalityWasPreparedBySyntaxLayer(
        MapEntsSyntaxDocument before,
        MapEntsSyntaxDocument after,
        MapEntsCardinalityEdit edit,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            MapEntsCardinalityEdit independent = edit.Operation switch
            {
                MapEntityCardinalityOperation.Append =>
                    before.PrepareScriptOriginAppend(
                        after.GetEntity(edit.EntityOrdinal)
                            .Properties.Select(value =>
                                new KeyValuePair<string, string>(
                                    value.Key,
                                    value.Value)),
                        cancellationToken),
                MapEntityCardinalityOperation.Remove =>
                    before.PrepareFinalScriptOriginRemoval(
                        edit.EntityOrdinal,
                        cancellationToken),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(edit),
                    edit.Operation,
                    "Unknown MapEnt cardinality operation.")
            };
            if (!independent.After.HasSameBytes(after) ||
                !independent.GetEntityBytesCopy().AsSpan().SequenceEqual(
                    edit.GetEntityBytesCopy()))
            {
                diagnostics.Add(
                    $"MapEnt {edit.Operation} at entity " +
                    $"{edit.EntityOrdinal} was not produced by the exact " +
                    "cardinality syntax operation.");
            }
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException)
        {
            diagnostics.Add(
                $"MapEnt {edit.Operation} at entity {edit.EntityOrdinal} " +
                $"failed independent cardinality validation: " +
                exception.Message);
        }
    }

    private void ValidateCardinalitySemanticProjection(
        EditorMapDocument document,
        MapEntsSyntaxDocument candidate,
        IReadOnlyList<MapObjectId> expectedFinalIdentities,
        ICollection<string> diagnostics)
    {
        EditorMapEntitySource? source = document.EntitySource;
        if (source is null)
            return;
        if (!source.Syntax.HasSameBytes(candidate) ||
            !source.GetRawBytesCopy().AsSpan().SequenceEqual(
                candidate.Serialize()) ||
            !string.Equals(
                source.CurrentDigest,
                candidate.ContentDigest,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                "The ordered MapEnt journal does not reproduce the semantic " +
                "document's current byte-authoritative syntax.");
        }
        if (candidate.Entities.Count != document.Entities.Count ||
            candidate.Entities.Count != expectedFinalIdentities.Count)
        {
            diagnostics.Add(
                "The final MapEnt syntax, semantic collection, and identity " +
                "journal cardinalities do not match.");
            return;
        }

        for (int index = 0; index < candidate.Entities.Count; index++)
        {
            MapEntsSyntaxEntity syntaxEntity = candidate.Entities[index];
            EditorEntity entity = document.Entities[index];
            MapEntityCompilationAssessment assessment =
                Classify(candidate, new MapEntEntityOrdinal(index));
            if (entity.Id != expectedFinalIdentities[index] ||
                entity.SyntaxOrdinal.Value != index ||
                entity.SourceOrdinal.Value != index ||
                entity.SourceByteOffset.Value != syntaxEntity.Span.Offset ||
                entity.SourceByteLength.Value != syntaxEntity.Span.Length ||
                entity.CompilationAssessment != assessment ||
                entity.KeyValues.Count != syntaxEntity.Properties.Count)
            {
                diagnostics.Add(
                    $"Semantic MapEnt entity {index} is not the exact syntax, " +
                    "identity, and independently classified projection of the " +
                    "ordered journal.");
                continue;
            }

            for (int propertyIndex = 0;
                 propertyIndex < syntaxEntity.Properties.Count;
                 propertyIndex++)
            {
                MapEntsSyntaxProperty syntaxProperty =
                    syntaxEntity.Properties[propertyIndex];
                EditorEntityProperty property =
                    entity.KeyValues[propertyIndex];
                if (property.Ordinal != syntaxProperty.Ordinal ||
                    !string.Equals(
                        property.Key,
                        syntaxProperty.Key,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        property.Value,
                        syntaxProperty.Value,
                        StringComparison.Ordinal) ||
                    property.Span != syntaxProperty.Span ||
                    property.KeyTokenSpan !=
                        syntaxProperty.KeyTokenSpan ||
                    property.KeyContentSpan !=
                        syntaxProperty.KeyContentSpan ||
                    property.ValueTokenSpan !=
                        syntaxProperty.ValueTokenSpan ||
                    property.ValueContentSpan !=
                        syntaxProperty.ValueContentSpan)
                {
                    diagnostics.Add(
                        $"Semantic MapEnt entity {index}, property " +
                        $"{propertyIndex} is not the exact ordered syntax " +
                        "projection.");
                }
            }
        }
    }

    private MapPatchValidation ValidateCardinalityAwarePreservation(
        IMapEntsBuildData baseline,
        IMapEntsBuildData candidate,
        IReadOnlyList<MapEntsJournalPatch> patches,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        diagnostics.AddRange(
            ValidateMetadataPreservation(baseline, candidate).Diagnostics);
        MapEntsSyntaxDocument current = MapEntsSyntaxParser.Parse(
            baseline.GetEntityStringBytesCopy(),
            cancellationToken);
        if (!current.CanEdit)
        {
            diagnostics.Add(
                "Baseline MapEnts syntax is not safely editable: " +
                FormatSyntaxDiagnostics(current));
        }

        foreach (MapEntsJournalPatch journalPatch in patches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (journalPatch.Property is { } propertyPatch)
            {
                current = ReplayPropertyPatch(
                    current,
                    propertyPatch,
                    diagnostics,
                    cancellationToken);
                continue;
            }

            MapEntsCardinalityPatch patch =
                journalPatch.Cardinality ??
                throw new InvalidOperationException(
                    "MapEnt ordered patch entry is empty.");
            if (patch.ObjectId.Value == Guid.Empty ||
                patch.SourceBinding.Value == Guid.Empty)
            {
                diagnostics.Add(
                    $"MapEnt {patch.Operation} at entity " +
                    $"{patch.EntityOrdinal} has no semantic identity or exact " +
                    "entity-string binding.");
            }
            if (!string.Equals(
                    current.ContentDigest,
                    patch.BeforeContentDigest,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"MapEnt {patch.Operation} at entity " +
                    $"{patch.EntityOrdinal} does not follow the preceding " +
                    "byte snapshot.");
                continue;
            }

            MapEntsCardinalityEdit replay;
            try
            {
                replay = patch.Operation switch
                {
                    MapEntityCardinalityOperation.Append =>
                        current.PrepareScriptOriginAppend(
                            ParseStandaloneEntityProperties(
                                patch.EntityBytes,
                                cancellationToken),
                            cancellationToken),
                    MapEntityCardinalityOperation.Remove =>
                        current.PrepareFinalScriptOriginRemoval(
                            patch.EntityOrdinal,
                            cancellationToken),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(patch.Operation))
                };
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException)
            {
                diagnostics.Add(
                    $"MapEnt {patch.Operation} at entity " +
                    $"{patch.EntityOrdinal} could not be replayed: " +
                    exception.Message);
                continue;
            }

            if (replay.EntityOrdinal != patch.EntityOrdinal ||
                !replay.GetEntityBytesCopy().AsSpan().SequenceEqual(
                    patch.EntityBytes) ||
                !string.Equals(
                    replay.After.ContentDigest,
                    patch.AfterContentDigest,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"MapEnt {patch.Operation} at entity " +
                    $"{patch.EntityOrdinal} does not produce its authorized " +
                    "entity bytes and after snapshot.");
            }
            MapEntityCardinalityAssessment assessment =
                AppendScriptOriginEntityCommand.Assess(
                    replay,
                    patch.Operation);
            if (assessment != patch.Assessment ||
                !assessment.IsPatchAuthorized)
            {
                diagnostics.Add(
                    $"MapEnt {patch.Operation} at entity " +
                    $"{patch.EntityOrdinal} no longer has its exact " +
                    "executable-backed consumer evidence.");
            }
            current = replay.After;
        }

        byte[] candidateBytes = candidate.GetEntityStringBytesCopy();
        if (!current.Serialize().AsSpan().SequenceEqual(candidateBytes))
        {
            diagnostics.Add(
                "MapEnts candidate bytes contain changes outside the exact " +
                "ordered property/cardinality patch journal.");
        }
        MapEntsSyntaxDocument candidateSyntax = MapEntsSyntaxParser.Parse(
            candidateBytes,
            cancellationToken);
        if (!candidateSyntax.CanEdit)
        {
            diagnostics.Add(
                "Candidate MapEnts syntax is not valid: " +
                FormatSyntaxDiagnostics(candidateSyntax));
        }
        diagnostics.AddRange(
            Emitter.Validate(candidate)
                .Select(issue =>
                    $"MapEnts emitter validation failed at {issue.Path}: " +
                    issue.Message));
        return new MapPatchValidation(diagnostics);
    }

    private MapEntsSyntaxDocument ReplayPropertyPatch(
        MapEntsSyntaxDocument current,
        MapEntsPropertyPatch patch,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        if (patch.ObjectId.Value == Guid.Empty ||
            patch.SourceBinding.Value == Guid.Empty)
        {
            diagnostics.Add(
                $"MapEnt entity {patch.EntityOrdinal}, property " +
                $"{patch.PropertyOrdinal} has no authorized semantic object " +
                "or exact source binding.");
        }
        if (!string.Equals(
                current.ContentDigest,
                patch.BeforeContentDigest,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                $"MapEnt entity {patch.EntityOrdinal}, property " +
                $"{patch.PropertyOrdinal} does not follow the preceding byte " +
                "snapshot.");
            return current;
        }

        try
        {
            MapEntsSyntaxProperty property = current.GetProperty(
                patch.EntityOrdinal,
                patch.PropertyOrdinal);
            MapEntSourceSpan contentSpan =
                patch.Field == MapEntPropertyField.Key
                    ? property.KeyContentSpan
                    : property.ValueContentSpan;
            string originalText =
                patch.Field == MapEntPropertyField.Key
                    ? property.Key
                    : property.Value;
            if (contentSpan != patch.OriginalContentSpan ||
                !string.Equals(
                    originalText,
                    patch.OriginalText,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"MapEnt entity {patch.EntityOrdinal}, property " +
                    $"{patch.PropertyOrdinal} no longer matches its " +
                    "authorized original content.");
            }

            MapEntsSyntaxDocument next =
                current.PreparePropertyReplacement(
                    patch.EntityOrdinal,
                    patch.PropertyOrdinal,
                    patch.Field,
                    patch.ReplacementText,
                    cancellationToken).After;
            if (!string.Equals(
                    next.ContentDigest,
                    patch.AfterContentDigest,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"MapEnt entity {patch.EntityOrdinal}, property " +
                    $"{patch.PropertyOrdinal} does not produce its authorized " +
                    "after snapshot.");
            }
            MapEntityCompilationAssessment beforeRelationship =
                Classify(current, patch.EntityOrdinal);
            MapEntityCompilationAssessment afterRelationship =
                Classify(next, patch.EntityOrdinal);
            MapEntityPropertyEditAssessment propertyAssessment =
                ClassifyPropertyEdit(
                    current,
                    next,
                    patch.EntityOrdinal,
                    patch.PropertyOrdinal,
                    patch.Field);
            if (beforeRelationship != patch.BeforeRelationship ||
                afterRelationship != patch.AfterRelationship ||
                !beforeRelationship.CanPatchExistingProperty ||
                !afterRelationship.CanPatchExistingProperty ||
                propertyAssessment != patch.PropertyEditAssessment ||
                !propertyAssessment.IsPatchAuthorized)
            {
                diagnostics.Add(
                    $"MapEnt entity {patch.EntityOrdinal}, property " +
                    $"{patch.PropertyOrdinal} no longer has its authorized " +
                    "consumer transition.");
            }
            return next;
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException)
        {
            diagnostics.Add(
                $"MapEnt entity {patch.EntityOrdinal}, property " +
                $"{patch.PropertyOrdinal} could not be replayed: " +
                exception.Message);
            return current;
        }
    }

    private static IReadOnlyList<KeyValuePair<string, string>>
        ParseStandaloneEntityProperties(
            ReadOnlySpan<byte> entityBytes,
            CancellationToken cancellationToken)
    {
        var source = new byte[entityBytes.Length + 1];
        entityBytes.CopyTo(source);
        MapEntsSyntaxDocument syntax = MapEntsSyntaxParser.Parse(
            source,
            cancellationToken);
        if (!syntax.CanEdit || syntax.Entities.Count != 1)
        {
            throw new InvalidDataException(
                "Authorized cardinality bytes do not contain exactly one " +
                "strict MapEnt entity.");
        }
        return Array.AsReadOnly(
            syntax.Entities[0].Properties
                .Select(value =>
                    new KeyValuePair<string, string>(
                        value.Key,
                        value.Value))
                .ToArray());
    }

    private static void ValidateEditWasPreparedBySyntaxLayer(
        MapEntsSyntaxDocument before,
        MapEntsSyntaxDocument after,
        MapEntsPropertyEdit edit,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            MapEntsPropertyEdit independentlyPrepared =
                before.PreparePropertyReplacement(
                    edit.EntityOrdinal,
                    edit.PropertyOrdinal,
                    edit.Field,
                    edit.ReplacementText,
                    cancellationToken);
            if (!string.Equals(
                    independentlyPrepared.OriginalText,
                    edit.OriginalText,
                    StringComparison.Ordinal) ||
                !independentlyPrepared.After.Serialize().AsSpan()
                    .SequenceEqual(after.Serialize()))
            {
                diagnostics.Add(
                    $"MapEnt entity {edit.EntityOrdinal}, property " +
                    $"{edit.PropertyOrdinal} was not produced by the exact " +
                    "fidelity-preserving syntax replacement.");
            }
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException)
        {
            diagnostics.Add(
                $"MapEnt entity {edit.EntityOrdinal}, property " +
                $"{edit.PropertyOrdinal} failed independent syntax " +
                $"validation: {exception.Message}");
        }
    }

    private MapEntityCompilationAssessment Classify(
        MapEntsSyntaxDocument document,
        MapEntEntityOrdinal ordinal) =>
        _consumerCatalog.Classify(
            document.GetEntity(ordinal).Properties.Select(
                value => new KeyValuePair<string, string>(
                    value.Key,
                    value.Value)));

    private MapEntityPropertyEditAssessment ClassifyPropertyEdit(
        MapEntsSyntaxDocument before,
        MapEntsSyntaxDocument after,
        MapEntEntityOrdinal entityOrdinal,
        MapEntPropertyOrdinal propertyOrdinal,
        MapEntPropertyField field)
    {
        MapEntsSyntaxEntity beforeEntity =
            before.GetEntity(entityOrdinal);
        MapEntsSyntaxProperty beforeProperty =
            before.GetProperty(entityOrdinal, propertyOrdinal);
        MapEntsSyntaxProperty afterProperty =
            after.GetProperty(entityOrdinal, propertyOrdinal);
        MapEntityPropertyEditOperation operation = field switch
        {
            MapEntPropertyField.Value =>
                MapEntityPropertyEditOperation.ReplaceValue,
            MapEntPropertyField.Key =>
                MapEntityPropertyEditOperation.ReplaceKey,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        return _consumerCatalog.ClassifyExistingPropertyEdit(
            beforeEntity.Properties.Select(value =>
                new KeyValuePair<string, string>(
                    value.Key,
                    value.Value)),
            beforeProperty.Key,
            operation,
            operation == MapEntityPropertyEditOperation.ReplaceKey
                ? afterProperty.Key
                : null);
    }

    private static void ValidateRelationships(
        MapEntsPropertyEdit edit,
        MapEntityCompilationAssessment before,
        MapEntityCompilationAssessment after,
        MapEntityPropertyEditAssessment propertyEdit,
        ICollection<string> diagnostics)
    {
        if (!before.CanPatchExistingProperty)
        {
            diagnostics.Add(
                $"MapEnt entity {edit.EntityOrdinal} is not proven MapEnt-only " +
                $"before the edit: {before.Evidence}");
        }
        if (!after.CanPatchExistingProperty)
        {
            diagnostics.Add(
                $"MapEnt entity {edit.EntityOrdinal} is not proven MapEnt-only " +
                $"after the edit: {after.Evidence}");
        }
        if (!propertyEdit.IsPatchAuthorized)
        {
            diagnostics.Add(
                $"MapEnt entity {edit.EntityOrdinal}, property " +
                $"{edit.PropertyOrdinal} exact property operation is not " +
                $"proven MapEnt-only: {propertyEdit.Evidence}");
        }
    }

    private static EditorEntity? ValidateSemanticEntity(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor descriptor,
        MapEntEntityOrdinal ordinal,
        ICollection<string> diagnostics)
    {
        EditorEntity[] matches = document.Entities
            .Where(value => value.SourceOrdinal.Value == ordinal.Value)
            .ToArray();
        if (matches.Length != 1)
        {
            diagnostics.Add(
                $"MapEnt entity ordinal {ordinal} resolves to " +
                $"{matches.Length} semantic entities.");
            return null;
        }

        EditorEntity entity = matches[0];
        MapObjectId expectedId = DeterministicMapIdentity.Object(
            bundle.MapIdentity,
            XAssetType.MapEnts.ToString(),
            descriptor.AssetName,
            "mapent-entity",
            ordinal.Value);
        if (entity.Id != expectedId ||
            entity.SyntaxOrdinal != ordinal ||
            entity.SourceOrdinal.Value != ordinal.Value)
        {
            diagnostics.Add(
                $"MapEnt entity ordinal {ordinal} does not retain its " +
                "deterministic imported identity and syntax ordinal.");
        }
        return entity;
    }

    private static void ValidatePropertyBinding(
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor descriptor,
        MapEntsPropertyEdit edit,
        EditorEntityProperty property,
        SourceBindingId bindingId,
        IEnumerable<CompiledSourceBinding> sourceBindings,
        ICollection<string> diagnostics)
    {
        if (property.Ordinal != edit.PropertyOrdinal ||
            bindingId.Value == Guid.Empty)
        {
            diagnostics.Add(
                $"MapEnt entity {edit.EntityOrdinal}, property " +
                $"{edit.PropertyOrdinal} has no stable exact field binding.");
            return;
        }

        CompiledSourceBinding[] matches = sourceBindings
            .Where(value =>
                value is not null &&
                value.Id == bindingId)
            .ToArray();
        if (matches.Length != 1)
        {
            diagnostics.Add(
                $"MapEnt property binding {bindingId} resolves to " +
                $"{matches.Length} catalog entries.");
            return;
        }

        string fieldName = edit.Field switch
        {
            MapEntPropertyField.Key => "key",
            MapEntPropertyField.Value => "value",
            _ => string.Empty
        };
        string expectedPath =
            $"{descriptor.SourcePath}.entityStringBytes.entities" +
            $"[{edit.EntityOrdinal.Value}].properties" +
            $"[{edit.PropertyOrdinal.Value}].{fieldName}";
        SourceBindingId expectedId = DeterministicMapIdentity.Binding(
            bundle.MapIdentity,
            XAssetType.MapEnts.ToString(),
            descriptor.AssetName,
            expectedPath,
            edit.EntityOrdinal.Value);
        CompiledSourceBinding binding = matches[0];
        MapValueProvenance semanticProvenance =
            edit.Field == MapEntPropertyField.Key
                ? property.KeyProvenance
                : property.ValueProvenance;
        if (binding.Id != expectedId ||
            binding.AssetType != XAssetType.MapEnts ||
            binding.OwnerRow != descriptor.OwnerRow ||
            binding.SourceOrdinal != edit.EntityOrdinal.Value ||
            binding.Provenance != MapValueProvenance.ExactDecodedRuntime ||
            semanticProvenance !=
                MapValueProvenance.ExactDecodedRuntime ||
            !string.Equals(
                binding.AssetName,
                descriptor.AssetName,
                StringComparison.Ordinal) ||
            !string.Equals(
                binding.FieldPath,
                expectedPath,
                StringComparison.Ordinal) ||
            !string.Equals(
                binding.BaselineDigest,
                descriptor.BaselineDigest,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                $"MapEnt entity {edit.EntityOrdinal}, property " +
                $"{edit.PropertyOrdinal} does not have the exact " +
                $"{fieldName} binding for the owned MapEnts descriptor.");
        }
    }

    private void ValidateSemanticProjection(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor descriptor,
        MapEntsSyntaxDocument baseline,
        MapEntsSyntaxDocument candidate,
        ICollection<string> diagnostics)
    {
        EditorMapEntitySource? source = document.EntitySource;
        if (source is null)
            return;
        if (!source.Syntax.HasSameBytes(candidate) ||
            !source.GetRawBytesCopy().AsSpan().SequenceEqual(
                candidate.Serialize()) ||
            !string.Equals(
                source.CurrentDigest,
                candidate.ContentDigest,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                "The declared MapEnt edit sequence does not reproduce the " +
                "semantic document's current byte-authoritative syntax.");
        }
        if (baseline.Entities.Count != candidate.Entities.Count ||
            candidate.Entities.Count != document.Entities.Count)
        {
            diagnostics.Add(
                "MapEnt entity cardinality changed outside the supported " +
                "existing-property patch scope.");
            return;
        }

        for (int index = 0; index < candidate.Entities.Count; index++)
        {
            MapEntsSyntaxEntity syntaxEntity = candidate.Entities[index];
            EditorEntity[] matches = document.Entities
                .Where(value => value.SyntaxOrdinal.Value == index)
                .ToArray();
            if (matches.Length != 1)
            {
                diagnostics.Add(
                    $"MapEnt syntax entity {index} resolves to " +
                    $"{matches.Length} semantic entities.");
                continue;
            }

            EditorEntity entity = matches[0];
            MapObjectId expectedId = DeterministicMapIdentity.Object(
                bundle.MapIdentity,
                XAssetType.MapEnts.ToString(),
                descriptor.AssetName,
                "mapent-entity",
                index);
            MapEntityCompilationAssessment assessment =
                Classify(candidate, new MapEntEntityOrdinal(index));
            if (entity.Id != expectedId ||
                entity.SourceByteOffset.Value != syntaxEntity.Span.Offset ||
                entity.SourceByteLength.Value != syntaxEntity.Span.Length ||
                entity.CompilationAssessment != assessment ||
                entity.KeyValues.Count != syntaxEntity.Properties.Count)
            {
                diagnostics.Add(
                    $"Semantic MapEnt entity {index} is not an exact " +
                    "projection of the candidate syntax and independently " +
                    "classified consumer evidence.");
                continue;
            }

            for (int propertyIndex = 0;
                 propertyIndex < syntaxEntity.Properties.Count;
                 propertyIndex++)
            {
                MapEntsSyntaxProperty syntaxProperty =
                    syntaxEntity.Properties[propertyIndex];
                EditorEntityProperty property =
                    entity.KeyValues[propertyIndex];
                if (property.Ordinal != syntaxProperty.Ordinal ||
                    !string.Equals(
                        property.Key,
                        syntaxProperty.Key,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        property.Value,
                        syntaxProperty.Value,
                        StringComparison.Ordinal) ||
                    property.Span != syntaxProperty.Span ||
                    property.KeyTokenSpan !=
                        syntaxProperty.KeyTokenSpan ||
                    property.KeyContentSpan !=
                        syntaxProperty.KeyContentSpan ||
                    property.ValueTokenSpan !=
                        syntaxProperty.ValueTokenSpan ||
                    property.ValueContentSpan !=
                        syntaxProperty.ValueContentSpan)
                {
                    diagnostics.Add(
                        $"Semantic MapEnt entity {index}, property " +
                        $"{propertyIndex} is not the exact ordered syntax " +
                        "projection.");
                }
            }
        }
    }

    private static void PrepareNestedOwner(
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor mapEntsDescriptor,
        MapEntsBuildData logicalBaseline,
        MapEntsBuildData logicalCandidate,
        ReadOnlySpan<byte> replacementBytes,
        ICollection<string> diagnostics,
        out MapAssetKind? ownerKind,
        out CompiledMapAssetDescriptor? ownerDescriptor,
        out ClipMapBuildData? ownerBaseline,
        out ClipMapBuildData? ownerBuildData,
        CancellationToken cancellationToken)
    {
        ownerKind = null;
        ownerDescriptor = null;
        ownerBaseline = null;
        ownerBuildData = null;

        CompiledMapAssetDescriptor[] owners = bundle.Assets
            .Where(value =>
                value.OwnerRow == mapEntsDescriptor.OwnerRow &&
                value.Kind is (
                    MapAssetKind.ColMapMp or
                    MapAssetKind.ColMapSp))
            .ToArray();
        if (owners.Length != 1)
        {
            diagnostics.Add(
                $"Nested MapEnts owner row resolves to {owners.Length} " +
                "ColMap descriptors.");
            return;
        }

        ownerDescriptor = owners[0];
        ownerKind = ownerDescriptor.Kind;
        if (!bundle.TryGetBaseline(
                ownerKind.Value,
                out ClipMapBuildData? clip) ||
            clip is null)
        {
            diagnostics.Add(
                "The nested MapEnts descriptor has no detached ColMap owner " +
                "baseline.");
            return;
        }

        try
        {
            if (clip.References.MapEntsLink?.IncomingDefinition is not
                IMapEntsBuildData currentMapEnts)
            {
                throw new InvalidDataException(
                    "The ColMap owner has no detached incoming MapEnts " +
                    "definition.");
            }
            ownerBaseline =
                clip.WithNestedMapEntsEntityStringBytes(
                    currentMapEnts.GetEntityStringBytesCopy());
            ownerBuildData =
                clip.WithNestedMapEntsEntityStringBytes(replacementBytes);
            RequireExactNestedOwnerTransition(
                bundle.MapIdentity,
                ownerDescriptor,
                mapEntsDescriptor,
                ownerBaseline,
                ownerBuildData,
                logicalBaseline,
                logicalCandidate,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException)
        {
            diagnostics.Add(
                "Could not construct the exact nested ColMap owner candidate: " +
                exception.Message);
            ownerKind = null;
            ownerDescriptor = null;
            ownerBaseline = null;
            ownerBuildData = null;
        }
    }

    private static void RequireExactNestedOwnerTransition(
        string mapIdentity,
        CompiledMapAssetDescriptor ownerDescriptor,
        CompiledMapAssetDescriptor mapEntsDescriptor,
        ClipMapBuildData baseline,
        ClipMapBuildData candidate,
        IMapEntsBuildData logicalBaseline,
        IMapEntsBuildData logicalCandidate,
        CancellationToken cancellationToken)
    {
        if (ownerDescriptor.OwnerRow != mapEntsDescriptor.OwnerRow ||
            baseline.SerializedType != ownerDescriptor.SerializedType ||
            candidate.SerializedType != ownerDescriptor.SerializedType)
        {
            throw new InvalidDataException(
                "Nested MapEnts and ColMap owner identities are inconsistent.");
        }

        NestedXAssetBuildLink baselineLink =
            baseline.References.MapEntsLink ??
            throw new InvalidDataException(
                "The baseline ColMap has no nested MapEnts link.");
        NestedXAssetBuildLink candidateLink =
            candidate.References.MapEntsLink ??
            throw new InvalidDataException(
                "The candidate ColMap has no nested MapEnts link.");
        if (baselineLink.Reference != candidateLink.Reference ||
            baselineLink.SourceForm != candidateLink.SourceForm ||
            baselineLink.ImportedPackedRaw !=
                candidateLink.ImportedPackedRaw ||
            baselineLink.SourceForm is not (
                NestedXAssetPointerSourceForm.Inline or
                NestedXAssetPointerSourceForm.Insert) ||
            baselineLink.IncomingDefinition is not IMapEntsBuildData
                baselineMapEnts ||
            candidateLink.IncomingDefinition is not IMapEntsBuildData
                candidateMapEnts)
        {
            throw new InvalidDataException(
                "Nested MapEnts pointer provenance or incoming-definition " +
                "identity was not preserved.");
        }

        MapPatchValidation mapEntsPreservation =
            ValidateMetadataPreservation(
                baselineMapEnts,
                candidateMapEnts);
        if (!mapEntsPreservation.IsValid)
        {
            throw new InvalidDataException(
                string.Join(
                    "; ",
                    mapEntsPreservation.Diagnostics));
        }
        if (!baselineMapEnts.GetEntityStringBytesCopy().AsSpan()
                .SequenceEqual(
                    logicalBaseline.GetEntityStringBytesCopy()) ||
            !candidateMapEnts.GetEntityStringBytesCopy().AsSpan()
                .SequenceEqual(
                    logicalCandidate.GetEntityStringBytesCopy()) ||
            !ValidateMetadataPreservation(
                    logicalBaseline,
                    baselineMapEnts).IsValid ||
            !ValidateMetadataPreservation(
                    logicalCandidate,
                    candidateMapEnts).IsValid)
        {
            throw new InvalidDataException(
                "The logical MapEnts descriptor and its nested ColMap " +
                "incoming definition do not describe the same exact " +
                "baseline/candidate transition.");
        }

        string importedOwnerDigest = ComputeAssetDigest(
            mapIdentity,
            ownerDescriptor,
            baseline,
            cancellationToken);
        if (!string.Equals(
                importedOwnerDigest,
                ownerDescriptor.BaselineDigest,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The retained ColMap owner no longer matches its imported " +
                "descriptor digest.");
        }

        // candidate is produced from baseline by the closed
        // WithNestedMapEntsEntityStringBytes method. Computing its complete
        // descriptor digest here establishes the exact owner payload expected
        // after the one nested field replacement.
        _ = ComputeAssetDigest(
            mapIdentity,
            ownerDescriptor,
            candidate,
            cancellationToken);
    }

    private static MapPatchValidation ValidateMetadataPreservation(
        IMapEntsBuildData baseline,
        IMapEntsBuildData candidate)
    {
        var diagnostics = new List<string>();
        if (!string.Equals(
                baseline.Name,
                candidate.Name,
                StringComparison.Ordinal))
        {
            diagnostics.Add("Nested MapEnts name was not preserved.");
        }
        if (!SameTriggers(baseline.Triggers, candidate.Triggers))
        {
            diagnostics.Add(
                "Nested MapEnts trigger tables were not preserved.");
        }
        if (!baseline.Stages.SequenceEqual(candidate.Stages))
            diagnostics.Add("Nested MapEnts stages were not preserved.");
        if (!baseline.GetPad29To2BCopy().AsSpan().SequenceEqual(
                candidate.GetPad29To2BCopy()))
        {
            diagnostics.Add(
                "Nested MapEnts tail padding was not preserved.");
        }
        return new MapPatchValidation(diagnostics);
    }

    private static void RequireExactOwner(
        string mapIdentity,
        CompiledMapAssetDescriptor descriptor,
        ClipMapBuildData expected,
        ClipMapBuildData actual,
        string message,
        CancellationToken cancellationToken)
    {
        string expectedDigest = ComputeAssetDigest(
            mapIdentity,
            descriptor,
            expected,
            cancellationToken);
        string actualDigest = ComputeAssetDigest(
            mapIdentity,
            descriptor,
            actual,
            cancellationToken);
        if (!string.Equals(
                expectedDigest,
                actualDigest,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{message}: expected {expectedDigest}, got {actualDigest}.");
        }
    }

    internal static string ComputeAssetDigest(
        string mapIdentity,
        CompiledMapAssetDescriptor descriptor,
        IXAssetBuildData source,
        CancellationToken cancellationToken = default) =>
        CompiledMapBaselineDigest.ComputeAsset(
            mapIdentity,
            new CompiledMapAssetDescriptorSeed(
                descriptor.Kind,
                descriptor.SerializedType,
                descriptor.AssetName,
                descriptor.OwnerRow,
                descriptor.IsNested,
                descriptor.SourcePath),
            source,
            cancellationToken);

    private static bool SameTriggers(
        MapTriggersBuildData left,
        MapTriggersBuildData right) =>
        left.Models.SequenceEqual(right.Models) &&
        left.Hulls.SequenceEqual(right.Hulls) &&
        left.Slabs.SequenceEqual(right.Slabs);

    private static string FormatSyntaxDiagnostics(
        MapEntsSyntaxDocument document) =>
        document.Diagnostics.Count == 0
            ? "unknown syntax validation failure"
            : string.Join(
                "; ",
                document.Diagnostics.Select(value =>
                    $"{value.Code} at byte {value.Span.Offset}: " +
                    value.Message));

    private static void RequireValidCandidate(
        MapEntsEntityStringPatchCandidate candidate)
    {
        if (!PreservationCoverage.IsProven ||
            !candidate.Validation.IsValid)
        {
            throw new InvalidOperationException(
                "An invalid or coverage-incomplete MapEnts candidate cannot " +
                "replace an editing-session draft.");
        }
    }

    private sealed record PreparedJournalTransition
    {
        private PreparedJournalTransition(
            MapObjectId objectId,
            MapEntsPropertyEdit? property,
            MapEntsCardinalityEdit? cardinality,
            MapEntityCardinalityAssessment? cardinalityAssessment)
        {
            if (objectId.Value == Guid.Empty)
                throw new ArgumentOutOfRangeException(nameof(objectId));
            if ((property is null) == (cardinality is null) ||
                (cardinality is null) !=
                    (cardinalityAssessment is null))
            {
                throw new ArgumentException(
                    "A prepared MapEnt journal transition must contain exactly " +
                    "one operation and matching cardinality evidence.");
            }

            ObjectId = objectId;
            Property = property;
            Cardinality = cardinality;
            CardinalityAssessment = cardinalityAssessment;
        }

        public MapObjectId ObjectId { get; }
        public MapEntsPropertyEdit? Property { get; }
        public MapEntsCardinalityEdit? Cardinality { get; }
        public MapEntityCardinalityAssessment? CardinalityAssessment { get; }

        public static PreparedJournalTransition ForProperty(
            MapObjectId objectId,
            MapEntsPropertyEdit property) =>
            new(
                objectId,
                property ??
                    throw new ArgumentNullException(nameof(property)),
                cardinality: null,
                cardinalityAssessment: null);

        public static PreparedJournalTransition ForCardinality(
            MapObjectId objectId,
            MapEntsCardinalityEdit cardinality,
            MapEntityCardinalityAssessment assessment) =>
            new(
                objectId,
                property: null,
                cardinality ??
                    throw new ArgumentNullException(nameof(cardinality)),
                assessment ??
                    throw new ArgumentNullException(nameof(assessment)));
    }
}
