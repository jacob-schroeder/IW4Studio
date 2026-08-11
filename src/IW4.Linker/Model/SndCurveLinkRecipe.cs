using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen SndCurve recipe preserving all sixteen serialized knot slots.
/// </summary>
internal sealed class SndCurveLinkRecipe : AssetLinkRecipe
{
    private readonly int[] _knotWords;

    private SndCurveLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        ushort knotCount,
        ushort padding,
        int[] knotWords,
        bool requireReferencePlaceholder)
        : base(
            key,
            originalSerializedName,
            requireReferencePlaceholder)
    {
        KnotCount = knotCount;
        Padding = padding;
        _knotWords = knotWords;
    }

    private ushort KnotCount { get; }
    private ushort Padding { get; }

    public static SndCurveLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        SndCurve definition)
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
            return CreateReference(key, originalSerializedName);
        }

        if (sourceKnots.Count != SndCurve.MaxKnotCount)
        {
            throw new InvalidDataException(
                $"SndCurve requires all {SndCurve.MaxKnotCount} serialized " +
                "knot slots, including unused slots.");
        }

        return new SndCurveLinkRecipe(
            key,
            originalSerializedName,
            definition.KnotCount,
            definition.Padding,
            FreezeKnotWords(sourceKnots),
            requireReferencePlaceholder: false);
    }

    public static SndCurveLinkRecipe CreateExternal(
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
                SndCurve.SerializedSize,
                alignment: 4);
            output.WriteInt32(-1);
            output.WriteUInt16(KnotCount);
            output.WriteUInt16(Padding);
            foreach (int word in _knotWords)
                output.WriteInt32(word);

            EmitName(output);
        }
        finally
        {
            output.PopTempScope();
        }
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

    private static SndCurveLinkRecipe CreateReference(
        AssetKey key,
        string originalSerializedName) =>
        new(
            key,
            originalSerializedName,
            knotCount: 0,
            padding: 0,
            knotWords: new int[SndCurve.MaxKnotCount * 2],
            requireReferencePlaceholder: true);

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
