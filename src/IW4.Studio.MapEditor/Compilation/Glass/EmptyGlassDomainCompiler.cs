using IW4.Assets.Assets.FxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Studio.MapEditor.Compilation.Collision;

namespace IW4.Studio.MapEditor.Compilation.Glass;

/// <summary>
/// Narrow authority of the M6 empty-glass compiler. Non-empty glass geometry
/// and gameplay bytes remain outside this authority.
/// </summary>
public enum EmptyGlassDomainAuthority
{
    ManagedEmptyDomainEmissionOnly = 0
}

public enum EmptyGlassDomainIssueKind
{
    IdentityPlanNotEmpty = 0,
    CollisionProjectionMismatch = 1,
    FxMapNameMismatch = 2,
    FxMapGlassSystemNotEmpty = 3,
    FxMapDefinitionReferencesNotEmpty = 4,
    GameMapNameMismatch = 5,
    GameMapGlassDataMissing = 6,
    GameMapGlassDataNotCanonicalEmpty = 7
}

public sealed record EmptyGlassDomainIssue(
    EmptyGlassDomainIssueKind Kind,
    string Message);

/// <summary>
/// Reusable, checked empty-glass projection across FxMap, GameMapMp, and the
/// ColMap brush-handle domain.
/// </summary>
public sealed class EmptyGlassDomainCompilation
{
    internal EmptyGlassDomainCompilation(
        string mapAssetName,
        GlassPieceIdentityAllocationPlan identityPlan,
        IFxWorldBuildData fxWorldBuildData,
        IGameWorldMpBuildData gameWorldMpBuildData)
    {
        MapAssetName = mapAssetName;
        IdentityPlan = identityPlan;
        FxWorldBuildData = fxWorldBuildData;
        GameWorldMpBuildData = gameWorldMpBuildData;
    }

    public EmptyGlassDomainAuthority Authority =>
        EmptyGlassDomainAuthority.ManagedEmptyDomainEmissionOnly;

    public bool NonEmptyEmissionAuthorized => false;

    public string MapAssetName { get; }

    public GlassPieceIdentityAllocationPlan IdentityPlan { get; }

    public IFxWorldBuildData FxWorldBuildData { get; }

    public IGameWorldMpBuildData GameWorldMpBuildData { get; }
}

/// <summary>
/// Builds only the exact zero-piece representation proven safe for the
/// current target probe.
/// </summary>
public static class EmptyGlassDomainCompiler
{
    public const string CompilerIdentity =
        "iw4-studio.glass.empty-domain.fx-game-col-zero@2";

    public static EmptyGlassDomainCompilation Compile(
        string mapAssetName,
        IEnumerable<ColMapGlassPieceHandle> collisionBrushHandles)
    {
        string normalizedName = MapCompilerContentIdentityInput
            .NormalizeMultiplayerMapAssetName(mapAssetName);
        ArgumentNullException.ThrowIfNull(collisionBrushHandles);
        ColMapGlassPieceHandle[] actualHandles =
            collisionBrushHandles.ToArray();

        GlassPieceIdentityAllocationPlan identityPlan =
            GlassPieceIdentityAllocator.Allocate(
                new GlassPieceIdentityAllocationRequest(
                    actualHandles.Length,
                    pieces: [],
                    membershipGroups: []));
        var compilation = new EmptyGlassDomainCompilation(
            normalizedName,
            identityPlan,
            new EmptyGlassFxWorldBuildData(normalizedName),
            new EmptyGlassGameWorldMpBuildData(normalizedName));
        EmptyGlassDomainValidator.RequireValid(
            compilation,
            actualHandles);
        return compilation;
    }
}

/// <summary>
/// Fail-closed validation of the complete empty-domain projection.
/// </summary>
public static class EmptyGlassDomainValidator
{
    public static IReadOnlyList<EmptyGlassDomainIssue> Validate(
        EmptyGlassDomainCompilation compilation,
        IEnumerable<ColMapGlassPieceHandle> collisionBrushHandles)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(collisionBrushHandles);
        ColMapGlassPieceHandle[] actualHandles =
            collisionBrushHandles.ToArray();
        var issues = new List<EmptyGlassDomainIssue>();

