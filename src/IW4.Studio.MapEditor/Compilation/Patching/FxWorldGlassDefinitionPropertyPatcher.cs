using System.Collections.ObjectModel;
using IW4.Assets.Assets.FxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Compilation.Validation;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.Patching;

/// <summary>
/// One effective replacement of an existing serialized FxGlassDef scalar.
/// Derived initial-piece projections are not separate serialized patches.
/// </summary>
public sealed record FxWorldGlassDefinitionHalfThicknessPatch
{
    public FxWorldGlassDefinitionHalfThicknessPatch(
        MapObjectId objectId,
        SourceBindingId sourceBinding,
        int sourceOrdinal,
        float baselineHalfThickness,
        float editedHalfThickness)
    {
        if (objectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        if (sourceBinding.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceBinding));
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        if (!IsValidHalfThickness(baselineHalfThickness))
        {
            throw new ArgumentOutOfRangeException(
                nameof(baselineHalfThickness),
                "An imported glass half thickness must be finite and " +
                "strictly positive.");
        }
        if (!IsValidHalfThickness(editedHalfThickness))
        {
            throw new ArgumentOutOfRangeException(
                nameof(editedHalfThickness),
                "An edited glass half thickness must be finite and " +
                "strictly positive.");
        }
        if (SameBits(baselineHalfThickness, editedHalfThickness))
        {
            throw new ArgumentException(
                "An effective glass patch must change the serialized float.",
                nameof(editedHalfThickness));
        }

        ObjectId = objectId;
        SourceBinding = sourceBinding;
        SourceOrdinal = sourceOrdinal;
        BaselineHalfThickness = baselineHalfThickness;
        EditedHalfThickness = editedHalfThickness;
    }

    public MapObjectId ObjectId { get; }
    public SourceBindingId SourceBinding { get; }
    public int SourceOrdinal { get; }
    public float BaselineHalfThickness { get; }
    public float EditedHalfThickness { get; }

    internal static bool IsValidHalfThickness(float value) =>
        float.IsFinite(value) &&
        value > 0f &&
        BitConverter.SingleToInt32Bits(value) !=
            BitConverter.SingleToInt32Bits(-0f);

    internal static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);
}

/// <summary>
/// One effective replacement of an existing serialized FxGlassDef packed
/// color. The color is definition-owned and has no derived initial-piece
/// projection.
/// </summary>
public sealed record FxWorldGlassDefinitionColorPatch
{
    public FxWorldGlassDefinitionColorPatch(
        MapObjectId objectId,
        SourceBindingId sourceBinding,
        int sourceOrdinal,
        uint baselineColor,
        uint editedColor)
    {
        if (objectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        if (sourceBinding.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceBinding));
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        if (baselineColor == editedColor)
        {
            throw new ArgumentException(
                "An effective glass patch must change the serialized color.",
                nameof(editedColor));
        }

        ObjectId = objectId;
        SourceBinding = sourceBinding;
        SourceOrdinal = sourceOrdinal;
        BaselineColor = baselineColor;
        EditedColor = editedColor;
    }

    public MapObjectId ObjectId { get; }
    public SourceBindingId SourceBinding { get; }
    public int SourceOrdinal { get; }
    public uint BaselineColor { get; }
    public uint EditedColor { get; }
}

/// <summary>
/// Detached, validated FxMap replacement. Each field-specific patch
/// collection contains only definition values that differ from the imported
/// baseline.
/// </summary>
internal class FxWorldGlassDefinitionPropertyPatchCandidate
{
    public FxWorldGlassDefinitionPropertyPatchCandidate(
        CompiledMapAssetDescriptor? descriptor,
        FxWorldBuildData? baseline,
        FxWorldBuildData? buildData,
        IEnumerable<FxWorldGlassDefinitionHalfThicknessPatch>
            halfThicknessPatches,
        IEnumerable<FxWorldGlassDefinitionColorPatch> colorPatches,
        string? baselineSemanticDigest,
        MapPatchValidation validation)
    {
        ArgumentNullException.ThrowIfNull(halfThicknessPatches);
        ArgumentNullException.ThrowIfNull(colorPatches);
        ArgumentNullException.ThrowIfNull(validation);

        Descriptor = descriptor;
        Baseline = baseline is null ? null : Copy(baseline);
        BuildData = buildData is null ? null : Copy(buildData);
        HalfThicknessPatches = new ReadOnlyCollection<
            FxWorldGlassDefinitionHalfThicknessPatch>(
                halfThicknessPatches.ToArray());
        ColorPatches = new ReadOnlyCollection<
            FxWorldGlassDefinitionColorPatch>(
                colorPatches.ToArray());
        BaselineSemanticDigest = baselineSemanticDigest;
        Validation = validation;
    }

    public CompiledMapAssetDescriptor? Descriptor { get; }
    public FxWorldBuildData? Baseline { get; }
    public FxWorldBuildData? BuildData { get; }
    public IReadOnlyList<FxWorldGlassDefinitionHalfThicknessPatch>
        HalfThicknessPatches { get; }
    public IReadOnlyList<FxWorldGlassDefinitionColorPatch>
        ColorPatches { get; }
    public string? BaselineSemanticDigest { get; }
    public MapPatchValidation Validation { get; }

    private static FxWorldBuildData Copy(FxWorldBuildData value) =>
        new(value.Name, value.GlassSystem, value.DefinitionReferences);
}

/// <summary>
/// Rebuilds fixed-cardinality FxGlassDef definition-owned properties from
/// typed semantic state. No initial-piece, runtime-cache, reference, or
/// topology mutation is represented by this patcher.
/// </summary>
internal sealed class FxWorldGlassDefinitionPropertyPatcher
{
    private static readonly FxWorldBodyEmitter Emitter = new();

