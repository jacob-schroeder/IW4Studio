using IW4.Assets.Assets.FxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Emission;
using IW4.FastFiles.Zone;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Compilation.Glass;
using IW4.Studio.MapEditor.Compilation.Lighting;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Closes the bounded M4/M5 graph with minimal entity, FX, and gameplay
/// roots. The result is intentionally stopped before linking or persistence.
/// </summary>
public static class MinimalMultiplayerMapTargetProbeAssembler
{
    public static MinimalMultiplayerMapTargetProbeCandidate Compile(
        GfxWorldNoBakeLightingCandidate lightingCandidate,
        MapCompilerContentIdentityInput contentIdentityInput)
    {
        ArgumentNullException.ThrowIfNull(lightingCandidate);
        ArgumentNullException.ThrowIfNull(contentIdentityInput);
        RequireEligibleLightingCandidate(lightingCandidate);

        MapCompilerContentIdentity contentIdentity =
            MapCompilerContentIdentityCalculator.Compute(
                contentIdentityInput);
        RequireMatchingContentIdentity(
            lightingCandidate,
            contentIdentityInput,
            contentIdentity);

        string mapName = contentIdentityInput.MapAssetName;
        var mapEnts =
            new MinimalMultiplayerMapProbeMapEntsBuildData(mapName);
        var collision =
            new MinimalMultiplayerMapProbeCollisionBuildData(
                lightingCandidate
                    .SpatialAssembly
                    .Collision
                    .Definition,
                mapEnts);
        EmptyGlassDomainCompilation glass =
            EmptyGlassDomainCompiler.Compile(
                mapName,
                collision.Definition.Brushes.Select(value =>
                    new ColMapGlassPieceHandle(
                        value.GlassPieceIndex)));

        var candidate =
            new MinimalMultiplayerMapTargetProbeCandidate(
                lightingCandidate,
                contentIdentityInput,
                contentIdentity,
                collision,
                mapEnts,
                glass,
                CreateBlockers());
        MinimalMultiplayerMapTargetProbeValidator
            .RequireValid(candidate);
        return candidate;
    }

    private static void RequireEligibleLightingCandidate(
        GfxWorldNoBakeLightingCandidate candidate)
    {
        if (!candidate.ManagedSerializerAccepted ||
            candidate.PersistenceAuthorized ||
            candidate.TargetConsumerAccepted)
        {
            throw new ArgumentException(
                "The M7 target probe requires the valid, non-persistable " +
                "M5 no-bake lighting candidate.",
                nameof(candidate));
        }
    }

    private static void RequireMatchingContentIdentity(
        GfxWorldNoBakeLightingCandidate candidate,
        MapCompilerContentIdentityInput input,
        MapCompilerContentIdentity computed)
    {
        MapPrimaryChecksumAssignment assignment =
            candidate
                .SpatialAssembly
                .ChecksumAssignment;
        if (!string.Equals(
                input.MapAssetName,
                candidate
                    .SpatialAssembly
                    .SourceCandidate
                    .MapAssetName,
                StringComparison.Ordinal) ||
            assignment.Kind !=
                MapPrimaryChecksumAssignmentKind.StudioCanonicalV1 ||
            assignment.ContentIdentity != computed ||
            assignment.Checksum !=
                MapPrimaryChecksumPolicy
                    .ComputeStudioCanonical(computed)
                    .Checksum)
        {
            throw new ArgumentException(
                "The M7 content-identity input must reproduce the exact " +
                "normalized map identity and StudioCanonicalV1 assignment " +
                "already carried by the M4/M5 candidate.",
                nameof(input));
        }
    }

    private static MinimalMultiplayerMapTargetProbeBlocker[]
        CreateBlockers() =>
    [
        new(
            MinimalMultiplayerMapTargetProbeBlockerKind
                .TargetMaterialDependencyNotResolved,
            "The bounded GfxMap still requires selection and resolution of " +
            "one target-proven opaque material."),
        new(
            MinimalMultiplayerMapTargetProbeBlockerKind
                .ManagedLinkPackageAndFreshLoadNotAccepted,
            "All six semantic roots pass their checked emitters, but the " +
            "five-root aggregate has not yet passed managed linking, " +
            "packaging, and fresh loading."),
        new(
            MinimalMultiplayerMapTargetProbeBlockerKind
                .RetailTargetAcceptanceNotEstablished,
            "The graph has not completed retail initialization, rendering, " +
            "collision, spawn, and delayed-crash acceptance."),
        new(
            MinimalMultiplayerMapTargetProbeBlockerKind
                .ProductionGameplayGraphNotCompiled,
            "The minimal MapEnts/FxMap/GameMapMp profile is a target probe, " +
            "not the complete authored gameplay and glass compiler."),
        new(
            MinimalMultiplayerMapTargetProbeBlockerKind
                .PersistenceNotAuthorized,
            "The target-probe graph cannot enter Save As until dependency, " +
            "managed fresh-load, and retail acceptance gates are closed.")
    ];
}

