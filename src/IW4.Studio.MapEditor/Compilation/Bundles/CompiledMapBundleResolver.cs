using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.Bundles;

public interface ICompiledMapBundleResolver
{
    MapBundleResolutionResult Resolve(
        FastFileWorkspace workspace,
        long sourceEditingSessionRevision = 0,
        CancellationToken cancellationToken = default);
}

public sealed class CompiledMapBundleResolver : ICompiledMapBundleResolver
{
    private static readonly XAssetType[] RootMapTypes =
    [
        XAssetType.GfxMap,
        XAssetType.ColMapSp,
        XAssetType.ColMapMp,
        XAssetType.ComMap,
        XAssetType.MapEnts,
        XAssetType.FxMap,
        XAssetType.GameMapMp
    ];

    private readonly AssetAuthoringAdapterRegistry _adapters;
    private readonly ZoneAssetDependencyCollectorRegistry _dependencies;

    public CompiledMapBundleResolver(
        AssetAuthoringAdapterRegistry? adapters = null,
        ZoneAssetDependencyCollectorRegistry? dependencies = null)
    {
        _adapters = adapters ?? AssetAuthoringAdapterRegistry.CreateDefault();
        _dependencies =
            dependencies ?? ZoneAssetDependencyCollectorRegistry.Default;
    }

