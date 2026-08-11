using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen SndCurve plan preserving all sixteen serialized knot slots.
/// </summary>
internal sealed class SndCurveLinkPlan : AssetLinkPlan
{
    private SndCurveLinkPlan(
        AssetKey key,
        string originalSerializedName,
        ushort knotCount,
        ushort padding,
        int[] knotWords,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        var writer = new LinkTemplateWriter(SndCurve.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteUInt16(knotCount);
        writer.WriteUInt16(padding);
        foreach (int word in knotWords)
            writer.WriteInt32(word);
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => [NameOperation(root, 0)]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        SndCurve definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.KnotCount > SndCurve.MaxKnotCount)
        {
            throw new InvalidDataException(
                $"SndCurve knot count {definition.KnotCount} exceeds the fixed " +
                $"capacity of {SndCurve.MaxKnotCount}.");
        }

        IReadOnlyList<SndCurveKnot> sourceKnots = definition.Knots
            ?? throw new InvalidDataException("SndCurve knots cannot be null.");
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition, sourceKnots);
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.SndCurve,
                originalSerializedName,
                freeze);
        }

        if (sourceKnots.Count != SndCurve.MaxKnotCount)
        {
            throw new InvalidDataException(
                $"SndCurve requires all {SndCurve.MaxKnotCount} serialized " +
                "knot slots, including unused slots.");
        }

        return new SndCurveLinkPlan(
            key,
            originalSerializedName,
            definition.KnotCount,
            definition.Padding,
            FreezeKnotWords(sourceKnots),
            freeze);
    }

    private static void ValidateReferenceShape(
        SndCurve definition,
        IReadOnlyList<SndCurveKnot> knots)
    {
        if (definition.KnotCount != 0 || definition.Padding != 0)
        {
            throw new InvalidDataException(
                "A comma-prefixed SndCurve provider must have zero count and padding.");
        }
        if (knots.Count is not (0 or SndCurve.MaxKnotCount))
        {
            throw new InvalidDataException(
                "A comma-prefixed SndCurve provider must have zero or sixteen knot slots.");
        }
        if (knots.Any(knot =>
                BitConverter.SingleToInt32Bits(knot.X) != 0 ||
                BitConverter.SingleToInt32Bits(knot.Y) != 0))
        {
            throw new InvalidDataException(
                "A comma-prefixed SndCurve provider must have zeroed knot slots.");
        }
    }

    private static int[] FreezeKnotWords(
        IReadOnlyList<SndCurveKnot> knots)
    {
        var words = new int[checked(knots.Count * 2)];
        for (int index = 0; index < knots.Count; index++)
        {
            words[index * 2] = BitConverter.SingleToInt32Bits(knots[index].X);
            words[index * 2 + 1] = BitConverter.SingleToInt32Bits(knots[index].Y);
        }

        return words;
    }
}
