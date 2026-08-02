using System.Collections.ObjectModel;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.TargetAcceptance;

namespace IW4.Studio.MapEditor.Compilation.Lighting;

/// <summary>
/// Explicit linker input used to prove ComWorld primary-light definition-name
/// closure. It carries identities only; it does not authorize zone emission.
/// </summary>
public sealed class GfxComLightingDependencyClosure
{
    public static GfxComLightingDependencyClosure Empty { get; } =
        new([]);

    private readonly IReadOnlyList<string> _lightDefinitionNames;
    private readonly HashSet<string> _lightDefinitionNameSet;

    public GfxComLightingDependencyClosure(
        IEnumerable<string> lightDefinitionNames)
    {
        ArgumentNullException.ThrowIfNull(lightDefinitionNames);
        string[] names = lightDefinitionNames
            .Select(name =>
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException(
                        "Light-definition dependency identities cannot be empty.",
                        nameof(lightDefinitionNames));
                }

                return name.Trim();
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        _lightDefinitionNames = Array.AsReadOnly(names);
        _lightDefinitionNameSet =
            new HashSet<string>(names, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> LightDefinitionNames =>
        _lightDefinitionNames;

    internal bool ContainsLightDefinition(string name) =>
        _lightDefinitionNameSet.Contains(name);
}

public enum GfxComLightingGraphIssueKind
{
    InvalidMapIdentity = 0,
    MapIdentityMismatch = 1,
    PrimaryLightCardinalityMismatch = 2,
    PrimaryLightOrdinalPlanMismatch = 3,
    PrimaryLightSentinelMismatch = 4,
    UnsupportedSunOrdinalPolicy = 5,
    ShadowGeometryCardinalityMismatch = 6,
    ShadowGeometrySurfaceIndexMismatch = 7,
    StaticModelShadowMembershipMismatch = 8,
    LightRegionCardinalityMismatch = 9,
    SurfaceLightingIndexOutOfRange = 10,
    StaticModelLightingIndexOutOfRange = 11,
    LightGridCardinalityMismatch = 12,
    LightGridIndexOutOfRange = 13,
    CellReflectionProbeIndexOutOfRange = 14,
    LightingReferenceCardinalityMismatch = 15,
    UnresolvedLightDefinitionDependency = 16,
    RuntimeStateAuthorshipViolation = 17
}

public sealed record GfxComLightingGraphIssue(
    GfxComLightingGraphIssueKind Kind,
    string Path,
    string Detail);

public sealed class GfxComLightingGraphAssessment
{
    private readonly IReadOnlyList<GfxComLightingGraphIssue> _issues;

    internal GfxComLightingGraphAssessment(
        IEnumerable<GfxComLightingGraphIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        _issues =
            new ReadOnlyCollection<GfxComLightingGraphIssue>(
                issues.ToArray());
    }

    public bool IsValid => Issues.Count == 0;

    public IReadOnlyList<GfxComLightingGraphIssue> Issues => _issues;
}

internal enum GfxComLightingGraphValidationContract
{
    SerializerCompatible = 0,
    TargetRuntime = 1
}

/// <summary>
/// Validates the shared, greenfield M5-A lighting index graph. Passing this
/// assessment proves local graph consistency only; target acceptance,
/// light baking, complete dependency closure, and persistence remain separate
/// gates.
/// </summary>
public static class GfxComLightingGraphValidator
{
    /// <summary>
    /// Retains the original serializer-compatible optional-index contract.
    /// Target-runtime compilers must use the stricter internal entry point.
    /// </summary>
    public static GfxComLightingGraphAssessment Assess(
        IGfxWorldBuildData gfxWorld,
        IComWorldBuildData comWorld,
        PrimaryLightOrdinalPlan ordinalPlan,
        GfxComLightingDependencyClosure dependencyClosure) =>
        Assess(
            gfxWorld,
            comWorld,
            ordinalPlan,
            dependencyClosure,
            GfxComLightingGraphValidationContract
                .SerializerCompatible);

    internal static GfxComLightingGraphAssessment
        AssessTargetRuntime(
            IGfxWorldBuildData gfxWorld,
            IComWorldBuildData comWorld,
            PrimaryLightOrdinalPlan ordinalPlan,
            GfxComLightingDependencyClosure dependencyClosure) =>
        Assess(
            gfxWorld,
            comWorld,
            ordinalPlan,
            dependencyClosure,
            GfxComLightingGraphValidationContract
                .TargetRuntime);

    private static GfxComLightingGraphAssessment Assess(
        IGfxWorldBuildData gfxWorld,
        IComWorldBuildData comWorld,
        PrimaryLightOrdinalPlan ordinalPlan,
        GfxComLightingDependencyClosure dependencyClosure,
        GfxComLightingGraphValidationContract validationContract)
    {
        ArgumentNullException.ThrowIfNull(gfxWorld);
        ArgumentNullException.ThrowIfNull(comWorld);
        ArgumentNullException.ThrowIfNull(ordinalPlan);
        ArgumentNullException.ThrowIfNull(dependencyClosure);
        if (!Enum.IsDefined(validationContract))
        {
            throw new ArgumentOutOfRangeException(
                nameof(validationContract));
        }

        GfxWorldAsset definition = gfxWorld.Definition;
        GfxWorldReferenceBuildData references = gfxWorld.References;
        var issues = new List<GfxComLightingGraphIssue>();

        AssessMapIdentity(definition.Name, comWorld.Name, issues);
        AssessPrimaryLights(
            definition,
            comWorld,
            ordinalPlan,
            dependencyClosure,
            issues);
        AssessReferenceDomains(
            definition,
            references,
            validationContract,
            issues);
        AssessSurfaceDomains(
            definition,
            validationContract,
            issues);
        AssessStaticModelDomains(
            definition,
            validationContract,
            issues);
        AssessShadowGeometry(definition, issues);
        AssessLightGrid(
            definition,
            validationContract,
            issues);
        AssessCellReflectionProbes(
            definition,
            validationContract,
            issues);
        AssessRuntimeOwnership(definition, comWorld, issues);

        return new GfxComLightingGraphAssessment(issues);
    }

    private static void AssessMapIdentity(
        string? gfxName,
        string? comName,
        ICollection<GfxComLightingGraphIssue> issues)
    {
        string? normalizedGfx =
            TryNormalizeMapIdentity(
                gfxName,
                "$.gfxWorld.name",
                issues);
        string? normalizedCom =
            TryNormalizeMapIdentity(
                comName,
                "$.comWorld.name",
                issues);
        if (normalizedGfx is not null &&
            normalizedCom is not null &&
            !string.Equals(
                normalizedGfx,
                normalizedCom,
                StringComparison.Ordinal))
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind.MapIdentityMismatch,
                "$.mapIdentity",
                $"GfxWorld '{normalizedGfx}' and ComWorld " +
                $"'{normalizedCom}' do not name the same multiplayer map.");
        }
    }

