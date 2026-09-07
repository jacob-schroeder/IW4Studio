using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;

namespace MapConverter.Game.IW3.PC.DynamicEntities;

internal sealed record Iw3DynamicEntityCompilation(
    IReadOnlyList<IReadOnlyList<DynEntityDef>> Definitions,
    int DestroyFxFallbackCount,
    IReadOnlyList<string> DestroyFxFallbackNames,
    int DestroyPiecesFallbackCount,
    IReadOnlyList<string> DestroyPiecesFallbackNames)
{
    internal int Count => Definitions.Sum(list => list.Count);
}

internal static class Iw3DynamicEntityCompiler
{
    internal static Iw3DynamicEntityCompilation Compile(
        Iw3DynamicEntitySourceData source,
        IReadOnlyDictionary<string, XModelAsset> xmodelsByName,
        IReadOnlyDictionary<string, PhysPresetAsset> physPresetsByName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(xmodelsByName);
        ArgumentNullException.ThrowIfNull(physPresetsByName);
        if (source.Definitions.Count != 2)
        {
            throw new InvalidDataException(
                "The IW3 dynamic-entity source must contain model and brush lists.");
        }

        int destroyFxFallbackCount = 0;
        int destroyPiecesFallbackCount = 0;
        var destroyFxFallbackNames = new SortedSet<string>(StringComparer.Ordinal);
        var destroyPiecesFallbackNames = new SortedSet<string>(StringComparer.Ordinal);
        var targetLists = new IReadOnlyList<DynEntityDef>[2];
        for (int listIndex = 0; listIndex < targetLists.Length; listIndex++)
        {
            IReadOnlyList<Iw3DynamicEntitySource> sourceList =
                source.Definitions[listIndex];
            var targetRows = new DynEntityDef[sourceList.Count];
            for (int index = 0; index < sourceList.Count; index++)
            {
                Iw3DynamicEntitySource item = sourceList[index] ??
                    throw new InvalidDataException(
                        $"IW3 dynamic-entity list {listIndex} row {index} is null.");
                XModelAsset? xmodel = Resolve(
                    item.XModelName,
                    xmodelsByName,
                    "XModel",
                    listIndex,
                    index);
                PhysPresetAsset? physPreset = Resolve(
                    item.PhysPresetName,
                    physPresetsByName,
                    "physics preset",
                    listIndex,
                    index);
                if (physPreset is null)
                {
                    throw new InvalidDataException(
                        $"IW3 dynamic-entity list {listIndex} row {index} has " +
                        "no physics preset.");
                }

                if (item.DestroyFxName is not null)
                {
                    destroyFxFallbackCount++;
                    destroyFxFallbackNames.Add(item.DestroyFxName);
                }
                if (item.DestroyPiecesName is not null)
                {
                    destroyPiecesFallbackCount++;
                    destroyPiecesFallbackNames.Add(item.DestroyPiecesName);
                }

                targetRows[index] = new DynEntityDef
                {
                    Type = item.Type,
                    Pose = new GfxPlacement
                    {
                        Quat = item.Quat.ToArray(),
                        Origin = Vec3(item.Origin)
                    },
                    XModel = xmodel,
                    BrushModel = item.BrushModel,
                    PhysicsBrushModel = item.PhysicsBrushModel,
                    // IW4 has no destroyPieces member. Destroy FX remains null
                    // until its nested IW3 representation has a proven lowering.
                    DestroyFx = null,
                    PhysPreset = physPreset,
                    Health = item.Health,
                    Mass = new PhysMass
                    {
                        CenterOfMass = Vec3(item.CenterOfMass),
                        MomentsOfInertia = Vec3(item.MomentsOfInertia),
                        ProductsOfInertia = Vec3(item.ProductsOfInertia)
                    },
                    Contents = item.Contents
                };
            }
            targetLists[listIndex] = Array.AsReadOnly(targetRows);
        }

        return new Iw3DynamicEntityCompilation(
            Array.AsReadOnly(targetLists),
            destroyFxFallbackCount,
            Array.AsReadOnly(destroyFxFallbackNames.ToArray()),
            destroyPiecesFallbackCount,
            Array.AsReadOnly(destroyPiecesFallbackNames.ToArray()));
    }

    private static TAsset? Resolve<TAsset>(
        string? name,
        IReadOnlyDictionary<string, TAsset> assetsByName,
        string description,
        int listIndex,
        int rowIndex)
        where TAsset : class
    {
        if (name is null)
            return null;
        string lookupName = name[0] == ','
            ? name[1..]
            : name;
        if (lookupName.Length == 0 ||
            !assetsByName.TryGetValue(lookupName, out TAsset? asset) ||
            asset is null)
        {
            throw new InvalidDataException(
                $"IW3 dynamic-entity list {listIndex} row {rowIndex} references " +
                $"{description} '{name}', but it was not resolved.");
        }
        return asset;
    }

    private static Vec3 Vec3(IReadOnlyList<float> values)
    {
        if (values.Count != 3)
            throw new InvalidDataException("A dynamic-entity vector must contain three values.");
        return new Vec3
        {
            X = values[0],
            Y = values[1],
            Z = values[2]
        };
    }
}
