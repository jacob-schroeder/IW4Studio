namespace IW4.Studio.Desktop.ViewModels;

public sealed class AssetTreeNode
{
    public AssetTreeNode(
        string name,
        string detail,
        string icon,
        string kindLabel,
        string description,
        string address,
        string toolTipText,
        bool isGroup,
        IReadOnlyList<AssetTreeNode>? children = null,
        AssetExplorerEntryViewModel? explorerEntry = null)
    {
        Name = name;
        Detail = detail;
        Icon = icon;
        KindLabel = kindLabel;
        Description = description;
        Address = address;
        ToolTipText = toolTipText;
        IsGroup = isGroup;
        Children = children ?? Array.Empty<AssetTreeNode>();
        ExplorerEntry = explorerEntry;
    }

    public string Name { get; }

    public string Detail { get; }

    public string Icon { get; }

    public string KindLabel { get; }

    public string Description { get; }

    public string Address { get; }

    public string ToolTipText { get; }

    public bool IsGroup { get; }

    public IReadOnlyList<AssetTreeNode> Children { get; }

    /// <summary>Null for grouping nodes; present for a stable catalog item.</summary>
    public AssetExplorerEntryViewModel? ExplorerEntry { get; }
}
