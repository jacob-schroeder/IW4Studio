namespace IW4.Studio.Documents;

/// <summary>One immutable Studio document-open request.</summary>
public sealed record FastFileDocumentOpenRequest
{
    public FastFileDocumentOpenRequest(string path, FastFileOpenMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(mode);

        Path = path;
        Mode = mode;
    }

    public string Path { get; }
    public FastFileOpenMode Mode { get; }
}
