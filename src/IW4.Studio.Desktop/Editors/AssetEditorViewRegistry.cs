using Avalonia.Controls;
using IW4.FastFiles.Zone;
using IW4.Gsc.Analysis;
using IW4.Studio.Documents;
using IW4.Studio.Gsc;
using IW4.Studio.Desktop.Editors.Gsc;
using IW4.Studio.Desktop.Editors.RawFile;
using IW4.Studio.Desktop.Editors.StringTable;
using IW4.Studio.Desktop.Workbench.Tools.GscUsages;

namespace IW4.Studio.Desktop.Editors;

/// <summary>
/// Desktop-only result of binding an Avalonia editor control to a backend
/// <see cref="AssetEditorSession"/>. Studio backend projects intentionally
/// know nothing about this type or Avalonia.
/// </summary>
public sealed record AssetEditorViewHost
{
    public AssetEditorViewHost(Control view, object viewModel)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(viewModel);
        View = view;
        ViewModel = viewModel;
    }

    public Control View { get; }

    public object ViewModel { get; }
}

/// <summary>
/// Desktop extension seam for one serialized asset type. Factories may create
/// Avalonia controls and view models, but they receive a session-owned draft
/// façade rather than creating or retaining drafts themselves.
/// </summary>
public interface IAssetEditorViewFactory
{
    XAssetType AssetType { get; }

    AssetEditorViewHost Create(AssetEditorSession editorSession);
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
        IGscUsagesPresenter? gscUsagesPresenter = null)
    {
        var registry = new AssetEditorViewRegistry();
        registry.Register(new RawFileViewFactory(
            new GscAnalyzer(),
            gscWorkspace,
            gscSourceNavigator,
            gscUsagesPresenter));
        registry.Register(new StringTableViewFactory());
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

    public AssetEditorViewHost Create(AssetEditorSession editorSession)
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        return RequireFactory(editorSession.Entry.AssetType).Create(editorSession)
            ?? throw new InvalidDataException(
                $"Desktop editor view factory for '{editorSession.Entry.AssetType}' returned no view host.");
    }
}
