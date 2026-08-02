using System.Collections.ObjectModel;
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

[Flags]
public enum PrimaryLightColorComponentSet
{
    None = 0,
    Red = 1 << 0,
    Green = 1 << 1,
    Blue = 1 << 2
}

public sealed record PrimaryLightColorPatch(
    MapObjectId ObjectId,
    SourceBindingId SourceBinding,
    int SourceOrdinal,
    MapVector3 BaselineColor,
    MapVector3 EditedColor,
    PrimaryLightColorComponentSet ChangedComponents);

public sealed record PrimaryLightExponentPatch(
    MapObjectId ObjectId,
    SourceBindingId SourceBinding,
    int SourceOrdinal,
    byte BaselineExponent,
    byte EditedExponent);

public sealed record PrimaryLightSpotFalloffPatch(
    MapObjectId ObjectId,
    SourceBindingId SourceBinding,
    int SourceOrdinal,
    float BaselineCosHalfFovInner,
    float EditedCosHalfFovInner);

/// <summary>
/// Fully detached candidate for the bounded existing-row ComPrimaryLight
/// property capability. It cannot represent spatial-envelope, topology, or
/// cardinality changes.
/// </summary>
internal sealed class ComWorldPrimaryLightPropertyPatchCandidate
{
    public ComWorldPrimaryLightPropertyPatchCandidate(
        ComWorldBuildData baseline,
        ComWorldBuildData buildData,
        IEnumerable<PrimaryLightColorPatch> colorPatches,
        IEnumerable<PrimaryLightExponentPatch> exponentPatches,
        IEnumerable<PrimaryLightSpotFalloffPatch> spotFalloffPatches,
        MapPatchValidation validation)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(buildData);
        ArgumentNullException.ThrowIfNull(colorPatches);
        ArgumentNullException.ThrowIfNull(exponentPatches);
        ArgumentNullException.ThrowIfNull(spotFalloffPatches);
        ArgumentNullException.ThrowIfNull(validation);

        Baseline = Copy(baseline);
        BuildData = Copy(buildData);
        ColorPatches = new ReadOnlyCollection<PrimaryLightColorPatch>(
            colorPatches.ToArray());
        ExponentPatches =
            new ReadOnlyCollection<PrimaryLightExponentPatch>(
                exponentPatches.ToArray());
        SpotFalloffPatches =
            new ReadOnlyCollection<PrimaryLightSpotFalloffPatch>(
                spotFalloffPatches.ToArray());
        Validation = validation;
    }

    public ComWorldBuildData Baseline { get; }
    public ComWorldBuildData BuildData { get; }
    public IReadOnlyList<PrimaryLightColorPatch> ColorPatches { get; }
    public IReadOnlyList<PrimaryLightExponentPatch> ExponentPatches { get; }
    public IReadOnlyList<PrimaryLightSpotFalloffPatch>
        SpotFalloffPatches { get; }
    public MapPatchValidation Validation { get; }

    private static ComWorldBuildData Copy(ComWorldBuildData value) =>
        new(value.Name, value.IsInUse, value.PrimaryLights);
}

/// <summary>
/// Creates and validates one detached replacement for existing ComMap primary
/// light Color components, Exponent bytes, and type-2 inner-cone falloff.
/// Every other root and light field is covered by an exact preservation
/// comparison.
/// </summary>
internal sealed class ComWorldPrimaryLightPropertyPatcher
{
    private static readonly ComWorldBodyEmitter Emitter = new();

    public static MapPreservationCoverage ColorPreservationCoverage { get; } =
        new(
        MapAssetKind.ComMap,
        "Existing ComMap primary-light Color component",
        MapPreservationCoverageStatus.Proven,
        preservedFields:
        [
            "$.name and row identity",
            "$.isInUse",
            "$.primaryLights count and ordering",
            "$.primaryLights[*].type",
            "$.primaryLights[*].canUseShadowMap",
            "$.primaryLights[*].exponent",
            "$.primaryLights[*].unused",
            "$.primaryLights[*].direction",
            "$.primaryLights[*].origin",
            "$.primaryLights[*].radius",
            "$.primaryLights[*].cosHalfFovOuter",
            "$.primaryLights[*].cosHalfFovInner",
            "$.primaryLights[*].cosHalfFovExpanded",
            "$.primaryLights[*].rotationLimit",
            "$.primaryLights[*].translationLimit",
            "$.primaryLights[*].defName",
            "Unselected Color components",
            "Fixed ComMap root and 0x44-byte primary-light record topology"
        ],
        mutableFields:
        [
            "$.primaryLights[existing-index].color.x",
            "$.primaryLights[existing-index].color.y",
            "$.primaryLights[existing-index].color.z"
        ]);

