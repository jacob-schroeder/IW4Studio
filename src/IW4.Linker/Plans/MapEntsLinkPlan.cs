using System.Text;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Math;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

internal sealed class MapEntsLinkPlan : AssetLinkPlan
{
    private MapEntsLinkPlan(
        AssetKey key,
        string originalSerializedName,
        MapEntsAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        LinkStorageSymbol? entity = MapEntsPlanStorage.CreateBytes(
            definition.EntityStringBytes,
            alignment: 1);
        LinkStorageSymbol? models = MapEntsPlanStorage.CreateModels(
            definition.Trigger.Models);
        LinkStorageSymbol? hulls = MapEntsPlanStorage.CreateHulls(
            definition.Trigger.Hulls);
        LinkStorageSymbol? slabs = MapEntsPlanStorage.CreateSlabs(
            definition.Trigger.Slabs);
        LinkStorageSymbol? stages = CreateStages(definition.Stages, freeze);

        var writer = new LinkTemplateWriter(MapEntsAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.NumEntityChars);
        MapEntsPlanStorage.WriteTriggerHeader(writer, definition.Trigger);
        writer.Skip(sizeof(int));
        writer.WriteByte(definition.StageCount);
        if (definition.Pad29To2B.Count == 0)
            writer.Skip(3);
        else
            writer.WriteBytes(definition.Pad29To2B.ToArray());

        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => CreateOperations(
                root,
                entity,
                models,
                hulls,
                slabs,
                stages));
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        MapEntsAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        MapEntsPlanStorage.ValidateCommon(
            definition.EntityStringBytes,
            definition.EntityString,
            definition.NumEntityChars,
            definition.Trigger,
            "MapEnts");
        IReadOnlyList<Stage> stages = definition.Stages ??
            throw new InvalidDataException("MapEnts.Stages cannot be null.");
        if (definition.StageCount != stages.Count)
            throw new InvalidDataException("MapEnts.StageCount must equal the semantic stage count.");
        if (definition.Pad29To2B is null || definition.Pad29To2B.Count is not (0 or 3))
            throw new InvalidDataException("MapEnts.Pad29To2B must contain zero or three bytes.");
        for (int index = 0; index < stages.Count; index++)
        {
            if (stages[index] is null)
                throw new InvalidDataException($"MapEnts.Stages[{index}] cannot be null.");
        }

        if (originalSerializedName.StartsWith(','))
        {
            if (!MapEntsPlanStorage.IsEmptyCommon(
                    definition.EntityStringBytes,
                    definition.NumEntityChars,
                    definition.Trigger) ||
                stages.Count != 0 ||
                definition.Pad29To2B.Any(value => value != 0))
            {
                throw new InvalidDataException(
                    "A comma-prefixed MapEnts provider must have a zeroed reference body.");
            }
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.MapEnts,
                originalSerializedName,
                freeze);
        }

        return new MapEntsLinkPlan(key, originalSerializedName, definition, freeze);
    }

    private IEnumerable<LinkOperation> CreateOperations(
        LinkStorageSymbol root,
        LinkStorageSymbol? entity,
        LinkStorageSymbol? models,
        LinkStorageSymbol? hulls,
        LinkStorageSymbol? slabs,
        LinkStorageSymbol? stages)
    {
        yield return NameOperation(root, 0);
        if (entity is not null)
            yield return MapEntsPlanStorage.Presence(root, 0x04, entity, "MapEnts.EntityString");
        if (models is not null)
            yield return MapEntsPlanStorage.Presence(root, 0x10, models, "MapEnts.Trigger.Models");
        if (hulls is not null)
            yield return MapEntsPlanStorage.Presence(root, 0x18, hulls, "MapEnts.Trigger.Hulls");
        if (slabs is not null)
            yield return MapEntsPlanStorage.Presence(root, 0x20, slabs, "MapEnts.Trigger.Slabs");
        if (stages is not null)
            yield return MapEntsPlanStorage.Presence(root, 0x24, stages, "MapEnts.Stages");
    }

    private static LinkStorageSymbol? CreateStages(
        IReadOnlyList<Stage> values,
        LinkAssetFreezeScope freeze)
    {
        if (values.Count == 0)
            return null;
        var names = new LinkStorageSymbol?[values.Count];
        var writer = new LinkTemplateWriter(checked(values.Count * Stage.SerializedSize));
        for (int index = 0; index < values.Count; index++)
        {
            Stage value = values[index];
            names[index] = freeze.FreezeOptionalXString(
                value.StageName,
                value.StageNamePointer.Untyped,
                $"MapEnts.Stages[{index}].StageName");
            writer.Skip(sizeof(int));
            MapEntsPlanStorage.WriteVec3(writer, value.Origin);
            writer.WriteUInt16(value.TriggerIndex);
            writer.WriteByte(value.SunPrimaryLightIndex);
            writer.WriteByte(value.Pad13);
        }

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            table => names
                .Select((storage, index) => (storage, index))
                .Where(item => item.storage is not null)
                .Select(item => XStringOperation(
                    table,
                    checked(item.index * Stage.SerializedSize),
                    item.storage!,
                    $"MapEnts.Stages[{item.index}].StageName")));
    }
}

