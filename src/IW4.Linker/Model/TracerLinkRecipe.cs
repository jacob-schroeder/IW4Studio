using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Tracer;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen TracerDef body. Its Material field is a provider AliasCell and is
/// therefore resolved through the selected provider graph.
/// </summary>
internal sealed class TracerLinkRecipe : AssetLinkRecipe
{
    private TracerLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        AssetDependency? material,
        uint drawInterval,
        int[] floatWords,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        var writer = new LinkTemplateWriter(TracerDefAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.WriteUInt32(drawInterval);
        foreach (int word in floatWords)
            writer.WriteInt32(word);
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => material is { } dependency
                ? [NameOperation(root, 0), ProviderOperation(root, sizeof(int), dependency)]
                : [NameOperation(root, 0)]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        TracerDefAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        IReadOnlyList<TracerColor> colors = definition.Colors ??
            throw new InvalidDataException("Tracer colors cannot be null.");
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition, colors);
            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.Tracer,
                originalSerializedName,
                freeze);
        }

        if (colors.Count != TracerDefAsset.ColorCount)
        {
            throw new InvalidDataException(
                $"Tracer requires exactly {TracerDefAsset.ColorCount} serialized color rows.");
        }

        AssetDependency? material = FreezeProviderDependency(
            definition.MaterialPointer.Untyped,
            definition.Material,
            XAssetType.Material,
            "Tracer.Material");

        return new TracerLinkRecipe(
            key,
            originalSerializedName,
            material,
            definition.DrawInterval,
            FreezeFloatWords(definition, colors),
            freeze);
    }

    private static int[] FreezeFloatWords(
        TracerDefAsset definition,
        IReadOnlyList<TracerColor> colors)
    {
        var words = new int[5 + TracerDefAsset.ColorCount * 4];
        words[0] = BitConverter.SingleToInt32Bits(definition.Speed);
        words[1] = BitConverter.SingleToInt32Bits(definition.BeamLength);
        words[2] = BitConverter.SingleToInt32Bits(definition.BeamWidth);
        words[3] = BitConverter.SingleToInt32Bits(definition.ScrewRadius);
        words[4] = BitConverter.SingleToInt32Bits(definition.ScrewDistance);
        for (int index = 0; index < colors.Count; index++)
        {
            TracerColor color = colors[index];
            int destination = 5 + index * 4;
            words[destination] = BitConverter.SingleToInt32Bits(color.Red);
            words[destination + 1] = BitConverter.SingleToInt32Bits(color.Green);
            words[destination + 2] = BitConverter.SingleToInt32Bits(color.Blue);
            words[destination + 3] = BitConverter.SingleToInt32Bits(color.Alpha);
        }

        return words;
    }

    private static void ValidateReferenceShape(
        TracerDefAsset definition,
        IReadOnlyList<TracerColor> colors)
    {
        if (definition.Material is not null ||
            definition.MaterialPointer.Raw != 0 ||
            definition.DrawInterval != 0)
        {
            throw new InvalidDataException(
                "A comma-prefixed Tracer provider must have a null Material and zero draw interval.");
        }
        if (colors.Count is not (0 or TracerDefAsset.ColorCount))
        {
            throw new InvalidDataException(
                "A comma-prefixed Tracer provider must have zero or five color rows.");
        }

        int[] words = FreezeFloatWords(definition, colors);
        if (words.Any(word => word != 0))
        {
            throw new InvalidDataException(
                "A comma-prefixed Tracer provider must have zeroed scalar and color fields.");
        }
    }

}
