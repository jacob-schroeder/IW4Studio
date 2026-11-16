using IW4.Game.Assets;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Assets.XModel;
using IW4.Linker.Contracts;
using IW4.Linker.D3dbsp;
using MapConverter.Game.IW3.PS3.Rules;

namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        private readonly Dictionary<string, XModelAsset[]> _splitStaticModels =
            new(StringComparer.OrdinalIgnoreCase);

        private StaticModelConversion ConvertStaticModels(GfxWorldDpvsStatic dpvs,
            GfxCellTree[] cells, GfxShadowGeometry[] shadows)
        {
            StaticModelConversion result;
            try { result = StaticModelRules.Convert(dpvs, cells, shadows); }
            catch (InvalidDataException error) { throw Invalid(error.Message); }
            catch (NotSupportedException error) { throw Unsupported(error.Message); }
            foreach (XModelAsset[] parts in result.SplitModels)
            {
                string name = parts[0].Name ?? throw Invalid("split static model has no name");
                if (!_splitStaticModels.TryAdd(name, parts))
                    throw Unsupported($"multiple render worlds split model '{name}'");
            }
            return result;
        }

        private XModelAsset? StaticCollisionModel(XModelAsset? model) =>
            model?.Name is { } name && _splitStaticModels.TryGetValue(name, out var parts)
                ? parts[0] : model;

        // Finalize after all source roots have been read: entities and dynamics
        // can refer to models whose static draw instances were expanded earlier.
        internal IReadOnlySet<string> ApplyStaticModelDefinitions()
        {
            var splitNames = _splitStaticModels.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (splitNames.Count == 0) return splitNames;
            bool IsSplit(string name) => splitNames.Contains(name) ||
                splitNames.Contains(_ownedNamePrefix + name);
            foreach (ClipMapAsset clip in _definitions.OfType<ClipMapAsset>())
                foreach (DynEntityDef entity in clip.DynEntDefList.SelectMany(list => list))
                    if (entity.XModel?.Name is { } name && IsSplit(name))
                        throw Unsupported($"split static model '{name}' is also used by a dynamic entity");
            foreach (MapEntsAsset entities in _definitions.OfType<MapEntsAsset>())
                foreach (string name in D3dbspAssetLinker.ReadNamedEntityModelNames(
                    entities.EntityStringBytes.ToArray()))
                    if (IsSplit(name))
                        throw Unsupported($"split static model '{name}' is also used by a map entity");

            var replacements = new Dictionary<AssetKey, BaseAsset>();
            var additions = new List<BaseAsset>();
            var occupied = _definitions.Select(AssetKey.FromDefinition).ToHashSet();
            foreach (var (name, parts) in _splitStaticModels.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                AssetKey modelKey = AssetKey.FromDefinition(parts[0]);
                XModelAsset original = _definitions.OfType<XModelAsset>()
                    .Single(model => AssetKey.FromDefinition(model) == modelKey);
                XModelSurfsAsset originalMesh = original.Lods[0].ModelSurfs ??
                    throw Invalid($"split static model '{name}' has no original surface provider");
                AssetKey meshKey = AssetKey.FromDefinition(originalMesh);
                if (_definitions.OfType<XModelAsset>().Any(model =>
                    !ReferenceEquals(model, original) && model.Lods.Any(lod =>
                        lod.ModelSurfs is { } mesh && AssetKey.FromDefinition(mesh) == meshKey)))
                    throw Unsupported($"split static model '{name}' shares its surface provider with another model");
                replacements.Add(modelKey, parts[0]);
                XModelSurfsAsset firstMesh = parts[0].Lods[0].ModelSurfs ??
                    throw Invalid($"split static model '{name}' has no first surface provider");
                if (!occupied.Contains(meshKey))
                    throw Invalid($"split static model '{name}' has an unregistered surface provider");
                replacements.Add(meshKey, firstMesh);
                foreach (XModelAsset part in parts.Skip(1))
                {
                    XModelSurfsAsset mesh = part.Lods[0].ModelSurfs ??
                        throw Invalid($"split static model '{part.Name}' has no surface provider");
                    foreach (BaseAsset asset in new BaseAsset[] { mesh, part })
                    {
                        if (!occupied.Add(AssetKey.FromDefinition(asset)))
                            throw Invalid($"generated static-model asset '{asset.SerializedAssetName}' conflicts with an existing asset");
                        additions.Add(asset);
                    }
                }
            }
            for (int index = 0; index < _definitions.Count; index++)
                if (replacements.TryGetValue(AssetKey.FromDefinition(_definitions[index]), out BaseAsset? replacement))
                    _definitions[index] = replacement;
            _definitions.AddRange(additions);
            return splitNames;
        }
    }
}
