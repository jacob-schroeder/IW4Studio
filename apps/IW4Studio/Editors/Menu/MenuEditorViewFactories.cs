using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels.Menu;
using IW4.Studio.Documents;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.Editors.Menu;

/// <summary>Desktop factory for the shared Menu designer host.</summary>
public sealed class MenuEditorViewFactory : IAssetEditorViewFactory
{
    private readonly MenuEditingCoordinator _coordinator;
    private readonly AssetReferencePickerService _assetReferencePicker;
    private readonly IMenuPreviewMaterialResolver _materialResolver;
    private readonly IMenuTextResourceResolver _textResourceResolver;

    public MenuEditorViewFactory(
        MenuEditingCoordinator coordinator,
        AssetReferencePickerService assetReferencePicker,
        IMenuPreviewMaterialResolver materialResolver,
        IMenuTextResourceResolver textResourceResolver)
    {
        _coordinator = coordinator ??
            throw new ArgumentNullException(nameof(coordinator));
        _assetReferencePicker = assetReferencePicker ??
            throw new ArgumentNullException(nameof(assetReferencePicker));
        _materialResolver = materialResolver ??
            throw new ArgumentNullException(nameof(materialResolver));
        _textResourceResolver = textResourceResolver ??
            throw new ArgumentNullException(nameof(textResourceResolver));
    }

    public XAssetType AssetType => XAssetType.Menu;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        AssetEditorSession editorSession = surface as AssetEditorSession
            ?? throw new InvalidDataException("Menu requires an authoring session.");
        var viewModel = new MenuEditorViewModel(
            editorSession,
            _coordinator,
            _materialResolver,
            _textResourceResolver,
            canSelectAssetReferences: true,
            isAssetReferenceResolved: _assetReferencePicker.IsResolved);
        var view = new MenuEditorView(viewModel, _assetReferencePicker);
        return new AssetEditorViewHost(view, viewModel, usesWorkbenchScrollViewer: false);
    }
}

/// <summary>Desktop factory for MenuFile plus the shared Menu designer.</summary>
public sealed class MenuFileEditorViewFactory : IAssetEditorViewFactory
{
    private readonly MenuEditingCoordinator _coordinator;
    private readonly FastFileEditingSession _editingSession;
    private readonly Func<AssetPackageImportPreview,
        IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice>, int> _applyPackageImport;
    private readonly AssetReferencePickerService _assetReferencePicker;
    private readonly IMenuPreviewMaterialResolver _materialResolver;
    private readonly IMenuTextResourceResolver _textResourceResolver;

    public MenuFileEditorViewFactory(
        MenuEditingCoordinator coordinator,
        FastFileEditingSession editingSession,
        Func<AssetPackageImportPreview,
            IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice>, int> applyPackageImport,
        AssetReferencePickerService assetReferencePicker,
        IMenuPreviewMaterialResolver materialResolver,
        IMenuTextResourceResolver textResourceResolver)
    {
        _coordinator = coordinator ??
            throw new ArgumentNullException(nameof(coordinator));
        _editingSession = editingSession ??
            throw new ArgumentNullException(nameof(editingSession));
        _applyPackageImport = applyPackageImport ??
            throw new ArgumentNullException(nameof(applyPackageImport));
        _assetReferencePicker = assetReferencePicker ??
            throw new ArgumentNullException(nameof(assetReferencePicker));
        _materialResolver = materialResolver ??
            throw new ArgumentNullException(nameof(materialResolver));
        _textResourceResolver = textResourceResolver ??
            throw new ArgumentNullException(nameof(textResourceResolver));
    }

    public XAssetType AssetType => XAssetType.MenuFile;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        AssetEditorSession editorSession = surface as AssetEditorSession
            ?? throw new InvalidDataException("MenuFile requires an authoring session.");
        var viewModel = new MenuFileEditorViewModel(
            editorSession,
            _coordinator,
            _materialResolver,
            _textResourceResolver,
            canSelectAssetReferences: true,
            isAssetReferenceResolved: _assetReferencePicker.IsResolved);
        var view = new MenuFileEditorView(viewModel, _assetReferencePicker,
            _editingSession, _applyPackageImport);
        return new AssetEditorViewHost(view, viewModel, usesWorkbenchScrollViewer: false);
    }
}
