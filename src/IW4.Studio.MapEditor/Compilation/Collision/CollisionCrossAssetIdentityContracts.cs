using IW4.Studio.MapEditor.Compilation;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Selects the evidence boundary applied to a cross-asset map candidate.
/// Runtime safety contains only invariants required by observed IW4
/// consumers. Authored compiler validation additionally preserves the
/// contracts exhibited by every official IW4 MP map in the M0 corpus.
/// </summary>
public enum CollisionCrossAssetValidationProfile
{
    RuntimeConsumerSafety = 0,
    AuthoredCompilerContract = 1
}

public enum CollisionCrossAssetRequirement
{
    RuntimeConsumerSafety = 0,
    AuthoredCompilerContract = 1
}

public enum CollisionCrossAssetContractArea
{
    InlineModelIdentity = 0,
    GlassIdentity = 1,
    PrimaryChecksum = 2
}

public enum CollisionCrossAssetContractIssueKind
{
    InlineModelOrdinalOutsideColMap = 0,
    InlineModelOrdinalOutsideGfxMap = 1,
    InlineModelTableCountMismatch = 2,
    InlineModelWorldOrdinalUsedByMapEnt = 3,
    InlineModelOrdinalPrefixNotDense = 4,
    GlassHandleOutsideGameMapMp = 5,
    GlassHandleOutsideFxMap = 6,
    GlassNameMembershipOutsideGameMapMp = 7,
    GlassNameMembershipOutsideFxMap = 8,
    GlassPieceCountMismatch = 9,
    GlassHandleCoverageNotDense = 10,
    PrimaryChecksumMismatch = 11,
    PrimaryChecksumAssignmentRequired = 12,
    PrimaryChecksumAssignmentMismatch = 13,
    InlineModelMapEntOrdinalClaimedMultipleTimes = 14,
    InlineModelAllocationCountMismatch = 15,
    DynamicBrushModelOrdinalOutsideColMap = 16,
    DynamicBrushModelOrdinalOutsideGfxMap = 17,
    DynamicBrushDefinitionOrdinalNotDense = 18,
    DynamicBrushModelSuffixOrdinalMismatch = 19,
    DynamicBrushPhysicsModelUnsupported = 20,
    InlineModelAddressableCountExceeded = 21,
    PrimaryChecksumContentIdentityMismatch = 22,
    PrimaryChecksumImportedBaselineMismatch = 23
}

/// <summary>
/// Current implementation authority for the primary ColMap/GfxMap checksum.
/// Imported output preserves the production value. Greenfield output can use
/// the versioned Studio canonical policy because IW4 consumers require shared
/// equality, not a runtime recomputation.
/// </summary>
public enum CollisionPrimaryChecksumRebuildStatus
{
    ImportedProductionPreservation = 0,
    StudioCanonicalV1Available = 1
}

/// <summary>
/// Physical MapEnt owner and the ordinal parsed from its
/// <c>"model" "*n"</c> value. The ordinal selects the same zero-based row in
/// both compiled model tables.
/// </summary>
public readonly record struct MapEntInlineModelBinding
{
    public MapEntInlineModelBinding(
        int physicalEntityOrdinal,
        ushort modelOrdinal)
    {
        if (physicalEntityOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicalEntityOrdinal));
        }

        PhysicalEntityOrdinal = physicalEntityOrdinal;
        ModelOrdinal = modelOrdinal;
    }

    public int PhysicalEntityOrdinal { get; }
    public ushort ModelOrdinal { get; }
    public int ColMapCModelOrdinal => ModelOrdinal;
    public int GfxMapModelOrdinal => ModelOrdinal;
}

/// <summary>
/// Shared inline-model identity carried by one
/// <c>ColMap.DynEntDefList[1]</c> row. The dynamic-definition ordinal owns
/// its position in the authored suffix; <see cref="BrushModelOrdinal"/>
/// selects the same ColMap CModel and GfxMap model row.
/// </summary>
public readonly record struct DynamicBrushModelBinding
{
    public DynamicBrushModelBinding(
        ushort dynamicDefinitionOrdinal,
        ushort brushModelOrdinal,
        ushort physicsBrushModelOrdinal)
    {
        DynamicDefinitionOrdinal = dynamicDefinitionOrdinal;
        BrushModelOrdinal = brushModelOrdinal;
        PhysicsBrushModelOrdinal = physicsBrushModelOrdinal;
    }

    public const int DynamicEntitySlot = 1;

    public ushort DynamicDefinitionOrdinal { get; }
    public ushort BrushModelOrdinal { get; }
    public ushort PhysicsBrushModelOrdinal { get; }
    public int ColMapCModelOrdinal => BrushModelOrdinal;
    public int GfxMapModelOrdinal => BrushModelOrdinal;
}