/// <summary>
/// Aggregate validation for all local and cross-root contracts required by
/// the bounded M7 target probe.
/// </summary>
internal static class MinimalMultiplayerMapTargetProbeValidator
{
    internal static void RequireValid(
        MinimalMultiplayerMapTargetProbeCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var issues = new List<string>();

        ValidateRootNames(candidate, issues);
        ValidatePrimaryLightSentinel(candidate, issues);
        ValidateMapEntProfile(candidate.MapEntsBuildData, issues);
        ValidateMapEntRuntimeDomains(candidate, issues);
        ValidateGlassDomain(candidate, issues);
        ValidateNestedMapEntOwnership(candidate, issues);
        ValidateCrossAssetIdentities(candidate, issues);
        ValidateEmitters(candidate, issues);

        if (issues.Count != 0)
        {
            throw new InvalidDataException(
                "The minimal multiplayer target-probe graph violates its " +
                "M7 contract: " +
                string.Join("; ", issues));
        }
    }

    private static void ValidateRootNames(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        ICollection<string> issues)
    {
        string expected = candidate.MapAssetName;
        (string Label, string? Name)[] roots =
        [
            ("GfxMap", candidate.GfxWorldBuildData.Definition.Name),
            ("ColMapMp", candidate.CollisionBuildData.Definition.Name),
            ("ComMap", candidate.ComWorldBuildData.Name),
            ("MapEnts", candidate.MapEntsBuildData.Name),
            ("FxMap", candidate.FxWorldBuildData.Name),
            ("GameMapMp", candidate.GameWorldMpBuildData.Name)
        ];
        foreach ((string label, string? name) in roots)
        {
            string? normalized = null;
            try
            {
                if (name is not null)
                {
                    normalized =
                        MapCompilerContentIdentityInput
                            .NormalizeMultiplayerMapAssetName(name);
                }
            }
            catch (ArgumentException)
            {
                // Report the root-specific identity issue below.
            }

            if (!string.Equals(
                    normalized,
                    expected,
                    StringComparison.Ordinal))
            {
                issues.Add(
                    $"{label} name '{name ?? "<null>"}' does not normalize " +
                    $"to '{expected}'");
            }
        }
    }

    private static void ValidatePrimaryLightSentinel(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        ICollection<string> issues)
    {
        var gfx = candidate.GfxWorldBuildData.Definition;
        IComWorldBuildData com = candidate.ComWorldBuildData;
        ComPrimaryLightBuildData? sentinel =
            com.PrimaryLights.Count == 1
                ? com.PrimaryLights[0]
                : null;
        bool isZeroSentinel =
            sentinel is { } light &&
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
        if (gfx.PrimaryLightCount != 1 ||
            gfx.SunPrimaryLightIndex != 0 ||
            gfx.ShadowGeom.Count != 1 ||
            gfx.LightRegions.Count != 1 ||
            !isZeroSentinel)
        {
            issues.Add(
                "GfxMap and ComMap do not share the exact one-row M5 " +
                "zero-valued primary-light sentinel domain");
        }
    }

