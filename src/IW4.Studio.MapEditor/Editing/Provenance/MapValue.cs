using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.Editing.Provenance;

public enum MapValueProvenance
{
    ExactSerialized,
    ExactDecodedRuntime,
    Authored,
    Derived,
    Heuristic,
    Unknown
}

/// <summary>
/// One imported semantic value with the opaque binding that proves where it
/// came from. Detailed FastFile row metadata remains in the compilation layer.
/// </summary>
public sealed record MapValue<T>
{
    public MapValue(
        T value,
        MapValueProvenance provenance,
        SourceBindingId sourceBinding)
    {
        if (sourceBinding.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceBinding));

        Value = value;
        Provenance = provenance;
        SourceBinding = sourceBinding;
    }

    public T Value { get; }

    public MapValueProvenance Provenance { get; }

    public SourceBindingId SourceBinding { get; }
}
