using System.Security.Cryptography;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Zone;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Explicit authority for creating bytes used only by the bounded retail
/// target test. It is intentionally unrelated to Map Editor Save As.
/// </summary>
public enum MinimalMultiplayerMapPreAcceptanceArtifactAuthority
{
    PreAcceptanceTargetTestOnly = 0
}

/// <summary>
/// A deterministic FastFile candidate that may be staged for dependency-plan
/// and retail-target validation. It grants no production persistence
/// authority.
/// </summary>
public sealed class MinimalMultiplayerMapPreAcceptanceFastFileArtifact
{
    private readonly byte[] _fastFileBytes;
    private readonly IReadOnlyList<ZoneAssetKey> _ownedAssetKeys;

    internal MinimalMultiplayerMapPreAcceptanceFastFileArtifact(
        MpTerminalMinimalTargetProbeCompilation sourceCompilation,
        byte[] fastFileBytes,
        int decodedZoneByteLength)
    {
        SourceCompilation = sourceCompilation ??
            throw new ArgumentNullException(nameof(sourceCompilation));
        ArgumentNullException.ThrowIfNull(fastFileBytes);
        if (fastFileBytes.Length == 0)
        {
            throw new ArgumentException(
                "A pre-acceptance artifact cannot be empty.",
                nameof(fastFileBytes));
        }
        if (decodedZoneByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decodedZoneByteLength));
        }

        _fastFileBytes = fastFileBytes.ToArray();
        _ownedAssetKeys =
            Array.AsReadOnly(
                sourceCompilation.Candidate.TopLevelRootTypes
                    .Select(value =>
                        new ZoneAssetKey(
                            value,
                            sourceCompilation
                                .Candidate
                                .MapAssetName))
                    .Concat(
                        sourceCompilation
                            .RuntimeSupport
                            .OwnedAssetKeys)
                    .OrderBy(value => value.Type)
                    .ThenBy(
                        value => value.LogicalName,
                        StringComparer.Ordinal)
                    .ToArray());
        FastFileByteLength = _fastFileBytes.Length;
        DecodedZoneByteLength = decodedZoneByteLength;
        FastFileSha256 = Convert.ToHexString(
                SHA256.HashData(_fastFileBytes))
            .ToLowerInvariant();
    }

    public MinimalMultiplayerMapPreAcceptanceArtifactAuthority Authority =>
        MinimalMultiplayerMapPreAcceptanceArtifactAuthority
            .PreAcceptanceTargetTestOnly;

    public bool DefaultMpDependencyPlanAccepted => false;

    public bool DiagnosticMarkerPackaged =>
        SourceCompilation.RuntimeSupport.DiagnosticMarkerCompiled;

    public bool LevelScriptPackaged =>
        SourceCompilation.RuntimeSupport.LevelScriptCompiled;

    public bool ConstantConfigStringTablePackaged =>
        SourceCompilation.RuntimeSupport
            .ConstantConfigStringTableCompiled;

    public bool GameplayModelSupportPackaged =>
        SourceCompilation.RuntimeSupport
            .GameplayModelSupportCompiled;

    public bool TargetLaunchReady =>
        SourceCompilation.RuntimeSupport.TargetLaunchReady;

    public bool TargetConsumerAccepted => false;

    public bool PersistenceAuthorized => false;

    public MpTerminalMinimalTargetProbeCompilation SourceCompilation
    {
        get;
    }

    public string MapAssetName =>
        SourceCompilation.Candidate.MapAssetName;

    public string TargetZoneName =>
        SourceCompilation.TargetZoneName;

    public string TargetFileName => $"{TargetZoneName}.ff";

    public int FastFileByteLength { get; }

    public int DecodedZoneByteLength { get; }

    public string FastFileSha256 { get; }

    public IReadOnlyList<ZoneAssetKey> OwnedAssetKeys =>
        _ownedAssetKeys;

    /// <summary>
    /// Returns an isolated copy for staging to a target-test path. The caller
    /// must not route these bytes through Map Editor Save As.
    /// </summary>
    internal byte[] GetFastFileBytesCopy() =>
        _fastFileBytes.ToArray();
}

