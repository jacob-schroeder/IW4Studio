using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Math;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>Frozen ComMap root and presence-owned primary-light table.</summary>
internal sealed class ComWorldLinkPlan : AssetLinkPlan
{
    private ComWorldLinkPlan(
        AssetKey key,
        string originalSerializedName,
        ComWorldAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        LinkStorageSymbol? lights = CreateLights(definition.PrimaryLights, freeze);
        var writer = new LinkTemplateWriter(ComWorldAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.IsInUse);
        writer.WriteInt32(definition.PrimaryLightCount);
        writer.Skip(sizeof(int));
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => lights is null
                ? [NameOperation(root, 0)]
                : [
                    NameOperation(root, 0),
                    PresenceOperation(
                        root,
                        0x0c,
                        lights,
                        "ComWorld.PrimaryLights")
                ]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        ComWorldAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        IReadOnlyList<ComPrimaryLight> lights = definition.PrimaryLights ??
            throw new InvalidDataException("ComWorld.PrimaryLights cannot be null.");
        if (definition.PrimaryLightCount < 0 ||
            definition.PrimaryLightCount > 0x10000 ||
            definition.PrimaryLightCount != lights.Count)
        {
            throw new InvalidDataException(
                "ComWorld.PrimaryLightCount must equal 0..65536 semantic light rows.");
        }
        for (int index = 0; index < lights.Count; index++)
        {
            if (lights[index] is null)
                throw new InvalidDataException($"ComWorld.PrimaryLights[{index}] cannot be null.");
        }

        if (originalSerializedName.StartsWith(','))
        {
            if (definition.IsInUse != 0 ||
                definition.PrimaryLightCount != 0 ||
                lights.Count != 0)
            {
                throw new InvalidDataException(
                    "A comma-prefixed ComMap provider must have a zeroed reference body.");
            }
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.ComMap,
                originalSerializedName,
                freeze);
        }

        return new ComWorldLinkPlan(key, originalSerializedName, definition, freeze);
    }

    private static LinkStorageSymbol? CreateLights(
        IReadOnlyList<ComPrimaryLight> lights,
        LinkAssetFreezeScope freeze)
    {
        if (lights.Count == 0)
            return null;

        var names = new LinkStorageSymbol?[lights.Count];
        var writer = new LinkTemplateWriter(
            checked(lights.Count * ComPrimaryLight.SerializedSize));
        for (int index = 0; index < lights.Count; index++)
        {
            ComPrimaryLight light = lights[index];
            names[index] = freeze.FreezeOptionalXString(
                light.DefName,
                light.DefNamePointer.Untyped,
                $"ComWorld.PrimaryLights[{index}].DefName");
            writer.WriteByte((byte)light.Type);
            writer.WriteByte(light.CanUseShadowMapRaw);
            writer.WriteByte(light.Exponent);
            writer.WriteByte(light.Unused);
            WriteVec3(writer, light.Color);
            WriteVec3(writer, light.Dir);
            WriteVec3(writer, light.Origin);
            writer.WriteSingle(light.Radius);
            writer.WriteSingle(light.CosHalfFovOuter);
            writer.WriteSingle(light.CosHalfFovInner);
            writer.WriteSingle(light.CosHalfFovExpanded);
            writer.WriteSingle(light.RotationLimit);
            writer.WriteSingle(light.TranslationLimit);
            writer.Skip(sizeof(int));
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
                    checked(item.index * ComPrimaryLight.SerializedSize + 0x40),
                    item.storage!,
                    $"ComWorld.PrimaryLights[{item.index}].DefName")));
    }

    private static void WriteVec3(LinkTemplateWriter writer, Vec3 value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        writer.WriteSingle(value.Z);
    }
}
