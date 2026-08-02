namespace IW4.Studio.Documents;

/// <summary>Immutable load policy for a document service.</summary>
public sealed record FastFileDocumentServiceOptions
{
    public FastFileDocumentServiceOptions(string? dependencyDirectory = null)
    {
        if (dependencyDirectory is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(dependencyDirectory);

        DependencyDirectory = dependencyDirectory is null
            ? null
            : Path.GetFullPath(dependencyDirectory);
    }

    /// <summary>
    /// Optional catalog root used for dependency-plan opens. When absent, the
    /// selected fastfile's containing directory is used.
    /// </summary>
    public string? DependencyDirectory { get; }

    public static FastFileDocumentServiceOptions Default { get; } = new();
}
