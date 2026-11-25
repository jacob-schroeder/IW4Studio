using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.DependencyGraph;

/// <summary>
/// Immutable workbench projection of the dependency load used to create the
/// current workspace.
/// </summary>
public sealed class DependencyGraphToolViewModel
{
    public DependencyGraphToolViewModel(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        LoadProfile = workspace.Document.Request.Mode switch
        {
            ZonePlan plan => $"{plan.ProfileName}.elf",
            _ => "Single file"
        };
        var nodes = workspace.DependencyGraph?.Nodes ?? [];
        Nodes = Array.AsReadOnly(nodes
            .Select((node, index) => new DependencyGraphNodeViewModel(
                node,
                hasPredecessor: index > 0,
                hasSuccessor: index < nodes.Count - 1))
            .ToArray());

        Summary = Nodes.Count == 0
            ? "No fastfiles loaded"
            : $"{Nodes.Count(node => node.IsLoaded)} loaded · {Nodes.Count(node => node.IsSkippedOptional)} optional";
    }

    public string LoadProfile { get; }

    public string Summary { get; }

    public IReadOnlyList<DependencyGraphNodeViewModel> Nodes { get; }
}
