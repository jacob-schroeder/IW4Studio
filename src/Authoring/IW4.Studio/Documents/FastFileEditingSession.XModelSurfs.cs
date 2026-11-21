using IW4.Game.Assets;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

public sealed partial class FastFileEditingSession
{
    private readonly Dictionary<AssetKey, XModelAsset> _surfaceProviderOwnerBases = [];
    private readonly Dictionary<AssetKey, XModelAsset> _surfaceProviderOwnerCurrent = [];
    private readonly Dictionary<AssetKey, XModelSurfsDraft> _surfaceGeometryOverlays = [];
    private readonly Dictionary<TargetZoneRowIdentity, XModelDraft> _surfaceRootOwnerBases = [];
    private readonly Dictionary<TargetZoneRowIdentity, XModelDraft> _surfaceRootOwnerCurrent = [];

    internal bool CanExpandSurfaceOwner(XModelAsset model)
    {
        lock (_gate)
            return TryFindEmbeddedSurfaceOwner(model, out _, out _) ||
                TryFindRootSurfaceOwner(model, out _);
    }

    private bool TryFindRootSurfaceOwner(XModelAsset model, out DraftState? state)
    {
        DraftState[] matches = _drafts.Values.Where(candidate =>
            candidate.Entry.AssetType == XAssetType.XModel &&
            candidate.Entry.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition &&
            candidate.Entry.Definition is XModelAsset original &&
            (ReferenceEquals(original, model) ||
             model.RuntimeAddress?.AssetPoolAddress is { } address &&
             original.RuntimeAddress?.AssetPoolAddress == address))
            .Take(2).ToArray();
        state = matches.Length == 1 ? matches[0] : null;
        return state is not null;
    }

    private bool TryFindEmbeddedSurfaceOwner(
        XModelAsset model,
        out WorkspaceAssetCatalogEntry? entry,
        out LinkAssetProvider? provider)
    {
        AssetKey key = AssetKey.FromDefinition(model);
        WorkspaceAssetCatalogEntry[] entries = Workspace.AssetCatalog.DependencyEntries
            .Where(candidate => candidate.Definition is XModelAsset original &&
                candidate.ResolvedProviderZone?.IsTarget == true &&
                (ReferenceEquals(original, model) ||
                 model.RuntimeAddress?.AssetPoolAddress is { } address &&
                 original.RuntimeAddress?.AssetPoolAddress == address))
            .Take(2).ToArray();
        LinkAssetProvider[] providers = _targetBaseAssets.Providers
            .Where(candidate => candidate.Key == key && !candidate.IsReferencePlaceholder)
            .Take(2).ToArray();
        entry = entries.Length == 1 ? entries[0] : null;
        provider = providers.Length == 1 ? providers[0] : null;
        return entry is not null && provider is not null &&
            !_authoredAssets.Providers.Any(candidate => candidate.Key == key && !candidate.IsReferencePlaceholder);
    }

    private LinkAssetPool ExpandEmbeddedSurfaceOwners(
        XModelSurfsDraft candidate,
        LinkAssetPool targetAssets,
        out Dictionary<AssetKey, XModelSurfsDraft> nextOverlays,
        out Dictionary<AssetKey, XModelAsset> nextOwners,
        out Dictionary<AssetKey, XModelAsset> nextBases,
        out IReadOnlyList<SurfaceOverlayCapture> captures)
    {
        nextOverlays = new Dictionary<AssetKey, XModelSurfsDraft>(_surfaceGeometryOverlays);
        AssetKey surfaceKey = AssetKey.FromDefinition(candidate.Source);
        if (candidate.ChangedSurfaceIndices().Any())
            nextOverlays[surfaceKey] = candidate.Clone();
        else
            nextOverlays.Remove(surfaceKey);
        nextOwners = new Dictionary<AssetKey, XModelAsset>(_surfaceProviderOwnerCurrent);
        nextBases = new Dictionary<AssetKey, XModelAsset>(_surfaceProviderOwnerBases);
        captures = nextOverlays.Values.Append(candidate)
            .DistinctBy(overlay => AssetKey.FromDefinition(overlay.Source))
            .Select(overlay =>
            {
                int[] changed = overlay.ChangedSurfaceIndices().ToArray();
                XModelAsset[] owners = XModelSurfsUsage.Find(this, overlay.Source)
                    .Select(usage => usage.Model).Distinct().ToArray();
                return new SurfaceOverlayCapture(
                    overlay, changed.Length == 0 ? overlay.Source : overlay.ToAsset(),
                    changed, owners);
            }).ToArray();

        var affected = new Dictionary<AssetKey, XModelAsset>();
        foreach (SurfaceOverlayCapture overlay in captures)
        foreach (XModelAsset model in overlay.Owners)
        {
            AssetKey key = AssetKey.FromDefinition(model);
            affected.TryAdd(key, model);
        }
        foreach ((AssetKey key, XModelAsset model) in _surfaceProviderOwnerBases)
            affected.TryAdd(key, model);

        foreach ((AssetKey key, XModelAsset observed) in affected)
        {
            if (!TryFindEmbeddedSurfaceOwner(observed, out WorkspaceAssetCatalogEntry? entry,
                    out LinkAssetProvider? provider))
                continue;
            XModelAsset baseline = _surfaceProviderOwnerBases.TryGetValue(key, out XModelAsset? saved)
                ? saved : observed;
            nextBases.TryAdd(key, baseline);
            XModelAsset expanded = baseline;
            foreach (SurfaceOverlayCapture overlay in captures)
            {
                if (overlay.Changed.Length == 0 ||
                    !overlay.Owners.Any(model => AssetKey.FromDefinition(model) == key) ||
                    !ModelReferencesSurface(expanded, overlay.Draft.Source))
                    continue;
                expanded = XModelDraft.ExpandBoundsForSurfs(
                    expanded, overlay.Asset, overlay.Changed);
            }
            XModelAsset current = _surfaceProviderOwnerCurrent.TryGetValue(key, out XModelAsset? prior)
                ? prior : baseline;
            if (!SurfaceOwnerBoundsEqual(current, expanded))
            {
                BaseAsset imported = entry!.Definition!;
                LinkAssetProviderSource source = LoadedZoneLinkExtensions.CreateCapturedProviderSource(
                    Workspace.LoadedZone, expanded, imported, provider!.OriginalSerializedName,
                    LinkAssetProviderSourceDisposition.AuthoredDetached);
                targetAssets = targetAssets.WithReplacedProvider(provider, source, out _);
            }
            nextOwners[key] = expanded;
        }
        return targetAssets;
    }

