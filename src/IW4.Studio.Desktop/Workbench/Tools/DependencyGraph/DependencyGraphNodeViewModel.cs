using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.DependencyGraph;

/// <summary>
/// Presentation-ready state for one node in the dependency load flow.
/// </summary>
public sealed class DependencyGraphNodeViewModel
{
    internal DependencyGraphNodeViewModel(
        FastFileDependencyNode node,
        bool hasSuccessor)
    {
        ArgumentNullException.ThrowIfNull(node);
        FileName = node.FileName;
        PhysicalPath = node.PhysicalPath;
        IsTarget = node.IsTarget;
        HasSuccessor = hasSuccessor;
        Status = node.Status;
    }

    public string FileName { get; }

    public string PhysicalPath { get; }

    public bool IsTarget { get; }

    public bool HasSuccessor { get; }

    public FastFileDependencyLoadStatus Status { get; }

    public bool IsLoaded => Status == FastFileDependencyLoadStatus.Loaded;

    public bool IsSkippedOptional => Status == FastFileDependencyLoadStatus.SkippedOptional;

    public string StatusText => Status switch
    {
        FastFileDependencyLoadStatus.Loaded => IsTarget ? "Target · loaded" : "Loaded",
        FastFileDependencyLoadStatus.SkippedOptional => "Optional · not needed",
        _ => throw new ArgumentOutOfRangeException()
    };
}
