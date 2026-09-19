using System.Globalization;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.ViewModels.Menu;

/// <summary>
/// Explicit Menu-to-Properties projection. Every writable row creates one of
/// the closed Studio Menu edits; no reflection or serialized graph mutation
/// occurs in Desktop.
/// </summary>
internal static partial class MenuInspectorProjection
{
    public static InspectorSelectionViewModel Create(
        MenuDesignerViewModel designer,
        MenuEditorSnapshot snapshot,
        MenuOutlineNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(designer);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(node);

        return node.Kind switch
        {
            MenuOutlineNodeKind.Menu => Menu(designer, snapshot),
            MenuOutlineNodeKind.Window => Window(
                designer,
                snapshot.Window.Value,
                isRoot: true,
                title: "Root Window",
                update: designer.IsEditable
                    ? designer.UpdateRootWindow
                    : null),
            MenuOutlineNodeKind.Items => Items(snapshot),
            MenuOutlineNodeKind.Item when node.NodeId is { } itemId =>
                Item(designer, snapshot, itemId),
            _ => throw new InvalidDataException(
                $"Unknown Menu outline selection '{node.Kind}'.")
        };
    }
}
