using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.FxMap;
using IW4.Assets.Assets.GameMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.RawFile;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Emitters.Packaging;
using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Render.Scheduling.Dpvs;
using IW4.Runtime.Database;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Authority produced only after the bounded M7 graph links, packages, and
/// reopens in an independent managed load session. It is not retail target or
/// Save As authority.
/// </summary>
public enum MinimalMultiplayerMapManagedRoundTripAuthority
{
    ManagedRoundTripEvidenceOnly = 0
}

/// <summary>
/// Immutable evidence from the dedicated M7 managed packaging boundary.
/// FastFile bytes remain internal until target dependency resolution creates
/// a separately authorized deployment candidate.
/// </summary>
public sealed class MinimalMultiplayerMapManagedRoundTripEvidence
{
    private readonly IReadOnlyList<XAssetType> _loadedTopLevelRootTypes;
    private readonly IReadOnlyList<ZoneAssetKey>
        _unresolvedExternalReferencePlaceholders;
    private readonly IReadOnlyList<string> _loaderWarnings;

    internal MinimalMultiplayerMapManagedRoundTripEvidence(
        MinimalMultiplayerMapTargetProbeCandidate sourceCandidate,
        byte[] fastFileBytes,
        int decodedZoneByteLength,
        IEnumerable<XAssetType> loadedTopLevelRootTypes,
        bool insertMapEntsMaterialized,
        int resolvedCameraCellIndex,
        IEnumerable<ZoneAssetKey>
            unresolvedExternalReferencePlaceholders,
        IEnumerable<string> loaderWarnings)
    {
        SourceCandidate = sourceCandidate ??
            throw new ArgumentNullException(nameof(sourceCandidate));
        ArgumentNullException.ThrowIfNull(fastFileBytes);
        if (fastFileBytes.Length == 0)
            throw new ArgumentException(
                "A managed map package cannot be empty.",
                nameof(fastFileBytes));
        if (decodedZoneByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decodedZoneByteLength));
        }
        ArgumentNullException.ThrowIfNull(loadedTopLevelRootTypes);
        ArgumentNullException.ThrowIfNull(
            unresolvedExternalReferencePlaceholders);
        ArgumentNullException.ThrowIfNull(loaderWarnings);

        byte[] fastFileCopy = fastFileBytes.ToArray();
        FastFileByteLength = fastFileCopy.Length;
        DecodedZoneByteLength = decodedZoneByteLength;
        _loadedTopLevelRootTypes =
            new ReadOnlyCollection<XAssetType>(
                loadedTopLevelRootTypes.ToArray());
        InsertMapEntsMaterialized = insertMapEntsMaterialized;
        if (resolvedCameraCellIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolvedCameraCellIndex));
        }
        ResolvedCameraCellIndex = resolvedCameraCellIndex;
        _unresolvedExternalReferencePlaceholders =
            new ReadOnlyCollection<ZoneAssetKey>(
                unresolvedExternalReferencePlaceholders.ToArray());
        _loaderWarnings =
            new ReadOnlyCollection<string>(loaderWarnings.ToArray());
        FastFileSha256 = Convert.ToHexString(
                SHA256.HashData(fastFileCopy))
            .ToLowerInvariant();
    }

    public MinimalMultiplayerMapManagedRoundTripAuthority Authority =>
        MinimalMultiplayerMapManagedRoundTripAuthority
            .ManagedRoundTripEvidenceOnly;

    public bool ManagedLinkAccepted => true;

    public bool ManagedPackageAccepted => true;

    public bool ManagedFreshLoadAccepted => true;

    public bool TargetConsumerAccepted => false;

    public bool PersistenceAuthorized => false;

    public MinimalMultiplayerMapTargetProbeCandidate SourceCandidate
    {
        get;
    }

    public int FastFileByteLength { get; }

    public int DecodedZoneByteLength { get; }

    public string FastFileSha256 { get; }

    public IReadOnlyList<XAssetType> LoadedTopLevelRootTypes =>
        _loadedTopLevelRootTypes;

    public bool InsertMapEntsMaterialized { get; }

    public int ResolvedCameraCellIndex { get; }

    public IReadOnlyList<ZoneAssetKey>
        UnresolvedExternalReferencePlaceholders =>
            _unresolvedExternalReferencePlaceholders;

    public IReadOnlyList<string> LoaderWarnings => _loaderWarnings;

}

