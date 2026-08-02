namespace IW4.Studio.MapEditor.Editing.Objects;

public readonly record struct MapVector3(float X, float Y, float Z)
{
    public bool IsFinite =>
        float.IsFinite(X) &&
        float.IsFinite(Y) &&
        float.IsFinite(Z);

    public static MapVector3 operator +(
        MapVector3 left,
        MapVector3 right) =>
        new(
            left.X + right.X,
            left.Y + right.Y,
            left.Z + right.Z);

    public static MapVector3 operator -(
        MapVector3 left,
        MapVector3 right) =>
        new(
            left.X - right.X,
            left.Y - right.Y,
            left.Z - right.Z);

    public override string ToString() =>
        FormattableString.Invariant($"{X:0.###}, {Y:0.###}, {Z:0.###}");
}

public readonly record struct MapBounds(MapVector3 MidPoint, MapVector3 HalfSize)
{
    public bool IsFinite =>
        MidPoint.IsFinite &&
        HalfSize.IsFinite;

    public MapBounds Translate(MapVector3 offset) =>
        new(MidPoint + offset, HalfSize);

    public override string ToString() =>
        FormattableString.Invariant(
            $"mid ({MidPoint}), half ({HalfSize})");
}

/// <summary>
/// Immutable, renderer-neutral transform projection for a static-model row.
/// Phase 5 changes translation only; imported scale and bounds remain part of
/// the state so command transitions can be checked and reversed atomically.
/// </summary>
public readonly record struct EditorStaticModelTransformState(
    MapVector3 Origin,
    float? Scale,
    MapBounds? Bounds)
{
    /// <summary>
    /// Creates an absolute translation from this state as the immutable
    /// anchor. Callers that support repeated edits should invoke this on the
    /// imported transform so the same destination always produces the same
    /// bounds, independent of edit history.
    /// </summary>
    public EditorStaticModelTransformState WithOrigin(
        MapVector3 origin)
    {
        if (!origin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(origin),
                "Static-model origins must contain only finite components.");
        }
        if (!Origin.IsFinite)
        {
            throw new InvalidOperationException(
                "The current static-model origin is not finite.");
        }
        if (Bounds is { IsFinite: false })
        {
            throw new InvalidOperationException(
                "The current static-model bounds are not finite.");
        }

        MapVector3 translation = origin - Origin;
        if (!translation.IsFinite)
        {
            throw new InvalidOperationException(
                "The requested static-model translation is outside the finite map coordinate range.");
        }

        MapBounds? translatedBounds =
            Bounds?.Translate(translation);
        if (translatedBounds is { IsFinite: false })
        {
            throw new InvalidOperationException(
                "The requested static-model translation produces non-finite bounds.");
        }

        return this with
        {
            Origin = origin,
            Bounds = translatedBounds
        };
    }
}