    public MapBundleResolutionResult Resolve(
        FastFileWorkspace workspace,
        long sourceEditingSessionRevision = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (sourceEditingSessionRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceEditingSessionRevision));
        }
        cancellationToken.ThrowIfCancellationRequested();

        WorkspaceAssetCatalogEntry[] gfxAnchors = workspace.AssetCatalog.TargetEntries
            .Where(IsOwnedDefinition)
            .Where(entry => entry.AssetType == XAssetType.GfxMap)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (gfxAnchors.Length == 0)
        {
            return Failed(
                MapBundleResolutionStatus.NotAMap,
                "The target fastfile has no owned GfxMap definition.");
        }

        WorkspaceAssetCatalogEntry? anchor = SelectAnchor(
            workspace.Document.TargetZone.LogicalZoneName,
            gfxAnchors);
        if (anchor is null)
        {
            return Failed(
                MapBundleResolutionStatus.Ambiguous,
                $"The target fastfile contains {gfxAnchors.Length} owned GfxMap definitions and no unique map identity could be selected.");
        }

        string originalMapName = RequireName(anchor);
        string mapIdentity = XAssetStableIdentity.NormalizeLookupName(
            originalMapName);
        var diagnostics = new List<string>();
        var baselines = new List<CompiledMapAssetBaseline>();

        foreach (XAssetType type in RootMapTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceAssetCatalogEntry[] matches = workspace.AssetCatalog.TargetEntries
                .Where(IsOwnedDefinition)
                .Where(entry => entry.AssetType == type)
                .Where(entry => string.Equals(
                    entry.NormalizedName,
                    mapIdentity,
                    StringComparison.Ordinal))
                .ToArray();
            if (matches.Length > 1)
            {
                return Failed(
                    MapBundleResolutionStatus.Ambiguous,
                    $"Map '{mapIdentity}' has {matches.Length} owned {type} definitions.");
            }

            if (matches.Length == 1)
            {
                CompiledMapAssetBaseline baseline;
                try
                {
                    baseline = CaptureTopLevel(
                        matches[0],
                        mapIdentity,
                        cancellationToken);
                }
                catch (Exception exception) when (
                    exception is not (
                        OutOfMemoryException or
                        OperationCanceledException))
                {
                    return Failed(
                        MapBundleResolutionStatus.Invalid,
                        $"Could not detach {type} authority for '{mapIdentity}': {exception.Message}");
                }
                baselines.Add(baseline);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        if (!baselines.Any(value => value.Descriptor.Kind == MapAssetKind.GfxMap))
        {
            return Failed(
                MapBundleResolutionStatus.Invalid,
                $"Map '{mapIdentity}' lost its GfxMap authority during capture.");
        }

        CompiledMapAssetBaseline[] collisionBaselines = baselines
            .Where(value =>
                value.Descriptor.Kind is
                    MapAssetKind.ColMapSp or
                    MapAssetKind.ColMapMp)
            .ToArray();
        if (collisionBaselines.Length > 1)
        {
            return Failed(
                MapBundleResolutionStatus.Ambiguous,
                $"Map '{mapIdentity}' contains both ColMapSp and ColMapMp authorities.");
        }
        CompiledMapAssetBaseline? clip = collisionBaselines.SingleOrDefault();
        if (clip is null)
        {
            diagnostics.Add(
                $"Map '{mapIdentity}' has no owned ColMapSp/ColMapMp definition; collision and nested MapEnts are unavailable.");
        }
        else if (clip.Source is ClipMapBuildData clipData)
        {
            MapBundleResolutionResult? mapEntResult = ResolveNestedMapEnts(
                mapIdentity,
                clip,
                clipData,
                baselines,
                diagnostics,
                cancellationToken);
            if (mapEntResult is not null)
                return mapEntResult;
        }

        foreach (MapAssetKind expected in new[]
                 {
                     MapAssetKind.ComMap,
                     MapAssetKind.MapEnts,
                     MapAssetKind.FxMap,
                     MapAssetKind.GameMapMp
                 })
        {
            if (!baselines.Any(value => value.Descriptor.Kind == expected))
            {
                diagnostics.Add(
                    $"Map '{mapIdentity}' has no authoritative {expected} source.");
            }
        }

        CompiledMapAssetDescriptor[] descriptors = baselines
            .Select(value => value.Descriptor)
            .ToArray();
        string bundleDigest = CompiledMapBaselineDigest.ComputeBundle(
            mapIdentity,
            descriptors,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<CompiledMapDependency> dependencies;
        try
        {
            dependencies = ResolveDependencies(
                workspace,
                baselines,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is not (
                OutOfMemoryException or
                OperationCanceledException))
        {
            return Failed(
                MapBundleResolutionStatus.Invalid,
                $"Could not resolve dependencies for '{mapIdentity}': {exception.Message}");
        }

        var bundle = new CompiledMapBundle(
            mapIdentity,
            originalMapName,
            workspace.Runtime.AssetPool.Revision,
            bundleDigest,
            baselines,
            dependencies,
            sourceEditingSessionRevision);
        return new MapBundleResolutionResult(
            MapBundleResolutionStatus.Ready,
            bundle,
            diagnostics);
    }

    private CompiledMapAssetBaseline CaptureTopLevel(
        WorkspaceAssetCatalogEntry entry,
        string mapIdentity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TargetZoneRowSource row = entry.TargetRow
            ?? throw new InvalidDataException(
                "An owned map definition has no target source row.");
        IAssetAuthoringAdapter adapter = _adapters.RequireAdapter(
            row.SerializedType);
        object snapshot = adapter.ImportAuthoredSnapshot(row);
        cancellationToken.ThrowIfCancellationRequested();
        object draft = adapter.CreateDraft(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        object exported = adapter.ExportBuildData(draft);
        cancellationToken.ThrowIfCancellationRequested();
        if (exported is not IXAssetBuildData source ||
            source.AssetType != entry.AssetType)
        {
            throw new InvalidDataException(
                $"Map adapter for {entry.AssetType} produced unsupported detached build data '{exported.GetType().Name}'.");
        }

        string assetName = RequireName(entry);
        ValidateDetachedName(source, assetName);
        MapAssetKind kind = Kind(entry.AssetType);
        var seed = new CompiledMapAssetDescriptorSeed(
            kind,
            entry.AssetType,
            assetName,
            row.Identity,
            IsNested: false,
            SourcePath: "$");
        string digest = CompiledMapBaselineDigest.ComputeAsset(
            mapIdentity,
            seed,
            source,
            cancellationToken);
        var descriptor = new CompiledMapAssetDescriptor(
            seed.Kind,
            seed.SerializedType,
            seed.AssetName,
            seed.OwnerRow,
            seed.IsNested,
            seed.SourcePath,
            digest);
        return new CompiledMapAssetBaseline(
            descriptor,
            source,
            source as IXAssetBuildData);
    }

    private static MapBundleResolutionResult? ResolveNestedMapEnts(
        string mapIdentity,
        CompiledMapAssetBaseline clip,
        ClipMapBuildData clipData,
        ICollection<CompiledMapAssetBaseline> baselines,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool hasTopLevel = baselines.Any(value =>
            value.Descriptor.Kind == MapAssetKind.MapEnts);
        SymbolicXAssetReference? reference = clipData.References.MapEnts;
        NestedXAssetBuildLink? link = clipData.References.MapEntsLink;
        if (reference is null && link is null)
            return null;
        if (reference is null || link is null)
        {
            return Failed(
                MapBundleResolutionStatus.Invalid,
                "ColMap MapEnts reference and source-link provenance are inconsistent.");
        }

        string nestedIdentity = XAssetStableIdentity.NormalizeLookupName(
            reference.OriginalSerializedName);
        if (!string.Equals(nestedIdentity, mapIdentity, StringComparison.Ordinal))
        {
            return Failed(
                MapBundleResolutionStatus.Invalid,
                $"ColMap references MapEnts '{nestedIdentity}', not bundle identity '{mapIdentity}'.");
        }

        if (link.SourceForm == NestedXAssetPointerSourceForm.PackedAlias)
        {
            diagnostics.Add(
                hasTopLevel
                    ? "The ColMap MapEnts source is a packed alias whose exact target-row provenance is not retained. The same-name top-level MapEnts is imported independently, but the cross-asset join remains unresolved."
                    : "The ColMap MapEnts source is a packed alias with no owned top-level MapEnts definition; entity authority remains unresolved.");
            return null;
        }

        if (link.SourceForm is not (
                NestedXAssetPointerSourceForm.Inline or
                NestedXAssetPointerSourceForm.Insert) ||
            link.IncomingDefinition is not IMapEntsBuildData incoming)
        {
            return Failed(
                MapBundleResolutionStatus.Invalid,
                $"ColMap MapEnts {link.SourceForm} source has no detached incoming definition.");
        }

        if (hasTopLevel)
        {
            return Failed(
                MapBundleResolutionStatus.Ambiguous,
                "The map contains both top-level and nested MapEnts definitions; equivalence has not been proven.");
        }

        string assetName = incoming.Name ?? reference.OriginalSerializedName;
        var seed = new CompiledMapAssetDescriptorSeed(
            MapAssetKind.MapEnts,
            XAssetType.MapEnts,
            assetName,
            clip.Descriptor.OwnerRow,
            IsNested: true,
            SourcePath: "references.mapEntsLink.incomingDefinition");
        string digest = CompiledMapBaselineDigest.ComputeAsset(
            mapIdentity,
            seed,
            incoming,
            cancellationToken);
        baselines.Add(new CompiledMapAssetBaseline(
            new CompiledMapAssetDescriptor(
                seed.Kind,
                seed.SerializedType,
                seed.AssetName,
                seed.OwnerRow,
                seed.IsNested,
                seed.SourcePath,
                digest),
            incoming,
            DependencySource: null));
        return null;
    }

    private IReadOnlyList<CompiledMapDependency> ResolveDependencies(
        FastFileWorkspace workspace,
        IEnumerable<CompiledMapAssetBaseline> baselines,
        CancellationToken cancellationToken)
    {
        var result = new List<CompiledMapDependency>();
        var available = new Dictionary<
            (XAssetType AssetType, string Name),
            WorkspaceAssetCatalogEntry>();
        foreach (WorkspaceAssetCatalogEntry entry in workspace.AssetCatalog.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.ContentSource == WorkspaceAssetContentSource.Unavailable)
                continue;
            string? normalizedName = entry.NormalizedName;
            if (string.IsNullOrWhiteSpace(normalizedName))
                continue;

            available.TryAdd(
                (entry.AssetType, normalizedName),
                entry);
        }

        foreach (CompiledMapAssetBaseline baseline in baselines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (baseline.DependencySource is not { } source)
                continue;

            IReadOnlyList<ZoneAssetDependency> dependencies =
                _dependencies.RequireCollect(source);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (ZoneAssetDependency dependency in dependencies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                available.TryGetValue(
                    (dependency.Target.Type, dependency.Target.LogicalName),
                    out WorkspaceAssetCatalogEntry? resolved);
                result.Add(new CompiledMapDependency(
                    baseline.Descriptor.SerializedType,
                    baseline.Descriptor.AssetName,
                    dependency.OwnerPath ?? "(unspecified)",
                    dependency.Target.Type,
                    dependency.Target.LogicalName,
                    dependency.Kind,
                    resolved is not null,
                    resolved?.Origin,
                    resolved?.TargetRowIdentity?.SerializedIndex));
            }
        }

        return Array.AsReadOnly(result
            .OrderBy(value => value.OwnerAssetType)
            .ThenBy(value => value.OwnerPath, StringComparer.Ordinal)
            .ThenBy(value => value.TargetAssetType)
            .ThenBy(value => value.TargetAssetName, StringComparer.Ordinal)
            .ToArray());
    }

    private static WorkspaceAssetCatalogEntry? SelectAnchor(
        string zoneName,
        IReadOnlyList<WorkspaceAssetCatalogEntry> anchors)
    {
        if (anchors.Count == 1)
            return anchors[0];

        string expected = XAssetStableIdentity.NormalizeLookupName(
            $"maps/mp/{zoneName}.d3dbsp");
        WorkspaceAssetCatalogEntry[] matching = anchors.Where(entry =>
            string.Equals(entry.NormalizedName, expected, StringComparison.Ordinal))
            .ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }

    private static bool IsOwnedDefinition(WorkspaceAssetCatalogEntry entry) =>
        entry.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition &&
        entry.TargetRow?.State == TargetZoneRowSourceState.Definition &&
        entry.TargetRow.AuthoredDefinition?.SemanticSnapshot is not null;

    private static string RequireName(WorkspaceAssetCatalogEntry entry) =>
        entry.OriginalName
        ?? entry.TargetRow?.OriginalSerializedName
        ?? throw new InvalidDataException(
            $"Owned {entry.AssetType} row has no serialized name.");

    private static void ValidateDetachedName(object source, string expectedName)
    {
        string? actual = source switch
        {
            GfxWorldBuildData value => value.Definition.Name,
            ClipMapBuildData value => value.Definition.Name,
            FxWorldBuildData value => value.Name,
            ComWorldBuildData value => value.Name,
            IMapEntsBuildData value => value.Name,
            GameWorldMpBuildData value => value.Name,
            _ => null
        };
        if (actual is null)
            return;
        if (!string.Equals(
                XAssetStableIdentity.NormalizeLookupName(actual),
                XAssetStableIdentity.NormalizeLookupName(expectedName),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Detached map name '{actual}' does not match target row name '{expectedName}'.");
        }
    }

    private static MapAssetKind Kind(XAssetType type) =>
        type switch
        {
            XAssetType.GfxMap => MapAssetKind.GfxMap,
            XAssetType.ColMapSp => MapAssetKind.ColMapSp,
            XAssetType.ColMapMp => MapAssetKind.ColMapMp,
            XAssetType.ComMap => MapAssetKind.ComMap,
            XAssetType.MapEnts => MapAssetKind.MapEnts,
            XAssetType.FxMap => MapAssetKind.FxMap,
            XAssetType.GameMapMp => MapAssetKind.GameMapMp,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

    private static MapBundleResolutionResult Failed(
        MapBundleResolutionStatus status,
        string diagnostic) =>
        new(status, null, [diagnostic]);
}
