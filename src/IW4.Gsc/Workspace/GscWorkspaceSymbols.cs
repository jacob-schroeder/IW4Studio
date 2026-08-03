using IW4.Gsc.Analysis;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Workspace;

public enum GscWorkspaceSymbolKind
{
    Function,
    Define,
    Parameter,
    Local
}

public enum GscWorkspaceReferenceKind
{
    Declaration,
    Read,
    Write,
    ReadWrite,
    Call,
    FunctionReference
}

public sealed record GscSourceLocation(
    GscScriptPath Path,
    GscTextSpan Span);

public sealed record GscSymbolId(
    GscSourceLocation Declaration,
    GscWorkspaceSymbolKind Kind);

/// <summary>
/// A symbol identity with both its canonical binding name and exact source
/// spelling.
/// </summary>
public sealed record GscSymbolDefinition(
    GscSymbolId Id,
    string Name,
    string SourceName,
    GscSymbolId? ContainingFunction = null,
    int? ParameterOrdinal = null)
{
    public GscSourceLocation Location => Id.Declaration;

    public GscWorkspaceSymbolKind Kind => Id.Kind;
}

public sealed class GscFunctionDefinition
{
    private readonly IReadOnlyList<GscSymbolDefinition> _parameters;

    internal GscFunctionDefinition(
        GscSymbolDefinition symbol,
        IEnumerable<GscSymbolDefinition> parameters,
        string declarationSignature)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(declarationSignature);
        _parameters = Array.AsReadOnly(parameters.ToArray());
        DeclarationSignature = declarationSignature;
    }

    public GscSymbolDefinition Symbol { get; }

    public string Name => Symbol.Name;

    public string SourceName => Symbol.SourceName;

    public GscSourceLocation Location => Symbol.Location;

    public IReadOnlyList<GscSymbolDefinition> Parameters => _parameters;

    /// <summary>
    /// Exact source text from the function name through its closing parameter
    /// parenthesis, excluding the body.
    /// </summary>
    public string DeclarationSignature { get; }
}

public sealed class GscSymbolReference
{
    private readonly IReadOnlyList<GscSymbolId> _targets;

    internal GscSymbolReference(
        GscSourceLocation location,
        string name,
        string sourceName,
        GscWorkspaceReferenceKind kind,
        IEnumerable<GscSymbolId> targets,
        GscScriptPath? qualifiedTargetPath = null)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(targets);
        Name = name;
        SourceName = sourceName;
        Kind = kind;
        _targets = Array.AsReadOnly(targets.Distinct().ToArray());
        QualifiedTargetPath = qualifiedTargetPath;
    }

    public GscSourceLocation Location { get; }

    public string Name { get; }

    /// <summary>Exact identifier spelling at this reference.</summary>
    public string SourceName { get; }

    public GscWorkspaceReferenceKind Kind { get; }

    public IReadOnlyList<GscSymbolId> Targets => _targets;

    /// <summary>
    /// The script named before <c>::</c>, or null for a local/native-style
    /// callable and for non-call references.
    /// </summary>
    public GscScriptPath? QualifiedTargetPath { get; }
}

public sealed record GscIncludeReference(
    GscSourceLocation Location,
    GscScriptPath TargetPath);

public sealed class GscIndexedDocument
{
    private readonly IReadOnlyList<GscSymbolDefinition> _definitions;
    private readonly IReadOnlyList<GscSymbolReference> _references;
    private readonly IReadOnlyList<GscIncludeReference> _includes;
    private readonly IReadOnlyList<GscFunctionDefinition> _functions;
    private readonly IReadOnlyList<GscObservedField> _observedFields;

    internal GscIndexedDocument(
        GscDocumentSnapshot snapshot,
        GscAnalysisResult analysis,
        IEnumerable<GscSymbolDefinition> definitions,
        IEnumerable<GscSymbolReference> references,
        IEnumerable<GscIncludeReference> includes,
        IEnumerable<GscFunctionDefinition> functions,
        IEnumerable<GscObservedField> observedFields)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        _definitions = Array.AsReadOnly(definitions.ToArray());
        _references = Array.AsReadOnly(references.ToArray());
        _includes = Array.AsReadOnly(includes.ToArray());
        _functions = Array.AsReadOnly(functions.ToArray());
        _observedFields = Array.AsReadOnly(observedFields.ToArray());
    }

    public GscDocumentSnapshot Snapshot { get; }

    public GscScriptPath Path => Snapshot.Path;

    public GscAnalysisResult Analysis { get; }

    public IReadOnlyList<GscSymbolDefinition> Definitions => _definitions;

    public IReadOnlyList<GscSymbolReference> References => _references;

    public IReadOnlyList<GscIncludeReference> Includes => _includes;

    public IReadOnlyList<GscFunctionDefinition> Functions => _functions;

    public IReadOnlyList<GscObservedField> ObservedFields => _observedFields;
}
