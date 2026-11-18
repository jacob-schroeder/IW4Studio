using IW4.Game.Assets;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

public sealed partial class AssetEditorSession
{
    public IReadOnlyList<string> CharacterReferenceNames(XAssetType type)
    {
        ThrowIfClosed();
        return _session.Document.Rows.Concat(Workspace.AssetCatalog.DependencyEntries)
            .Where(entry => entry.AssetType == type && entry.HasDefinition)
            .Select(entry => entry.NormalizedName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public T? ResolveCharacterReference<T>(XAssetType type, string? name,
        out IReadOnlyList<BaseAsset> providers) where T : BaseAsset
    {
        ThrowIfClosed();
        return _session.ResolveCharacterReference<T>(type, name, out providers);
    }

    public bool CanOverrideCharacter => Mode == WorkspaceAssetAccess.ReadOnly &&
        Entry.Origin == WorkspaceAssetOrigin.DependencyOnly && Definition is RawFileAsset { Name: { } name } &&
        _session.ValidateNewAssetName(XAssetType.RawFile, name, allowDependencyOverride: true) is null;

    public string OverrideCharacter()
    {
        ThrowIfClosed();
        if (!CanOverrideCharacter || Definition is not RawFileAsset { Name: { } name } source)
            throw new InvalidOperationException("The character cannot be overridden in this fastfile.");
        _session.AddAsset(source, new RawFileAdapter(), allowDependencyOverride: true);
        return name;
    }

    public void AddCharacterModel(XModelAssemblyCompileResult model)
    {
        RequireEditableCharacterScript();
        if (!model.IsSuccess)
            throw new InvalidDataException(string.Join(" ", model.Issues.Select(issue => issue.Message)));
        _session.AddAsset(model.Definition, new XModelAdapter(), model.Providers);
    }

    public void CopyCharacterAlias(RawFileAsset original, string newPath)
    {
        RequireEditableCharacterScript();
        var copy = new RawFileAsset
        {
            Name = newPath, Len = original.Len, CompressedLen = original.CompressedLen,
            Buffer = original.Buffer?.ToArray()
        };
        _session.AddAsset(copy, new RawFileAdapter());
    }

    private void RequireEditableCharacterScript()
    {
        ThrowIfClosed();
        if (!CanEdit || _adapter.AssetType != XAssetType.RawFile)
            throw new InvalidOperationException("An editable character script is required.");
    }
}

public sealed partial class FastFileEditingSession
{
    internal T? ResolveCharacterReference<T>(XAssetType type, string? name,
        out IReadOnlyList<BaseAsset> providers) where T : BaseAsset
    {
        providers = [];
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
                if (target.TargetRowIdentity is { } row && _compiledAuxiliaryProviders.TryGetValue(row, out var owned))
                    providers = owned.Values.ToArray();
                return CurrentDefinitionForEntry(target) as T;
            }

            if (Workspace.IsBlank || !Workspace.LoadedZone.Context.AssetPool.TryResolve(type, normalized, out T? asset) ||
                asset?.RuntimeAddress?.AssetPoolAddress is not { } address ||
                !Workspace.LoadedZone.Context.AssetPool.TryGetSlot(address, out var slot) ||
                slot is null || slot.ActiveProvider.IsReferencePlaceholder)
                return null;
            var identity = new WorkspaceDependencyAssetIdentity(Document.DocumentId, slot.ActiveProvider.Id.Value);
            return _providerDrafts.TryGetValue(identity, out var state) && state.IsChanged
                ? state.CreateCurrentDefinition() as T : asset;
        }
    }
}