/// <summary>
/// Serialized <c>CBrush.GlassPieceIndex</c>. Zero is the no-glass sentinel;
/// a nonzero handle is one greater than the shared GameMapMp/FxMap ordinal.
/// </summary>
public readonly record struct ColMapGlassPieceHandle(ushort Value)
{
    public bool IsNone => Value == 0;

    public int? SharedPieceOrdinal =>
        IsNone
            ? null
            : Value - 1;
}

/// <summary>
/// Zero-based identity shared by GameMapMp glass pieces, FxMap initial
/// pieces, and GameMapMp glass-name memberships.
/// </summary>
public readonly record struct GlassPieceOrdinal(ushort Value);

public sealed class CollisionInlineModelIdentityInput
{
    private readonly IReadOnlyList<MapEntInlineModelBinding>
        _mapEntInlineModels;
    private readonly IReadOnlyList<DynamicBrushModelBinding>
        _dynamicBrushModels;

    public CollisionInlineModelIdentityInput(
        int colMapCModelCount,
        int gfxMapModelCount,
        IEnumerable<MapEntInlineModelBinding> mapEntInlineModels,
        IEnumerable<DynamicBrushModelBinding>? dynamicBrushModels = null)
    {
        if (colMapCModelCount < 0)
            throw new ArgumentOutOfRangeException(nameof(colMapCModelCount));
        if (gfxMapModelCount < 0)
            throw new ArgumentOutOfRangeException(nameof(gfxMapModelCount));
        ArgumentNullException.ThrowIfNull(mapEntInlineModels);

        ColMapCModelCount = colMapCModelCount;
        GfxMapModelCount = gfxMapModelCount;
        _mapEntInlineModels = Array.AsReadOnly(
            mapEntInlineModels.ToArray());
        _dynamicBrushModels = Array.AsReadOnly(
            dynamicBrushModels?.ToArray() ?? []);
    }

    public int ColMapCModelCount { get; }
    public int GfxMapModelCount { get; }

    public IReadOnlyList<MapEntInlineModelBinding> MapEntInlineModels =>
        _mapEntInlineModels;

    public IReadOnlyList<DynamicBrushModelBinding> DynamicBrushModels =>
        _dynamicBrushModels;
}

public sealed class CollisionGlassIdentityInput
{
    private readonly IReadOnlyList<ColMapGlassPieceHandle>
        _colMapBrushHandles;
    private readonly IReadOnlyList<GlassPieceOrdinal>
        _glassNameMemberships;

    public CollisionGlassIdentityInput(
        int gameMapMpPieceCount,
        int fxMapInitialPieceCount,
        IEnumerable<ColMapGlassPieceHandle> colMapBrushHandles,
        IEnumerable<GlassPieceOrdinal> glassNameMemberships)
    {
        if (gameMapMpPieceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(gameMapMpPieceCount));
        if (fxMapInitialPieceCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fxMapInitialPieceCount));
        }
        ArgumentNullException.ThrowIfNull(colMapBrushHandles);
        ArgumentNullException.ThrowIfNull(glassNameMemberships);

        GameMapMpPieceCount = gameMapMpPieceCount;
        FxMapInitialPieceCount = fxMapInitialPieceCount;
        _colMapBrushHandles = Array.AsReadOnly(
            colMapBrushHandles.ToArray());
        _glassNameMemberships = Array.AsReadOnly(
            glassNameMemberships.ToArray());
    }

    public int GameMapMpPieceCount { get; }
    public int FxMapInitialPieceCount { get; }

    public IReadOnlyList<ColMapGlassPieceHandle> ColMapBrushHandles =>
        _colMapBrushHandles;

    public IReadOnlyList<GlassPieceOrdinal> GlassNameMemberships =>
        _glassNameMemberships;
}