    private LinkAssetPool ExpandRootSurfaceOwners(
        IReadOnlyList<SurfaceOverlayCapture> captures,
        LinkAssetPool authoredAssets,
        out List<(DraftState State, XModelDraft Draft)> updates,
        out Dictionary<TargetZoneRowIdentity, XModelDraft> nextBases,
        out Dictionary<TargetZoneRowIdentity, XModelDraft> nextCurrent)
    {
        updates = [];
        nextBases = new Dictionary<TargetZoneRowIdentity, XModelDraft>(_surfaceRootOwnerBases);
        nextCurrent = new Dictionary<TargetZoneRowIdentity, XModelDraft>(_surfaceRootOwnerCurrent);
        var affected = new Dictionary<AssetKey, XModelAsset>();
        foreach (SurfaceOverlayCapture overlay in captures)
        foreach (XModelAsset model in overlay.Owners)
            affected.TryAdd(AssetKey.FromDefinition(model), model);
        foreach (XModelDraft prior in _surfaceRootOwnerBases.Values)
            affected.TryAdd(AssetKey.FromDefinition(prior.Model), prior.Model);

        var sources = new List<LinkAssetProviderSource>();
        foreach ((AssetKey key, XModelAsset observed) in affected)
        {
            if (!TryFindRootSurfaceOwner(observed, out DraftState? state)) continue;
            TargetZoneRowIdentity identity = state!.Identity;
            XModelDraft baseline = _surfaceRootOwnerBases.TryGetValue(identity, out XModelDraft? saved)
                ? saved : (XModelDraft)state.CloneCurrent();
            if (_surfaceRootOwnerCurrent.TryGetValue(identity, out XModelDraft? last) &&
                !state.SemanticallyEqualsCurrent(last))
                baseline = (XModelDraft)state.CloneCurrent();
            nextBases[identity] = baseline;
            XModelAsset expanded = baseline.ToAsset();
            foreach (SurfaceOverlayCapture overlay in captures)
            {
                if (overlay.Changed.Length == 0 ||
                    !overlay.Owners.Any(model => AssetKey.FromDefinition(model) == key) ||
                    !ModelReferencesSurface(expanded, overlay.Draft.Source))
                    continue;
                expanded = XModelDraft.ExpandBoundsForSurfs(
                    expanded, overlay.Asset, overlay.Changed);
            }
            XModelDraft next = new(expanded);
            nextCurrent[identity] = next;
            if (state.SemanticallyEqualsCurrent(next)) continue;
            LinkAssetProviderSource source = !Workspace.IsBlank &&
                identity.SerializedIndex < Workspace.InitialLinkRequest.Roots.Count &&
                state.Entry.Definition is XModelAsset imported
                ? LoadedZoneLinkExtensions.CreateCapturedProviderSource(
                    Workspace.LoadedZone, expanded, imported, state.Entry.OriginalName,
                    LinkAssetProviderSourceDisposition.AuthoredDetached)
                : new LinkAssetProviderSource(expanded,
                    serializedName: state.Entry.OriginalName).AsAuthoredDetached();
            sources.Add(source);
            updates.Add((state, next));
        }
        if (sources.Count != 0)
        {
            AssetKey[] keys = sources.Select(source => AssetKey.FromDefinition(source.Definition)).ToArray();
            authoredAssets = authoredAssets.WithoutProviders(keys)
                .WithHighestPrecedenceProviders(sources);
        }
        return authoredAssets;
    }

    private sealed record SurfaceOverlayCapture(
        XModelSurfsDraft Draft,
        XModelSurfsAsset Asset,
        int[] Changed,
        IReadOnlyList<XModelAsset> Owners);

