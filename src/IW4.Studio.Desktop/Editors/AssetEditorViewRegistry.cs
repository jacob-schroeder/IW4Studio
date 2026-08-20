using Avalonia.Controls;
using IW4.FastFiles.Zone;
using IW4.Gsc.Analysis;
using IW4.Studio.Documents;
using IW4.Studio.Desktop.Gsc;
using IW4.Studio.Desktop.Editors.Gsc;
using IW4.Studio.Desktop.Editors.Localize;
using IW4.Studio.Desktop.Editors.MaterialTechset;
using IW4.Studio.Desktop.Editors.RawFile;
using IW4.Studio.Desktop.Editors.StringTable;
using IW4.Studio.Desktop.Editors.StructuredData;
using IW4.Studio.Desktop.Editors.XModel;
using IW4.Studio.Desktop.Editors.Weapon;
using IW4.Studio.Desktop.Workbench.Tools.GscUsages;
using IW4.Studio.Desktop.Editors.AssetReferences;

namespace IW4.Studio.Desktop.Editors;

/// <summary>
/// Desktop-only result of binding an Avalonia editor control to a backend
/// <see cref="AssetEditorSurface"/>. Studio backend projects intentionally
/// know nothing about this type or Avalonia.
/// </summary>
public sealed record AssetEditorViewHost
{
    public AssetEditorViewHost(
        Control view,
        object viewModel,
        bool usesWorkbenchScrollViewer = true)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(viewModel);
        View = view;
        ViewModel = viewModel;
        UsesWorkbenchScrollViewer = usesWorkbenchScrollViewer;
    }

    public Control View { get; }

    public object ViewModel { get; }

    public bool UsesWorkbenchScrollViewer { get; }
}

/// <summary>
/// Desktop extension seam for one serialized asset type. Factories may create
/// Avalonia controls and view models from the backend-owned editable or
/// structural surface for the selected catalog entry.
/// </summary>
public interface IAssetEditorViewFactory
{
    XAssetType AssetType { get; }

    AssetEditorViewHost Create(AssetEditorSurface surface);
}

/// <summary>
/// Desktop-only view/view-model registry, separate from backend authoring
/// adapter registration. No production editor factories are registered until
/// concrete editor work begins.
/// </summary>
public sealed class AssetEditorViewRegistry
{
    private readonly Dictionary<XAssetType, IAssetEditorViewFactory> _factories = [];

    /// <summary>Production Desktop factories available in this Studio step.</summary>
    public static AssetEditorViewRegistry CreateDefault(
        GscWorkspaceIndexService? gscWorkspace = null,
        IGscSourceNavigator? gscSourceNavigator = null,
        IGscUsagesPresenter? gscUsagesPresenter = null,
        AssetReferencePickerService? assetReferencePicker = null)
    {
        var registry = new AssetEditorViewRegistry();
        registry.Register(new RawFileViewFactory(
            new GscAnalyzer(),
            gscWorkspace,
            gscSourceNavigator,
            gscUsagesPresenter));
        registry.Register(new StringTableViewFactory());
        registry.Register(new StructuredDataDefViewFactory());
        registry.Register(new LocalizeViewFactory());
        registry.Register(new MaterialTechsetViewFactory());
        registry.Register(new XModelViewFactory(assetReferencePicker));
        registry.Register(new WeaponViewFactory(assetReferencePicker));
        return registry;
    }

    public void Register(IAssetEditorViewFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!Enum.IsDefined(factory.AssetType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(factory),
                $"Editor view factory type '{factory.AssetType}' is not a defined serialized XAssetType.");
        }
        if (!_factories.TryAdd(factory.AssetType, factory))
        {
            throw new InvalidOperationException(
                $"An editor view factory is already registered for serialized type '{factory.AssetType}'.");
        }
    }

    public bool TryGetFactory(XAssetType assetType, out IAssetEditorViewFactory? factory) =>
        _factories.TryGetValue(assetType, out factory);

    public IAssetEditorViewFactory RequireFactory(XAssetType assetType) =>
        TryGetFactory(assetType, out IAssetEditorViewFactory? factory)
            ? factory!
            : throw new KeyNotFoundException(
                $"No Desktop editor view factory is registered for serialized type '{assetType}'.");

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return RequireFactory(surface.Entry.AssetType).Create(surface)
            ?? throw new InvalidDataException(
                $"Desktop editor view factory for '{surface.Entry.AssetType}' returned no view host.");
    }
}