internal sealed class AddonMapEntsLinkPlan : AssetLinkPlan
{
    private AddonMapEntsLinkPlan(
        AssetKey key,
        string originalSerializedName,
        AddonMapEntsAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        LinkStorageSymbol? entity = MapEntsPlanStorage.CreateBytes(
            definition.EntityStringBytes,
            alignment: 1);
        LinkStorageSymbol? models = MapEntsPlanStorage.CreateModels(
            definition.Trigger.Models);
        LinkStorageSymbol? hulls = MapEntsPlanStorage.CreateHulls(
            definition.Trigger.Hulls);
        LinkStorageSymbol? slabs = MapEntsPlanStorage.CreateSlabs(
            definition.Trigger.Slabs);

        var writer = new LinkTemplateWriter(AddonMapEntsAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.NumEntityChars);
        MapEntsPlanStorage.WriteTriggerHeader(writer, definition.Trigger);
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => CreateOperations(root, entity, models, hulls, slabs));
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        AddonMapEntsAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        MapEntsPlanStorage.ValidateCommon(
            definition.EntityStringBytes,
            definition.EntityString,
            definition.NumEntityChars,
            definition.Trigger,
            "AddonMapEnts");
        if (originalSerializedName.StartsWith(','))
        {
            if (!MapEntsPlanStorage.IsEmptyCommon(
                    definition.EntityStringBytes,
                    definition.NumEntityChars,
                    definition.Trigger))
            {
                throw new InvalidDataException(
                    "A comma-prefixed AddonMapEnts provider must have a zeroed reference body.");
            }
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.AddonMapEnts,
                originalSerializedName,
                freeze);
        }

        return new AddonMapEntsLinkPlan(key, originalSerializedName, definition, freeze);
    }

    private IEnumerable<LinkOperation> CreateOperations(
        LinkStorageSymbol root,
        LinkStorageSymbol? entity,
        LinkStorageSymbol? models,
        LinkStorageSymbol? hulls,
        LinkStorageSymbol? slabs)
    {
        yield return NameOperation(root, 0);
        if (entity is not null)
            yield return MapEntsPlanStorage.Presence(root, 0x04, entity, "AddonMapEnts.EntityString");
        if (models is not null)
            yield return MapEntsPlanStorage.Presence(root, 0x10, models, "AddonMapEnts.Trigger.Models");
        if (hulls is not null)
            yield return MapEntsPlanStorage.Presence(root, 0x18, hulls, "AddonMapEnts.Trigger.Hulls");
        if (slabs is not null)
            yield return MapEntsPlanStorage.Presence(root, 0x20, slabs, "AddonMapEnts.Trigger.Slabs");
    }
}

internal static class MapEntsPlanStorage
{
    private const int MaximumEntityBytes = 0x4000000;

    public static LinkStorageSymbol? CreateBytes(
        IReadOnlyList<byte> values,
        int alignment) =>
        values.Count == 0
            ? null
            : LinkStorageSymbol.SourceBytes(
                XFileBlockType.LARGE,
                values.ToArray(),
                alignment);

