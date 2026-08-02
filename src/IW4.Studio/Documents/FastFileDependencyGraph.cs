namespace IW4.Studio.Documents;

/// <summary>
/// Outcome of one physical fastfile request in a document's dependency load.
/// </summary>
public enum FastFileDependencyLoadStatus
{
    Loaded,
    SkippedOptional,
    MissingRequired
}

/// <summary>
/// One ordered fastfile node in the dependency load graph.
/// </summary>
public sealed record FastFileDependencyNode
{
    internal FastFileDependencyNode(
        string physicalPath,
        FastFileDependencyLoadStatus status,
        bool isTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalPath);

        PhysicalPath = Path.GetFullPath(physicalPath);
        FileName = Path.GetFileName(PhysicalPath);
        Status = status;
        IsTarget = isTarget;
    }

    public string PhysicalPath { get; }

    public string FileName { get; }

    public FastFileDependencyLoadStatus Status { get; }

    public bool IsTarget { get; }
}

/// <summary>
/// Immutable, dependency-ordered projection of the physical fastfiles
/// considered while opening a Studio document.
/// </summary>
public sealed record FastFileDependencyGraph
{
    internal FastFileDependencyGraph(
        IEnumerable<FastFileDependencyNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        FastFileDependencyNode[] snapshot = nodes.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "A fastfile dependency graph requires at least one node.",
                nameof(nodes));
        }
        if (snapshot.Count(node => node.IsTarget) != 1)
        {
            throw new ArgumentException(
                "A fastfile dependency graph requires exactly one target node.",
                nameof(nodes));
        }

        Nodes = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<FastFileDependencyNode> Nodes { get; }
}
