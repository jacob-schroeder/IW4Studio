using IW4.Game.Assets;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.Weapon;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Studio.Documents.AssetReferences;

namespace IW4.Studio.Documents;

public sealed partial class AssetEditorSession
{
    /// <summary>Assigns a same-type provider selected from the current workspace or clears the field.</summary>
    public void AssignVehicleReference(VehicleDraft draft, VehicleField field, string? name)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(field);
        if (_adapter.AssetType != XAssetType.Vehicle || field.Kind != VehicleFieldKind.AssetReference ||
            field.ReferenceType is not { } type)
        {
            throw new ArgumentException("A Vehicle provider reference field is required.", nameof(field));
        }

        draft.SetReference(field, string.IsNullOrWhiteSpace(name)
            ? null
            : _session.ResolveVehicleProvider(type, name));
    }
}

public sealed partial class FastFileEditingSession
{
    internal BaseAsset ResolveVehicleProvider(XAssetType type, string name)
    {
        AssetKey key = AssetKey.FromWireName(CanonicalAssetFamily.FromSerializedType(type), name);
        lock (_gate)
        {
            ThrowIfDisposedCore();
            WorkspaceAssetCatalogEntry? target = Document.Rows.FirstOrDefault(entry =>
                entry.AssetType == type && entry.NormalizedName == key.NormalizedName &&
                entry.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition);
            if (target is not null)
                return CurrentDefinitionForEntry(target);

            ZoneLinkRequest link = _revision.LinkRequest;
            string? externalRootName = link.Roots.FirstOrDefault(root =>
                root.Asset == key && root.Intent == LinkRootIntent.External)?.OriginalSerializedName;
            LinkAssetProvider? provider = link.Assets.Providers
                .Where(current => current.Key == key && current.SerializedType == type)
                .OrderBy(current => current.IsReferencePlaceholder)
                .FirstOrDefault();
            string wireName;
            if (externalRootName is not null)
                wireName = externalRootName;
            else if (provider is not null)
                wireName = provider.OriginalSerializedName;
            else
            {
                WorkspaceAssetReferenceCandidate candidate = new WorkspaceAssetReferenceCatalog(this).Find(type, name) ??
                    throw new ArgumentException($"'{name}' is not an available {type} reference.", nameof(name));
                // A catalog-only provider stays external. A Vehicle change
                // never imports a dependency zone's provider payload.
                wireName = $",{candidate.Name}";
            }

            return type switch
            {
                XAssetType.Material => new MaterialAsset { Info = new MaterialInfo { Name = wireName } },
                XAssetType.PhysPreset => new PhysPresetAsset { Name = wireName },
                XAssetType.Weapon => new WeaponAsset { Variant = new WeaponVariantDef { InternalName = wireName } },
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
        }
    }
}