/// <summary>
/// The only M7 seam allowed to move the probe-only six-root graph through the
/// managed linker and packager. It immediately performs a fresh load and
/// checks the reopened cross-root semantics before returning evidence.
/// </summary>
public static class MinimalMultiplayerMapManagedRoundTripVerifier
{
    public static MinimalMultiplayerMapManagedRoundTripEvidence Verify(
        MinimalMultiplayerMapTargetProbeCandidate candidate)
    {
        ManagedPackage package = BuildPackage(candidate);
        var loadSession = new DbLoadSession(
            selectedLanguageMask:
                GreenfieldLanguagePolicy.English
                    .SelectedLanguageMask);
        LoadedXZone loaded = loadSession.DB_LoadXZone(
            package.FastFileBytes,
            package.FastFileBytes.Length,
            "m7-minimal-multiplayer-managed-probe");
        ManagedFreshLoadValidation freshLoad =
            ValidateFreshLoad(
                candidate,
                package.Request,
                package.LinkedXFile,
                package.DecodedZoneBytes,
                loadSession,
                loaded,
                runtimeSupport: null,
                resolvedSurfaceMaterial: null);

        return new MinimalMultiplayerMapManagedRoundTripEvidence(
            candidate,
            package.FastFileBytes,
            package.DecodedZoneBytes.Length,
            loaded.XAssetList.Assets.Select(value => value.Type),
            insertMapEntsMaterialized: true,
            freshLoad.ResolvedCameraCellIndex,
            freshLoad.UnresolvedExternalReferencePlaceholders,
            loaded.Warnings);
    }

    internal static ManagedPackage BuildPackage(
        MinimalMultiplayerMapTargetProbeCandidate candidate) =>
        BuildPackageCore(candidate, runtimeSupport: null);

    internal static ManagedPackage BuildPackage(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        MinimalMultiplayerMapRuntimeSupportCompilation runtimeSupport)
    {
        ArgumentNullException.ThrowIfNull(runtimeSupport);
        MinimalMultiplayerMapRuntimeSupportCompiler.RequireValid(
            runtimeSupport);
        return BuildPackageCore(candidate, runtimeSupport);
    }

    /// <summary>
    /// Reopens the exact target artifact, including its separately owned
    /// runtime-support rows, in an isolated managed database and applies the
    /// same cross-root checks as the five-root compiler probe.
    /// </summary>
    internal static void RequireTargetArtifactFreshLoad(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        MinimalMultiplayerMapRuntimeSupportCompilation runtimeSupport,
        ManagedPackage package)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(runtimeSupport);
        ArgumentNullException.ThrowIfNull(package);
        MinimalMultiplayerMapRuntimeSupportCompiler.RequireValid(
            runtimeSupport);

