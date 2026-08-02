using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Linking;

namespace IW4.Studio.Documents;

/// <summary>
/// One-way adapter from the document-bound import snapshot to the immutable,
/// source-independent linker graph. It intentionally copies no source path,
/// decoded zone, raw header (except native no-ops), runtime object, pool, or
/// UI state.
/// </summary>
public static class ZoneBuildSnapshotLinkAdapter
{
    public static ZoneLinkRequest Create(ZoneBuildSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.Validation.IsValid)
            throw new InvalidOperationException("An invalid build snapshot cannot be converted to a linker request.");

        var builder = new ZoneBuildGraphBuilder();
        foreach (ZoneBuildRow row in snapshot.Rows)
        {
            ZoneAssetKey key = new(row.AssetType, LogicalName(row));
            string entryId = $"row:{row.Index}";
            switch (row)
            {
                case OwnedDefinitionBuildRow owned:
                    builder.AddOwned(key, owned.BuildData, importedOrder: row.Index, entryId: entryId);
                    break;
                case ExternalReferenceBuildRow external:
                    builder.AddExternal(new ZoneAssetKey(row.AssetType, external.Reference.OriginalSerializedName.TrimStart(',')), row.Index, entryId);
                    break;
                case NullBuildRow:
                    builder.AddNull(key, row.Index, entryId);
                    break;
                case OpaqueNativeNoOpBuildRow opaque:
                    builder.AddOpaqueNativeNoOp(key, opaque.RawHeader, row.Index, entryId);
                    break;
                default:
                    throw new InvalidDataException($"Snapshot row {row.Index} has unsupported linker classification '{row.GetType().Name}'.");
            }
        }

        // Existing detached build data still carries source-local script
        // indices. Keep the imported slots during the compatibility bridge;
        // canonical direct requests use value-deduplication by default. Keep
        // the imported block capacities as floors as well: native zones can
        // reserve unused destination space that allocation high-water alone
        // cannot reconstruct.
        var layout = new ZoneLinkLayoutPolicy(
            externalSize: snapshot.DecodedMetadata.ExternalSize,
            blockSizeFloors: snapshot.DecodedMetadata.BlockSizeFloors);
        return builder.Freeze(
            snapshot.ScriptStrings.OrderBy(value => value.Index).Select(value => value.Value),
            ZoneLinkOutputPolicy.LegacyImported,
            layout);
    }

    internal static string LogicalName(ZoneBuildRow row) => row switch
    {
        OwnedDefinitionBuildRow { BuildData: IRawFileBuildData raw } => raw.OriginalName,
        OwnedDefinitionBuildRow { BuildData: ILocalizeBuildData localize } when localize.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IStringTableBuildData table } when table.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: StructuredDataBuildData data } when data.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: ITechniqueSetBuildData techset } when techset.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IWeaponBuildData weapon } when weapon.Variant.InternalName is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IMenuFileBuildData menuFile } when menuFile.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IMenuBuildData menu } when menu.Definition.Window.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IXAnimBuildData xanim } when xanim.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IXModelBuildData xmodel } when xmodel.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IMaterialBuildData material } when material.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: ISoundAliasListBuildData sound } when sound.AliasName is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IClipMapBuildData clipMap } when clipMap.Definition.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IComWorldBuildData comWorld } when comWorld.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IGameWorldMpBuildData gameWorld } when gameWorld.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IFxWorldBuildData fxWorld } when fxWorld.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IGfxWorldBuildData gfxWorld } when gfxWorld.Definition.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: ILightDefBuildData lightDef } when lightDef.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IFxEffectDefBuildData effect } when effect.Name is { Length: > 0 } name => name,
        OwnedDefinitionBuildRow { BuildData: IFxImpactTableBuildData impact } when impact.Name is { Length: > 0 } name => name,
        ExternalReferenceBuildRow external => external.Reference.OriginalSerializedName.TrimStart(','),
        _ => $"row/{row.Index}"
    };
}