    private static bool ModelReferencesSurface(XModelAsset model, XModelSurfsAsset source) =>
        model.Lods.Any(lod => lod.ModelSurfs is { } surfs &&
            (ReferenceEquals(surfs, source) ||
             source.RuntimeAddress?.AssetPoolAddress is { } address &&
             surfs.RuntimeAddress?.AssetPoolAddress == address));

    private XModelAsset PreserveActiveSurfaceBounds(
        DraftState state,
        XModelAsset candidate,
        out XModelDraft? unexpanded)
    {
        unexpanded = null;
        if (state.Entry.AssetType != XAssetType.XModel ||
            state.Entry.Definition is not XModelAsset original)
            return candidate;
        AssetKey key = AssetKey.FromDefinition(original);
        var overlays = _surfaceGeometryOverlays.Values
            .Select(overlay => new
            {
                Draft = overlay,
                Changed = overlay.ChangedSurfaceIndices().ToArray(),
                Owned = XModelSurfsUsage.Find(this, overlay.Source).Any(usage =>
                    AssetKey.FromDefinition(usage.Model) == key)
            })
            .Where(overlay => overlay.Changed.Length != 0 && overlay.Owned)
            .ToArray();
        foreach (var overlay in overlays)
        {
            AssetKey surfaceKey = AssetKey.FromDefinition(overlay.Draft.Source);
            if (!ModelReferencesSurface(candidate, overlay.Draft.Source) &&
                candidate.Lods.Any(lod => lod.ModelSurfs is { } surfs &&
                    AssetKey.FromDefinition(surfs) == surfaceKey))
                throw new InvalidOperationException(
                    "Finish or revert the edited XModelSurfs collection before replacing its XModel LOD surface body.");
        }
        overlays = overlays.Where(overlay =>
            ModelReferencesSurface(candidate, overlay.Draft.Source)).ToArray();
        if (overlays.Length == 0) return candidate;
        if (candidate.NumBones != original.NumBones ||
            !candidate.BoneNames.SequenceEqual(original.BoneNames) ||
            !candidate.ParentList.SequenceEqual(original.ParentList) ||
            !candidate.BaseMat.SequenceEqual(original.BaseMat))
            throw new InvalidOperationException(
                "The XModel rig changed while an edited XModelSurfs collection still references it. Resolve the surface edit before replacing the rig.");
        unexpanded = new XModelDraft(candidate);
        foreach (var overlay in overlays)
            candidate = XModelDraft.ExpandBoundsForSurfs(
                candidate, overlay.Draft.ToAsset(), overlay.Changed);
        return candidate;
    }

    private static bool SurfaceOwnerBoundsEqual(XModelAsset left, XModelAsset right) =>
        left.Bounds.MidPoint.Equals(right.Bounds.MidPoint) &&
        left.Bounds.HalfSize.Equals(right.Bounds.HalfSize) &&
        left.Radius.Equals(right.Radius) &&
        left.BoneInfo.Count == right.BoneInfo.Count &&
        left.BoneInfo.Zip(right.BoneInfo).All(pair =>
            pair.First.Bounds.MidPoint.Equals(pair.Second.Bounds.MidPoint) &&
            pair.First.Bounds.HalfSize.Equals(pair.Second.Bounds.HalfSize) &&
            pair.First.RadiusSquared.Equals(pair.Second.RadiusSquared));

    private void CommitSurfaceOwnerBounds(
        Dictionary<AssetKey, XModelSurfsDraft> overlays,
        Dictionary<AssetKey, XModelAsset> owners,
        Dictionary<AssetKey, XModelAsset> bases)
    {
        _surfaceGeometryOverlays.Clear();
        foreach ((AssetKey key, XModelSurfsDraft value) in overlays)
            _surfaceGeometryOverlays.Add(key, value);
        _surfaceProviderOwnerCurrent.Clear();
        foreach ((AssetKey key, XModelAsset value) in owners)
            _surfaceProviderOwnerCurrent.Add(key, value);
        _surfaceProviderOwnerBases.Clear();
        foreach ((AssetKey key, XModelAsset value) in bases)
            _surfaceProviderOwnerBases.Add(key, value);
    }

    private void CommitSurfaceRootOwnerBounds(
        IReadOnlyList<(DraftState State, XModelDraft Draft)> updates,
        Dictionary<TargetZoneRowIdentity, XModelDraft> bases,
        Dictionary<TargetZoneRowIdentity, XModelDraft> current)
    {
        _surfaceRootOwnerBases.Clear();
        foreach ((TargetZoneRowIdentity key, XModelDraft draft) in bases)
            _surfaceRootOwnerBases.Add(key, draft);
        _surfaceRootOwnerCurrent.Clear();
        foreach ((TargetZoneRowIdentity key, XModelDraft draft) in current)
            _surfaceRootOwnerCurrent.Add(key, draft);
        foreach ((DraftState state, XModelDraft draft) in updates)
            state.SetCurrent(draft, _revision.Revision);
    }

}
