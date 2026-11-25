using IW4.Linker.Linking;

namespace IW4.Studio.Documents;

/// <summary>An immutable edit starting point for the target zone's script strings.</summary>
public sealed class ScriptStringEditSnapshot
{
    internal ScriptStringEditSnapshot(
        FastFileEditingSession owner,
        long revision,
        IReadOnlyList<ZoneScriptStringRow> rows)
    {
        Owner = owner;
        Revision = revision;
        Rows = rows;
    }

    internal FastFileEditingSession Owner { get; }
    public long Revision { get; }
    public IReadOnlyList<ZoneScriptStringRow> Rows { get; }
}
