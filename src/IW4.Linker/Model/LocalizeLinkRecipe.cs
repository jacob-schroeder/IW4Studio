using IW4.Assets.Assets.Localize;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen Localize value/name recipe. Each semantic XString occurrence owns a
/// canonical inline body; equal text is not treated as storage identity.
/// </summary>
internal sealed class LocalizeLinkRecipe : AssetLinkRecipe
{
    private readonly byte[]? _value;

    private LocalizeLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        byte[]? value,
        bool requireReferencePlaceholder)
        : base(
            key,
            originalSerializedName,
            requireReferencePlaceholder)
    {
        _value = value;
    }

    public static LocalizeLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        LocalizeAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (originalSerializedName.StartsWith(','))
        {
            if (definition.Value is not null)
            {
                throw new InvalidDataException(
                    "A comma-prefixed Localize provider must have a null value.");
            }

            return CreateReference(key, originalSerializedName);
        }

        return new LocalizeLinkRecipe(
            key,
            originalSerializedName,
            FreezeOptionalXString(definition.Value, "Localize.Value"),
            requireReferencePlaceholder: false);
    }

    public static LocalizeLinkRecipe CreateExternal(
        AssetKey key,
        string originalSerializedName) =>
        CreateReference(key, originalSerializedName);

    public override void Emit(
        ZoneEmissionWriter output,
        Action<AssetDependency, XBlockAddress, int> emitDependency)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(emitDependency);

        output.PushTempScope();
        try
        {
            output.Allocate(
                XFileBlockType.TEMP,
                LocalizeAsset.SerializedSize,
                alignment: 4);
            output.WriteInt32(XStringSourcePointer(_value));
            output.WriteInt32(-1);

            EmitFrozenXString(output, _value);
            EmitName(output);
        }
        finally
        {
            output.PopTempScope();
        }
    }

    private static LocalizeLinkRecipe CreateReference(
        AssetKey key,
        string originalSerializedName) =>
        new(
            key,
            originalSerializedName,
            value: null,
            requireReferencePlaceholder: true);
}