    public static MapPreservationCoverage
        HalfThicknessPreservationCoverage { get; } =
        new(
            MapAssetKind.FxMap,
            "Existing FxGlassDef HalfThickness scalar",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "$.name and FxMap row identity",
                "$.glassSystem root scalar and cardinality fields",
                "$.glassSystem.defs count and ordering",
                "$.glassSystem.defs[*] texture vectors, color, mip radii",
                "$.definitionReferences and nested pointer semantics",
                "$.glassSystem.initPieceStates and initGeoData",
                "$.glassSystem piece-place/state/dynamics runtime shapes",
                "$.glassSystem geoData, isInUse, cellBits, and visData",
                "$.glassSystem linkOrg and lightingHandles",
                "$.glassSystem.halfThickness RUNTIME cache",
                "Unselected FxGlassDef HalfThickness float bits"
            ],
            mutableFields:
            [
                "$.glassSystem.defs[existing-index].halfThickness"
            ]);

    public static MapPreservationCoverage ColorPreservationCoverage { get; } =
        new(
            MapAssetKind.FxMap,
            "Existing FxGlassDef packed color",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "$.name and FxMap row identity",
                "$.glassSystem root scalar and cardinality fields",
                "$.glassSystem.defs count and ordering",
                "$.glassSystem.defs[*] half thickness, texture vectors, and mip radii",
                "$.definitionReferences and nested pointer semantics",
                "$.glassSystem.initPieceStates and initGeoData",
                "$.glassSystem piece-place/state/dynamics runtime shapes",
                "$.glassSystem geoData, isInUse, cellBits, and visData",
                "$.glassSystem linkOrg and lightingHandles",
                "$.glassSystem.halfThickness RUNTIME cache",
                "Unselected FxGlassDef packed colors"
            ],
            mutableFields:
            [
                "$.glassSystem.defs[existing-index].color"
            ]);

    public static MapPreservationCoverage PreservationCoverage { get; } =
        new(
            MapAssetKind.FxMap,
            "Existing FxGlassDef definition properties",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "$.name and FxMap row identity",
                "$.glassSystem root scalar and cardinality fields",
                "$.glassSystem.defs count and ordering",
                "$.glassSystem.defs[*] texture vectors and mip radii",
                "$.definitionReferences and nested pointer semantics",
                "$.glassSystem.initPieceStates and initGeoData",
                "$.glassSystem all RUNTIME arrays and caches",
                "Unselected FxGlassDef property values"
            ],
            mutableFields:
            [
                "$.glassSystem.defs[existing-index].halfThickness",
                "$.glassSystem.defs[existing-index].color"
            ]);

    public FxWorldGlassDefinitionPropertyPatchCandidate Prepare(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        IEnumerable<CompiledSourceBinding> sourceBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(sourceBindings);

        var diagnostics = new List<string>();
        if (!bundle.TryGetBaseline(
                MapAssetKind.FxMap,
                out FxWorldBuildData? baseline) ||
            baseline is null)
        {
            diagnostics.Add(
                "The compiled map bundle has no detached FxMap baseline.");
            return InvalidCandidate(diagnostics);
        }

        CompiledMapAssetDescriptor descriptor =
            bundle.RequireAsset(MapAssetKind.FxMap);
        if (descriptor.SerializedType != XAssetType.FxMap ||
            descriptor.Kind != MapAssetKind.FxMap)
        {
            diagnostics.Add(
                "The compiled FxMap descriptor does not own an FxMap row.");
        }

        Dictionary<SourceBindingId, CompiledSourceBinding> bindingCatalog =
            BuildBindingCatalog(sourceBindings, diagnostics);
        EditorGlassObject[] definitions = document.Glass
            .Where(value =>
                value.Representation == GlassRepresentation.FxDefinition)
            .ToArray();
        if (definitions.Length != baseline.GlassSystem.Defs.Count)
        {
            diagnostics.Add(
                $"Fx glass-definition cardinality changed from " +
                $"{baseline.GlassSystem.Defs.Count} to {definitions.Length}; " +
                "this patcher cannot rebuild glass tables.");
        }

        var definitionsByOrdinal =
            new Dictionary<int, EditorGlassObject>();
        foreach (EditorGlassObject definition in definitions)
        {
            int ordinal = definition.SourceOrdinal.Value;
            if (ordinal < 0 ||
                ordinal >= baseline.GlassSystem.Defs.Count)
            {
                diagnostics.Add(
                    $"Fx glass definition {definition.Id} has out-of-range " +
                    $"source ordinal {ordinal}.");
                continue;
            }
            if (!definitionsByOrdinal.TryAdd(ordinal, definition))
            {
                diagnostics.Add(
                    $"More than one Fx glass definition claims source " +
                    $"ordinal {ordinal}.");
            }
        }

        var halfThicknessPatches =
            new List<FxWorldGlassDefinitionHalfThicknessPatch>();
        var colorPatches =
            new List<FxWorldGlassDefinitionColorPatch>();
        for (int ordinal = 0;
             ordinal < baseline.GlassSystem.Defs.Count;
             ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!definitionsByOrdinal.TryGetValue(
                    ordinal,
                    out EditorGlassObject? definition))
            {
                diagnostics.Add(
                    $"Fx glass-definition source ordinal {ordinal} is " +
                    "missing from the semantic document.");
                continue;
            }

            ValidateDefinitionIdentityAndBinding(
                bundle,
                descriptor,
                definition,
                ordinal,
                bindingCatalog,
                diagnostics);

            float baselineHalfThickness =
                baseline.GlassSystem.Defs[ordinal].HalfThickness;
            if (!FxWorldGlassDefinitionHalfThicknessPatch
                    .IsValidHalfThickness(baselineHalfThickness))
            {
                diagnostics.Add(
                    $"Imported Fx glass definition {ordinal} has a " +
                    "non-positive, negative-zero, or non-finite half " +
                    "thickness.");
            }
            else if (
                definition.HalfThickness.Value is not
                    float editedHalfThickness)
            {
                diagnostics.Add(
                    $"Fx glass definition {ordinal} has no half-thickness " +
                    "value.");
            }
            else if (!FxWorldGlassDefinitionHalfThicknessPatch
                         .IsValidHalfThickness(editedHalfThickness))
            {
                diagnostics.Add(
                    $"Fx glass definition {ordinal} requires a finite, " +
                    "strictly positive half thickness; negative zero is not " +
                    "valid.");
            }
            else
            {
                bool changed =
                    !FxWorldGlassDefinitionHalfThicknessPatch.SameBits(
                        baselineHalfThickness,
                        editedHalfThickness);
                MapValueProvenance expectedProvenance = changed
                    ? MapValueProvenance.Authored
                    : MapValueProvenance.ExactDecodedRuntime;
                if (definition.HalfThickness.Provenance !=
                    expectedProvenance)
                {
                    diagnostics.Add(
                        $"Fx glass definition {ordinal} half-thickness " +
                        $"provenance is " +
                        $"{definition.HalfThickness.Provenance}, not " +
                        $"{expectedProvenance}.");
                }
                if (changed)
                {
                    halfThicknessPatches.Add(
                        new FxWorldGlassDefinitionHalfThicknessPatch(
                            definition.Id,
                            definition.HalfThickness.SourceBinding,
                            ordinal,
                            baselineHalfThickness,
                            editedHalfThickness));
                }
            }

            uint baselineColor =
                baseline.GlassSystem.Defs[ordinal].Color;
            if (definition.Color.Value is not uint editedColor)
            {
                diagnostics.Add(
                    $"Fx glass definition {ordinal} has no packed-color " +
                    "value.");
            }
            else
            {
                bool changed = baselineColor != editedColor;
                MapValueProvenance expectedProvenance = changed
                    ? MapValueProvenance.Authored
                    : MapValueProvenance.ExactDecodedRuntime;
                if (definition.Color.Provenance != expectedProvenance)
                {
                    diagnostics.Add(
                        $"Fx glass definition {ordinal} packed-color " +
                        $"provenance is {definition.Color.Provenance}, not " +
                        $"{expectedProvenance}.");
                }
                if (changed)
                {
                    colorPatches.Add(
                        new FxWorldGlassDefinitionColorPatch(
                            definition.Id,
                            definition.Color.SourceBinding,
                            ordinal,
                            baselineColor,
                            editedColor));
                }
            }
        }

        FxWorldBuildData candidate;
        string baselineSemanticDigest;
        try
        {
            baselineSemanticDigest =
                RelocationInvariantAssetSemanticDigest.Compute(
                    baseline,
                    cancellationToken);
            candidate = baseline.WithGlassDefinitionProperties(
                    halfThicknessReplacements:
                    halfThicknessPatches
                        .OrderBy(value => value.SourceOrdinal)
                        .Select(value =>
                            new KeyValuePair<int, float>(
                                value.SourceOrdinal,
                                value.EditedHalfThickness)),
                    colorReplacements:
                    colorPatches
                        .OrderBy(value => value.SourceOrdinal)
                        .Select(value =>
                            new KeyValuePair<int, uint>(
                                value.SourceOrdinal,
                                value.EditedColor)));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException)
        {
            diagnostics.Add(
                $"The canonical FxMap definition-property transformation " +
                $"failed: " +
                exception.Message);
            return InvalidCandidate(diagnostics);
        }

        ValidateInitialPieceProjection(
            document,
            bundle,
            descriptor,
            baseline,
            candidate,
            bindingCatalog,
            diagnostics);
        diagnostics.AddRange(
            ValidatePreservation(
                    baseline,
                    candidate,
                    halfThicknessPatches,
                    colorPatches,
                    cancellationToken)
                .Diagnostics);
        if (!string.Equals(
                baselineSemanticDigest,
                RelocationInvariantAssetSemanticDigest.Compute(
                    baseline,
                    cancellationToken),
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                "Preparing the Fx glass candidate mutated the immutable " +
                "compiled baseline.");
        }

        return new FxWorldGlassDefinitionPropertyPatchCandidate(
            descriptor,
            baseline,
            candidate,
            halfThicknessPatches.OrderBy(value => value.SourceOrdinal),
            colorPatches.OrderBy(value => value.SourceOrdinal),
            baselineSemanticDigest,
            new MapPatchValidation(diagnostics));
    }

    public MapPatchValidation ValidatePreservation(
        FxWorldBuildData baseline,
        FxWorldBuildData candidate,
        IEnumerable<FxWorldGlassDefinitionHalfThicknessPatch>
            halfThicknessPatches,
        IEnumerable<FxWorldGlassDefinitionColorPatch> colorPatches,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(halfThicknessPatches);
        ArgumentNullException.ThrowIfNull(colorPatches);

        var diagnostics = new List<string>();
        FxWorldGlassDefinitionHalfThicknessPatch[] halfPatchCopy =
            halfThicknessPatches.ToArray();
        FxWorldGlassDefinitionColorPatch[] colorPatchCopy =
            colorPatches.ToArray();
        var halfByOrdinal = new Dictionary<
            int,
            FxWorldGlassDefinitionHalfThicknessPatch>();
        var colorByOrdinal = new Dictionary<
            int,
            FxWorldGlassDefinitionColorPatch>();
        var halfObjectIds = new HashSet<MapObjectId>();
        var colorObjectIds = new HashSet<MapObjectId>();
        var objectOrdinals = new Dictionary<MapObjectId, int>();
        var bindingIds = new HashSet<SourceBindingId>();
        foreach (FxWorldGlassDefinitionHalfThicknessPatch? patch in
                 halfPatchCopy)
        {
            if (patch is null)
            {
                diagnostics.Add(
                    "The Fx glass half-thickness patch set contains null.");
                continue;
            }
            if (!halfByOrdinal.TryAdd(patch.SourceOrdinal, patch))
            {
                diagnostics.Add(
                    $"The Fx glass patch set contains duplicate definition " +
                    $"ordinal {patch.SourceOrdinal}.");
            }
            if (!halfObjectIds.Add(patch.ObjectId))
            {
                diagnostics.Add(
                    $"The Fx glass patch set reuses semantic object " +
                    $"{patch.ObjectId}.");
            }
            if (objectOrdinals.TryGetValue(
                    patch.ObjectId,
                    out int existingOrdinal) &&
                existingOrdinal != patch.SourceOrdinal)
            {
                diagnostics.Add(
                    $"Fx glass semantic object {patch.ObjectId} is assigned " +
                    $"to both definition ordinal {existingOrdinal} and " +
                    $"{patch.SourceOrdinal}.");
            }
            else
            {
                objectOrdinals[patch.ObjectId] = patch.SourceOrdinal;
            }
            if (!bindingIds.Add(patch.SourceBinding))
            {
                diagnostics.Add(
                    $"The Fx glass patch set reuses compiled binding " +
                    $"{patch.SourceBinding}.");
            }
        }
        foreach (FxWorldGlassDefinitionColorPatch? patch in colorPatchCopy)
        {
            if (patch is null)
            {
                diagnostics.Add(
                    "The Fx glass color patch set contains null.");
                continue;
            }
            if (!colorByOrdinal.TryAdd(patch.SourceOrdinal, patch))
            {
                diagnostics.Add(
                    $"The Fx glass color patch set contains duplicate " +
                    $"definition ordinal {patch.SourceOrdinal}.");
            }
            if (!colorObjectIds.Add(patch.ObjectId))
            {
                diagnostics.Add(
                    $"The Fx glass color patch set reuses semantic object " +
                    $"{patch.ObjectId}.");
            }
            if (objectOrdinals.TryGetValue(
                    patch.ObjectId,
                    out int existingOrdinal) &&
                existingOrdinal != patch.SourceOrdinal)
            {
                diagnostics.Add(
                    $"Fx glass semantic object {patch.ObjectId} is assigned " +
                    $"to both definition ordinal {existingOrdinal} and " +
                    $"{patch.SourceOrdinal}.");
            }
            else
            {
                objectOrdinals[patch.ObjectId] = patch.SourceOrdinal;
            }
            if (!bindingIds.Add(patch.SourceBinding))
            {
                diagnostics.Add(
                    $"The Fx glass property patch set reuses compiled " +
                    $"binding {patch.SourceBinding}.");
            }
        }

        if (!string.Equals(
                baseline.Name,
                candidate.Name,
                StringComparison.Ordinal))
        {
            diagnostics.Add("FxMap name/row identity was not preserved.");
        }

        FxGlassSystem source = baseline.GlassSystem;
        FxGlassSystem edited = candidate.GlassSystem;
        ValidateRootPreservation(source, edited, diagnostics);
        ValidateDefinitionPreservation(
            source.Defs,
            edited.Defs,
            halfByOrdinal,
            colorByOrdinal,
            diagnostics);
        ValidateReferencePreservation(
            baseline.DefinitionReferences,
            candidate.DefinitionReferences,
            diagnostics,
            cancellationToken);
        ValidateArrayPreservation(source, edited, diagnostics);

        foreach (int ordinal in halfByOrdinal.Keys
                     .Concat(colorByOrdinal.Keys)
                     .Distinct()
                     .Where(value =>
                     value < 0 ||
                     value >= Math.Min(
                         source.Defs.Count,
                         edited.Defs.Count)))
        {
            diagnostics.Add(
                $"Fx glass patch definition ordinal {ordinal} is outside " +
                "the preserved definition table.");
        }

        try
        {
            FxWorldBuildData canonical =
                baseline.WithGlassDefinitionProperties(
                        halfThicknessReplacements:
                        halfByOrdinal.Values
                            .OrderBy(value => value.SourceOrdinal)
                            .Select(value =>
                                new KeyValuePair<int, float>(
                                    value.SourceOrdinal,
                                    value.EditedHalfThickness)),
                        colorReplacements:
                        colorByOrdinal.Values
                            .OrderBy(value => value.SourceOrdinal)
                            .Select(value =>
                                new KeyValuePair<int, uint>(
                                    value.SourceOrdinal,
                                    value.EditedColor)));
            if (!string.Equals(
                    RelocationInvariantAssetSemanticDigest.Compute(
                        canonical,
                        cancellationToken),
                    RelocationInvariantAssetSemanticDigest.Compute(
                        candidate,
                        cancellationToken),
                    StringComparison.Ordinal))
            {
                diagnostics.Add(
                    "FxMap candidate differs outside the canonical " +
                    "fixed-cardinality definition-property transformation.");
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidDataException)
        {
            diagnostics.Add(
                $"The authorized FxMap transformation is invalid: " +
                exception.Message);
        }

        diagnostics.AddRange(
            Emitter.Validate(candidate)
                .Select(value =>
                    $"FxMap emitter validation failed at {value.Path}: " +
                    value.Message));
        return new MapPatchValidation(diagnostics);
    }

    public void ApplyValidatedCandidate(
        FxWorldDraft draft,
        FxWorldGlassDefinitionPropertyPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireValidCandidate(candidate);

        FxWorldBuildData baseline = candidate.Baseline!;
        FxWorldBuildData current = draft.Data;
        if (!string.Equals(
                current.Name,
                baseline.Name,
                StringComparison.Ordinal) ||
            !HasSameTopology(current.GlassSystem, baseline.GlassSystem) ||
            current.DefinitionReferences.Count !=
                baseline.DefinitionReferences.Count)
        {
            throw new InvalidOperationException(
                "The staged FxMap draft changed row identity or glass-table " +
                "topology after map import.");
        }

        foreach (FxWorldGlassDefinitionHalfThicknessPatch patch in
                 candidate.HalfThicknessPatches)
        {
            float currentValue =
                current.GlassSystem.Defs[
                    patch.SourceOrdinal].HalfThickness;
            if (!FxWorldGlassDefinitionHalfThicknessPatch.SameBits(
                    currentValue,
                    patch.BaselineHalfThickness) &&
                !FxWorldGlassDefinitionHalfThicknessPatch.SameBits(
                    currentValue,
                    patch.EditedHalfThickness))
            {
                throw new InvalidOperationException(
                    $"The staged FxMap draft independently changed glass " +
                    $"definition {patch.SourceOrdinal} HalfThickness; the " +
                    "overlapping patch cannot be merged safely.");
            }
        }
        foreach (FxWorldGlassDefinitionColorPatch patch in
                 candidate.ColorPatches)
        {
            uint currentValue =
                current.GlassSystem.Defs[patch.SourceOrdinal].Color;
            if (currentValue != patch.BaselineColor &&
                currentValue != patch.EditedColor)
            {
                throw new InvalidOperationException(
                    $"The staged FxMap draft independently changed glass " +
                    $"definition {patch.SourceOrdinal} Color; the " +
                    "overlapping patch cannot be merged safely.");
            }
        }

        FxWorldBuildData merged =
            current.WithGlassDefinitionProperties(
                    halfThicknessReplacements:
                    candidate.HalfThicknessPatches.Select(value =>
                        new KeyValuePair<int, float>(
                            value.SourceOrdinal,
                            value.EditedHalfThickness)),
                    colorReplacements:
                    candidate.ColorPatches.Select(value =>
                        new KeyValuePair<int, uint>(
                            value.SourceOrdinal,
                            value.EditedColor)));
        var errors = Emitter.Validate(merged);
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                "The merged FxMap draft failed emitter validation: " +
                string.Join(
                    "; ",
                    errors.Select(value =>
                        $"{value.Path}: {value.Message}")));
        }

        draft.Replace(merged);
    }

    private static void RequireValidCandidate(
        FxWorldGlassDefinitionPropertyPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!PreservationCoverage.IsProven ||
            candidate.Descriptor is null ||
            candidate.Descriptor.Kind != MapAssetKind.FxMap ||
            candidate.Descriptor.SerializedType != XAssetType.FxMap ||
            candidate.Baseline is null ||
            candidate.BuildData is null ||
            candidate.BaselineSemanticDigest is null ||
            !candidate.Validation.IsValid ||
            (candidate.HalfThicknessPatches.Count == 0 &&
             candidate.ColorPatches.Count == 0))
        {
            throw new InvalidOperationException(
                "An invalid, empty, or coverage-incomplete Fx glass " +
                "candidate cannot replace a staged draft.");
        }
        if (!string.Equals(
                RelocationInvariantAssetSemanticDigest.Compute(
                    candidate.Baseline),
                candidate.BaselineSemanticDigest,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The detached FxMap baseline changed after candidate " +
                "validation.");
        }
    }

    private static Dictionary<SourceBindingId, CompiledSourceBinding>
        BuildBindingCatalog(
            IEnumerable<CompiledSourceBinding> sourceBindings,
            ICollection<string> diagnostics)
    {
        var result =
            new Dictionary<SourceBindingId, CompiledSourceBinding>();
        foreach (CompiledSourceBinding? binding in sourceBindings)
        {
            if (binding is null)
            {
                diagnostics.Add(
                    "The imported compiled-binding catalog contains null.");
                continue;
            }
            if (!result.TryAdd(binding.Id, binding))
            {
                diagnostics.Add(
                    $"The imported compiled-binding catalog contains " +
                    $"duplicate ID {binding.Id}.");
            }
        }
        return result;
    }

    private static void ValidateDefinitionIdentityAndBinding(
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor descriptor,
        EditorGlassObject definition,
        int ordinal,
        IReadOnlyDictionary<SourceBindingId, CompiledSourceBinding>
            bindingCatalog,
        ICollection<string> diagnostics)
    {
        if (definition.DefinitionIndex.Value != ordinal ||
            definition.Origin.Value is not null)
        {
            diagnostics.Add(
                $"Fx glass definition {ordinal} changes an unsupported " +
                "semantic definition field.");
        }

        MapObjectId expectedObject = DeterministicMapIdentity.Object(
            bundle.MapIdentity,
            XAssetType.FxMap.ToString(),
            descriptor.AssetName,
            "fx-glass-definition",
            ordinal);
        if (definition.Id != expectedObject)
        {
            diagnostics.Add(
                $"Fx glass definition {ordinal} has a non-deterministic " +
                "semantic identity.");
        }

        ValidateDefinitionBinding(
            bundle,
            descriptor,
            ordinal,
            "HalfThickness",
            $"$.glassSystem.defs[{ordinal}].halfThickness",
            definition.HalfThickness.SourceBinding,
            bindingCatalog,
            diagnostics);
        ValidateDefinitionBinding(
            bundle,
            descriptor,
            ordinal,
            "Color",
            $"$.glassSystem.defs[{ordinal}].color",
            definition.Color.SourceBinding,
            bindingCatalog,
            diagnostics);
    }

    private static void ValidateDefinitionBinding(
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor descriptor,
        int ordinal,
        string propertyName,
        string expectedPath,
        SourceBindingId bindingId,
        IReadOnlyDictionary<SourceBindingId, CompiledSourceBinding>
            bindingCatalog,
        ICollection<string> diagnostics)
    {
        SourceBindingId expectedBinding =
            DeterministicMapIdentity.Binding(
                bundle.MapIdentity,
                XAssetType.FxMap.ToString(),
                descriptor.AssetName,
                expectedPath,
                ordinal);
        if (bindingId != expectedBinding)
        {
            diagnostics.Add(
                $"Fx glass definition {ordinal} {propertyName} binding is " +
                "not its deterministic compiled-field identity.");
        }
        if (!bindingCatalog.TryGetValue(
                bindingId,
                out CompiledSourceBinding? binding))
        {
            diagnostics.Add(
                $"Fx glass definition {ordinal} has no compiled " +
                $"{propertyName} binding {bindingId}.");
            return;
        }
        if (binding.AssetType != XAssetType.FxMap ||
            binding.OwnerRow != descriptor.OwnerRow ||
            !string.Equals(
                binding.AssetName,
                descriptor.AssetName,
                StringComparison.Ordinal) ||
            !string.Equals(
                binding.BaselineDigest,
                descriptor.BaselineDigest,
                StringComparison.Ordinal) ||
            !string.Equals(
                binding.FieldPath,
                expectedPath,
                StringComparison.Ordinal) ||
            binding.SourceOrdinal != ordinal ||
            binding.Provenance !=
                MapValueProvenance.ExactDecodedRuntime)
        {
            diagnostics.Add(
                $"Fx glass definition {ordinal} does not have the exact " +
                $"owned FxMap {propertyName} binding.");
        }
    }

    private static void ValidateInitialPieceProjection(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor descriptor,
        FxWorldBuildData baseline,
        FxWorldBuildData candidate,
        IReadOnlyDictionary<SourceBindingId, CompiledSourceBinding>
            bindingCatalog,
        ICollection<string> diagnostics)
    {
        EditorGlassObject[] pieces = document.Glass
            .Where(value =>
                value.Representation ==
                    GlassRepresentation.FxInitialPiece)
            .ToArray();
        if (pieces.Length != baseline.GlassSystem.InitPieceStates.Count)
        {
            diagnostics.Add(
                $"Fx initial-piece cardinality changed from " +
                $"{baseline.GlassSystem.InitPieceStates.Count} to " +
                $"{pieces.Length}; this patcher cannot rebuild glass tables.");
        }

        var byOrdinal = new Dictionary<int, EditorGlassObject>();
        foreach (EditorGlassObject piece in pieces)
        {
            int ordinal = piece.SourceOrdinal.Value;
            if (ordinal < 0 ||
                ordinal >= baseline.GlassSystem.InitPieceStates.Count)
            {
                diagnostics.Add(
                    $"Fx initial glass piece {piece.Id} has out-of-range " +
                    $"source ordinal {ordinal}.");
                continue;
            }
            if (!byOrdinal.TryAdd(ordinal, piece))
            {
                diagnostics.Add(
                    $"More than one Fx initial glass piece claims source " +
                    $"ordinal {ordinal}.");
            }
        }

        for (int ordinal = 0;
             ordinal < baseline.GlassSystem.InitPieceStates.Count;
             ordinal++)
        {
            if (!byOrdinal.TryGetValue(
                    ordinal,
                    out EditorGlassObject? editor))
            {
                diagnostics.Add(
                    $"Fx initial glass-piece source ordinal {ordinal} is " +
                    "missing from the semantic document.");
                continue;
            }

            FxGlassInitPieceState source =
                baseline.GlassSystem.InitPieceStates[ordinal];
            MapObjectId expectedObject = DeterministicMapIdentity.Object(
                bundle.MapIdentity,
                XAssetType.FxMap.ToString(),
                descriptor.AssetName,
                "fx-glass-initial-piece",
                ordinal);
            if (editor.Id != expectedObject)
            {
                diagnostics.Add(
                    $"Fx initial glass piece {ordinal} has a " +
                    "non-deterministic semantic identity.");
            }
            if (editor.DefinitionIndex.Value != source.DefIndex ||
                !SameOrigin(editor.Origin.Value, source.Frame.Origin))
            {
                diagnostics.Add(
                    $"Fx initial glass piece {ordinal} changes DefIndex or " +
                    "serialized origin.");
            }

            float? expectedThickness =
                source.DefIndex < candidate.GlassSystem.Defs.Count
                    ? candidate.GlassSystem.Defs[
                        source.DefIndex].HalfThickness
                    : null;
            if (!SameNullable(
                    editor.HalfThickness.Value,
                    expectedThickness))
            {
                diagnostics.Add(
                    $"Fx initial glass piece {ordinal} does not project its " +
                    "definition's current HalfThickness.");
            }
            if (expectedThickness is not null)
            {
                string expectedPath =
                    $"$.glassSystem.defs[{source.DefIndex}].halfThickness";
                SourceBindingId expectedBinding =
                    DeterministicMapIdentity.Binding(
                        bundle.MapIdentity,
                        XAssetType.FxMap.ToString(),
                        descriptor.AssetName,
                        expectedPath,
                        source.DefIndex);
                if (editor.HalfThickness.SourceBinding != expectedBinding ||
                    editor.HalfThickness.Provenance !=
                        MapValueProvenance.Derived ||
                    !bindingCatalog.ContainsKey(expectedBinding))
                {
                    diagnostics.Add(
                        $"Fx initial glass piece {ordinal} does not retain " +
                        "the derived projection of its exact definition " +
                        "binding.");
                }
            }
        }
    }

    private static void ValidateRootPreservation(
        FxGlassSystem baseline,
        FxGlassSystem candidate,
        ICollection<string> diagnostics)
    {
        if (baseline.Time != candidate.Time ||
            baseline.PrevTime != candidate.PrevTime ||
            baseline.DefCount != candidate.DefCount ||
            baseline.PieceLimit != candidate.PieceLimit ||
            baseline.PieceWordCount != candidate.PieceWordCount ||
            baseline.InitPieceCount != candidate.InitPieceCount ||
            baseline.CellCount != candidate.CellCount ||
            baseline.ActivePieceCount != candidate.ActivePieceCount ||
            baseline.FirstFreePiece != candidate.FirstFreePiece ||
            baseline.GeoDataLimit != candidate.GeoDataLimit ||
            baseline.GeoDataCount != candidate.GeoDataCount ||
            baseline.InitGeoDataCount != candidate.InitGeoDataCount ||
            baseline.NeedToCompactData != candidate.NeedToCompactData ||
            baseline.InitCount != candidate.InitCount ||
            baseline.Pad66 != candidate.Pad66 ||
            !SameBits(
                baseline.EffectChanceAccum,
                candidate.EffectChanceAccum) ||
            baseline.LastPieceDeletionTime !=
                candidate.LastPieceDeletionTime)
        {
            diagnostics.Add(
                "FxMap glass-system root scalar or cardinality fields were " +
                "not preserved.");
        }
    }

    private static void ValidateDefinitionPreservation(
        IReadOnlyList<FxGlassDef> baseline,
        IReadOnlyList<FxGlassDef> candidate,
        IReadOnlyDictionary<
            int,
            FxWorldGlassDefinitionHalfThicknessPatch>
            halfThicknessPatches,
        IReadOnlyDictionary<
            int,
            FxWorldGlassDefinitionColorPatch> colorPatches,
        ICollection<string> diagnostics)
    {
        if (baseline.Count != candidate.Count)
        {
            diagnostics.Add(
                "FxMap glass-definition count or ordering was not preserved.");
        }
        int count = Math.Min(baseline.Count, candidate.Count);
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            FxGlassDef source = baseline[ordinal];
            FxGlassDef edited = candidate[ordinal];
            if (!SameSequence(source.TexVecs, edited.TexVecs, Same) ||
                !SameBits(
                    source.InvHighMipRadius,
                    edited.InvHighMipRadius) ||
                !SameBits(
                    source.ShatteredInvHighMipRadius,
                    edited.ShatteredInvHighMipRadius))
            {
                diagnostics.Add(
                    $"Fx glass definition {ordinal} changed outside " +
                    "the authorized definition properties.");
            }

            halfThicknessPatches.TryGetValue(
                ordinal,
                out FxWorldGlassDefinitionHalfThicknessPatch? halfPatch);
            if (halfPatch is null)
            {
                if (!SameBits(
                        source.HalfThickness,
                        edited.HalfThickness))
                {
                    diagnostics.Add(
                        $"Fx glass definition {ordinal} changed " +
                        "HalfThickness without an authorized patch.");
                }
            }
            else if (
                !SameBits(
                    halfPatch.BaselineHalfThickness,
                    source.HalfThickness) ||
                !SameBits(
                    halfPatch.EditedHalfThickness,
                    edited.HalfThickness) ||
                !FxWorldGlassDefinitionHalfThicknessPatch
                    .IsValidHalfThickness(source.HalfThickness) ||
                !FxWorldGlassDefinitionHalfThicknessPatch
                    .IsValidHalfThickness(edited.HalfThickness) ||
                SameBits(source.HalfThickness, edited.HalfThickness))
            {
                diagnostics.Add(
                    $"Fx glass definition {ordinal} does not match its " +
                    "authorized HalfThickness patch.");
            }

            colorPatches.TryGetValue(
                ordinal,
                out FxWorldGlassDefinitionColorPatch? colorPatch);
            if (colorPatch is null)
            {
                if (source.Color != edited.Color)
                {
                    diagnostics.Add(
                        $"Fx glass definition {ordinal} changed Color " +
                        "without an authorized patch.");
                }
            }
            else if (
                colorPatch.BaselineColor != source.Color ||
                colorPatch.EditedColor != edited.Color ||
                source.Color == edited.Color)
            {
                diagnostics.Add(
                    $"Fx glass definition {ordinal} does not match its " +
                    "authorized Color patch.");
            }

            if (halfPatch is not null &&
                colorPatch is not null &&
                halfPatch.ObjectId != colorPatch.ObjectId)
            {
                diagnostics.Add(
                    $"Fx glass definition {ordinal} property patches do not " +
                    "name the same semantic definition object.");
            }
        }
    }

    private static void ValidateReferencePreservation(
        IReadOnlyList<FxGlassDefReferenceBuildData> baseline,
        IReadOnlyList<FxGlassDefReferenceBuildData> candidate,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        if (baseline.Count != candidate.Count)
        {
            diagnostics.Add(
                "FxMap definition-reference cardinality was not preserved.");
        }
        int count = Math.Min(baseline.Count, candidate.Count);
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SameReference(
                    baseline[ordinal],
                    candidate[ordinal],
                    cancellationToken))
            {
                diagnostics.Add(
                    $"Fx glass definition {ordinal} changed symbolic or " +
                    "nested asset-reference semantics.");
            }
        }
    }

    private static void ValidateArrayPreservation(
        FxGlassSystem baseline,
        FxGlassSystem candidate,
        ICollection<string> diagnostics)
    {
        Check(
            SameSequence(
                baseline.PiecePlaces,
                candidate.PiecePlaces,
                Same),
            "piece-place RUNTIME cache",
            diagnostics);
        Check(
            SameSequence(
                baseline.PieceStates,
                candidate.PieceStates,
                Same),
            "piece-state RUNTIME cache",
            diagnostics);
        Check(
            SameSequence(
                baseline.PieceDynamics,
                candidate.PieceDynamics,
                Same),
            "piece-dynamics RUNTIME cache",
            diagnostics);
        Check(
            baseline.GeoData.SequenceEqual(candidate.GeoData),
            "geometry RUNTIME cache",
            diagnostics);
        Check(
            baseline.IsInUse.SequenceEqual(candidate.IsInUse),
            "is-in-use RUNTIME cache",
            diagnostics);
        Check(
            baseline.CellBits.SequenceEqual(candidate.CellBits),
            "cell-bit RUNTIME cache",
            diagnostics);
        Check(
            baseline.VisData.SequenceEqual(candidate.VisData),
            "visibility RUNTIME cache",
            diagnostics);
        Check(
            SameSequence(baseline.LinkOrg, candidate.LinkOrg, Same),
            "link-origin RUNTIME cache",
            diagnostics);
        Check(
            SameSequence(
                baseline.HalfThickness,
                candidate.HalfThickness,
                SameBits),
            "half-thickness RUNTIME cache",
            diagnostics);
        Check(
            baseline.LightingHandles.SequenceEqual(
                candidate.LightingHandles),
            "lighting-handle table",
            diagnostics);
        Check(
            SameSequence(
                baseline.InitPieceStates,
                candidate.InitPieceStates,
                Same),
            "initial-piece table",
            diagnostics);
        Check(
            baseline.InitGeoData.SequenceEqual(candidate.InitGeoData),
            "initial-geometry table",
            diagnostics);
    }

    private static void Check(
        bool preserved,
        string field,
        ICollection<string> diagnostics)
    {
        if (!preserved)
            diagnostics.Add($"FxMap {field} was not preserved.");
    }

    private static bool HasSameTopology(
        FxGlassSystem left,
        FxGlassSystem right) =>
        left.DefCount == right.DefCount &&
        left.PieceLimit == right.PieceLimit &&
        left.PieceWordCount == right.PieceWordCount &&
        left.InitPieceCount == right.InitPieceCount &&
        left.CellCount == right.CellCount &&
        left.GeoDataLimit == right.GeoDataLimit &&
        left.InitGeoDataCount == right.InitGeoDataCount &&
        left.Defs.Count == right.Defs.Count &&
        left.PiecePlaces.Count == right.PiecePlaces.Count &&
        left.PieceStates.Count == right.PieceStates.Count &&
        left.PieceDynamics.Count == right.PieceDynamics.Count &&
        left.GeoData.Count == right.GeoData.Count &&
        left.IsInUse.Count == right.IsInUse.Count &&
        left.CellBits.Count == right.CellBits.Count &&
        left.VisData.Count == right.VisData.Count &&
        left.LinkOrg.Count == right.LinkOrg.Count &&
        left.HalfThickness.Count == right.HalfThickness.Count &&
        left.LightingHandles.Count == right.LightingHandles.Count &&
        left.InitPieceStates.Count == right.InitPieceStates.Count &&
        left.InitGeoData.Count == right.InitGeoData.Count;

    private static bool SameReference(
        FxGlassDefReferenceBuildData left,
        FxGlassDefReferenceBuildData right,
        CancellationToken cancellationToken) =>
        Equals(left.Material, right.Material) &&
        Equals(left.ShatteredMaterial, right.ShatteredMaterial) &&
        Equals(left.PhysPreset, right.PhysPreset) &&
        SameLink(left.MaterialLink, right.MaterialLink, cancellationToken) &&
        SameLink(
            left.ShatteredMaterialLink,
            right.ShatteredMaterialLink,
            cancellationToken) &&
        SameLink(
            left.PhysPresetLink,
            right.PhysPresetLink,
            cancellationToken);

    private static bool SameLink(
        NestedXAssetBuildLink? left,
        NestedXAssetBuildLink? right,
        CancellationToken cancellationToken)
    {
        if (left is null || right is null)
            return left is null && right is null;
        if (!Equals(left.Reference, right.Reference) ||
            left.SourceForm != right.SourceForm ||
            left.ImportedPackedRaw != right.ImportedPackedRaw ||
            left.ImportedOwnerCellRaw != right.ImportedOwnerCellRaw)
        {
            return false;
        }
        if (left.IncomingDefinition is null ||
            right.IncomingDefinition is null)
        {
            return left.IncomingDefinition is null &&
                   right.IncomingDefinition is null;
        }
        if (ReferenceEquals(
                left.IncomingDefinition,
                right.IncomingDefinition))
        {
            return true;
        }
        return string.Equals(
            RelocationInvariantAssetSemanticDigest.Compute(
                left.IncomingDefinition,
                cancellationToken),
            RelocationInvariantAssetSemanticDigest.Compute(
                right.IncomingDefinition,
                cancellationToken),
            StringComparison.Ordinal);
    }

    private static bool Same(
        FxGlassPiecePlace left,
        FxGlassPiecePlace right) =>
        Same(left.Frame, right.Frame) &&
        SameBits(left.Radius, right.Radius) &&
        left.NextFree == right.NextFree;

    private static bool Same(
        FxGlassPieceState left,
        FxGlassPieceState right) =>
        Same(left.TexCoordOrigin, right.TexCoordOrigin) &&
        left.SupportMask == right.SupportMask &&
        left.InitIndex == right.InitIndex &&
        left.GeoDataStart == right.GeoDataStart &&
        left.DefIndex == right.DefIndex &&
        left.Pad11.SequenceEqual(right.Pad11) &&
        left.VertCount == right.VertCount &&
        left.HoleDataCount == right.HoleDataCount &&
        left.CrackDataCount == right.CrackDataCount &&
        left.FanDataCount == right.FanDataCount &&
        left.Flags == right.Flags &&
        SameBits(left.AreaX2, right.AreaX2);

    private static bool Same(
        FxGlassPieceDynamics left,
        FxGlassPieceDynamics right) =>
        left.FallTime == right.FallTime &&
        left.PhysObjId == right.PhysObjId &&
        left.PhysJointId == right.PhysJointId &&
        Same(left.Vel, right.Vel) &&
        Same(left.AVel, right.AVel);

    private static bool Same(
        FxGlassInitPieceState left,
        FxGlassInitPieceState right) =>
        Same(left.Frame, right.Frame) &&
        SameBits(left.Radius, right.Radius) &&
        Same(left.TexCoordOrigin, right.TexCoordOrigin) &&
        left.SupportMask == right.SupportMask &&
        SameBits(left.AreaX2, right.AreaX2) &&
        left.DefIndex == right.DefIndex &&
        left.VertCount == right.VertCount &&
        left.FanDataCount == right.FanDataCount &&
        left.Pad33 == right.Pad33;

    private static bool Same(
        FxSpatialFrame left,
        FxSpatialFrame right) =>
        Same(left.Quat, right.Quat) &&
        Same(left.Origin, right.Origin);

    private static bool Same(FxQuat left, FxQuat right) =>
        SameBits(left.X, right.X) &&
        SameBits(left.Y, right.Y) &&
        SameBits(left.Z, right.Z) &&
        SameBits(left.W, right.W);

    private static bool Same(FxVec2 left, FxVec2 right) =>
        SameBits(left.X, right.X) &&
        SameBits(left.Y, right.Y);

    private static bool Same(FxVec3 left, FxVec3 right) =>
        SameBits(left.X, right.X) &&
        SameBits(left.Y, right.Y) &&
        SameBits(left.Z, right.Z);

    private static bool SameOrigin(
        MapVector3? left,
        FxVec3 right) =>
        left is { } value &&
        SameBits(value.X, right.X) &&
        SameBits(value.Y, right.Y) &&
        SameBits(value.Z, right.Z);

    private static bool SameNullable(float? left, float? right) =>
        left is null || right is null
            ? left is null && right is null
            : SameBits(left.Value, right.Value);

    private static bool SameSequence<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, T, bool> equal)
    {
        if (left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            if (!equal(left[index], right[index]))
                return false;
        }
        return true;
    }

    private static bool SameBits(float left, float right) =>
        FxWorldGlassDefinitionHalfThicknessPatch.SameBits(left, right);

    private static FxWorldGlassDefinitionPropertyPatchCandidate
        InvalidCandidate(IEnumerable<string> diagnostics) =>
        new(
            descriptor: null,
            baseline: null,
            buildData: null,
            halfThicknessPatches: [],
            colorPatches: [],
            baselineSemanticDigest: null,
            new MapPatchValidation(diagnostics));
}
