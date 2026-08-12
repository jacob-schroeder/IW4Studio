using IW4.FastFiles.Loaders.Database.Planning;

namespace IW4.Studio.Documents;

public sealed record FastFileDependencyNode(
    string PhysicalPath,
    DbDependencyRequestLoadStatus Status,
    bool IsTarget)
{
    public string FileName => Path.GetFileName(PhysicalPath);
}

/// <summary>Ordered physical request outcome for the workspace load.</summary>
public sealed class FastFileDependencyGraph
{
    internal FastFileDependencyGraph(IEnumerable<FastFileDependencyNode> nodes)
    {
        FastFileDependencyNode[] copy = nodes?.ToArray() ?? throw new ArgumentNullException(nameof(nodes));
        if (copy.Count(node => node.IsTarget) != 1)
            throw new ArgumentException("A dependency graph requires exactly one target node.", nameof(nodes));
        Nodes = Array.AsReadOnly(copy);
    }

    public IReadOnlyList<FastFileDependencyNode> Nodes { get; }
}