    private static void ValidateMapEntProfile(
        IMapEntsBuildData mapEnts,
        ICollection<string> issues)
    {
        byte[] bytes = mapEnts.GetEntityStringBytesCopy();
        MapEntsSyntaxDocument syntax =
            MapEntsSyntaxParser.Parse(bytes);
        if (!syntax.CanEdit ||
            !syntax.HasTrailingNul ||
            syntax.Entities.Count != 3)
        {
            string diagnostics = syntax.Diagnostics.Count == 0
                ? "wrong entity cardinality or missing trailing NUL"
                : string.Join(
                    ", ",
                    syntax.Diagnostics.Select(value =>
                        value.Code.ToString()));
            issues.Add(
                $"MapEnts is not the strict three-entity target profile " +
                $"({diagnostics})");
            return;
        }

        if (!MatchesProperties(
                syntax.Entities[0],
                [("classname", "worldspawn")]) ||
            !MatchesProperties(
                syntax.Entities[1],
                [
                    ("classname", "mp_dm_spawn"),
                    ("origin", "0 0 64"),
                    ("angles", "0 0 0")
                ]) ||
            !MatchesProperties(
                syntax.Entities[2],
                [
                    ("classname", "mp_global_intermission"),
                    ("origin", "0 -192 128"),
                    ("angles", "15 90 0")
                ]))
        {
            issues.Add(
                "MapEnts must contain canonical worldspawn, mp_dm_spawn, " +
                "and mp_global_intermission rows with bounded transforms");
        }
    }

    private static bool MatchesProperties(
        MapEntsSyntaxEntity entity,
        IReadOnlyList<(string Key, string Value)> expected) =>
        entity.Properties.Count == expected.Count &&
        entity.Properties
            .Select(value => (value.Key, value.Value))
            .SequenceEqual(expected);

    private static void ValidateMapEntRuntimeDomains(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        ICollection<string> issues)
    {
        IMapEntsBuildData mapEnts = candidate.MapEntsBuildData;
        var expectedStage = new StageBuildData(
            "stage 0",
            new Float3BuildData(0, 0, 0),
            TriggerIndex: 0x0400,
            SunPrimaryLightIndex: 1,
            Pad13: 0);
        if (mapEnts.Triggers.Models.Count != 0 ||
            mapEnts.Triggers.Hulls.Count != 0 ||
            mapEnts.Triggers.Slabs.Count != 0 ||
            mapEnts.Stages.Count != 1 ||
            mapEnts.Stages[0] != expectedStage ||
            !mapEnts.GetPad29To2BCopy().SequenceEqual(
                new byte[] { 0, 0, 0 }))
        {
            issues.Add(
                "The minimal MapEnts profile requires empty triggers and " +
                "the exact native stage-0 sentinel plus a zero-valued " +
                "three-byte tail");
        }

        var col = candidate.CollisionBuildData.Definition;
        bool emptyDynamics =
            col.DynEntCount.Count == 2 &&
            col.DynEntCount.All(value => value == 0) &&
            col.DynEntDefList.Count == 2 &&
            col.DynEntDefList.All(value => value.Count == 0) &&
            col.DynEntPoseList.Count == 2 &&
            col.DynEntPoseList.All(value => value.Count == 0) &&
            col.DynEntClientList.Count == 2 &&
            col.DynEntClientList.All(value => value.Count == 0) &&
            col.DynEntCollList.Count == 2 &&
            col.DynEntCollList.All(value => value.Count == 0) &&
            candidate.CollisionBuildData
                .References
                .DynamicEntities.Count == 2 &&
            candidate.CollisionBuildData
                .References
                .DynamicEntities.All(value => value.Count == 0);
        if (!emptyDynamics)
        {
            issues.Add(
                "The minimal ColMapMp profile requires both dynamic-entity " +
                "slots and all parallel runtime tables to be empty");
        }

    }

    private static void ValidateGlassDomain(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        ICollection<string> issues)
    {
        IEnumerable<ColMapGlassPieceHandle> collisionHandles =
            candidate.CollisionBuildData.Definition.Brushes.Select(
                value => new ColMapGlassPieceHandle(
                    value.GlassPieceIndex));
        foreach (EmptyGlassDomainIssue issue in
                 EmptyGlassDomainValidator.Validate(
                     candidate.GlassCompilation,
                     collisionHandles))
        {
            issues.Add(
                $"Glass/{issue.Kind}: {issue.Message}");
        }
    }

    private static void ValidateNestedMapEntOwnership(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        ICollection<string> issues)
    {
        ClipMapReferenceBuildData references =
            candidate.CollisionBuildData.References;
        NestedXAssetBuildLink? link = references.MapEntsLink;
        string expectedReference = $",{candidate.MapAssetName}";
        if (references.MapEnts is not { } reference ||
            reference.AssetType != XAssetType.MapEnts ||
            !string.Equals(
                reference.OriginalSerializedName,
                expectedReference,
                StringComparison.Ordinal) ||
            link is null ||
            link.SourceForm != NestedXAssetPointerSourceForm.Insert ||
            !ReferenceEquals(link.Reference, reference) ||
            !ReferenceEquals(
                link.IncomingDefinition,
                candidate.MapEntsBuildData) ||
            link.ImportedPackedRaw is not null ||
            link.ImportedOwnerCellRaw is not null)
        {
            issues.Add(
                "ColMapMp must exclusively own MapEnts through one " +
                "address-free Insert source link");
        }
    }

