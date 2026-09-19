using IW4.FastFiles.Loaders.Database.Planning;
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

    public DbDependencyRequestLoadStatus Status { get; }

    public bool IsLoaded => Status == DbDependencyRequestLoadStatus.Loaded;

    public bool IsSkippedOptional => Status == DbDependencyRequestLoadStatus.SkippedOptional;

    public string StatusText => Status switch
    {
        DbDependencyRequestLoadStatus.Loaded => IsTarget ? "Target · loaded" : "Loaded",
        DbDependencyRequestLoadStatus.SkippedOptional => "Optional · not needed",
        _ => throw new ArgumentOutOfRangeException()
    };
}
