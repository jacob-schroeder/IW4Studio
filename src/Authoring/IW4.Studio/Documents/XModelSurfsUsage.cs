using IW4.Game.Assets.XModel;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

/// <summary>A model LOD that uses this exact surface collection.</summary>
public sealed record XModelSurfsUsage(XModelAsset Model, int LodIndex)
{
    public string? MaterialName(int surfaceIndex)
    {
        var lod = Model.Lods[LodIndex];
        return surfaceIndex >= 0 && surfaceIndex < lod.NumSurfs
            ? Model.Materials.ElementAtOrDefault(lod.SurfIndex + surfaceIndex)?.Info.Name
            : null;
    }

    internal static IReadOnlyList<XModelSurfsUsage> Find(FastFileEditingSession session, XModelSurfsAsset source)
    {
        var applied = session.CaptureCurrentTargetAssets([XAssetType.XModel]).Definitions;
        var models = applied.Select(row => row.Definition).OfType<XModelAsset>()
            .Concat(session.Workspace.AssetCatalog.Entries
                .Where(row => row.Origin != WorkspaceAssetOrigin.TargetOwnedDefinition || row.Access != WorkspaceAssetAccess.Editable)
                .Select(row => row.Definition).OfType<XModelAsset>()).Distinct();
        var address = source.RuntimeAddress?.AssetPoolAddress;
        var result = new List<XModelSurfsUsage>();
        foreach (var model in models)
        {
            int count = model.NumLods == 0 ? model.Lods.Count : Math.Min(model.NumLods, model.Lods.Count);
            for (int i = 0; i < count; i++)
            {
                var mesh = model.Lods[i].ModelSurfs;
                if (ReferenceEquals(mesh, source) || address.HasValue && mesh?.RuntimeAddress?.AssetPoolAddress == address)
                    result.Add(new(model, i));
            }
        }
        return result.OrderBy(row => row.Model.Name, StringComparer.Ordinal).ThenBy(row => row.LodIndex).ToArray();
    }
}

public sealed partial class AssetEditorSession
{
    public XModelSurfsDraft? OpenXModelSurfsSource(string name)
    {
        ThrowIfClosed();
        var embedded = Workspace.AssetCatalog.DependencyEntries.FirstOrDefault(row =>
            row.AssetType == XAssetType.XModelSurfs && row.TargetProvider is not null &&
            string.Equals(row.NormalizedName, name, StringComparison.OrdinalIgnoreCase));
        if (embedded?.DependencyIdentity is { } id && _session.CanEditProvider(embedded, _adapter))
            return (XModelSurfsDraft)_session.CloneCurrentProviderDraft(id);

        var asset = ResolveEditorReference<XModelSurfsAsset>(XAssetType.XModelSurfs, name, out _);
        return asset is null ? null : new XModelSurfsDraft(asset);
    }

    public IReadOnlyList<XModelSurfsUsage> FindXModelSurfsUsages(XModelSurfsDraft collection)
    {
        ThrowIfClosed();
        return XModelSurfsUsage.Find(_session, collection.Source);
    }
}
