using IW4.Game.Assets.Sound;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

/// <summary>Detached sound falloff curve with all sixteen serialized knot slots.</summary>
public sealed class SndCurveDraft
{
    private readonly SndCurveKnot[] _knots;
    private readonly SndCurveKnot[] _originalKnots;
    private readonly int _offset;
    private readonly XRuntimeAddress? _runtimeAddress;
    private readonly XPointer<string> _filenamePointer;
    private readonly ushort _padding;

    internal SndCurveDraft(SndCurve asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Filename = asset.Filename;
        KnotCount = asset.KnotCount;
        _padding = asset.Padding;
        _offset = asset.Offset;
        _runtimeAddress = asset.RuntimeAddress;
        _filenamePointer = asset.FilenamePointer;
        _knots = asset.Knots.ToArray();
        if (_knots.Length != SndCurve.MaxKnotCount)
            throw new InvalidDataException("A SndCurve requires sixteen serialized knot slots.");
        _originalKnots = _knots.ToArray();
    }

    private SndCurveDraft(SndCurveDraft source)
    {
        Filename = source.Filename;
        KnotCount = source.KnotCount;
        _padding = source._padding;
        _offset = source._offset;
        _runtimeAddress = source._runtimeAddress;
        _filenamePointer = source._filenamePointer;
        _knots = source._knots.ToArray();
        _originalKnots = source._originalKnots;
    }

    public string? Filename { get; }
    public int KnotCount { get; private set; }
    public IReadOnlyList<SndCurveKnot> ActiveKnots => Array.AsReadOnly(_knots[..Math.Min(KnotCount, SndCurve.MaxKnotCount)]);
    public string? EditBlocker
    {
        get
        {
            if (KnotCount > SndCurve.MaxKnotCount)
                return "This curve declares more than sixteen active knots.";
            if (KnotCount < 2)
                return "This curve has fewer than two active knots.";
            for (int index = 0; index < KnotCount; index++)
            {
                SndCurveKnot knot = _knots[index];
                if (!float.IsFinite(knot.X) || !float.IsFinite(knot.Y) ||
                    knot.X < 0 || knot.X > 1 || knot.Y < 0 || knot.Y > 1 ||
                    index > 0 && knot.X <= _knots[index - 1].X)
                    return "This curve has nonstandard knots outside the normalized, increasing graph range.";
            }
            return null;
        }
    }

    public SndCurveDraft Clone() => new(this);

    /// <summary>Replaces a nonstandard active curve with a usable linear fade.</summary>
    public void StartLinearFade()
    {
        Array.Copy(_originalKnots, _knots, _knots.Length);
        _knots[0] = new SndCurveKnot(0, 1);
        _knots[1] = new SndCurveKnot(1, 0);
        KnotCount = 2;
    }

    /// <summary>Moves one knot within normalized bounds and its current neighbors.</summary>
    public void MoveKnot(int index, float x, float y)
    {
        RequireEditable();
        RequireActiveIndex(index);
        RequireNormalized(x);
        RequireNormalized(y);
        if ((index == 0 || index == KnotCount - 1) && x != _knots[index].X)
            throw new ArgumentOutOfRangeException(nameof(x), "A curve endpoint must keep its original X position.");
        if (index > 0 && x <= _knots[index - 1].X ||
            index + 1 < KnotCount && x >= _knots[index + 1].X)
            throw new InvalidOperationException("Keep this point between its neighbors on the distance axis.");
        _knots[index] = new SndCurveKnot(x, y);
    }

    /// <summary>Inserts a knot at the interpolated volume and returns its active index.</summary>
    public int AddKnot(float x)
    {
        RequireEditable();
        RequireNormalized(x);
        if (KnotCount == SndCurve.MaxKnotCount)
            throw new InvalidOperationException("A SndCurve can have at most sixteen knots.");
        int index = 1;
        while (index < KnotCount && _knots[index].X < x)
            index++;
        if (index == KnotCount || x <= _knots[index - 1].X || x >= _knots[index].X)
            throw new ArgumentOutOfRangeException(nameof(x), "Choose a distinct point between two existing knots.");
        float y = VolumeAt(x);
        for (int slot = KnotCount; slot > index; slot--)
            _knots[slot] = _knots[slot - 1];
        _knots[index] = new SndCurveKnot(x, y);
        KnotCount++;
        return index;
    }

    public void RemoveKnot(int index)
    {
        RequireEditable();
        RequireActiveIndex(index);
        if (index == 0 || index == KnotCount - 1)
            throw new InvalidOperationException("The first and last knots are curve endpoints and cannot be removed.");
        for (int slot = index; slot + 1 < KnotCount; slot++)
            _knots[slot] = _knots[slot + 1];
        _knots[--KnotCount] = _originalKnots[KnotCount];
    }

    /// <summary>Samples the active segments with the playback falloff beyond the last knot.</summary>
    public float VolumeAt(float x)
    {
        RequireEditable();
        RequireNormalized(x);
        for (int index = 1; index < KnotCount; index++)
        {
            SndCurveKnot left = _knots[index - 1];
            SndCurveKnot right = _knots[index];
            if (x <= right.X)
            {
                float point = Math.Clamp((x - left.X) / (right.X - left.X), 0, 1);
                return Math.Clamp(left.Y + (right.Y - left.Y) * point, 0, 1);
            }
        }
        return 1 - x;
    }

    public bool Matches(SndCurveDraft other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Filename == other.Filename && KnotCount == other.KnotCount &&
            _padding == other._padding && _knots.Zip(other._knots).All(pair =>
                BitConverter.SingleToInt32Bits(pair.First.X) == BitConverter.SingleToInt32Bits(pair.Second.X) &&
                BitConverter.SingleToInt32Bits(pair.First.Y) == BitConverter.SingleToInt32Bits(pair.Second.Y));
    }

    public void ApplyEdits(SndCurveDraft before, SndCurveDraft after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.Matches(after))
            return;
        if (!Matches(before))
            throw new InvalidOperationException("This curve changed since the edit began; refresh it before applying edits.");
        if (Filename != after.Filename)
            throw new InvalidOperationException("A curve edit cannot change its asset identity.");
        KnotCount = after.KnotCount;
        Array.Copy(after._knots, _knots, _knots.Length);
    }

    internal SndCurve ToAsset() => new()
    {
        Offset = _offset,
        RuntimeAddress = _runtimeAddress,
        FilenamePointer = _filenamePointer,
        Filename = Filename,
        KnotCount = checked((ushort)KnotCount),
        Padding = _padding,
        Knots = _knots.ToArray()
    };

    private void RequireActiveIndex(int index)
    {
        if ((uint)index >= (uint)KnotCount)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    private void RequireEditable()
    {
        if (EditBlocker is { } reason)
            throw new InvalidOperationException(reason);
    }

    private static void RequireNormalized(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new InvalidOperationException("Enter a percentage from 0 to 100.");
    }
}

internal sealed class SndCurveAdapter : AssetAuthoringAdapter<SndCurve, SndCurveDraft>
{
    public override XAssetType AssetType => XAssetType.SndCurve;
    public override SndCurveDraft CreateDraft(SndCurve asset) => new(asset);
    public override SndCurveDraft CloneDraft(SndCurveDraft draft) => draft.Clone();
    public override SndCurve CreateDefinition(SndCurveDraft draft) => draft.ToAsset();
    public override bool SemanticallyEquals(SndCurveDraft left, SndCurveDraft right) => left.Matches(right);
}