/// <summary>
/// Reuses the managed M7 package pipeline and requires byte-for-byte
/// determinism against the independently completed managed round trip.
/// </summary>
public static class MinimalMultiplayerMapPreAcceptanceArtifactBuilder
{
    public static
        MinimalMultiplayerMapPreAcceptanceFastFileArtifact Build(
            MpTerminalMinimalTargetProbeCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (!compilation.ManagedCompilationAccepted ||
            !compilation.ExternalMaterialIdentityResolved ||
            !compilation.ManagedIsolatedRoundTripAccepted ||
            !compilation.RuntimeSupport.TargetLaunchReady ||
            compilation.DefaultMpDependencyPlanAccepted ||
            compilation.TargetConsumerAccepted ||
            compilation.PersistenceAuthorized)
        {
            throw new ArgumentException(
                "A pre-acceptance artifact requires the coherent managed " +
                "mp_terminal compilation before any target authority.",
                nameof(compilation));
        }

        MinimalMultiplayerMapManagedRoundTripVerifier.ManagedPackage
            package =
                MinimalMultiplayerMapManagedRoundTripVerifier
                    .BuildPackage(
                        compilation.Candidate,
                        compilation.RuntimeSupport);
        RequireExactDependencyShape(compilation, package.Request);

        MinimalMultiplayerMapManagedRoundTripVerifier.ManagedPackage
            repeatedPackage =
                MinimalMultiplayerMapManagedRoundTripVerifier
                    .BuildPackage(
                        compilation.Candidate,
                        compilation.RuntimeSupport);
        RequireExactDependencyShape(
            compilation,
            repeatedPackage.Request);

        byte[] bytes = package.FastFileBytes.ToArray();
        if (!bytes.SequenceEqual(
                repeatedPackage.FastFileBytes) ||
            !package.DecodedZoneBytes.Span.SequenceEqual(
                repeatedPackage.DecodedZoneBytes.Span))
        {
            throw new InvalidDataException(
                "Repeated M7 target-artifact packaging was not " +
                "byte-for-byte deterministic.");
        }

        // Retail gameplay XModels intentionally retain external common-zone
        // Material/physics dependencies. Their semantic fresh-load gate is the
        // Default MP plan, not an isolated target-only database.
        if (!compilation.RuntimeSupport.GameplayModelSupportCompiled)
        {
            MinimalMultiplayerMapManagedRoundTripVerifier
                .RequireTargetArtifactFreshLoad(
                    compilation.Candidate,
                    compilation.RuntimeSupport,
                    package);
        }

        return new MinimalMultiplayerMapPreAcceptanceFastFileArtifact(
            compilation,
            bytes,
            package.DecodedZoneBytes.Length);
    }

