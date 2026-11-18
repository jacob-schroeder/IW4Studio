using IW4.Game.Assets;
using IW4.Game.Zone;
using IW4.Game.Assets.Sound;
using IW4.Runtime.Assets.Sound;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

public sealed partial class AssetEditorSession
{
    public IReadOnlyList<string> EditorReferenceNames(XAssetType type)
    {
        ThrowIfClosed();
        return _session.Document.Rows.Concat(Workspace.AssetCatalog.DependencyEntries)
            .Where(entry => entry.AssetType == type && entry.HasDefinition)
            .Select(entry => entry.NormalizedName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public T? ResolveEditorReference<T>(XAssetType type, string? name,
        out IReadOnlyList<BaseAsset> providers) where T : BaseAsset
    {
        ThrowIfClosed();
        return _session.ResolveEditorReference<T>(type, name, out providers, out _);
    }

    public SoundAliasListAsset? ResolveEditorSound(string name, out ISoundPayloadResolver resolver, out string reason)
    {
        ThrowIfClosed();
        var sound = _session.ResolveEditorReference<SoundAliasListAsset>(XAssetType.Sound, name, out _, out var owner);
        if (sound is null)
        {
            resolver = UnavailableSoundPayloadResolver.Instance;
            reason = "The sound alias is unavailable in this workspace.";
            return null;
        }
        Workspace.TryGetSoundPayloadResolver(sound, owner, name, out resolver, out reason);
        return sound;
    }
}

public sealed partial class FastFileEditingSession
{
    internal T? ResolveEditorReference<T>(XAssetType type, string? name,
        out IReadOnlyList<BaseAsset> providers, out long? owningProviderId) where T : BaseAsset
    {
        providers = [];
        owningProviderId = null;
        if (string.IsNullOrWhiteSpace(name))
            return null;
        string normalized = AssetKey.FromWireName(CanonicalAssetFamily.FromSerializedType(type), name).NormalizedName;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            var target = Document.Rows.FirstOrDefault(entry => entry.AssetType == type &&
                entry.NormalizedName == normalized && entry.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition);
            if (target is not null)
            {
                owningProviderId = target.ResolvedProvider?.ProviderId;
                if (target.TargetRowIdentity is { } row && _compiledAuxiliaryProviders.TryGetValue(row, out var owned))
                    providers = owned.Values.ToArray();
                return CurrentDefinitionForEntry(target) as T;
            }

            if (Workspace.IsBlank || !Workspace.LoadedZone.Context.AssetPool.TryResolve(type, normalized, out T? asset) ||
                asset?.RuntimeAddress?.AssetPoolAddress is not { } address ||
                !Workspace.LoadedZone.Context.AssetPool.TryGetSlot(address, out var slot) ||
                slot is null || slot.ActiveProvider.IsReferencePlaceholder)
                return null;
            owningProviderId = slot.ActiveProvider.Id.Value;
            var identity = new WorkspaceDependencyAssetIdentity(Document.DocumentId, slot.ActiveProvider.Id.Value);
            return _providerDrafts.TryGetValue(identity, out var state) && state.IsChanged
                ? state.CreateCurrentDefinition() as T : asset;
        }
    }
}
