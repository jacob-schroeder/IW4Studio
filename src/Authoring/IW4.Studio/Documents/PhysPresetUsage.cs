using IW4.Game.Assets;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.FxMap;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.Vehicle;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

/// <summary>A direct preset pointer in an applied target or loaded catalog definition.</summary>
public sealed record PhysPresetUsage(string AssetName, string Field)
{
    private static readonly XAssetType[] ReferringTypes =
    [
        XAssetType.XModel,
        XAssetType.ColMapSp,
        XAssetType.ColMapMp,
        XAssetType.FxMap,
        XAssetType.Vehicle
    ];

    internal static IReadOnlyList<PhysPresetUsage> Capture(
        FastFileEditingSession editingSession,
        AssetEditorSession editor)
    {
        if (editor.Definition is not PhysPresetAsset preset)
            return [];

        WorkspaceAssetCatalog catalog = editingSession.Workspace.AssetCatalog;
        var seenLoaded = new HashSet<BaseAsset>(ReferenceEqualityComparer.Instance);
        var usages = new List<PhysPresetUsage>();

        foreach (AppliedAssetDefinition applied in editingSession.CaptureCurrentTargetAssets(ReferringTypes).Definitions)
        {
            int index = applied.RowIdentity.SerializedIndex;
            if (index < catalog.TargetEntries.Count &&
                catalog.TargetEntries[index].Definition is { } baseline)
            {
                // A catalog reference may show the same provider as the target row.
                seenLoaded.Add(baseline);
            }

            AddUsages(applied.Definition, preset, usages);
        }

        foreach (WorkspaceAssetCatalogEntry entry in catalog.Entries)
        {
            if (entry.Origin != WorkspaceAssetOrigin.TargetOwnedDefinition &&
                ReferringTypes.Contains(entry.AssetType) &&
                entry.Definition is { } owner &&
                seenLoaded.Add(owner))
            {
                AddUsages(owner, preset, usages);
            }
        }

        return Array.AsReadOnly(usages
            .OrderBy(usage => usage.AssetName, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    private static void AddUsages(BaseAsset owner, PhysPresetAsset preset, List<PhysPresetUsage> usages)
    {
        string name = owner.SerializedAssetName ?? "(unnamed asset)";
        // Compare the materialized definitions, since different zone providers can share a name.
        switch (owner)
        {
            case XModelAsset model when ReferenceEquals(model.PhysPreset, preset):
                usages.Add(new(name, "XModel · PhysPreset"));
                break;
            case VehicleDefAsset vehicle when ReferenceEquals(vehicle.Phys.PhysPreset, preset):
                usages.Add(new(name, "Vehicle · Phys.PhysPreset"));
                break;
            case FxWorldAsset fxWorld:
                for (int index = 0; index < fxWorld.GlassSystem.Defs.Count; index++)
                {
                    if (ReferenceEquals(fxWorld.GlassSystem.Defs[index].PhysPreset, preset))
                        usages.Add(new(name, $"Fx glass definition {index} · PhysPreset"));
                }
                break;
            case ClipMapAsset clipMap:
                for (int list = 0; list < clipMap.DynEntDefList.Count; list++)
                {
                    IReadOnlyList<DynEntityDef> entities = clipMap.DynEntDefList[list];
                    for (int index = 0; index < entities.Count; index++)
                    {
                        DynEntityDef entity = entities[index];
                        if (!ReferenceEquals(entity.PhysPreset, preset))
                            continue;

                        string modelName = entity.XModel?.Name is { } modelAsset ? $", {modelAsset}" : string.Empty;
                        usages.Add(new(name, $"ClipMap dynamic entity {index} ({(DynEntityDrawType)list} list, {entity.Type}{modelName}) · PhysPreset"));
                    }
                }
                break;
        }
    }
}