    private static void AssessPrimaryLights(
        GfxWorldAsset definition,
        IComWorldBuildData comWorld,
        PrimaryLightOrdinalPlan ordinalPlan,
        GfxComLightingDependencyClosure dependencyClosure,
        ICollection<GfxComLightingGraphIssue> issues)
    {
        int expectedCount = ordinalPlan.PrimaryLightCount;
        if (expectedCount is < 1 or >
            PrimaryLightOrdinalPlan.MaximumPrimaryLightCount ||
            definition.PrimaryLightCount != expectedCount ||
            comWorld.PrimaryLights.Count != expectedCount)
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind
                    .PrimaryLightCardinalityMismatch,
                "$.primaryLights",
                $"The ordinal plan requires {expectedCount} rows, GfxWorld " +
                $"declares {definition.PrimaryLightCount}, and ComWorld " +
                $"materializes {comWorld.PrimaryLights.Count}.");
        }

        if (comWorld.PrimaryLights.Count == 0 ||
            !IsZeroSentinel(comWorld.PrimaryLights[0]))
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind
                    .PrimaryLightSentinelMismatch,
                "$.comWorld.primaryLights[0]",
                "Primary-light row zero must be the complete compiler-owned " +
                "zero sentinel and cannot carry a LightDef name.");
        }

        if (!ordinalPlan.ComPrimaryLights.SequenceEqual(
                comWorld.PrimaryLights))
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind
                    .PrimaryLightOrdinalPlanMismatch,
                "$.comWorld.primaryLights",
                "ComWorld rows do not exactly match the sentinel-plus-stable-" +
                "source-ID ordinal plan.");
        }

        if (definition.SunPrimaryLightIndex !=
                PrimaryLightOrdinalPlan.NoSunPrimaryLightIndex ||
            definition.LightGrid.SunPrimaryLightIndex !=
                PrimaryLightOrdinalPlan.NoSunPrimaryLightIndex)
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind
                    .UnsupportedSunOrdinalPolicy,
                "$.gfxWorld.sunPrimaryLightIndex",
                "M5-A has no directional-sun allocation policy; both GfxWorld " +
                "sun indices must remain the row-zero no-sun value.");
        }

        if (definition.ShadowGeom.Count != expectedCount)
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind
                    .ShadowGeometryCardinalityMismatch,
                "$.gfxWorld.shadowGeom",
                $"Shadow geometry materializes {definition.ShadowGeom.Count} " +
                $"rows for {expectedCount} planned primary lights.");
        }
        if (definition.LightRegions.Count != expectedCount)
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind
                    .LightRegionCardinalityMismatch,
                "$.gfxWorld.lightRegions",
                $"Light regions materialize {definition.LightRegions.Count} " +
                $"rows for {expectedCount} planned primary lights.");
        }

        for (int ordinal = 1;
             ordinal < comWorld.PrimaryLights.Count;
             ordinal++)
        {
            ComPrimaryLightBuildData light =
                comWorld.PrimaryLights[ordinal];
            if (light.Type is not (
                (byte)AuthoredPrimaryLightKind.Spot or
                (byte)AuthoredPrimaryLightKind.Omni))
            {
                Add(
                    issues,
                    GfxComLightingGraphIssueKind
                        .PrimaryLightOrdinalPlanMismatch,
                    $"$.comWorld.primaryLights[{ordinal}].type",
                    $"M5-A authored row {ordinal} has unsupported non-sun " +
                    $"primary-light type {light.Type}.");
            }

            if (light.DefName is null)
                continue;
            if (string.IsNullOrWhiteSpace(light.DefName) ||
                !dependencyClosure.ContainsLightDefinition(
                    light.DefName))
            {
                Add(
                    issues,
                    GfxComLightingGraphIssueKind
                        .UnresolvedLightDefinitionDependency,
                    $"$.comWorld.primaryLights[{ordinal}].defName",
                    $"Primary-light definition '{light.DefName}' is not in " +
                    "the explicit LightDef dependency closure.");
            }
        }
    }

    private static void AssessReferenceDomains(
        GfxWorldAsset definition,
        GfxWorldReferenceBuildData references,
        GfxComLightingGraphValidationContract validationContract,
        ICollection<GfxComLightingGraphIssue> issues)
    {
        GfxWorldDraw draw = definition.WorldDraw;
        bool reflectionProbeCardinalityValid =
            IsReflectionProbeCountValid(
                draw.ReflectionProbeCount,
                validationContract) &&
            draw.ReflectionProbeImages.Count ==
                draw.ReflectionProbeCount &&
            draw.ReflectionProbeOrigins.Count ==
                draw.ReflectionProbeCount &&
            references.ReflectionProbeImages.Count ==
                draw.ReflectionProbeCount;
        if (!reflectionProbeCardinalityValid)
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind
                    .LightingReferenceCardinalityMismatch,
                "$.gfxWorld.worldDraw.reflectionProbes",
                $"Reflection-probe count {draw.ReflectionProbeCount} must fit " +
                "the byte index domain and parallel image, origin, and " +
                "symbolic-reference rows.");
        }

        bool lightmapCardinalityValid =
            IsLightmapCountValid(
                draw.LightmapCount,
                validationContract) &&
            draw.Lightmaps.Count == draw.LightmapCount &&
            references.Lightmaps.Count == draw.LightmapCount;
        if (!lightmapCardinalityValid)
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind
                    .LightingReferenceCardinalityMismatch,
                "$.gfxWorld.worldDraw.lightmaps",
                $"Lightmap count {draw.LightmapCount} must fit the byte index " +
                "domain and parallel payload and symbolic-reference rows.");
        }
    }

    private static void AssessSurfaceDomains(
        GfxWorldAsset definition,
        GfxComLightingGraphValidationContract validationContract,
        ICollection<GfxComLightingGraphIssue> issues)
    {
        int primaryLightCount = definition.PrimaryLightCount;
        long reflectionProbeCount =
            definition.WorldDraw.ReflectionProbeCount;
        int lightmapCount = definition.WorldDraw.LightmapCount;
        for (int surfaceIndex = 0;
             surfaceIndex < definition.Dpvs.Surfaces.Count;
             surfaceIndex++)
        {
            GfxSurface surface =
                definition.Dpvs.Surfaces[surfaceIndex];
            if (!IsPrimaryLightIndexInRange(
                    surface.PrimaryLightIndex,
                    primaryLightCount))
            {
                Add(
                    issues,
                    GfxComLightingGraphIssueKind
                        .SurfaceLightingIndexOutOfRange,
                    $"$.gfxWorld.dpvs.surfaces[{surfaceIndex}].primaryLightIndex",
                    $"Primary-light index {surface.PrimaryLightIndex} is " +
                    $"outside {primaryLightCount} rows.");
            }
            if (!IsReflectionProbeIndexInRange(
                    surface.ReflectionProbeIndex,
                    reflectionProbeCount,
                    validationContract))
            {
                Add(
                    issues,
                    GfxComLightingGraphIssueKind
                        .SurfaceLightingIndexOutOfRange,
                    $"$.gfxWorld.dpvs.surfaces[{surfaceIndex}].reflectionProbeIndex",
                    $"Reflection-probe index {surface.ReflectionProbeIndex} " +
                    $"is outside {reflectionProbeCount} rows.");
            }
            if (!IsLightmapIndexInRange(
                    surface.LightmapIndex,
                    lightmapCount,
                    validationContract))
            {
                Add(
                    issues,
                    GfxComLightingGraphIssueKind
                        .SurfaceLightingIndexOutOfRange,
                    $"$.gfxWorld.dpvs.surfaces[{surfaceIndex}].lightmapIndex",
                    $"Lightmap index {surface.LightmapIndex} is outside " +
                    $"{lightmapCount} rows.");
            }
        }
    }

    private static void AssessStaticModelDomains(
        GfxWorldAsset definition,
        GfxComLightingGraphValidationContract validationContract,
        ICollection<GfxComLightingGraphIssue> issues)
    {
        long reflectionProbeCount =
            definition.WorldDraw.ReflectionProbeCount;
        for (int staticModelIndex = 0;
             staticModelIndex <
                 definition.Dpvs.SModelDrawInsts.Count;
             staticModelIndex++)
        {
            GfxStaticModelDrawInst draw =
                definition.Dpvs.SModelDrawInsts[
                    staticModelIndex];
            if (!IsPrimaryLightIndexInRange(
                    draw.PrimaryLightIndex,
                    definition.PrimaryLightCount))
            {
                Add(
                    issues,
                    GfxComLightingGraphIssueKind
                        .StaticModelLightingIndexOutOfRange,
                    $"$.gfxWorld.dpvs.smodelDrawInsts[{staticModelIndex}].primaryLightIndex",
                    $"Primary-light index {draw.PrimaryLightIndex} is outside " +
                    $"{definition.PrimaryLightCount} rows.");
            }
            if (!IsReflectionProbeIndexInRange(
                    draw.ReflectionProbeIndex,
                    reflectionProbeCount,
                    validationContract))
            {
                Add(
                    issues,
                    GfxComLightingGraphIssueKind
                        .StaticModelLightingIndexOutOfRange,
                    $"$.gfxWorld.dpvs.smodelDrawInsts[{staticModelIndex}].reflectionProbeIndex",
                    $"Reflection-probe index {draw.ReflectionProbeIndex} is " +
                    $"outside {reflectionProbeCount} rows.");
            }
        }

        GfxStaticModelShadowMembershipAssessment shadowAssessment =
            GfxStaticModelShadowMembershipAssessor.Assess(definition);
        foreach (GfxStaticModelShadowMembershipIssue issue in
                 shadowAssessment.Issues)
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind
                    .StaticModelShadowMembershipMismatch,
                issue.PrimaryLightIndex is { } lightIndex
                    ? $"$.gfxWorld.shadowGeom[{lightIndex}].smodelIndex"
                    : "$.gfxWorld.shadowGeom.smodelIndex",
                issue.Detail);
        }
    }

    private static void AssessShadowGeometry(
        GfxWorldAsset definition,
        ICollection<GfxComLightingGraphIssue> issues)
    {
        int surfaceCount = definition.Dpvs.Surfaces.Count;
        for (int primaryLightIndex = 0;
             primaryLightIndex < definition.ShadowGeom.Count;
             primaryLightIndex++)
        {
            GfxShadowGeometry shadow =
                definition.ShadowGeom[primaryLightIndex];
            if (shadow.SurfaceCount !=
                shadow.SortedSurfIndex.Count)
            {
                Add(
                    issues,
                    GfxComLightingGraphIssueKind
                        .ShadowGeometrySurfaceIndexMismatch,
                    $"$.gfxWorld.shadowGeom[{primaryLightIndex}].sortedSurfIndex",
                    $"The row declares {shadow.SurfaceCount} surfaces but " +
                    $"materializes {shadow.SortedSurfIndex.Count} indices.");
            }

            ushort? previous = null;
            for (int elementIndex = 0;
                 elementIndex < shadow.SortedSurfIndex.Count;
                 elementIndex++)
            {
                ushort surfaceIndex =
                    shadow.SortedSurfIndex[elementIndex];
                if (surfaceIndex >= surfaceCount ||
                    previous is not null &&
                    surfaceIndex <= previous.Value)
                {
                    Add(
                        issues,
                        GfxComLightingGraphIssueKind
                            .ShadowGeometrySurfaceIndexMismatch,
                        $"$.gfxWorld.shadowGeom[{primaryLightIndex}].sortedSurfIndex[{elementIndex}]",
                        $"Surface index {surfaceIndex} must be in range " +
                        $"[0, {surfaceCount}) and rows must be strictly ascending.");
                }
                previous = surfaceIndex;
            }
        }
    }

    private static void AssessLightGrid(
        GfxWorldAsset definition,
        GfxComLightingGraphValidationContract validationContract,
        ICollection<GfxComLightingGraphIssue> issues)
    {
        GfxLightGrid lightGrid = definition.LightGrid;
        if (lightGrid.EntryCount != lightGrid.Entries.Count ||
            lightGrid.ColorCount != lightGrid.Colors.Count ||
            lightGrid.RawRowDataSize != lightGrid.RawRowData.Count)
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind
                    .LightGridCardinalityMismatch,
                "$.gfxWorld.lightGrid",
                "Light-grid entry, color, and raw-row declared counts must " +
                "match their materialized payloads.");
        }
        if (validationContract ==
                GfxComLightingGraphValidationContract
                    .TargetRuntime &&
            (lightGrid.ColorCount < 2 ||
             lightGrid.Colors.Count < 2))
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind
                    .LightGridCardinalityMismatch,
                "$.gfxWorld.lightGrid.colors",
                "A target-runtime GfxWorld requires at least two light-grid " +
                "color rows because native world initialization reads row " +
                "one unconditionally.");
        }

        if (validationContract ==
            GfxComLightingGraphValidationContract
                .TargetRuntime)
        {
            for (int rowIndex = 0;
                 rowIndex < lightGrid.RowDataStart.Count;
                 rowIndex++)
            {
                ushort rowDataStart =
                    lightGrid.RowDataStart[rowIndex];
                if (rowDataStart ==
                    GfxWorldNoBakeRuntimeDefaults
                        .EmptyLightGridRowDataStart)
                {
                    continue;
                }

                long rawByteOffset =
                    (long)rowDataStart * sizeof(uint);
                if (rawByteOffset >= lightGrid.RawRowDataSize)
                {
                    Add(
                        issues,
                        GfxComLightingGraphIssueKind
                            .LightGridIndexOutOfRange,
                        $"$.gfxWorld.lightGrid.rowDataStart[{rowIndex}]",
                        $"Row-data offset {rowDataStart} addresses byte " +
                        $"{rawByteOffset}, outside the " +
                        $"{lightGrid.RawRowDataSize}-byte raw payload. Empty " +
                        "rows must use 0xFFFF.");
                }
            }
        }

        for (int entryIndex = 0;
             entryIndex < lightGrid.Entries.Count;
             entryIndex++)
        {
            GfxLightGridEntry entry =
                lightGrid.Entries[entryIndex];
            if (!IsPrimaryLightIndexInRange(
                    entry.PrimaryLightIndex,
                    definition.PrimaryLightCount))
            {
                Add(
                    issues,
                    GfxComLightingGraphIssueKind
                        .LightGridIndexOutOfRange,
                    $"$.gfxWorld.lightGrid.entries[{entryIndex}].primaryLightIndex",
                    $"Primary-light index {entry.PrimaryLightIndex} is outside " +
                    $"{definition.PrimaryLightCount} rows.");
            }
            if (lightGrid.ColorCount == 0 ||
                entry.ColorsIndex >= lightGrid.ColorCount)
            {
                Add(
                    issues,
                    GfxComLightingGraphIssueKind
                        .LightGridIndexOutOfRange,
                    $"$.gfxWorld.lightGrid.entries[{entryIndex}].colorsIndex",
                    $"Color index {entry.ColorsIndex} is outside " +
                    $"{lightGrid.ColorCount} rows.");
            }
        }
    }

    private static void AssessCellReflectionProbes(
        GfxWorldAsset definition,
        GfxComLightingGraphValidationContract validationContract,
        ICollection<GfxComLightingGraphIssue> issues)
    {
        long reflectionProbeCount =
            definition.WorldDraw.ReflectionProbeCount;
        for (int cellIndex = 0;
             cellIndex < definition.Cells.Count;
             cellIndex++)
        {
            GfxCell cell = definition.Cells[cellIndex];
            if ((validationContract ==
                     GfxComLightingGraphValidationContract
                         .TargetRuntime &&
                 cell.ReflectionProbeCount == 0) ||
                cell.ReflectionProbeCount !=
                    cell.ReflectionProbes.Count)
            {
                Add(
                    issues,
                    GfxComLightingGraphIssueKind
                        .LightingReferenceCardinalityMismatch,
                    $"$.gfxWorld.cells[{cellIndex}].reflectionProbes",
                    $"The cell declares {cell.ReflectionProbeCount} probes " +
                    $"but materializes {cell.ReflectionProbes.Count} " +
                    "indices." +
                    (validationContract ==
                        GfxComLightingGraphValidationContract
                            .TargetRuntime
                            ? " Target-runtime cells require at least one."
                            : string.Empty));
            }
            for (int probeIndex = 0;
                 probeIndex < cell.ReflectionProbes.Count;
                 probeIndex++)
            {
                byte ordinal = cell.ReflectionProbes[probeIndex];
                if (!IsReflectionProbeIndexInRange(
                        ordinal,
                        reflectionProbeCount,
                        validationContract))
                {
                    Add(
                        issues,
                        GfxComLightingGraphIssueKind
                            .CellReflectionProbeIndexOutOfRange,
                        $"$.gfxWorld.cells[{cellIndex}].reflectionProbes[{probeIndex}]",
                        $"Reflection-probe index {ordinal} is outside " +
                        $"{reflectionProbeCount} rows.");
                }
            }
        }
    }

    private static void AssessRuntimeOwnership(
        GfxWorldAsset definition,
        IComWorldBuildData comWorld,
        ICollection<GfxComLightingGraphIssue> issues)
    {
        bool runtimeStateIsEmpty =
            comWorld.IsInUse == 0 &&
            definition.PrimaryLightEntityShadowVis.Count == 0 &&
            definition.PrimaryLightDynEntShadowVis0.Count == 0 &&
            definition.PrimaryLightDynEntShadowVis1.Count == 0 &&
            definition.PrimaryLightForModelDynEnt.Count == 0;
        if (!runtimeStateIsEmpty)
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind
                    .RuntimeStateAuthorshipViolation,
                "$.lighting.runtime",
                "The compiler may declare lighting allocation inputs but " +
                "cannot author ComWorld in-use state or runtime-populated " +
                "primary-light visibility arrays.");
        }
    }

    private static string? TryNormalizeMapIdentity(
        string? value,
        string path,
        ICollection<GfxComLightingGraphIssue> issues)
    {
        try
        {
            return MapCompilerContentIdentityInput
                .NormalizeMultiplayerMapAssetName(value!);
        }
        catch (ArgumentException exception)
        {
            Add(
                issues,
                GfxComLightingGraphIssueKind.InvalidMapIdentity,
                path,
                exception.Message);
            return null;
        }
    }

    private static bool IsPrimaryLightIndexInRange(
        byte index,
        int count) =>
        count is > 0 and <=
            PrimaryLightOrdinalPlan.MaximumPrimaryLightCount &&
        index < count;

    private static bool IsReflectionProbeCountValid(
        uint count,
        GfxComLightingGraphValidationContract validationContract) =>
        validationContract ==
            GfxComLightingGraphValidationContract.TargetRuntime
            ? count is > 0 and < byte.MaxValue
            : count <=
                PrimaryLightOrdinalPlan.MaximumPrimaryLightCount;

    private static bool IsLightmapCountValid(
        int count,
        GfxComLightingGraphValidationContract validationContract) =>
        validationContract ==
            GfxComLightingGraphValidationContract.TargetRuntime
            ? count is >= 0 and <=
                GfxWorldNoBakeRuntimeDefaults
                    .NoLightmapSurfaceIndex
            : count is >= 0 and <=
                PrimaryLightOrdinalPlan.MaximumPrimaryLightCount;

    private static bool IsReflectionProbeIndexInRange(
        byte index,
        long count,
        GfxComLightingGraphValidationContract validationContract) =>
        validationContract ==
            GfxComLightingGraphValidationContract.TargetRuntime
            ? count is > 0 and < byte.MaxValue &&
              index < count
            : IsSerializerCompatibleOptionalByteIndexInRange(
                index,
                count);

    private static bool IsLightmapIndexInRange(
        byte index,
        int count,
        GfxComLightingGraphValidationContract validationContract) =>
        validationContract ==
            GfxComLightingGraphValidationContract.TargetRuntime
            ? index ==
                  GfxWorldNoBakeRuntimeDefaults
                      .NoLightmapSurfaceIndex ||
              count is > 0 and <=
                  GfxWorldNoBakeRuntimeDefaults
                      .NoLightmapSurfaceIndex &&
              index < count
            : IsSerializerCompatibleOptionalByteIndexInRange(
                index,
                count);

    private static bool
        IsSerializerCompatibleOptionalByteIndexInRange(
            byte index,
            long count) =>
        count == 0
            ? index == 0
            : count is > 0 and <=
                PrimaryLightOrdinalPlan.MaximumPrimaryLightCount &&
              index < count;

    private static bool IsZeroSentinel(
        ComPrimaryLightBuildData light) =>
        light.Type == 0 &&
        light.CanUseShadowMap == 0 &&
        light.Exponent == 0 &&
        light.Unused == 0 &&
        light.Color == new Float3BuildData(0, 0, 0) &&
        light.Direction == new Float3BuildData(0, 0, 0) &&
        light.Origin == new Float3BuildData(0, 0, 0) &&
        light.Radius == 0 &&
        light.CosHalfFovOuter == 0 &&
        light.CosHalfFovInner == 0 &&
        light.CosHalfFovExpanded == 0 &&
        light.RotationLimit == 0 &&
        light.TranslationLimit == 0 &&
        light.DefName is null;

    private static void Add(
        ICollection<GfxComLightingGraphIssue> issues,
        GfxComLightingGraphIssueKind kind,
        string path,
        string detail) =>
        issues.Add(new(kind, path, detail));
}