        GlassPieceIdentityAllocationPlan plan =
            compilation.IdentityPlan;
        if (plan.PieceCount != 0 ||
            plan.MembershipGroups.Count != 0 ||
            plan.CollisionBrushHandles.Any(value => !value.IsNone))
        {
            issues.Add(new EmptyGlassDomainIssue(
                EmptyGlassDomainIssueKind.IdentityPlanNotEmpty,
                "The empty glass domain contains allocated pieces, " +
                "memberships, or nonzero planned collision handles."));
        }

        if (actualHandles.Length != plan.CollisionBrushCount ||
            !actualHandles.SequenceEqual(plan.CollisionBrushHandles))
        {
            issues.Add(new EmptyGlassDomainIssue(
                EmptyGlassDomainIssueKind.CollisionProjectionMismatch,
                "ColMap brush glass handles do not match the complete " +
                "zero-valued empty-domain projection."));
        }

        IFxWorldBuildData fx = compilation.FxWorldBuildData;
        if (!HasExpectedName(fx.Name, compilation.MapAssetName))
        {
            issues.Add(new EmptyGlassDomainIssue(
                EmptyGlassDomainIssueKind.FxMapNameMismatch,
                "The empty FxMap root does not share the compiled map " +
                "asset identity."));
        }

        if (!IsExactlyEmpty(fx.GlassSystem))
        {
            issues.Add(new EmptyGlassDomainIssue(
                EmptyGlassDomainIssueKind.FxMapGlassSystemNotEmpty,
                "The FxMap glass header, pointer provenance, or arrays " +
                "are not the canonical zero-valued empty representation."));
        }

        if (fx.DefinitionReferences.Count != 0)
        {
            issues.Add(new EmptyGlassDomainIssue(
                EmptyGlassDomainIssueKind
                    .FxMapDefinitionReferencesNotEmpty,
                "The empty FxMap root cannot own glass definition " +
                "asset references."));
        }

        IGameWorldMpBuildData game =
            compilation.GameWorldMpBuildData;
        if (!HasExpectedName(game.Name, compilation.MapAssetName))
        {
            issues.Add(new EmptyGlassDomainIssue(
                EmptyGlassDomainIssueKind.GameMapNameMismatch,
                "The empty GameMapMp root does not share the compiled map " +
                "asset identity."));
        }

        if (game.GlassData is null)
        {
            issues.Add(new EmptyGlassDomainIssue(
                EmptyGlassDomainIssueKind.GameMapGlassDataMissing,
                "The empty GameMapMp root must own a non-null GGlassData " +
                "header."));
        }
        else if (!IsExactlyEmpty(game.GlassData))
        {
            issues.Add(new EmptyGlassDomainIssue(
                EmptyGlassDomainIssueKind
                    .GameMapGlassDataNotCanonicalEmpty,
                "The GameMapMp GGlassData header must have zero pieces, " +
                "zero names, zero damage thresholds, null child pointers, " +
                "and a zero-valued 0x6c-byte tail."));
        }

