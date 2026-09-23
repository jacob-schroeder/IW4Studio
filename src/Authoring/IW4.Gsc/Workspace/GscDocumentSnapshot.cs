using IW4.Gsc.Syntax;

namespace IW4.Gsc.Workspace;

/// <summary>Immutable source snapshot for one normalized script identity.</summary>
public sealed record GscDocumentSnapshot
{
    public GscDocumentSnapshot(GscScriptPath path, GscSourceText source)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public GscScriptPath Path { get; }

    public GscSourceText Source { get; }
}
