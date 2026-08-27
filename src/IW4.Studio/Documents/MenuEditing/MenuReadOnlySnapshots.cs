using IW4.Assets.Assets.Menu;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>Detached read-only projection of a resolved Menu provider.</summary>
public sealed class MenuReadOnlySnapshot
{
    private MenuReadOnlySnapshot(MenuEditorSnapshot menu) => Menu = menu;

    public MenuEditorSnapshot Menu { get; }

    public static MenuReadOnlySnapshot CaptureResolvedProvider(
        AssetEditorSession editorSession)
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        if (editorSession.Definition is not MenuDefAsset definition)
            throw new InvalidDataException("The selected provider is not a Menu definition.");
        MenuDefAsset detached = new MenuGraphClone(false).CloneMenu(definition);
        return new MenuReadOnlySnapshot(MenuAssetProjector.Project(
            detached,
            MenuDocumentIdentity.Create(detached)));
    }
}

/// <summary>Detached read-only projection of a resolved MenuFile provider.</summary>
public sealed class MenuFileReadOnlySnapshot
{
    private MenuFileReadOnlySnapshot(MenuFileEditorSnapshot menuFile) => MenuFile = menuFile;

    public MenuFileEditorSnapshot MenuFile { get; }

    public static MenuFileReadOnlySnapshot CaptureResolvedProvider(
        AssetEditorSession editorSession)
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        if (editorSession.Definition is not MenuFileAsset definition)
            throw new InvalidDataException("The selected provider is not a MenuFile definition.");
        MenuFileAsset detached = MenuAssetProjector.Clone(definition);
        return new MenuFileReadOnlySnapshot(MenuAssetProjector.Project(
            detached,
            MenuFileDocumentIdentity.Create(detached)));
    }
}
