using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.ViewModels.Menu;

public enum MenuOutlineNodeKind
{
    Menu,
    Window,
    Items,
    Item
}

/// <summary>
/// Presentation-only outline node carrying the stable editor identity owned
/// by the Studio snapshot. The identity is never serialized.
/// </summary>
public sealed class MenuOutlineNodeViewModel
{
    internal MenuOutlineNodeViewModel(
        string key,
        string title,
        MenuOutlineNodeKind kind,
        MenuNodeId? nodeId = null,
        int? itemIndex = null,
        IEnumerable<MenuOutlineNodeViewModel>? children = null,
        bool isExpanded = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Key = key;
        Title = title;
        Kind = kind;
        NodeId = nodeId;
        ItemIndex = itemIndex;
        Children = Array.AsReadOnly(children?.ToArray() ?? []);
        IsExpanded = isExpanded;
    }

    public string Key { get; }

    public string Title { get; }

    public MenuOutlineNodeKind Kind { get; }

    public string KindText => Kind.ToString();

    public MenuNodeId? NodeId { get; }

    public int? ItemIndex { get; }

    public IReadOnlyList<MenuOutlineNodeViewModel> Children { get; }

    public bool IsExpanded { get; set; }
}