public sealed class CollisionPrimaryChecksumInput
{
    public CollisionPrimaryChecksumInput(
        MapPrimaryChecksum colMapChecksum,
        MapPrimaryChecksum gfxMapChecksum,
        MapPrimaryChecksumContentDisposition contentDisposition,
        MapPrimaryChecksumAssignment? assignment = null,
        MapCompilerContentIdentity? candidateContentIdentity = null,
        ImportedMapPrimaryChecksumCandidateEvidence?
            importedCandidateEvidence = null)
    {
        if (!Enum.IsDefined(contentDisposition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentDisposition));
        }

        ColMapChecksum = colMapChecksum;
        GfxMapChecksum = gfxMapChecksum;
        ContentDisposition = contentDisposition;
        Assignment = assignment;
        CandidateContentIdentity = candidateContentIdentity;
        ImportedCandidateEvidence = importedCandidateEvidence;
    }

    public MapPrimaryChecksum ColMapChecksum { get; }
    public MapPrimaryChecksum GfxMapChecksum { get; }
    public MapPrimaryChecksumContentDisposition ContentDisposition { get; }

    /// <summary>
    /// Versioned assignment authority. Imported-production values are legal
    /// only for preserved imported content; any Studio-authored whole-map
    /// content requires the StudioCanonicalV1 policy.
    /// </summary>
    public MapPrimaryChecksumAssignment? Assignment { get; }

    /// <summary>
    /// Independently calculated identity of the candidate being validated.
    /// It must match the identity embedded in a StudioCanonicalV1 assignment;
    /// otherwise a checksum from another map or stale build could be reused.
    /// </summary>
    public MapCompilerContentIdentity? CandidateContentIdentity { get; }

    /// <summary>
    /// Map identity and asset-graph digest independently calculated from the
    /// candidate under validation. Imported checksum preservation is legal
    /// only when these values match the immutable assignment baseline.
    /// </summary>
    public ImportedMapPrimaryChecksumCandidateEvidence?
        ImportedCandidateEvidence { get; }
}

public sealed class CollisionCrossAssetContractInput
{
    public CollisionCrossAssetContractInput(
        CollisionInlineModelIdentityInput inlineModels,
        CollisionGlassIdentityInput glass,
        CollisionPrimaryChecksumInput primaryChecksum)
    {
        ArgumentNullException.ThrowIfNull(inlineModels);
        ArgumentNullException.ThrowIfNull(glass);
        ArgumentNullException.ThrowIfNull(primaryChecksum);

        InlineModels = inlineModels;
        Glass = glass;
        PrimaryChecksum = primaryChecksum;
    }

    public CollisionInlineModelIdentityInput InlineModels { get; }
    public CollisionGlassIdentityInput Glass { get; }
    public CollisionPrimaryChecksumInput PrimaryChecksum { get; }
}

public sealed record CollisionCrossAssetContractIssue(
    CollisionCrossAssetContractArea Area,
    CollisionCrossAssetContractIssueKind Kind,
    CollisionCrossAssetRequirement Requirement,
    string Message);

public sealed class CollisionCrossAssetContractValidation
{
    private readonly IReadOnlyList<CollisionCrossAssetContractIssue> _issues;

    internal CollisionCrossAssetContractValidation(
        CollisionCrossAssetValidationProfile profile,
        IEnumerable<CollisionCrossAssetContractIssue> issues)
    {
        if (!Enum.IsDefined(profile))
            throw new ArgumentOutOfRangeException(nameof(profile));
        ArgumentNullException.ThrowIfNull(issues);

        Profile = profile;
        _issues = Array.AsReadOnly(issues.ToArray());
    }

    public CollisionCrossAssetValidationProfile Profile { get; }

    public CollisionPrimaryChecksumRebuildStatus
        PrimaryChecksumRebuildStatus =>
        CollisionPrimaryChecksumRebuildStatus
            .StudioCanonicalV1Available;

    public IReadOnlyList<CollisionCrossAssetContractIssue> Issues => _issues;
    public bool IsValid => _issues.Count == 0;

    public bool IsRuntimeSafe => _issues.All(issue =>
        issue.Requirement !=
        CollisionCrossAssetRequirement.RuntimeConsumerSafety);
}