    private static void RequireExactDependencyShape(
        MpTerminalMinimalTargetProbeCompilation compilation,
        ZoneLinkRequest request)
    {
        ZoneAssetKey materialKey =
            compilation.MaterialResolution.Dependency.AssetKey;
        ZoneAssetKey[] expectedOwnedKeys =
            compilation.Candidate.TopLevelRootTypes
                .Select(value =>
                    new ZoneAssetKey(
                        value,
                        compilation.Candidate.MapAssetName))
                .Concat(
                    compilation.RuntimeSupport.OwnedAssetKeys)
                .OrderBy(value => value.Type)
                .ThenBy(
                    value => value.LogicalName,
                    StringComparer.Ordinal)
                .ToArray();
        ZoneAssetKey[] actualOwnedKeys =
            request.Entries
                .Where(value =>
                    value.Intent ==
                        ZoneAssetReferenceIntent.Owned)
                .Select(value => value.Key)
                .OrderBy(value => value.Type)
                .ThenBy(
                    value => value.LogicalName,
                    StringComparer.Ordinal)
                .ToArray();
        MapGameplayModelSupportCompilation? gameplay =
            compilation.RuntimeSupport.GameplayModelSupport;
        bool exactScriptStringPolicy =
            gameplay is null
                ? !request.OutputPolicy
                    .PreserveImportedScriptStringOrder &&
                  request.ScriptStrings.Count == 0
                : request.OutputPolicy
                      .PreserveImportedScriptStringOrder &&
                  !request.OutputPolicy.PreserveImportedAssetOrder &&
                  request.ScriptStrings.SequenceEqual(
                      gameplay.ScriptStrings,
                      StringComparer.Ordinal);
        Dictionary<ZoneAssetKey, int> officialSourceOrder =
            gameplay is null
                ? []
                : gameplay.OwnedAssetKeys
                    .Zip(
                        gameplay.OwnedImportedOrders,
                        (key, importedOrder) => (
                            Key: key,
                            ImportedOrder: importedOrder))
                    .ToDictionary(
                        value => value.Key,
                        value => value.ImportedOrder);
        bool exactMixedSourceOrdering =
            gameplay is null
                ? !request.OutputPolicy.PreferImportedOrder &&
                  request.Entries.All(value =>
                      value.ImportedOrder is null)
                : request.OutputPolicy.PreferImportedOrder &&
                  !request.OutputPolicy.PreserveImportedAssetOrder &&
                  request.Entries.Count(value =>
                      value.ImportedOrder is not null) ==
                      officialSourceOrder.Count &&
                  request.Entries.All(value =>
                      value.ImportedOrder is not int order ||
                      officialSourceOrder.TryGetValue(
                          value.Key,
                          out int expectedOrder) &&
                      order == expectedOrder) &&
                  request.GetDeterministicLinkOrder()
                      .Where(value =>
                          officialSourceOrder.ContainsKey(
                              value.Key))
                      .Select(value => value.Key)
                      .SequenceEqual(
                          officialSourceOrder
                              .OrderBy(value => value.Value)
                              .Select(value => value.Key));
        ZoneAssetKey[] expectedStateOwnerMaterials =
            gameplay?.StateOwnerMaterials
                .Select(value => value.Key)
                .OrderBy(
                    value => value.LogicalName,
                    StringComparer.Ordinal)
                .ToArray() ??
            [];
        ZoneAssetKey[] actualStateOwnerMaterials = request.Entries
            .Where(value =>
                value.Intent == ZoneAssetReferenceIntent.Owned &&
                value.Key.Type == XAssetType.Material)
            .Select(value => value.Key)
            .OrderBy(
                value => value.LogicalName,
                StringComparer.Ordinal)
            .ToArray();
        bool exactStateOwnerMaterials =
            actualStateOwnerMaterials.SequenceEqual(
                expectedStateOwnerMaterials);
        if (request.Entries.Count != expectedOwnedKeys.Length ||
            !actualOwnedKeys.SequenceEqual(expectedOwnedKeys) ||
            !exactScriptStringPolicy ||
            !exactMixedSourceOrdering ||
            !exactStateOwnerMaterials)
        {
            throw new InvalidDataException(
                "The target artifact must retain the exact five compiled " +
                "map roots plus its target runtime-support rows, including " +
                "only the two exact gameplay Material state owners.");
        }

        ZoneAssetEntry gfx = request.Entries.Single(value =>
            value.Key.Type == XAssetType.GfxMap);
        ZoneAssetEntry col = request.Entries.Single(value =>
            value.Key.Type == XAssetType.ColMapMp);
        ZoneAssetDependency[] externalDependencies =
            request.Entries
                .SelectMany(value => value.Dependencies)
                .Where(value => value.IsExternal)
                .ToArray();
        ZoneAssetDependency[] gfxMaterialDependencies =
            gfx.Dependencies
                .Where(value =>
                    value.IsExternal &&
                    value.Target == materialKey)
                .ToArray();
        ZoneAssetKey mapEntsKey =
            new(
                XAssetType.MapEnts,
                compilation.Candidate.MapAssetName);
        ZoneAssetDependency[] colMapEntsDependencies =
            col.Dependencies
                .Where(value =>
                    value.IsExternal &&
                    value.Target == mapEntsKey)
                .ToArray();
        NestedXAssetBuildLink? mapEntsLink =
            compilation
                .Candidate
                .CollisionBuildData
                .References
                .MapEntsLink;
        bool exactInsertMapEntsOwnership =
            mapEntsLink is not null &&
            mapEntsLink.SourceForm ==
                NestedXAssetPointerSourceForm.Insert &&
            ReferenceEquals(
                mapEntsLink.Reference,
                compilation
                    .Candidate
                    .CollisionBuildData
                    .References
                    .MapEnts) &&
            ReferenceEquals(
                mapEntsLink.IncomingDefinition,
                compilation.Candidate.MapEntsBuildData) &&
            mapEntsLink.ImportedPackedRaw is null &&
            mapEntsLink.ImportedOwnerCellRaw is null;
        int gameplayExternalDependencyCount = 0;
        bool exactGameplayDependencies = gameplay is null ||
            gameplay.RuntimeDefinitionKeys
                .Zip(
                    gameplay.RuntimeDefinitionBuildData,
                    (key, buildData) => (
                        Key: key,
                        BuildData: buildData))
                .All(value =>
            {
                ZoneAssetEntry entry = request.Entries.Single(
                    candidate => candidate.Key == value.Key);
                IReadOnlyList<ZoneAssetDependency> expected =
                    ZoneAssetDependencyCollectorRegistry
                        .Default
                        .RequireCollect(value.BuildData);
                gameplayExternalDependencyCount +=
                    expected.Count(dependency =>
                        dependency.IsExternal);
                return entry.Dependencies.SequenceEqual(expected);
            });
        if (externalDependencies.Length !=
                2 + gameplayExternalDependencyCount ||
            gfxMaterialDependencies.Length != 1 ||
            colMapEntsDependencies.Length != 1 ||
            !exactGameplayDependencies ||
            !exactInsertMapEntsOwnership ||
            !string.Equals(
                gfxMaterialDependencies[0].OwnerPath,
                "references.surfaceMaterials[0]",
                StringComparison.Ordinal) ||
            !string.Equals(
                colMapEntsDependencies[0].OwnerPath,
                "references.mapEnts",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The target artifact must contain exactly one external " +
                "Material edge owned by GfxMap surface zero and one " +
                "ColMap-owned Insert MapEnts identity edge, plus only the " +
                "dependencies discovered from imported gameplay Material " +
                "owners and XModels. " +
                $"Observed external edges: [" +
                string.Join(
                    ", ",
                    externalDependencies.Select(value =>
                        $"{value.Target} at " +
                        $"{value.OwnerPath ?? "<unspecified>"}")) +
                "].");
        }
    }
}