    public static MapPreservationCoverage
        ExponentPreservationCoverage { get; } = new(
            MapAssetKind.ComMap,
            "Existing ComMap primary-light Exponent byte",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "$.name and row identity",
                "$.isInUse",
                "$.primaryLights count and ordering",
                "$.primaryLights[*].type",
                "$.primaryLights[*].canUseShadowMap",
                "$.primaryLights[*].unused",
                "$.primaryLights[*].color",
                "$.primaryLights[*].direction",
                "$.primaryLights[*].origin",
                "$.primaryLights[*].radius",
                "$.primaryLights[*].cosHalfFovOuter",
                "$.primaryLights[*].cosHalfFovInner",
                "$.primaryLights[*].cosHalfFovExpanded",
                "$.primaryLights[*].rotationLimit",
                "$.primaryLights[*].translationLimit",
                "$.primaryLights[*].defName",
                "Fixed ComMap root and 0x44-byte primary-light record topology"
            ],
            mutableFields:
            [
                "$.primaryLights[existing-index].exponent"
            ]);

    public static MapPreservationCoverage
        SpotFalloffPreservationCoverage { get; } = new(
            MapAssetKind.ComMap,
            "Existing type-2 ComMap primary-light inner-cone falloff",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "$.name and row identity",
                "$.isInUse",
                "$.primaryLights count and ordering",
                "$.primaryLights[*].type",
                "$.primaryLights[*].canUseShadowMap",
                "$.primaryLights[*].exponent",
                "$.primaryLights[*].unused",
                "$.primaryLights[*].color",
                "$.primaryLights[*].direction",
                "$.primaryLights[*].origin",
                "$.primaryLights[*].radius",
                "$.primaryLights[*].cosHalfFovOuter",
                "$.primaryLights[*].cosHalfFovExpanded",
                "$.primaryLights[*].rotationLimit",
                "$.primaryLights[*].translationLimit",
                "$.primaryLights[*].defName",
                "Every unselected inner-cone value",
                "Fixed ComMap root and 0x44-byte primary-light record topology",
                "Imported Gfx light-region, shadow, surface, static-model, and light-grid membership"
            ],
            mutableFields:
            [
                "$.primaryLights[type-2-existing-index].cosHalfFovInner"
            ]);

    public ComWorldPrimaryLightPropertyPatchCandidate Prepare(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        IEnumerable<CompiledSourceBinding> sourceBindings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(sourceBindings);

        var diagnostics = new List<string>();
        if (!bundle.TryGetBaseline(
                MapAssetKind.ComMap,
                out ComWorldBuildData? baseline) ||
            baseline is null)
        {
            diagnostics.Add(
                "The compiled map bundle has no detached ComMap baseline.");
            return InvalidCandidate(diagnostics);
        }

        CompiledMapAssetDescriptor descriptor =
            bundle.RequireAsset(MapAssetKind.ComMap);
        Dictionary<SourceBindingId, CompiledSourceBinding> bindingCatalog =
            BuildBindingCatalog(sourceBindings, diagnostics);
        EditorPrimaryLight[] editorLights = document.PrimaryLights.ToArray();
        if (editorLights.Length != baseline.PrimaryLights.Count)
        {
            diagnostics.Add(
                $"Primary-light cardinality changed from " +
                $"{baseline.PrimaryLights.Count} to {editorLights.Length}; " +
                "the bounded primary-light property patcher cannot rebuild " +
                "light tables.");
        }

        var byOrdinal = new Dictionary<int, EditorPrimaryLight>();
        foreach (EditorPrimaryLight editorLight in editorLights)
        {
            int ordinal = editorLight.SourceOrdinal.Value;
            if (ordinal < 0 || ordinal >= baseline.PrimaryLights.Count)
            {
                diagnostics.Add(
                    $"Primary light {editorLight.Id} has out-of-range source " +
                    $"ordinal {ordinal}.");
                continue;
            }
            if (!byOrdinal.TryAdd(ordinal, editorLight))
            {
                diagnostics.Add(
                    $"More than one primary light claims source ordinal {ordinal}.");
            }
        }

        ComPrimaryLightBuildData[] candidateLights =
            baseline.PrimaryLights.ToArray();
        var colorPatches = new List<PrimaryLightColorPatch>();
        var exponentPatches = new List<PrimaryLightExponentPatch>();
        var spotFalloffPatches =
            new List<PrimaryLightSpotFalloffPatch>();
        for (int ordinal = 0;
             ordinal < baseline.PrimaryLights.Count;
             ordinal++)
        {
            if (!byOrdinal.TryGetValue(
                    ordinal,
                    out EditorPrimaryLight? editorLight))
            {
                diagnostics.Add(
                    $"Primary-light source ordinal {ordinal} is missing from " +
                    "the semantic document.");
                continue;
            }

            ComPrimaryLightBuildData source = baseline.PrimaryLights[ordinal];
            ValidateUnsupportedSemanticPropertiesUnchanged(
                editorLight,
                source,
                diagnostics);
            ValidateObjectIdentity(
                bundle,
                descriptor,
                editorLight,
                ordinal,
                diagnostics);

            MapVector3 baselineColor = ToMapVector(source.Color);
            MapVector3 editedColor = editorLight.Color.Value;
            PrimaryLightColorComponentSet changed =
                ChangedComponents(baselineColor, editedColor);
            if (changed != PrimaryLightColorComponentSet.None)
            {
                ValidatePropertyBinding(
                    bundle,
                    descriptor,
                    editorLight.Color.SourceBinding,
                    ordinal,
                    "color",
                    "Color",
                    bindingCatalog,
                    diagnostics);
                if (!IsValidColor(editedColor))
                {
                    diagnostics.Add(
                        $"Primary-light source ordinal {ordinal} has a Color " +
                        "component that is non-finite or negative.");
                }
                else
                {
                    colorPatches.Add(new PrimaryLightColorPatch(
                        editorLight.Id,
                        editorLight.Color.SourceBinding,
                        ordinal,
                        baselineColor,
                        editedColor,
                        changed));
                }
            }

            byte editedExponent = editorLight.Exponent.Value;
            if (editedExponent != source.Exponent)
            {
                ValidatePropertyBinding(
                    bundle,
                    descriptor,
                    editorLight.Exponent.SourceBinding,
                    ordinal,
                    "exponent",
                    "Exponent",
                    bindingCatalog,
                    diagnostics);
                exponentPatches.Add(new PrimaryLightExponentPatch(
                    editorLight.Id,
                    editorLight.Exponent.SourceBinding,
                    ordinal,
                    source.Exponent,
                    editedExponent));
            }

            float editedCosHalfFovInner =
                editorLight.CosHalfFovInner.Value;
            if (!Same(editedCosHalfFovInner, source.CosHalfFovInner))
            {
                ValidatePropertyBinding(
                    bundle,
                    descriptor,
                    editorLight.CosHalfFovInner.SourceBinding,
                    ordinal,
                    "cosHalfFovInner",
                    "CosHalfFovInner",
                    bindingCatalog,
                    diagnostics);
                if (!IsValidSpotFalloff(
                        source.Type,
                        source.CosHalfFovOuter,
                        source.CosHalfFovInner) ||
                    !IsValidSpotFalloff(
                        editorLight.LightType.Value,
                        editorLight.CosHalfFovOuter.Value,
                        editedCosHalfFovInner))
                {
                    diagnostics.Add(
                        $"Primary-light source ordinal {ordinal} cannot edit " +
                        "spot falloff: imported and edited values must both be " +
                        "type 2 and satisfy 0 < outer < inner <= 1.");
                }
                else
                {
                    spotFalloffPatches.Add(
                        new PrimaryLightSpotFalloffPatch(
                            editorLight.Id,
                            editorLight.CosHalfFovInner.SourceBinding,
                            ordinal,
                            source.CosHalfFovInner,
                            editedCosHalfFovInner));
                }
            }

            candidateLights[ordinal] = source with
            {
                Color = ToBuildVector(editedColor),
                Exponent = editedExponent,
                CosHalfFovInner = editedCosHalfFovInner
            };
        }

        var candidate = new ComWorldBuildData(
            baseline.Name,
            baseline.IsInUse,
            candidateLights);
        MapPatchValidation preservation = ValidatePreservation(
            baseline,
            candidate,
            colorPatches,
            exponentPatches,
            spotFalloffPatches);
        diagnostics.AddRange(preservation.Diagnostics);
        return new ComWorldPrimaryLightPropertyPatchCandidate(
            baseline,
            candidate,
            colorPatches,
            exponentPatches,
            spotFalloffPatches,
            new MapPatchValidation(diagnostics));
    }

    public MapPatchValidation ValidatePreservation(
        ComWorldBuildData baseline,
        ComWorldBuildData candidate,
        IEnumerable<PrimaryLightColorPatch> colorPatches,
        IEnumerable<PrimaryLightExponentPatch> exponentPatches) =>
        ValidatePreservation(
            baseline,
            candidate,
            colorPatches,
            exponentPatches,
            spotFalloffPatches: []);

    public MapPatchValidation ValidatePreservation(
        ComWorldBuildData baseline,
        ComWorldBuildData candidate,
        IEnumerable<PrimaryLightColorPatch> colorPatches,
        IEnumerable<PrimaryLightExponentPatch> exponentPatches,
        IEnumerable<PrimaryLightSpotFalloffPatch> spotFalloffPatches)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(colorPatches);
        ArgumentNullException.ThrowIfNull(exponentPatches);
        ArgumentNullException.ThrowIfNull(spotFalloffPatches);

        var diagnostics = new List<string>();
        PrimaryLightColorPatch[] colorPatchCopy = colorPatches.ToArray();
        Dictionary<int, PrimaryLightColorPatch> colorByOrdinal = [];
        foreach (PrimaryLightColorPatch patch in colorPatchCopy)
        {
            if (patch is null)
            {
                diagnostics.Add("The color patch set contains a null patch.");
                continue;
            }
            if (patch.SourceOrdinal < 0 ||
                !colorByOrdinal.TryAdd(patch.SourceOrdinal, patch))
            {
                diagnostics.Add(
                    $"The color patch set has invalid or duplicate source " +
                    $"ordinal {patch.SourceOrdinal}.");
            }
            if (patch.ChangedComponents == PrimaryLightColorComponentSet.None ||
                (patch.ChangedComponents &
                 ~(PrimaryLightColorComponentSet.Red |
                   PrimaryLightColorComponentSet.Green |
                   PrimaryLightColorComponentSet.Blue)) != 0)
            {
                diagnostics.Add(
                    $"Primary-light source ordinal {patch.SourceOrdinal} has " +
                    "an invalid changed-component set.");
            }
        }

        PrimaryLightExponentPatch[] exponentPatchCopy =
            exponentPatches.ToArray();
        Dictionary<int, PrimaryLightExponentPatch> exponentByOrdinal = [];
        foreach (PrimaryLightExponentPatch patch in exponentPatchCopy)
        {
            if (patch is null)
            {
                diagnostics.Add("The exponent patch set contains a null patch.");
                continue;
            }
            if (patch.SourceOrdinal < 0 ||
                !exponentByOrdinal.TryAdd(patch.SourceOrdinal, patch))
            {
                diagnostics.Add(
                    $"The exponent patch set has invalid or duplicate source " +
                    $"ordinal {patch.SourceOrdinal}.");
            }
            if (patch.BaselineExponent == patch.EditedExponent)
            {
                diagnostics.Add(
                    $"Primary-light source ordinal {patch.SourceOrdinal} has " +
                    "a net-zero Exponent patch.");
            }
        }

        PrimaryLightSpotFalloffPatch[] spotFalloffPatchCopy =
            spotFalloffPatches.ToArray();
        Dictionary<int, PrimaryLightSpotFalloffPatch>
            spotFalloffByOrdinal = [];
        foreach (PrimaryLightSpotFalloffPatch patch in spotFalloffPatchCopy)
        {
            if (patch is null)
            {
                diagnostics.Add(
                    "The spot-falloff patch set contains a null patch.");
                continue;
            }
            if (patch.SourceOrdinal < 0 ||
                !spotFalloffByOrdinal.TryAdd(
                    patch.SourceOrdinal,
                    patch))
            {
                diagnostics.Add(
                    $"The spot-falloff patch set has invalid or duplicate " +
                    $"source ordinal {patch.SourceOrdinal}.");
            }
            if (Same(
                    patch.BaselineCosHalfFovInner,
                    patch.EditedCosHalfFovInner))
            {
                diagnostics.Add(
                    $"Primary-light source ordinal {patch.SourceOrdinal} has " +
                    "a net-zero spot-falloff patch.");
            }
        }

        if (!string.Equals(
                baseline.Name,
                candidate.Name,
                StringComparison.Ordinal))
        {
            diagnostics.Add("ComMap name/row identity was not preserved.");
        }
        if (baseline.IsInUse != candidate.IsInUse)
            diagnostics.Add("ComMap isInUse was not preserved.");
        if (baseline.PrimaryLights.Count != candidate.PrimaryLights.Count)
        {
            diagnostics.Add(
                "ComMap primary-light count and ordering were not preserved.");
        }

        int count = Math.Min(
            baseline.PrimaryLights.Count,
            candidate.PrimaryLights.Count);
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            ComPrimaryLightBuildData source = baseline.PrimaryLights[ordinal];
            ComPrimaryLightBuildData edited = candidate.PrimaryLights[ordinal];
            colorByOrdinal.TryGetValue(
                ordinal,
                out PrimaryLightColorPatch? colorPatch);
            exponentByOrdinal.TryGetValue(
                ordinal,
                out PrimaryLightExponentPatch? exponentPatch);
            spotFalloffByOrdinal.TryGetValue(
                ordinal,
                out PrimaryLightSpotFalloffPatch? spotFalloffPatch);
            ValidateLightPreservation(
                ordinal,
                source,
                edited,
                colorPatch,
                exponentPatch,
                spotFalloffPatch,
                diagnostics);
        }

        foreach (int missingOrdinal in colorByOrdinal.Keys.Where(
                     ordinal => ordinal >= count))
        {
            diagnostics.Add(
                $"Color patch source ordinal {missingOrdinal} is outside the " +
                "preserved primary-light table.");
        }
        foreach (int missingOrdinal in exponentByOrdinal.Keys.Where(
                     ordinal => ordinal >= count))
        {
            diagnostics.Add(
                $"Exponent patch source ordinal {missingOrdinal} is outside " +
                "the preserved primary-light table.");
        }
        foreach (int missingOrdinal in spotFalloffByOrdinal.Keys.Where(
                     ordinal => ordinal >= count))
        {
            diagnostics.Add(
                $"Spot-falloff patch source ordinal {missingOrdinal} is " +
                "outside the preserved primary-light table.");
        }

        diagnostics.AddRange(
            Emitter.Validate(candidate)
                .Select(issue =>
                    $"ComMap emitter validation failed at {issue.Path}: " +
                    issue.Message));
        return new MapPatchValidation(diagnostics);
    }

    public void ApplyValidatedCandidate(
        ComWorldDraft draft,
        ComWorldPrimaryLightPropertyPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(candidate);
        if ((candidate.ColorPatches.Count != 0 &&
             !ColorPreservationCoverage.IsProven) ||
            (candidate.ExponentPatches.Count != 0 &&
             !ExponentPreservationCoverage.IsProven) ||
            (candidate.SpotFalloffPatches.Count != 0 &&
             !SpotFalloffPreservationCoverage.IsProven) ||
            !candidate.Validation.IsValid)
        {
            throw new InvalidOperationException(
                "An invalid or coverage-incomplete ComMap candidate cannot " +
                "replace an editing-session draft.");
        }

        if (!string.Equals(
                candidate.Baseline.Name,
                draft.Name,
                StringComparison.Ordinal) ||
            candidate.Baseline.PrimaryLights.Count !=
                draft.PrimaryLights.Count)
        {
            throw new InvalidOperationException(
                "The ComMap editing-session draft changed row identity or " +
                "primary-light cardinality after map import.");
        }

        foreach (PrimaryLightColorPatch patch in candidate.ColorPatches)
        {
            ComPrimaryLightBuildData current =
                draft.PrimaryLights[patch.SourceOrdinal];
            Float3BuildData baseline =
                candidate.Baseline.PrimaryLights[
                    patch.SourceOrdinal].Color;
            Float3BuildData edited =
                candidate.BuildData.PrimaryLights[
                    patch.SourceOrdinal].Color;
            RequireCompatibleComponent(
                patch,
                PrimaryLightColorComponentSet.Red,
                baseline.X,
                current.Color.X,
                edited.X);
            RequireCompatibleComponent(
                patch,
                PrimaryLightColorComponentSet.Green,
                baseline.Y,
                current.Color.Y,
                edited.Y);
            RequireCompatibleComponent(
                patch,
                PrimaryLightColorComponentSet.Blue,
                baseline.Z,
                current.Color.Z,
                edited.Z);
            var mergedColor = new Float3BuildData(
                patch.ChangedComponents.HasFlag(
                    PrimaryLightColorComponentSet.Red)
                    ? edited.X
                    : current.Color.X,
                patch.ChangedComponents.HasFlag(
                    PrimaryLightColorComponentSet.Green)
                    ? edited.Y
                    : current.Color.Y,
                patch.ChangedComponents.HasFlag(
                    PrimaryLightColorComponentSet.Blue)
                    ? edited.Z
                    : current.Color.Z);
            draft.SetPrimaryLight(
                patch.SourceOrdinal,
                current with
                {
                    Color = mergedColor
                });
        }

        foreach (PrimaryLightExponentPatch patch in candidate.ExponentPatches)
        {
            ComPrimaryLightBuildData current =
                draft.PrimaryLights[patch.SourceOrdinal];
            byte baseline =
                candidate.Baseline.PrimaryLights[
                    patch.SourceOrdinal].Exponent;
            byte edited =
                candidate.BuildData.PrimaryLights[
                    patch.SourceOrdinal].Exponent;
            if (current.Exponent != baseline &&
                current.Exponent != edited)
            {
                throw new InvalidOperationException(
                    $"The captured Studio draft independently changed primary " +
                    $"light {patch.SourceOrdinal} Exponent from the imported " +
                    "value; the overlapping map patch cannot be merged safely.");
            }

            draft.SetPrimaryLight(
                patch.SourceOrdinal,
                current with
                {
                    Exponent = edited
                });
        }

        foreach (PrimaryLightSpotFalloffPatch patch in
                 candidate.SpotFalloffPatches)
        {
            ComPrimaryLightBuildData current =
                draft.PrimaryLights[patch.SourceOrdinal];
            ComPrimaryLightBuildData baseline =
                candidate.Baseline.PrimaryLights[patch.SourceOrdinal];
            float edited =
                candidate.BuildData.PrimaryLights[
                    patch.SourceOrdinal].CosHalfFovInner;
            if (current.Type != 2 ||
                current.Type != baseline.Type ||
                !Same(current.CosHalfFovOuter, baseline.CosHalfFovOuter) ||
                !IsValidSpotFalloff(
                    current.Type,
                    current.CosHalfFovOuter,
                    current.CosHalfFovInner) ||
                (!Same(
                     current.CosHalfFovInner,
                     baseline.CosHalfFovInner) &&
                 !Same(current.CosHalfFovInner, edited)))
            {
                throw new InvalidOperationException(
                    $"The captured Studio draft independently changed " +
                    $"primary light {patch.SourceOrdinal} type, outer cone, " +
                    "or inner cone; the overlapping spot-falloff patch cannot " +
                    "be merged safely.");
            }

            draft.SetPrimaryLight(
                patch.SourceOrdinal,
                current with
                {
                    CosHalfFovInner = edited
                });
        }
    }

    private static void RequireCompatibleComponent(
        PrimaryLightColorPatch patch,
        PrimaryLightColorComponentSet component,
        float baseline,
        float current,
        float edited)
    {
        if (!patch.ChangedComponents.HasFlag(component) ||
            SameBits(current, baseline) ||
            SameBits(current, edited))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The captured Studio draft independently changed primary light " +
            $"{patch.SourceOrdinal} {component} from the imported value; " +
            "the overlapping map patch cannot be merged safely.");
    }

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);

    private static Dictionary<SourceBindingId, CompiledSourceBinding>
        BuildBindingCatalog(
            IEnumerable<CompiledSourceBinding> sourceBindings,
            ICollection<string> diagnostics)
    {
        var result =
            new Dictionary<SourceBindingId, CompiledSourceBinding>();
        foreach (CompiledSourceBinding binding in sourceBindings)
        {
            if (binding is null)
            {
                diagnostics.Add(
                    "The imported compiled-binding catalog contains a null entry.");
                continue;
            }
            if (!result.TryAdd(binding.Id, binding))
            {
                diagnostics.Add(
                    $"The imported compiled-binding catalog contains duplicate " +
                    $"ID {binding.Id}.");
            }
        }
        return result;
    }

    private static void ValidatePropertyBinding(
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor descriptor,
        SourceBindingId bindingId,
        int ordinal,
        string serializedPropertyName,
        string displayPropertyName,
        IReadOnlyDictionary<SourceBindingId, CompiledSourceBinding>
            bindingCatalog,
        ICollection<string> diagnostics)
    {
        if (!bindingCatalog.TryGetValue(
                bindingId,
                out CompiledSourceBinding? binding))
        {
            diagnostics.Add(
                $"Primary-light source ordinal {ordinal} has no compiled " +
                $"{displayPropertyName} binding {bindingId}.");
            return;
        }

        string expectedPath =
            $"$.primaryLights[{ordinal}].{serializedPropertyName}";
        if (binding.AssetType != XAssetType.ComMap ||
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
            binding.Provenance != MapValueProvenance.ExactDecodedRuntime)
        {
            diagnostics.Add(
                $"Primary-light source ordinal {ordinal} does not have the " +
                $"exact owned ComMap {displayPropertyName} binding.");
        }

        SourceBindingId expectedBinding = DeterministicMapIdentity.Binding(
            bundle.MapIdentity,
            XAssetType.ComMap.ToString(),
            descriptor.AssetName,
            expectedPath,
            ordinal);
        if (binding.Id != expectedBinding)
        {
            diagnostics.Add(
                $"Primary-light source ordinal {ordinal} " +
                $"{displayPropertyName} binding is not its deterministic " +
                "compiled-field identity.");
        }
    }

    private static void ValidateObjectIdentity(
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor descriptor,
        EditorPrimaryLight editorLight,
        int ordinal,
        ICollection<string> diagnostics)
    {
        MapObjectId expectedObject = DeterministicMapIdentity.Object(
            bundle.MapIdentity,
            XAssetType.ComMap.ToString(),
            descriptor.AssetName,
            "primary-light",
            ordinal);
        if (editorLight.Id != expectedObject)
        {
            diagnostics.Add(
                $"Primary-light source ordinal {ordinal} has a non-deterministic " +
                "semantic identity.");
        }
    }

    private static void ValidateUnsupportedSemanticPropertiesUnchanged(
        EditorPrimaryLight editor,
        ComPrimaryLightBuildData source,
        ICollection<string> diagnostics)
    {
        int ordinal = editor.SourceOrdinal.Value;
        if (editor.LightType.Value != source.Type ||
            editor.CanUseShadowMap.Value != source.CanUseShadowMap ||
            editor.Unused.Value != source.Unused ||
            !Same(editor.Direction.Value, source.Direction) ||
            !Same(editor.Origin.Value, source.Origin) ||
            !Same(editor.Radius.Value, source.Radius) ||
            !Same(editor.CosHalfFovOuter.Value, source.CosHalfFovOuter) ||
            !Same(editor.CosHalfFovExpanded.Value, source.CosHalfFovExpanded) ||
            !Same(editor.RotationLimit.Value, source.RotationLimit) ||
            !Same(editor.TranslationLimit.Value, source.TranslationLimit) ||
            !string.Equals(
                editor.DefinitionName.Value,
                source.DefName,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                $"Primary-light source ordinal {ordinal} changes its spatial " +
                "envelope or another unsupported serialized field.");
        }
    }

    private static void ValidateLightPreservation(
        int ordinal,
        ComPrimaryLightBuildData source,
        ComPrimaryLightBuildData edited,
        PrimaryLightColorPatch? colorPatch,
        PrimaryLightExponentPatch? exponentPatch,
        PrimaryLightSpotFalloffPatch? spotFalloffPatch,
        ICollection<string> diagnostics)
    {
        if (source.Type != edited.Type ||
            source.CanUseShadowMap != edited.CanUseShadowMap ||
            source.Unused != edited.Unused ||
            !Same(source.Direction, edited.Direction) ||
            !Same(source.Origin, edited.Origin) ||
            !Same(source.Radius, edited.Radius) ||
            !Same(source.CosHalfFovOuter, edited.CosHalfFovOuter) ||
            !Same(source.CosHalfFovExpanded, edited.CosHalfFovExpanded) ||
            !Same(source.RotationLimit, edited.RotationLimit) ||
            !Same(source.TranslationLimit, edited.TranslationLimit) ||
            !string.Equals(
                source.DefName,
                edited.DefName,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                $"ComMap primary light {ordinal} changed outside Color, " +
                "Exponent, or type-2 spot falloff.");
        }

        PrimaryLightColorComponentSet actual = ChangedComponents(
            ToMapVector(source.Color),
            ToMapVector(edited.Color));
        if (!IsValidColor(ToMapVector(edited.Color)))
        {
            diagnostics.Add(
                $"ComMap primary light {ordinal} has a Color component that " +
                "is non-finite or negative.");
        }
        if (colorPatch is null)
        {
            if (actual != PrimaryLightColorComponentSet.None)
            {
                diagnostics.Add(
                    $"ComMap primary light {ordinal} changed Color without an " +
                    "authorized patch.");
            }
        }
        else if (colorPatch.SourceOrdinal != ordinal ||
                 !Same(colorPatch.BaselineColor, source.Color) ||
                 !Same(colorPatch.EditedColor, edited.Color) ||
                 colorPatch.ChangedComponents != actual)
        {
            diagnostics.Add(
                $"ComMap primary light {ordinal} Color does not match its " +
                "authorized component patch.");
        }

        bool exponentChanged = source.Exponent != edited.Exponent;
        if (exponentPatch is null)
        {
            if (exponentChanged)
            {
                diagnostics.Add(
                    $"ComMap primary light {ordinal} changed Exponent without " +
                    "an authorized patch.");
            }
        }
        else if (exponentPatch.SourceOrdinal != ordinal ||
                 exponentPatch.BaselineExponent != source.Exponent ||
                 exponentPatch.EditedExponent != edited.Exponent ||
                 !exponentChanged)
        {
            diagnostics.Add(
                $"ComMap primary light {ordinal} Exponent does not match its " +
                "authorized property patch.");
        }

        bool spotFalloffChanged = !Same(
            source.CosHalfFovInner,
            edited.CosHalfFovInner);
        if (spotFalloffPatch is null)
        {
            if (spotFalloffChanged)
            {
                diagnostics.Add(
                    $"ComMap primary light {ordinal} changed CosHalfFovInner " +
                    "without an authorized spot-falloff patch.");
            }
        }
        else if (spotFalloffPatch.SourceOrdinal != ordinal ||
                 !Same(
                     spotFalloffPatch.BaselineCosHalfFovInner,
                     source.CosHalfFovInner) ||
                 !Same(
                     spotFalloffPatch.EditedCosHalfFovInner,
                     edited.CosHalfFovInner) ||
                 !spotFalloffChanged ||
                 !IsValidSpotFalloff(
                     source.Type,
                     source.CosHalfFovOuter,
                     source.CosHalfFovInner) ||
                 !IsValidSpotFalloff(
                     edited.Type,
                     edited.CosHalfFovOuter,
                     edited.CosHalfFovInner))
        {
            diagnostics.Add(
                $"ComMap primary light {ordinal} CosHalfFovInner does not " +
                "match its authorized type-2 spot-falloff patch.");
        }
    }

    private static ComWorldPrimaryLightPropertyPatchCandidate InvalidCandidate(
        IEnumerable<string> diagnostics)
    {
        var empty = new ComWorldBuildData(null, 0, []);
        return new ComWorldPrimaryLightPropertyPatchCandidate(
            empty,
            empty,
            [],
            [],
            [],
            new MapPatchValidation(diagnostics));
    }

    private static PrimaryLightColorComponentSet ChangedComponents(
        MapVector3 source,
        MapVector3 edited)
    {
        PrimaryLightColorComponentSet result =
            PrimaryLightColorComponentSet.None;
        if (!Same(source.X, edited.X))
            result |= PrimaryLightColorComponentSet.Red;
        if (!Same(source.Y, edited.Y))
            result |= PrimaryLightColorComponentSet.Green;
        if (!Same(source.Z, edited.Z))
            result |= PrimaryLightColorComponentSet.Blue;
        return result;
    }

    private static MapVector3 ToMapVector(Float3BuildData value) =>
        new(value.X, value.Y, value.Z);

    private static Float3BuildData ToBuildVector(MapVector3 value) =>
        new(value.X, value.Y, value.Z);

    private static bool IsValidColor(MapVector3 value) =>
        float.IsFinite(value.X) && value.X >= 0 &&
        float.IsFinite(value.Y) && value.Y >= 0 &&
        float.IsFinite(value.Z) && value.Z >= 0;

    private static bool IsValidSpotFalloff(
        byte type,
        float outer,
        float inner) =>
        type == 2 &&
        float.IsFinite(outer) &&
        float.IsFinite(inner) &&
        outer > 0f &&
        outer < inner &&
        inner <= 1f;

    private static bool Same(MapVector3 left, Float3BuildData right) =>
        Same(left.X, right.X) &&
        Same(left.Y, right.Y) &&
        Same(left.Z, right.Z);

    private static bool Same(Float3BuildData left, Float3BuildData right) =>
        Same(left.X, right.X) &&
        Same(left.Y, right.Y) &&
        Same(left.Z, right.Z);

    private static bool Same(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);
}