/// <summary>
/// Validates cross-asset identities without depending on loader objects,
/// editor state, or UI state. Inputs contain only the counts and scalar
/// identities consumed by the proven IW4 contracts.
/// </summary>
public static class CollisionCrossAssetContractValidator
{
    public static CollisionCrossAssetContractValidation Validate(
        CollisionCrossAssetContractInput input,
        CollisionCrossAssetValidationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(profile))
            throw new ArgumentOutOfRangeException(nameof(profile));

        var issues = new List<CollisionCrossAssetContractIssue>();
        ValidateInlineModels(input.InlineModels, profile, issues);
        ValidateGlass(input.Glass, profile, issues);
        ValidatePrimaryChecksum(input.PrimaryChecksum, profile, issues);

        return new CollisionCrossAssetContractValidation(profile, issues);
    }

    private static void ValidateInlineModels(
        CollisionInlineModelIdentityInput input,
        CollisionCrossAssetValidationProfile profile,
        ICollection<CollisionCrossAssetContractIssue> issues)
    {
        // Xbox IW4 MP evidence:
        // - G_ParseEntityField @ 0x82336430 recognizes '*', calls atoi at
        //   0x82336600, and stores that exact ushort ordinal at 0x82336614.
        // - CM_GetBrushModel @ 0x823531F8 indexes ColMap CModels[n].
        // - R_RegisterInlineModel @ 0x825B24F0 indexes GfxMap Models[n].
        // Neither consumer decrements or remaps n.
        foreach (MapEntInlineModelBinding model in
                 input.MapEntInlineModels)
        {
            int ordinal = model.ModelOrdinal;
            if (ordinal >= input.ColMapCModelCount)
            {
                Add(
                    issues,
                    CollisionCrossAssetContractArea.InlineModelIdentity,
                    CollisionCrossAssetContractIssueKind
                        .InlineModelOrdinalOutsideColMap,
                    CollisionCrossAssetRequirement.RuntimeConsumerSafety,
                    $"MapEnt inline model *{ordinal} is outside the " +
                    $"{input.ColMapCModelCount}-row ColMap CModel domain.");
            }
            if (ordinal >= input.GfxMapModelCount)
            {
                Add(
                    issues,
                    CollisionCrossAssetContractArea.InlineModelIdentity,
                    CollisionCrossAssetContractIssueKind
                        .InlineModelOrdinalOutsideGfxMap,
                    CollisionCrossAssetRequirement.RuntimeConsumerSafety,
                    $"MapEnt inline model *{ordinal} is outside the " +
                    $"{input.GfxMapModelCount}-row GfxMap model domain.");
            }
        }

        foreach (DynamicBrushModelBinding dynamicModel in
                 input.DynamicBrushModels)
        {
            int ordinal = dynamicModel.BrushModelOrdinal;
            if (ordinal >= input.ColMapCModelCount)
            {
                Add(
                    issues,
                    CollisionCrossAssetContractArea.InlineModelIdentity,
                    CollisionCrossAssetContractIssueKind
                        .DynamicBrushModelOrdinalOutsideColMap,
                    CollisionCrossAssetRequirement.RuntimeConsumerSafety,
                    $"Dynamic brush definition " +
                    $"{dynamicModel.DynamicDefinitionOrdinal} selects " +
                    $"model {ordinal}, outside the " +
                    $"{input.ColMapCModelCount}-row ColMap CModel domain.");
            }
            if (ordinal >= input.GfxMapModelCount)
            {
                Add(
                    issues,
                    CollisionCrossAssetContractArea.InlineModelIdentity,
                    CollisionCrossAssetContractIssueKind
                        .DynamicBrushModelOrdinalOutsideGfxMap,
                    CollisionCrossAssetRequirement.RuntimeConsumerSafety,
                    $"Dynamic brush definition " +
                    $"{dynamicModel.DynamicDefinitionOrdinal} selects " +
                    $"model {ordinal}, outside the " +
                    $"{input.GfxMapModelCount}-row GfxMap model domain.");
            }
        }

        if (profile !=
            CollisionCrossAssetValidationProfile.AuthoredCompilerContract)
        {
            return;
        }

        if (input.ColMapCModelCount != input.GfxMapModelCount)
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.InlineModelIdentity,
                CollisionCrossAssetContractIssueKind
                    .InlineModelTableCountMismatch,
                CollisionCrossAssetRequirement.AuthoredCompilerContract,
                $"Authored ColMap/GfxMap inline-model counts must match; " +
                $"received {input.ColMapCModelCount} and " +
                $"{input.GfxMapModelCount}.");
        }

        MapEntInlineModelBinding[] mapEntModels = input.MapEntInlineModels
            .OrderBy(value => value.PhysicalEntityOrdinal)
            .ToArray();
        if (mapEntModels.Length != mapEntModels
            .Select(value => value.PhysicalEntityOrdinal)
            .Distinct()
            .Count())
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.InlineModelIdentity,
                CollisionCrossAssetContractIssueKind
                    .InlineModelMapEntOrdinalClaimedMultipleTimes,
                CollisionCrossAssetRequirement.AuthoredCompilerContract,
                "Each authored MapEnt brush model must own one unique " +
                "physical entity order.");
        }
        if (mapEntModels.Length != mapEntModels
            .Select(value => value.ModelOrdinal)
            .Distinct()
            .Count())
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.InlineModelIdentity,
                CollisionCrossAssetContractIssueKind
                    .InlineModelMapEntOrdinalClaimedMultipleTimes,
                CollisionCrossAssetRequirement.AuthoredCompilerContract,
                "Each authored MapEnt brush model must own one unique " +
                "shared ColMap/GfxMap model row.");
        }
        if (mapEntModels.Length != 0 &&
            mapEntModels[0].ModelOrdinal == 0)
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.InlineModelIdentity,
                CollisionCrossAssetContractIssueKind
                    .InlineModelWorldOrdinalUsedByMapEnt,
                CollisionCrossAssetRequirement.AuthoredCompilerContract,
                "Authored MapEnt inline models must begin at *1; slot 0 is " +
                "the compiled world model.");
        }

        for (int index = 0; index < mapEntModels.Length; index++)
        {
            int expected = index + 1;
            int actual = mapEntModels[index].ModelOrdinal;
            if (actual == expected)
                continue;

            Add(
                issues,
                CollisionCrossAssetContractArea.InlineModelIdentity,
                CollisionCrossAssetContractIssueKind
                    .InlineModelOrdinalPrefixNotDense,
                CollisionCrossAssetRequirement.AuthoredCompilerContract,
                "Authored MapEnt inline-model identities must follow physical " +
                $"entity order as the dense prefix *1..*K; expected " +
                $"*{expected}, found *{actual}. Only the separately owned " +
                "dynamic-brush suffix may follow that prefix.");
            break;
        }

        int expectedModelCount = checked(
            1 +
            input.MapEntInlineModels.Count +
            input.DynamicBrushModels.Count);
        if (expectedModelCount > ushort.MaxValue + 1)
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.InlineModelIdentity,
                CollisionCrossAssetContractIssueKind
                    .InlineModelAddressableCountExceeded,
                CollisionCrossAssetRequirement.AuthoredCompilerContract,
                $"Authored inline-model allocation requires " +
                $"{expectedModelCount} rows, but its MapEnt and dynamic " +
                "consumers carry unsigned-16 ordinals and can address at " +
                $"most {ushort.MaxValue + 1} rows.");
        }
        if (input.ColMapCModelCount != expectedModelCount ||
            input.GfxMapModelCount != expectedModelCount)
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.InlineModelIdentity,
                CollisionCrossAssetContractIssueKind
                    .InlineModelAllocationCountMismatch,
                CollisionCrossAssetRequirement.AuthoredCompilerContract,
                "Authored inline-model tables must contain exactly one " +
                "world row, one row per MapEnt brush model, and one row per " +
                $"dynamic brush definition; expected {expectedModelCount}, " +
                $"received ColMap {input.ColMapCModelCount} and GfxMap " +
                $"{input.GfxMapModelCount}.");
        }

        int dynamicSuffixStart =
            checked(1 + input.MapEntInlineModels.Count);
        DynamicBrushModelBinding[] dynamicModels = input.DynamicBrushModels
            .OrderBy(value => value.DynamicDefinitionOrdinal)
            .ToArray();
        for (int index = 0; index < dynamicModels.Length; index++)
        {
            DynamicBrushModelBinding dynamicModel = dynamicModels[index];
            if (dynamicModel.DynamicDefinitionOrdinal != index)
            {
                Add(
                    issues,
                    CollisionCrossAssetContractArea.InlineModelIdentity,
                    CollisionCrossAssetContractIssueKind
                        .DynamicBrushDefinitionOrdinalNotDense,
                    CollisionCrossAssetRequirement.AuthoredCompilerContract,
                    "Authored DynEntDefList[1] brush definitions must own a " +
                    $"dense zero-based order; expected {index}, found " +
                    $"{dynamicModel.DynamicDefinitionOrdinal}.");
                break;
            }

            int expectedModelOrdinal = checked(dynamicSuffixStart + index);
            if (dynamicModel.BrushModelOrdinal != expectedModelOrdinal)
            {
                Add(
                    issues,
                    CollisionCrossAssetContractArea.InlineModelIdentity,
                    CollisionCrossAssetContractIssueKind
                        .DynamicBrushModelSuffixOrdinalMismatch,
                    CollisionCrossAssetRequirement.AuthoredCompilerContract,
                    $"Dynamic brush definition {index} must select shared " +
                    $"model suffix row {expectedModelOrdinal}; found " +
                    $"{dynamicModel.BrushModelOrdinal}.");
            }
            if (dynamicModel.PhysicsBrushModelOrdinal != 0)
            {
                Add(
                    issues,
                    CollisionCrossAssetContractArea.InlineModelIdentity,
                    CollisionCrossAssetContractIssueKind
                        .DynamicBrushPhysicsModelUnsupported,
                    CollisionCrossAssetRequirement.AuthoredCompilerContract,
                    $"Dynamic brush definition {index} carries unsupported " +
                    "nonzero PhysicsBrushModel. That field uses an " +
                    "independent unresolved namespace and must never mirror " +
                    "the shared BrushModel ordinal by assumption.");
            }
        }
    }

    private static void ValidateGlass(
        CollisionGlassIdentityInput input,
        CollisionCrossAssetValidationProfile profile,
        ICollection<CollisionCrossAssetContractIssue> issues)
    {
        // Xbox IW4 MP evidence:
        // - CM_TraceThroughBrush @ 0x82353F58 loads the ushort brush handle
        //   at 0x82354814, treats zero as absent, and passes h - 1 at
        //   0x82354854.
        // - G_PhysBreakGlass @ 0x822FFD98 subtracts one at 0x822FFDC8.
        // - G_Glass_PieceStateChanged @ 0x822FF158 passes the same zero-based
        //   GameMapMp ordinal to FxMap at 0x822FF280 and 0x822FF28C.
        foreach (ColMapGlassPieceHandle handle in input.ColMapBrushHandles)
        {
            int? sharedOrdinal = handle.SharedPieceOrdinal;
            if (sharedOrdinal is null)
                continue;

            ValidateGlassOrdinal(
                sharedOrdinal.Value,
                $"ColMap glass handle {handle.Value}",
                input,
                issues,
                CollisionCrossAssetContractIssueKind
                    .GlassHandleOutsideGameMapMp,
                CollisionCrossAssetContractIssueKind
                    .GlassHandleOutsideFxMap);
        }

        // G_Glass_GetIndicesFromName @ 0x822FEE80 returns serialized ushorts
        // unchanged. GScr_GetGlass and GScr_GetGlassArray return them
        // unchanged at 0x8232753C and 0x823275B8; GScr_DestroyGlass validates
        // the supplied ordinal directly at 0x82327750.
        foreach (GlassPieceOrdinal membership in input.GlassNameMemberships)
        {
            ValidateGlassOrdinal(
                membership.Value,
                $"Glass-name membership {membership.Value}",
                input,
                issues,
                CollisionCrossAssetContractIssueKind
                    .GlassNameMembershipOutsideGameMapMp,
                CollisionCrossAssetContractIssueKind
                    .GlassNameMembershipOutsideFxMap);
        }

        if (profile !=
            CollisionCrossAssetValidationProfile.AuthoredCompilerContract)
        {
            return;
        }

        if (input.GameMapMpPieceCount != input.FxMapInitialPieceCount)
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.GlassIdentity,
                CollisionCrossAssetContractIssueKind
                    .GlassPieceCountMismatch,
                CollisionCrossAssetRequirement.AuthoredCompilerContract,
                $"Authored GameMapMp/FxMap glass-piece counts must match; " +
                $"received {input.GameMapMpPieceCount} and " +
                $"{input.FxMapInitialPieceCount}.");
        }

        ushort[] distinctHandles = input.ColMapBrushHandles
            .Select(value => value.Value)
            .Where(value => value != 0)
            .Distinct()
            .Order()
            .ToArray();
        int expectedHandleCount = input.GameMapMpPieceCount;
        bool denseCoverage =
            distinctHandles.Length == expectedHandleCount;
        if (denseCoverage)
        {
            for (int index = 0; index < distinctHandles.Length; index++)
            {
                if (distinctHandles[index] == index + 1)
                    continue;

                denseCoverage = false;
                break;
            }
        }

        if (!denseCoverage)
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.GlassIdentity,
                CollisionCrossAssetContractIssueKind
                    .GlassHandleCoverageNotDense,
                CollisionCrossAssetRequirement.AuthoredCompilerContract,
                "Distinct nonzero authored ColMap glass handles must cover " +
                $"exactly 1..{expectedHandleCount}. Repeated brush handles " +
                "are valid and do not create additional pieces.");
        }
    }

    private static void ValidateGlassOrdinal(
        int ordinal,
        string source,
        CollisionGlassIdentityInput input,
        ICollection<CollisionCrossAssetContractIssue> issues,
        CollisionCrossAssetContractIssueKind gameIssue,
        CollisionCrossAssetContractIssueKind fxIssue)
    {
        if (ordinal >= input.GameMapMpPieceCount)
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.GlassIdentity,
                gameIssue,
                CollisionCrossAssetRequirement.RuntimeConsumerSafety,
                $"{source} resolves to zero-based piece {ordinal}, outside " +
                $"the {input.GameMapMpPieceCount}-row GameMapMp domain.");
        }
        if (ordinal >= input.FxMapInitialPieceCount)
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.GlassIdentity,
                fxIssue,
                CollisionCrossAssetRequirement.RuntimeConsumerSafety,
                $"{source} resolves to zero-based piece {ordinal}, outside " +
                $"the {input.FxMapInitialPieceCount}-row FxMap domain.");
        }
    }

    private static void ValidatePrimaryChecksum(
        CollisionPrimaryChecksumInput input,
        CollisionCrossAssetValidationProfile profile,
        ICollection<CollisionCrossAssetContractIssue> issues)
    {
        // Xbox IW4 MP evidence:
        // - CM_LoadMap @ 0x8234D598 returns ColMap root + 0xCC.
        // - SV_SpawnServer publishes it as mapcrc at 0x823EB4F8-0x823EB510.
        // - R_LoadWorld @ 0x825C26E8 returns GfxMap root + 0x148.
        // - CG_Init compares that value with mapcrc at 0x821C4230-0x821C425C.
        // These paths copy and compare authored scalars; no runtime
        // recomputation is performed. The probable production CRC32 input
        // scope remains unknown, so imported values preserve production
        // fidelity while greenfield builds use a separately identified
        // consumer-compatible Studio policy.
        if (input.ColMapChecksum != input.GfxMapChecksum)
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.PrimaryChecksum,
                CollisionCrossAssetContractIssueKind.PrimaryChecksumMismatch,
                CollisionCrossAssetRequirement.RuntimeConsumerSafety,
                $"ColMap primary checksum " +
                $"0x{input.ColMapChecksum.Value:X8} does not match GfxMap " +
                $"primary checksum 0x{input.GfxMapChecksum.Value:X8}.");
        }

        if (profile !=
            CollisionCrossAssetValidationProfile.AuthoredCompilerContract)
        {
            return;
        }

        MapPrimaryChecksumAssignmentKind requiredKind =
            input.ContentDisposition ==
            MapPrimaryChecksumContentDisposition.StudioAuthoredContent
                ? MapPrimaryChecksumAssignmentKind.StudioCanonicalV1
                : MapPrimaryChecksumAssignmentKind.ImportedProduction;
        if (input.Assignment is null)
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.PrimaryChecksum,
                CollisionCrossAssetContractIssueKind
                    .PrimaryChecksumAssignmentRequired,
                CollisionCrossAssetRequirement.AuthoredCompilerContract,
                requiredKind ==
                    MapPrimaryChecksumAssignmentKind.StudioCanonicalV1
                    ? "A Studio-authored candidate requires the versioned " +
                      "StudioCanonicalV1 checksum calculated from its " +
                      "whole-map content identity."
                    : "A preserved imported candidate requires explicit " +
                      "ImportedProduction checksum preservation.");
            return;
        }

        if (input.Assignment.Kind != requiredKind ||
            input.Assignment.Checksum != input.ColMapChecksum ||
            input.Assignment.Checksum != input.GfxMapChecksum)
        {
            Add(
                issues,
                CollisionCrossAssetContractArea.PrimaryChecksum,
                CollisionCrossAssetContractIssueKind
                    .PrimaryChecksumAssignmentMismatch,
                CollisionCrossAssetRequirement.AuthoredCompilerContract,
                $"Primary checksum assignment must be {requiredKind} and " +
                "match both serialized roots. Assignment " +
                $"{input.Assignment.Kind} carries " +
                $"0x{input.Assignment.Checksum.Value:X8}; ColMap carries " +
                $"0x{input.ColMapChecksum.Value:X8}, GfxMap carries " +
                $"0x{input.GfxMapChecksum.Value:X8}.");
        }

        if (requiredKind ==
            MapPrimaryChecksumAssignmentKind.StudioCanonicalV1)
        {
            MapPrimaryChecksumAssignment? recomputed =
                input.CandidateContentIdentity is null
                    ? null
                    : MapPrimaryChecksumPolicy.ComputeStudioCanonical(
                        input.CandidateContentIdentity);
            if (recomputed is null ||
                input.Assignment.ContentIdentity !=
                input.CandidateContentIdentity ||
                input.Assignment.ImportedBaseline is not null ||
                input.Assignment.Checksum != recomputed.Checksum ||
                input.ColMapChecksum != recomputed.Checksum ||
                input.GfxMapChecksum != recomputed.Checksum)
            {
                Add(
                    issues,
                    CollisionCrossAssetContractArea.PrimaryChecksum,
                    CollisionCrossAssetContractIssueKind
                        .PrimaryChecksumContentIdentityMismatch,
                    CollisionCrossAssetRequirement.AuthoredCompilerContract,
                    "A StudioCanonicalV1 assignment must be recomputed from " +
                    "the exact whole-map content identity independently " +
                    "calculated for this candidate; stale or cross-map " +
                    "assignments are invalid.");
            }
        }
        else
        {
            ImportedMapPrimaryChecksumBaseline? baseline =
                input.Assignment.ImportedBaseline;
            ImportedMapPrimaryChecksumCandidateEvidence? candidate =
                input.ImportedCandidateEvidence;
            if (baseline is null ||
                candidate is null ||
                !string.Equals(
                    baseline.MapAssetName,
                    candidate.MapAssetName,
                    StringComparison.Ordinal) ||
                baseline.ImportedAssetGraphDigest !=
                candidate.AssetGraphDigest ||
                input.Assignment.ContentIdentity is not null ||
                input.Assignment.Checksum !=
                baseline.Checksum ||
                input.ColMapChecksum != baseline.Checksum ||
                input.GfxMapChecksum != baseline.Checksum)
            {
                Add(
                    issues,
                    CollisionCrossAssetContractArea.PrimaryChecksum,
                    CollisionCrossAssetContractIssueKind
                        .PrimaryChecksumImportedBaselineMismatch,
                    CollisionCrossAssetRequirement.AuthoredCompilerContract,
                    "ImportedProduction preservation requires immutable " +
                    "baseline evidence matching the independently calculated " +
                    "candidate map identity and asset-graph digest, plus the " +
                    "exact captured checksum in both serialized roots.");
            }
        }
    }

    private static void Add(
        ICollection<CollisionCrossAssetContractIssue> issues,
        CollisionCrossAssetContractArea area,
        CollisionCrossAssetContractIssueKind kind,
        CollisionCrossAssetRequirement requirement,
        string message) =>
        issues.Add(new CollisionCrossAssetContractIssue(
            area,
            kind,
            requirement,
            message));
}