        var loadSession = new DbLoadSession(
            selectedLanguageMask:
                GreenfieldLanguagePolicy.English
                    .SelectedLanguageMask);
        LoadedXZone loaded = loadSession.DB_LoadXZone(
            package.FastFileBytes,
            package.FastFileBytes.Length,
            "m7-minimal-multiplayer-target-artifact");
        _ = ValidateFreshLoad(
            candidate,
            package.Request,
            package.LinkedXFile,
            package.DecodedZoneBytes,
            loadSession,
            loaded,
            runtimeSupport,
            resolvedSurfaceMaterial: null);
    }

    /// <summary>
    /// Applies the shared artifact/root validation to a target reopened inside
    /// the Default-MP dependency lifecycle, where the surface Material has
    /// already resolved to its active full provider.
    /// </summary>
    internal static void RequireResolvedTargetGraph(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        MinimalMultiplayerMapRuntimeSupportCompilation runtimeSupport,
        DbLoadSession loadSession,
        LoadedXZone loaded,
        MaterialAsset resolvedSurfaceMaterial)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(runtimeSupport);
        ArgumentNullException.ThrowIfNull(loadSession);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(resolvedSurfaceMaterial);
        MinimalMultiplayerMapRuntimeSupportCompiler.RequireValid(
            runtimeSupport);
        if (runtimeSupport.GameplayModelSupport is
            { IncludesNestedDependencyDefinitions: true } gameplay)
        {
            MapGameplayImageStreamIntegrityVerifier
                .RequireReopenedManifest(
                    gameplay,
                    loadSession,
                    loaded);
        }

        var issues = new List<string>();
        XAssetType[] expectedTypes =
            candidate.TopLevelRootTypes
                .Concat(
                    runtimeSupport.OwnedAssetKeys.Select(value =>
                        value.Type))
                .Order()
                .ToArray();
        XAssetType[] actualTypes =
            loaded.XAssetList.Assets
                .Select(value => value.Type)
                .Order()
                .ToArray();
        if (loaded.XAssetList.AssetCount != expectedTypes.Length ||
            loaded.LoadedAssets.Count != expectedTypes.Length ||
            !actualTypes.SequenceEqual(expectedTypes) ||
            loaded.LoadedAssets.Any(value =>
                value.Materialization.Disposition !=
                    XAssetMaterializationDisposition.FullDefinition))
        {
            issues.Add(
                "resolved target does not contain the exact full-definition " +
                "top-level artifact type multiset");
        }
        if (loaded.Warnings.Count != 0)
        {
            issues.Add(
                "resolved target produced loader warnings: " +
                string.Join(", ", loaded.Warnings));
        }

        _ = ValidateLoadedGraph(
            candidate,
            runtimeSupport,
            loadSession,
            loaded,
            resolvedSurfaceMaterial,
            issues);
    }

    private static ManagedPackage BuildPackageCore(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        MinimalMultiplayerMapRuntimeSupportCompilation? runtimeSupport)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        MinimalMultiplayerMapTargetProbeValidator.RequireValid(candidate);
        if (!candidate.ManagedEmitterAccepted ||
            candidate.ManagedFreshLoadAccepted ||
            candidate.TargetConsumerAccepted ||
            candidate.PersistenceAuthorized)
        {
            throw new ArgumentException(
                "Managed packaging requires an emitter-accepted, " +
                "non-persistable M7 target-probe graph.",
                nameof(candidate));
        }

        NewZoneDocument document =
            CreateDocument(candidate, runtimeSupport);
        ZoneLinkRequest request =
            CreateTargetArtifactRequest(document, runtimeSupport);
        MapGameplayModelSupportCompilation? gameplay =
            runtimeSupport?.GameplayModelSupport;
        if (runtimeSupport is null)
        {
            RequireExpectedTopLevelGraph(candidate, request);
        }
        else
        {
            RequireExpectedTargetArtifactGraph(
                candidate,
                runtimeSupport,
                request);
        }

        ZoneLinkResult link = new ZoneLinker().Link(request);
        if (!link.Succeeded ||
            link.DecodedBytes is null ||
            link.XFile is null ||
            link.Errors.Count != 0)
        {
            throw new InvalidDataException(
                "The minimal M7 graph failed managed linking: " +
                Describe(link.Errors));
        }
        if (gameplay is { IncludesNestedDependencyDefinitions: true })
        {
            MapGameplayImageStreamIntegrityVerifier
                .RequireLinkedManifest(
                    gameplay,
                    request,
                    link);
        }

        FastFilePackagingResult package =
            new FastFilePackager().Package(
                link.DecodedBytes.Value,
                document.CreateEnvelope(
                    link.SelectedLanguageImageStreamEntries),
                document.ContainerPolicy.PackagingPolicy);
        if (!package.Succeeded ||
            package.Bytes is null ||
            package.Errors.Count != 0)
        {
            throw new InvalidDataException(
                "The minimal M7 graph failed managed packaging: " +
                Describe(
                    package.Errors.Select(value =>
                        $"{value.Code}: {value.Message}")));
        }

        return new ManagedPackage(
            request,
            link.XFile,
            link.DecodedBytes.Value,
            package.Bytes.Value.ToArray());
    }

    private static ZoneLinkRequest CreateTargetArtifactRequest(
        NewZoneDocument document,
        MinimalMultiplayerMapRuntimeSupportCompilation? runtimeSupport)
    {
        ZoneLinkRequest canonical = document.FreezeRequest();
        MapGameplayModelSupportCompilation? gameplay =
            runtimeSupport?.GameplayModelSupport;
        if (gameplay is null)
        {
            return canonical;
        }
        if (!gameplay.PreserveImportedScriptStringOrderRequired)
        {
            throw new InvalidDataException(
                "Imported gameplay XModels must retain their exact source " +
                "script-string slot order.");
        }

        Dictionary<ZoneAssetKey, int> officialSourceOrder =
            gameplay.OwnedAssetKeys
                .Zip(
                    gameplay.OwnedImportedOrders,
                    (key, importedOrder) => (
                        Key: key,
                        ImportedOrder: importedOrder))
                .ToDictionary(
                    value => value.Key,
                    value => value.ImportedOrder);
        ZoneAssetEntry[] entries = canonical.Entries
            .Select(entry =>
                new ZoneAssetEntry(
                    entry.EntryId,
                    entry.Key,
                    entry.Intent,
                    entry.BuildData,
                    entry.AliasTarget,
                    officialSourceOrder.TryGetValue(
                        entry.Key,
                        out int importedOrder)
                            ? importedOrder
                            : null,
                    entry.Dependencies,
                    entry.OpaqueHeader,
                    entry.OriginalSpelling))
            .ToArray();
        if (entries.Count(value =>
                value.ImportedOrder is not null) !=
                officialSourceOrder.Count ||
            entries.Any(value =>
                value.ImportedOrder is int order &&
                (!officialSourceOrder.TryGetValue(
                     value.Key,
                     out int expectedOrder) ||
                 order != expectedOrder)))
        {
            throw new InvalidDataException(
                "Only exact official gameplay-support rows may carry " +
                "Source.SerializedIndex ordering provenance.");
        }

        var outputPolicy = new ZoneLinkOutputPolicy(
            PreferImportedOrder: true,
            PreserveImportedScriptStringOrder: true,
            RequireDeterministicPackageMetadata: true,
            DeduplicateScriptStrings: false,
            PreserveImportedAssetOrder: false);
        var request = new ZoneLinkRequest(
            entries,
            gameplay.ScriptStrings,
            outputPolicy,
            canonical.LayoutPolicy);
        ZoneAssetKey[] expectedOfficialOrder = officialSourceOrder
            .OrderBy(value => value.Value)
            .Select(value => value.Key)
            .ToArray();
        ZoneAssetKey[] actualOfficialOrder = request
            .GetDeterministicLinkOrder()
            .Where(value =>
                officialSourceOrder.ContainsKey(value.Key))
            .Select(value => value.Key)
            .ToArray();
        if (!actualOfficialOrder.SequenceEqual(
                expectedOfficialOrder))
        {
            throw new InvalidDataException(
                "Dependency traversal changed the exact official gameplay " +
                "support Source.SerializedIndex order.");
        }
        return request;
    }

    private static NewZoneDocument CreateDocument(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        MinimalMultiplayerMapRuntimeSupportCompilation? runtimeSupport)
    {
        GreenfieldContainerPolicy? containerPolicy =
            runtimeSupport?.GameplayModelSupport is
                { IncludesNestedDependencyDefinitions: true }
                ? new GreenfieldContainerPolicy(
                    sidecarPolicy:
                        GreenfieldSidecarPolicy
                            .ReferenceExistingImagePackages)
                : null;
        var document = new NewZoneDocument(containerPolicy);
        foreach (var buildData in candidate.TopLevelBuildData)
        {
            document.AddOwned(
                new ZoneAssetKey(
                    buildData.AssetType,
                    candidate.MapAssetName),
                buildData);
        }
        if (runtimeSupport is not null)
        {
            foreach ((ZoneAssetKey key, IXAssetBuildData buildData) in
                     runtimeSupport.OwnedAssetKeys.Zip(
                         runtimeSupport.OwnedBuildData))
            {
                document.AddOwned(key, buildData);
            }
        }
        return document;
    }

    private static void RequireExpectedTopLevelGraph(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        ZoneLinkRequest request)
    {
        XAssetType[] actualTypes = request.Entries
            .Where(value =>
                value.Intent == ZoneAssetReferenceIntent.Owned)
            .Select(value => value.Key.Type)
            .Order()
            .ToArray();
        XAssetType[] expectedTypes = candidate.TopLevelRootTypes
            .Order()
            .ToArray();
        if (request.Entries.Count !=
                candidate.TopLevelRootCount ||
            !actualTypes.SequenceEqual(expectedTypes) ||
            request.Entries.Any(value =>
                !string.Equals(
                    value.Key.LogicalName,
                    candidate.MapAssetName,
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "The managed M7 package must contain exactly five owned " +
                "top-level map roots with one shared normalized identity.");
        }
    }

    private static void RequireExpectedTargetArtifactGraph(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        MinimalMultiplayerMapRuntimeSupportCompilation runtimeSupport,
        ZoneLinkRequest request)
    {
        ZoneAssetKey[] expectedKeys =
            candidate.TopLevelRootTypes
                .Select(value =>
                    new ZoneAssetKey(
                        value,
                        candidate.MapAssetName))
                .Concat(runtimeSupport.OwnedAssetKeys)
                .OrderBy(value => value.Type)
                .ThenBy(
                    value => value.LogicalName,
                    StringComparer.Ordinal)
                .ToArray();
        ZoneAssetKey[] actualKeys =
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
        if (request.Entries.Count != expectedKeys.Length ||
            !actualKeys.SequenceEqual(expectedKeys))
        {
            throw new InvalidDataException(
                "The managed target artifact must contain the exact five " +
                "compiled map roots plus its separately owned runtime " +
                "support rows.");
        }
    }

    private static ManagedFreshLoadValidation ValidateFreshLoad(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        ZoneLinkRequest request,
        XFile linkedXFile,
        ReadOnlyMemory<byte> decodedZoneBytes,
        DbLoadSession loadSession,
        LoadedXZone loaded,
        MinimalMultiplayerMapRuntimeSupportCompilation? runtimeSupport,
        MaterialAsset? resolvedSurfaceMaterial)
    {
        var issues = new List<string>();
        XAssetType[] expectedLinkOrder = request
            .GetDeterministicLinkOrder()
            .Select(value => value.Key.Type)
            .ToArray();
        XAssetType[] loadedTypes = loaded.XAssetList.Assets
            .Select(value => value.Type)
            .ToArray();
        if (loaded.XAssetList.AssetCount != request.Entries.Count ||
            loaded.LoadedAssets.Count != request.Entries.Count ||
            !loadedTypes.SequenceEqual(expectedLinkOrder) ||
            loaded.LoadedAssets.Any(value =>
                value.Materialization.Disposition !=
                    XAssetMaterializationDisposition.FullDefinition))
        {
            issues.Add(
                "fresh XAssetList does not contain every full-definition " +
                "top-level artifact row in linker order");
        }
        if (!loaded.LoadedAssets
                .Select(value => value.Index)
                .SequenceEqual(
                    loaded.XAssetList.Assets.Select(value => value.Index)))
        {
            issues.Add(
                "fresh loaded-asset results do not align with their " +
                "serialized XAsset rows");
        }
        if (loadSession.LoadHistory.Count != 1 ||
            loadSession.ActiveZones.Count != 1 ||
            !ReferenceEquals(loadSession.LoadHistory[0], loaded))
        {
            issues.Add(
                "fresh-load verification did not remain isolated to one " +
                "managed zone session");
        }
        if (loaded.XFile.Size != linkedXFile.Size ||
            loaded.XFile.ExternalSize != linkedXFile.ExternalSize ||
            !loaded.XFile.BlockSizes.SequenceEqual(
                linkedXFile.BlockSizes))
        {
            issues.Add(
                "fresh XFile header differs from the linker-produced " +
                "allocation contract");
        }
        if (!loaded.ZoneBytes.SequenceEqual(decodedZoneBytes.Span))
        {
            issues.Add(
                "fresh loader retained zone bytes that differ from the " +
                "linker output");
        }
        if (loaded.Warnings.Count != 0)
        {
            issues.Add(
                "fresh loader produced warnings: " +
                string.Join(", ", loaded.Warnings));
        }
        if (runtimeSupport?.GameplayModelSupport is
            { IncludesNestedDependencyDefinitions: true } gameplay)
        {
            MapGameplayImageStreamIntegrityVerifier
                .RequireReopenedManifest(
                    gameplay,
                    loadSession,
                    loaded);
        }

        return ValidateLoadedGraph(
            candidate,
            runtimeSupport,
            loadSession,
            loaded,
            resolvedSurfaceMaterial,
            issues);
    }

    private static ManagedFreshLoadValidation ValidateLoadedGraph(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        MinimalMultiplayerMapRuntimeSupportCompilation? runtimeSupport,
        DbLoadSession loadSession,
        LoadedXZone loaded,
        MaterialAsset? resolvedSurfaceMaterial,
        List<string> issues)
    {
        GfxWorldAsset? gfx = Single<GfxWorldAsset>(loaded, issues);
        ClipMapAsset? col = Single<ClipMapAsset>(loaded, issues);
        ComWorldAsset? com = Single<ComWorldAsset>(loaded, issues);
        FxWorldAsset? fx = Single<FxWorldAsset>(loaded, issues);
        GameWorldMpAsset? game =
            Single<GameWorldMpAsset>(loaded, issues);
        if (gfx is null ||
            col is null ||
            com is null ||
            fx is null ||
            game is null)
        {
            ThrowIfIssues(issues);
            throw new InvalidDataException(
                "The managed M7 fresh-load validator reached an " +
                "unreachable incomplete-root state.");
        }

        if (runtimeSupport is not null)
        {
            ValidateRuntimeSupport(
                loaded,
                runtimeSupport,
                issues);
        }

        string expectedName = candidate.MapAssetName;
        if (new[]
            {
                gfx.Name,
                col.Name,
                com.Name,
                fx.Name,
                game.Name
            }
            .Any(value =>
                !string.Equals(
                    value,
                    expectedName,
                    StringComparison.Ordinal)))
        {
            issues.Add(
                "one or more reopened root names differ from the candidate");
        }
        if (gfx.Checksum != candidate.PrimaryChecksum ||
            col.Checksum != candidate.PrimaryChecksum ||
            gfx.Checksum != col.Checksum)
        {
            issues.Add(
                "reopened GfxMap and ColMapMp checksums are not synchronized");
        }

        MapEntsAsset? incomingMapEnts =
            col.MapEntsIncomingDefinition;
        MapEntsAsset? activeMapEnts = col.MapEnts;
        Stage? stage =
            incomingMapEnts is
            {
                StageCount: 1,
                Stages.Count: 1
            }
                ? incomingMapEnts.Stages[0]
                : null;
        byte[] expectedEntityBytes =
            candidate.MapEntsBuildData.GetEntityStringBytesCopy();
        bool mapEntsOwnsFullDefinition =
            activeMapEnts is not null &&
            loadSession.AssetPool.TryResolve<MapEntsAsset>(
                XAssetType.MapEnts,
                expectedName,
                out MapEntsAsset? pooledMapEnts) &&
            ReferenceEquals(pooledMapEnts, activeMapEnts) &&
            loadSession.AssetPool.TryGetEntry(
                activeMapEnts,
                out var mapEntsPoolEntry) &&
            !mapEntsPoolEntry.IsReferencePlaceholder;
        if (incomingMapEnts is null ||
            activeMapEnts is null ||
            !ReferenceEquals(incomingMapEnts, activeMapEnts) ||
            !mapEntsOwnsFullDefinition ||
            col.MapEntsPointer.Type != PointerType.Insert ||
            !string.Equals(
                incomingMapEnts.Name,
                expectedName,
                StringComparison.Ordinal) ||
            !string.Equals(
                activeMapEnts.Name,
                expectedName,
                StringComparison.Ordinal) ||
            !incomingMapEnts.EntityStringBytes
                .SequenceEqual(expectedEntityBytes) ||
            !activeMapEnts.EntityStringBytes
                .SequenceEqual(expectedEntityBytes) ||
            !MapEntsSyntaxParser
                .Parse(
                    incomingMapEnts.EntityStringBytes
                        .ToArray())
                .CanEdit ||
            incomingMapEnts.Trigger.Count != 0 ||
            incomingMapEnts.Trigger.HullCount != 0 ||
            incomingMapEnts.Trigger.SlabCount != 0 ||
            stage is null ||
            !string.Equals(
                stage.StageName,
                "stage 0",
                StringComparison.Ordinal) ||
            stage.Origin.X != 0 ||
            stage.Origin.Y != 0 ||
            stage.Origin.Z != 0 ||
            stage.TriggerIndex != 0x0400 ||
            stage.SunPrimaryLightIndex != 1 ||
            stage.Pad13 != 0)
        {
            issues.Add(
                "insert-owned MapEnts did not reopen as the exact active and " +
                "incoming three-entity definition with its native stage-0 " +
                "sentinel");
        }

        if (gfx.PrimaryLightCount != 1 ||
            com.PrimaryLightCount != 1 ||
            com.PrimaryLights.Count != 1 ||
            com.PrimaryLights[0].Type != 0 ||
            gfx.Dpvs.LitSurfsBegin != 0 ||
            gfx.Dpvs.LitSurfsEnd !=
                checked((uint)gfx.SurfaceCount) ||
            gfx.Dpvs.VisibilityCounts.Count != 8 ||
            gfx.Dpvs.VisibilityCounts.Take(6)
                .Any(value =>
                    value != checked((uint)gfx.SurfaceCount)))
        {
            issues.Add(
                "reopened M5 sentinel or all-opaque surface partition " +
                "diverged");
        }

        if (!CollisionStructuralReachabilityValidator
                .Assess(col)
                .IsValid)
        {
            issues.Add(
                "reopened collision graph is structurally unreachable");
        }
        GGlassData? gameGlass = game.GlassData;
        if (fx.GlassSystem.DefCount != 0 ||
            fx.GlassSystem.InitPieceCount != 0 ||
            fx.GlassSystem.Defs.Count != 0 ||
            fx.GlassSystem.InitPieceStates.Count != 0 ||
            game.GlassDataPointer.Raw == 0 ||
            gameGlass is null ||
            gameGlass.PieceCount != 0 ||
            gameGlass.GlassPiecesPointer.Raw != 0 ||
            gameGlass.GlassPieces.Count != 0 ||
            gameGlass.DamageToWeaken != 0 ||
            gameGlass.DamageToDestroy != 0 ||
            gameGlass.GlassNameCount != 0 ||
            gameGlass.GlassNamesPointer.Raw != 0 ||
            gameGlass.GlassNames.Count != 0 ||
            gameGlass.Pad14To7F.Count != 0x6c ||
            gameGlass.Pad14To7F.Any(value => value != 0))
        {
            issues.Add(
                "reopened FxMap/GameMapMp no longer satisfy the runtime-safe " +
                "empty-glass profile");
        }

        ZoneAssetKey[] actualExternalPlaceholders = [];
        if (resolvedSurfaceMaterial is null)
        {
            ZoneAssetKey[] expectedExternalPlaceholders =
                candidate.GfxWorldBuildData.References.SurfaceMaterials
                    .Where(value => value is not null)
                    .Select(value =>
                        ZoneAssetKey.FromWireName(
                            XAssetType.Material,
                            value!.OriginalSerializedName))
                    .Distinct()
                    .OrderBy(value => value.Type)
                    .ThenBy(
                        value => value.LogicalName,
                        StringComparer.Ordinal)
                    .ToArray();
            actualExternalPlaceholders =
                loadSession.AssetPool.Entries
                    .Where(value => value.IsReferencePlaceholder)
                    .Select(value =>
                        new ZoneAssetKey(
                            value.AssetType,
                            value.Name))
                    .Distinct()
                    .OrderBy(value => value.Type)
                    .ThenBy(
                        value => value.LogicalName,
                        StringComparer.Ordinal)
                    .ToArray();
            if (!actualExternalPlaceholders.SequenceEqual(
                    expectedExternalPlaceholders) ||
                gfx.Dpvs.Surfaces.Any(surface =>
                    surface.Material is null ||
                    !loadSession.AssetPool.TryGetEntry(
                        surface.Material,
                        out var entry) ||
                    !entry.IsReferencePlaceholder))
            {
                issues.Add(
                    "reopened surface materials do not resolve exactly " +
                    "through the expected isolated-session external " +
                    "placeholders");
            }
        }
        else if (gfx.Dpvs.Surfaces.Any(surface =>
                     !ReferenceEquals(
                         surface.Material,
                         resolvedSurfaceMaterial)))
        {
            issues.Add(
                "reopened surface materials do not resolve to the expected " +
                "active dependency provider");
        }

        Vector3 spawnOrigin =
            RequireSingleEntityTransform(
                activeMapEnts,
                "mp_dm_spawn",
                issues);
        _ = RequireSingleEntityTransform(
            activeMapEnts,
            "mp_global_intermission",
            issues);
        MapRenderWorldDpvsCameraCellResolutionResult cameraCell =
            MapRenderWorldDpvsCameraCellResolver.Resolve(
                gfx,
                spawnOrigin);
        if (!cameraCell.IsSuccess ||
            cameraCell.CellIndex != 0)
        {
            issues.Add(
                "the reopened mp_dm_spawn origin does not resolve to the " +
                "bounded single camera cell");
        }

        ThrowIfIssues(issues);
        return new ManagedFreshLoadValidation(
            cameraCell.CellIndex!.Value,
            actualExternalPlaceholders);
    }

    private static void ValidateRuntimeSupport(
        LoadedXZone loaded,
        MinimalMultiplayerMapRuntimeSupportCompilation support,
        ICollection<string> issues)
    {
        MinimalMultiplayerMapRuntimeSupportCompiler.RequireValid(support);

        RawFileAsset[] diagnosticMarkers =
            loaded.LoadedAssets
                .Select(value => value.Asset)
                .OfType<RawFileAsset>()
                .Where(value =>
                    string.Equals(
                        value.Name,
                        support.DiagnosticMarker.OriginalName,
                        StringComparison.Ordinal))
                .ToArray();
        RawFileAsset[] levelScripts =
            loaded.LoadedAssets
                .Select(value => value.Asset)
                .OfType<RawFileAsset>()
                .Where(value =>
                    string.Equals(
                        value.Name,
                        support.LevelScript.OriginalName,
                        StringComparison.Ordinal))
                .ToArray();
        StringTableAsset[] constantConfigStringTables =
            loaded.LoadedAssets
                .Select(value => value.Asset)
                .OfType<StringTableAsset>()
                .Where(value =>
                    string.Equals(
                        value.Name,
                        support.ConstantConfigStringTable.Name,
                        StringComparison.Ordinal))
                .ToArray();

        byte[] markerPayload =
            support.DiagnosticMarker.GetSerializedPayloadCopy();
        bool exactDiagnosticMarker =
            diagnosticMarkers.Length == 1 &&
            diagnosticMarkers[0].CompressedLen ==
                support.DiagnosticMarker.CompressedLength &&
            diagnosticMarkers[0].Len ==
                support.DiagnosticMarker.UncompressedLength &&
            diagnosticMarkers[0].Buffer is { } markerBytes &&
            markerBytes.SequenceEqual(markerPayload);

        byte[] levelScriptPayload =
            support.LevelScript.GetSerializedPayloadCopy();
        bool exactLevelScript =
            levelScripts.Length == 1 &&
            levelScripts[0].CompressedLen ==
                support.LevelScript.CompressedLength &&
            levelScripts[0].Len ==
                support.LevelScript.UncompressedLength &&
            levelScripts[0].Buffer is { } levelScriptBytes &&
            levelScriptBytes.SequenceEqual(levelScriptPayload);

        var expectedTable = support.ConstantConfigStringTable;
        StringTableAsset? table =
            constantConfigStringTables.Length == 1
                ? constantConfigStringTables[0]
                : null;
        bool exactConstantConfigStringTable =
            table is not null &&
            table.ColumnCount == expectedTable.ColumnCount &&
            table.RowCount == expectedTable.RowCount &&
            table.Cells.Count == expectedTable.Cells.Count &&
            (expectedTable.Cells.Count == 0
                ? table.CellsPointer.Raw == 0
                : table.CellsPointer.Raw != 0) &&
            table.Cells
                .Zip(expectedTable.Cells)
                .All(pair =>
                    string.Equals(
                        pair.First.String,
                        pair.Second.Value,
                        StringComparison.Ordinal) &&
                    pair.First.Hash == pair.Second.Hash);
        MapGameplayModelSupportCompilation? gameplay =
            support.GameplayModelSupport;
        ZoneAssetKey[] expectedGameplayModels =
            gameplay?.OwnedAssets
                .Select(value => value.Key)
                .OrderBy(value => value.LogicalName, StringComparer.Ordinal)
                .ToArray() ??
            [];
        ZoneAssetKey[] actualGameplayModels =
            loaded.LoadedAssets
                .Select(value => value.Asset)
                .OfType<XModelAsset>()
                .Where(value => value.Name is not null)
                .Select(value =>
                    new ZoneAssetKey(
                        XAssetType.XModel,
                        value.Name!))
                .OrderBy(value => value.LogicalName, StringComparer.Ordinal)
                .ToArray();
        bool exactGameplayModels =
            actualGameplayModels.SequenceEqual(expectedGameplayModels);
        ZoneAssetKey[] expectedStateOwnerMaterials =
            gameplay?.StateOwnerMaterials
                .Select(value => value.Key)
                .OrderBy(value => value.LogicalName, StringComparer.Ordinal)
                .ToArray() ??
            [];
        ZoneAssetKey[] actualStateOwnerMaterials =
            loaded.LoadedAssets
                .Select(value => value.Asset)
                .OfType<MaterialAsset>()
                .Where(value => value.Info.Name is not null)
                .Select(value =>
                    new ZoneAssetKey(
                        XAssetType.Material,
                        value.Info.Name!))
                .OrderBy(value => value.LogicalName, StringComparer.Ordinal)
                .ToArray();
        bool exactStateOwnerMaterials =
            actualStateOwnerMaterials.SequenceEqual(
                expectedStateOwnerMaterials);

        if (!exactDiagnosticMarker ||
            !exactLevelScript ||
            !exactConstantConfigStringTable ||
            !exactGameplayModels ||
            !exactStateOwnerMaterials)
        {
            issues.Add(
                "runtime support did not reopen as the exact diagnostic " +
                "marker, minimal level script, and split-screen DM " +
                "constant-configstring/gameplay Material-owner/XModel " +
                "closure");
        }
    }

    private static Vector3 RequireSingleEntityTransform(
        MapEntsAsset? mapEnts,
        string classname,
        ICollection<string> issues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classname);
        if (mapEnts is null)
        {
            issues.Add(
                $"the reopened MapEnts has no {classname} transform");
            return Vector3.Zero;
        }

        MapEntsSyntaxDocument syntax =
            MapEntsSyntaxParser.Parse(
                mapEnts.EntityStringBytes.ToArray());
        MapEntsSyntaxEntity[] matches =
            syntax.Entities
                .Where(entity =>
                    entity.Properties.Any(property =>
                        string.Equals(
                            property.Key,
                            "classname",
                            StringComparison.Ordinal) &&
                        string.Equals(
                            property.Value,
                            classname,
                            StringComparison.Ordinal)))
                .ToArray();
        if (matches.Length != 1)
        {
            issues.Add(
                $"the reopened MapEnts must contain exactly one " +
                $"{classname} entity");
            return Vector3.Zero;
        }

        Vector3 origin = RequireFiniteVectorProperty(
            matches[0],
            classname,
            "origin",
            issues);
        _ = RequireFiniteVectorProperty(
            matches[0],
            classname,
            "angles",
            issues);
        return origin;
    }

    private static Vector3 RequireFiniteVectorProperty(
        MapEntsSyntaxEntity entity,
        string classname,
        string propertyName,
        ICollection<string> issues)
    {
        string[] values = entity.Properties
            .Where(property =>
                string.Equals(
                    property.Key,
                    propertyName,
                    StringComparison.Ordinal))
            .Select(property => property.Value)
            .ToArray();
        string? source = values.Length == 1 ? values[0] : null;
        string[] components =
            source?.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries) ??
            [];
        if (components.Length != 3 ||
            !float.TryParse(
                components[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float x) ||
            !float.TryParse(
                components[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float y) ||
            !float.TryParse(
                components[2],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float z) ||
            !float.IsFinite(x) ||
            !float.IsFinite(y) ||
            !float.IsFinite(z))
        {
            issues.Add(
                $"the reopened {classname} must contain exactly one finite " +
                $"three-component {propertyName}");
            return Vector3.Zero;
        }
        return new Vector3(x, y, z);
    }

    private static T? Single<T>(
        LoadedXZone loaded,
        ICollection<string> issues)
        where T : class
    {
        T[] matches = loaded.LoadedAssets
            .Select(value => value.Asset)
            .OfType<T>()
            .ToArray();
        if (matches.Length == 1)
            return matches[0];

        issues.Add(
            $"fresh load produced {matches.Length} {typeof(T).Name} roots");
        return null;
    }

    private static void ThrowIfIssues(IReadOnlyCollection<string> issues)
    {
        if (issues.Count != 0)
        {
            throw new InvalidDataException(
                "The minimal M7 managed package failed fresh-load " +
                "validation: " +
                string.Join("; ", issues));
        }
    }

    private static string Describe(IEnumerable<string> values)
    {
        string[] messages = values.ToArray();
        return messages.Length == 0
            ? "<no diagnostics>"
            : string.Join("; ", messages);
    }

    private sealed record ManagedFreshLoadValidation(
        int ResolvedCameraCellIndex,
        IReadOnlyList<ZoneAssetKey>
            UnresolvedExternalReferencePlaceholders);

    internal sealed class ManagedPackage
    {
        internal ManagedPackage(
            ZoneLinkRequest request,
            XFile linkedXFile,
            ReadOnlyMemory<byte> decodedZoneBytes,
            byte[] fastFileBytes)
        {
            Request = request ??
                throw new ArgumentNullException(nameof(request));
            LinkedXFile = linkedXFile ??
                throw new ArgumentNullException(nameof(linkedXFile));
            if (decodedZoneBytes.IsEmpty)
            {
                throw new ArgumentException(
                    "A managed package requires decoded zone bytes.",
                    nameof(decodedZoneBytes));
            }
            ArgumentNullException.ThrowIfNull(fastFileBytes);
            if (fastFileBytes.Length == 0)
            {
                throw new ArgumentException(
                    "A managed package requires FastFile bytes.",
                    nameof(fastFileBytes));
            }

            DecodedZoneBytes = decodedZoneBytes.ToArray();
            FastFileBytes = fastFileBytes.ToArray();
        }

        internal ZoneLinkRequest Request { get; }

        internal XFile LinkedXFile { get; }

        internal ReadOnlyMemory<byte> DecodedZoneBytes { get; }

        internal byte[] FastFileBytes { get; }
    }
}