        return issues.AsReadOnly();
    }

    public static void RequireValid(
        EmptyGlassDomainCompilation compilation,
        IEnumerable<ColMapGlassPieceHandle> collisionBrushHandles)
    {
        IReadOnlyList<EmptyGlassDomainIssue> issues = Validate(
            compilation,
            collisionBrushHandles);
        if (issues.Count == 0)
            return;

        throw new InvalidDataException(
            "The empty glass compilation violates its M6 contract: " +
            string.Join(
                "; ",
                issues.Select(value =>
                    $"{value.Kind}: {value.Message}")));
    }

    private static bool HasExpectedName(
        string? actual,
        string expected)
    {
        try
        {
            return actual is not null &&
                string.Equals(
                    MapCompilerContentIdentityInput
                        .NormalizeMultiplayerMapAssetName(actual),
                    expected,
                    StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsExactlyEmpty(FxGlassSystem glass) =>
        glass.Offset == 0 &&
        glass.Time == 0 &&
        glass.PrevTime == 0 &&
        glass.DefCount == 0 &&
        glass.PieceLimit == 0 &&
        glass.PieceWordCount == 0 &&
        glass.InitPieceCount == 0 &&
        glass.CellCount == 0 &&
        glass.ActivePieceCount == 0 &&
        glass.FirstFreePiece == 0 &&
        glass.GeoDataLimit == 0 &&
        glass.GeoDataCount == 0 &&
        glass.InitGeoDataCount == 0 &&
        glass.DefsPointer.Raw == 0 &&
        glass.Defs.Count == 0 &&
        glass.PiecePlacesPointer.Raw == 0 &&
        glass.PiecePlaces.Count == 0 &&
        glass.PieceStatesPointer.Raw == 0 &&
        glass.PieceStates.Count == 0 &&
        glass.PieceDynamicsPointer.Raw == 0 &&
        glass.PieceDynamics.Count == 0 &&
        glass.GeoDataPointer.Raw == 0 &&
        glass.GeoData.Count == 0 &&
        glass.IsInUsePointer.Raw == 0 &&
        glass.IsInUse.Count == 0 &&
        glass.CellBitsPointer.Raw == 0 &&
        glass.CellBits.Count == 0 &&
        glass.VisDataPointer.Raw == 0 &&
        glass.VisData.Count == 0 &&
        glass.LinkOrgPointer.Raw == 0 &&
        glass.LinkOrg.Count == 0 &&
        glass.HalfThicknessPointer.Raw == 0 &&
        glass.HalfThickness.Count == 0 &&
        glass.LightingHandlesPointer.Raw == 0 &&
        glass.LightingHandles.Count == 0 &&
        glass.InitPieceStatesPointer.Raw == 0 &&
        glass.InitPieceStates.Count == 0 &&
        glass.InitGeoDataPointer.Raw == 0 &&
        glass.InitGeoData.Count == 0 &&
        glass.NeedToCompactData == 0 &&
        glass.InitCount == 0 &&
        glass.Pad66 == 0 &&
        glass.EffectChanceAccum == 0 &&
        glass.LastPieceDeletionTime == 0;

    private static bool IsExactlyEmpty(GGlassDataBuildData glass) =>
        glass.Pieces.Count == 0 &&
        glass.DamageToWeaken == 0 &&
        glass.DamageToDestroy == 0 &&
        glass.Names.Count == 0 &&
        glass.ImportedGlassNamesPointerRaw is null or 0 &&
        glass.GetPad14To7FCopy() is { Length: 0x6c } pad &&
        pad.All(value => value == 0);
}

internal sealed class EmptyGlassFxWorldBuildData : IFxWorldBuildData
{
    internal EmptyGlassFxWorldBuildData(string name)
    {
        Name = name;
    }

    public XAssetType AssetType => XAssetType.FxMap;

    public string Name { get; }

    public FxGlassSystem GlassSystem { get; } = new();

    public IReadOnlyList<FxGlassDefReferenceBuildData>
        DefinitionReferences =>
            Array.Empty<FxGlassDefReferenceBuildData>();
}

internal sealed class EmptyGlassGameWorldMpBuildData :
    IGameWorldMpBuildData
{
    internal EmptyGlassGameWorldMpBuildData(string name)
    {
        Name = name;
    }

    public XAssetType AssetType => XAssetType.GameMapMp;

    public string Name { get; }

    public GGlassDataBuildData GlassData { get; } =
        new(
            pieces: [],
            damageToWeaken: 0,
            damageToDestroy: 0,
            names: [],
            pad14To7F: new byte[0x6c]);
}
