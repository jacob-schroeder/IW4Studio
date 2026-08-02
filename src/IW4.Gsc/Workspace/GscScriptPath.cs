namespace IW4.Gsc.Workspace;

/// <summary>The compiler family selected by a RawFile script extension.</summary>
public enum GscScriptKind
{
    Gsc,
    Csc
}

/// <summary>
/// Canonical, host-independent identity of one GSC or CSC RawFile. The value
/// uses lower-case forward slashes and retains its compiler-family extension.
/// </summary>
public sealed record GscScriptPath
{
    private GscScriptPath(string value, GscScriptKind kind)
    {
        Value = value;
        Kind = kind;
    }

    public string Value { get; }

    public GscScriptKind Kind { get; }

    public static GscScriptPath FromAssetName(string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        string normalized = NormalizeSeparatorsAndCase(assetName);
        GscScriptKind kind = GetKind(normalized);
        return new GscScriptPath(normalized, kind);
    }

    public static GscScriptPath FromSourceReference(
        string sourceReference,
        GscScriptKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        string normalized = NormalizeSeparatorsAndCase(sourceReference);
        if (HasScriptExtension(normalized))
        {
            GscScriptKind explicitKind = GetKind(normalized);
            if (explicitKind != kind)
            {
                throw new ArgumentException(
                    "A source reference cannot cross GSC and CSC compiler families.",
                    nameof(sourceReference));
            }
        }
        else
        {
            normalized += GetExtension(kind);
        }

        return new GscScriptPath(normalized, kind);
    }

    public GscScriptPath ResolveReference(string sourceReference) =>
        FromSourceReference(sourceReference, Kind);

    public override string ToString() => Value;

    private static string NormalizeSeparatorsAndCase(string value)
    {
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A script path cannot have surrounding whitespace.", nameof(value));

        string normalized = value.Replace('\\', '/').ToLowerInvariant();
        if (normalized.Length == 0 ||
            normalized[0] == '/' ||
            normalized[^1] == '/' ||
            normalized.Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("A script path must be a non-rooted canonical asset path.", nameof(value));
        }

        return normalized;
    }

    private static bool HasScriptExtension(string value) =>
        value.EndsWith(".gsc", StringComparison.Ordinal) ||
        value.EndsWith(".csc", StringComparison.Ordinal);

    private static GscScriptKind GetKind(string normalizedAssetName)
    {
        if (normalizedAssetName.EndsWith(".gsc", StringComparison.Ordinal))
            return GscScriptKind.Gsc;
        if (normalizedAssetName.EndsWith(".csc", StringComparison.Ordinal))
            return GscScriptKind.Csc;

        throw new ArgumentException(
            "A script asset name must end in .gsc or .csc.",
            nameof(normalizedAssetName));
    }

    private static string GetExtension(GscScriptKind kind) => kind switch
    {
        GscScriptKind.Gsc => ".gsc",
        GscScriptKind.Csc => ".csc",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
