namespace IW4.Studio.Desktop.Editors.Gsc;

public enum GscEditorCompletionKind
{
    Function,
    ObservedFunction,
    Field,
    BuiltIn
}

/// <summary>Pure editor completion result; AvaloniaEdit owns its presentation.</summary>
public sealed record GscEditorCompletion(
    int ReplacementStart,
    string InsertionText,
    string DisplayText,
    string FilterText,
    string Description,
    GscEditorCompletionKind Kind = GscEditorCompletionKind.Function,
    double Priority = 0);

public sealed record GscEditorSignature(
    string Header,
    string ActiveParameterText);

/// <summary>Signature-help snapshot for the call containing the caret.</summary>
public sealed class GscEditorSignatureHelp
{
    private readonly IReadOnlyList<GscEditorSignature> _signatures;

    public GscEditorSignatureHelp(
        IEnumerable<GscEditorSignature> signatures,
        int activeParameter)
    {
        ArgumentNullException.ThrowIfNull(signatures);
        ArgumentOutOfRangeException.ThrowIfNegative(activeParameter);

        GscEditorSignature[] copiedSignatures = signatures.ToArray();
        if (copiedSignatures.Length == 0)
        {
            throw new ArgumentException(
                "Signature help requires at least one function.",
                nameof(signatures));
        }

        _signatures = Array.AsReadOnly(copiedSignatures);
        ActiveParameter = activeParameter;
    }

    public IReadOnlyList<GscEditorSignature> Signatures => _signatures;

    public int ActiveParameter { get; }
}