    private static void ValidateCrossAssetIdentities(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        ICollection<string> issues)
    {
        var col = candidate.CollisionBuildData.Definition;
        var gfx = candidate.GfxWorldBuildData.Definition;
        FxGlassSystem fxGlass =
            candidate.FxWorldBuildData.GlassSystem;
        GGlassDataBuildData? gameGlass =
            candidate.GameWorldMpBuildData.GlassData;
        MapPrimaryChecksumAssignment assignment =
            candidate
                .LightingCandidate
                .SpatialAssembly
                .ChecksumAssignment;
        var validation =
            CollisionCrossAssetContractValidator.Validate(
                new CollisionCrossAssetContractInput(
                    new CollisionInlineModelIdentityInput(
                        col.NumSubModels,
                        gfx.ModelCount,
                        mapEntInlineModels: [],
                        dynamicBrushModels: []),
                    new CollisionGlassIdentityInput(
                        gameMapMpPieceCount:
                            gameGlass?.Pieces.Count ?? 0,
                        fxMapInitialPieceCount:
                            checked((int)fxGlass.InitPieceCount),
                        colMapBrushHandles:
                            col.Brushes.Select(value =>
                                new ColMapGlassPieceHandle(
                                    value.GlassPieceIndex)),
                        glassNameMemberships:
                            gameGlass?.Names.SelectMany(
                                value => value.PieceIndices.Select(
                                    pieceOrdinal =>
                                        new GlassPieceOrdinal(
                                            pieceOrdinal))) ??
                            []),
                    new CollisionPrimaryChecksumInput(
                        new MapPrimaryChecksum(col.Checksum),
                        new MapPrimaryChecksum(gfx.Checksum),
                        MapPrimaryChecksumContentDisposition
                            .StudioAuthoredContent,
                        assignment,
                        candidate.ContentIdentity)),
                CollisionCrossAssetValidationProfile
                    .AuthoredCompilerContract);
        foreach (CollisionCrossAssetContractIssue issue in
                 validation.Issues)
        {
            issues.Add(
                $"{issue.Area}/{issue.Kind}: {issue.Message}");
        }
    }

    private static void ValidateEmitters(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        ICollection<string> issues)
    {
        ValidateEmitter(
            "GfxMap",
            candidate.GfxWorldBuildData,
            new GfxWorldBodyEmitter(),
            issues);
        ValidateEmitter(
            "ColMapMp",
            candidate.CollisionBuildData,
            new ClipMapBodyEmitter(XAssetType.ColMapMp),
            issues);
        ValidateEmitter(
            "ComMap",
            candidate.ComWorldBuildData,
            new ComWorldBodyEmitter(),
            issues);
        ValidateEmitter(
            "MapEnts",
            candidate.MapEntsBuildData,
            new MapEntsBodyEmitter(),
            issues);
        ValidateEmitter(
            "FxMap",
            candidate.FxWorldBuildData,
            new FxWorldBodyEmitter(),
            issues);
        ValidateEmitter(
            "GameMapMp",
            candidate.GameWorldMpBuildData,
            new GameWorldMpBodyEmitter(),
            issues);
    }

    private static void ValidateEmitter(
        string label,
        IXAssetBuildData buildData,
        IXAssetBodyEmitter emitter,
        ICollection<string> issues)
    {
        IReadOnlyList<EmissionError> diagnostics =
            emitter.Validate(buildData);
        if (diagnostics.Count != 0)
        {
            issues.Add(
                $"{label} emitter validation failed: " +
                string.Join(
                    ", ",
                    diagnostics.Select(value =>
                        $"{value.Path}: {value.Message}")));
            return;
        }

        try
        {
            _ = emitter.Plan(buildData, new EmissionPlan());
        }
        catch (Exception exception)
        {
            issues.Add(
                $"{label} emitter planning failed: {exception.Message}");
        }
    }
}