    public static LinkStorageSymbol? CreateModels(IReadOnlyList<TriggerModel> values)
    {
        if (values.Count == 0)
            return null;
        var writer = new LinkTemplateWriter(checked(values.Count * TriggerModel.SerializedSize));
        foreach (TriggerModel value in values)
        {
            writer.WriteInt32(value.Contents);
            writer.WriteUInt16(value.HullCount);
            writer.WriteUInt16(value.FirstHull);
        }
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4);
    }

    public static LinkStorageSymbol? CreateHulls(IReadOnlyList<TriggerHull> values)
    {
        if (values.Count == 0)
            return null;
        var writer = new LinkTemplateWriter(checked(values.Count * TriggerHull.SerializedSize));
        foreach (TriggerHull value in values)
        {
            WriteVec3(writer, value.Bounds.MidPoint);
            WriteVec3(writer, value.Bounds.HalfSize);
            writer.WriteInt32(value.Contents);
            writer.WriteUInt16(value.SlabCount);
            writer.WriteUInt16(value.FirstSlab);
        }
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4);
    }

    public static LinkStorageSymbol? CreateSlabs(IReadOnlyList<TriggerSlab> values)
    {
        if (values.Count == 0)
            return null;
        var writer = new LinkTemplateWriter(checked(values.Count * TriggerSlab.SerializedSize));
        foreach (TriggerSlab value in values)
        {
            WriteVec3(writer, value.Dir);
            writer.WriteSingle(value.MidPoint);
            writer.WriteSingle(value.HalfSize);
        }
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4);
    }

    public static void WriteTriggerHeader(
        LinkTemplateWriter writer,
        MapTriggers trigger)
    {
        writer.WriteUInt32(trigger.Count);
        writer.Skip(sizeof(int));
        writer.WriteUInt32(trigger.HullCount);
        writer.Skip(sizeof(int));
        writer.WriteUInt32(trigger.SlabCount);
        writer.Skip(sizeof(int));
    }

    public static void WriteVec3(LinkTemplateWriter writer, Vec3 value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        writer.WriteSingle(value.Z);
    }

    public static PresenceStorageLinkOperation Presence(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageSymbol target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            LinkStorageView.Whole(target),
            fieldPath);

    public static void ValidateCommon(
        IReadOnlyList<byte>? entityBytes,
        string? entityText,
        int declaredEntityCount,
        MapTriggers? trigger,
        string fieldPrefix)
    {
        if (entityBytes is null)
            throw new InvalidDataException($"{fieldPrefix}.EntityStringBytes cannot be null.");
        if (declaredEntityCount < 0 || declaredEntityCount != entityBytes.Count)
        {
            throw new InvalidDataException(
                $"{fieldPrefix}.NumEntityChars must equal the semantic entity-byte count.");
        }
        if (entityBytes.Count > MaximumEntityBytes)
            throw new InvalidDataException($"{fieldPrefix}.EntityStringBytes exceeds the native bounded size.");
        if (entityText is not null)
        {
            string decoded = Encoding.Latin1.GetString(entityBytes.ToArray()).TrimEnd('\0');
            if (!string.Equals(decoded, entityText, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{fieldPrefix}.EntityString does not match EntityStringBytes.");
            }
        }

        if (trigger is null)
            throw new InvalidDataException($"{fieldPrefix}.Trigger cannot be null.");
        IReadOnlyList<TriggerModel> models = trigger.Models ??
            throw new InvalidDataException($"{fieldPrefix}.Trigger.Models cannot be null.");
        IReadOnlyList<TriggerHull> hulls = trigger.Hulls ??
            throw new InvalidDataException($"{fieldPrefix}.Trigger.Hulls cannot be null.");
        IReadOnlyList<TriggerSlab> slabs = trigger.Slabs ??
            throw new InvalidDataException($"{fieldPrefix}.Trigger.Slabs cannot be null.");
        if (trigger.Count != models.Count ||
            trigger.HullCount != hulls.Count ||
            trigger.SlabCount != slabs.Count)
        {
            throw new InvalidDataException(
                $"{fieldPrefix}.Trigger counts must equal their semantic table counts.");
        }
        for (int index = 0; index < models.Count; index++)
        {
            TriggerModel model = models[index] ?? throw new InvalidDataException(
                $"{fieldPrefix}.Trigger.Models[{index}] cannot be null.");
            if ((uint)model.FirstHull + model.HullCount > hulls.Count)
            {
                throw new InvalidDataException(
                    $"{fieldPrefix}.Trigger.Models[{index}] hull range exceeds the hull table.");
            }
        }
        for (int index = 0; index < hulls.Count; index++)
        {
            TriggerHull hull = hulls[index] ?? throw new InvalidDataException(
                $"{fieldPrefix}.Trigger.Hulls[{index}] cannot be null.");
            if (hull.Bounds is null)
                throw new InvalidDataException($"{fieldPrefix}.Trigger.Hulls[{index}].Bounds cannot be null.");
            if ((uint)hull.FirstSlab + hull.SlabCount > slabs.Count)
            {
                throw new InvalidDataException(
                    $"{fieldPrefix}.Trigger.Hulls[{index}] slab range exceeds the slab table.");
            }
        }
        for (int index = 0; index < slabs.Count; index++)
        {
            if (slabs[index] is null)
                throw new InvalidDataException($"{fieldPrefix}.Trigger.Slabs[{index}] cannot be null.");
        }
    }

    public static bool IsEmptyCommon(
        IReadOnlyList<byte> entityBytes,
        int declaredEntityCount,
        MapTriggers trigger) =>
        entityBytes.Count == 0 &&
        declaredEntityCount == 0 &&
        trigger.Count == 0 &&
        trigger.HullCount == 0 &&
        trigger.SlabCount == 0 &&
        trigger.Models.Count == 0 &&
        trigger.Hulls.Count == 0 &&
        trigger.Slabs.Count == 0;
}
