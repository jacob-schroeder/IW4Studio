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

        Header = workspace.Document.Request.Mode switch
        {
            Isolated => "Single File",
            ZonePlan plan => $"{plan.ProfileName}.elf Dependencies",
            _ => "Fastfile Dependencies"
        };

        FastFileDependencyNode[] nodes = workspace.DependencyGraph.Nodes.ToArray();
        Nodes = Array.AsReadOnly(nodes
            .Select((node, index) => new DependencyGraphNodeViewModel(
                node,
                hasSuccessor: index < nodes.Length - 1))
            .ToArray());
    }

    public string Header { get; }

    public IReadOnlyList<DependencyGraphNodeViewModel> Nodes { get; }
}
