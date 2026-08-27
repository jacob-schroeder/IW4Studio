using Avalonia.Controls;
using IW4.FastFiles.Zone;
using IW4.Gsc.Analysis;
using IW4.Studio.Documents;
using IW4.Studio.Desktop.Gsc;
using IW4.Studio.Desktop.Editors.Gsc;
using IW4.Studio.Desktop.Editors.Localize;
using IW4.Studio.Desktop.Editors.Material;
using IW4.Studio.Desktop.Editors.MaterialTechset;
using IW4.Studio.Desktop.Editors.RawFile;
using IW4.Studio.Desktop.Editors.Sound;
using IW4.Studio.Desktop.Editors.StringTable;
using IW4.Studio.Desktop.Editors.StructuredData;
using IW4.Studio.Desktop.Editors.XAnim;
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
    private readonly Dictionary<XAssetType, FactoryRegistration> _factories = [];

    /// <summary>Production Desktop factories available in this Studio step.</summary>
    public static AssetEditorViewRegistry CreateDefault(
        GscWorkspaceIndexService? gscWorkspace = null,
        IGscSourceNavigator? gscSourceNavigator = null,
        IGscUsagesPresenter? gscUsagesPresenter = null,
        AssetReferencePickerService? assetReferencePicker = null,
        FastFileWorkspace? workspace = null)
    {
        var registry = new AssetEditorViewRegistry();
        registry.Register(new RawFileViewFactory(
            new GscAnalyzer(),
            gscWorkspace,
            gscSourceNavigator,
            gscUsagesPresenter));
        registry.Register(new SoundViewFactory(workspace));
        registry.Register(new StringTableViewFactory());
        registry.Register(new StructuredDataDefViewFactory());
        registry.Register(new LocalizeViewFactory());
        registry.Register(new MaterialViewFactory());
        registry.Register(new MaterialTechsetViewFactory());
        registry.Register(new XAnimViewFactory(workspace));
        registry.Register(new XModelViewFactory(assetReferencePicker));
        registry.Register(new WeaponViewFactory(assetReferencePicker));
        return registry;
    }

    public void Register(IAssetEditorViewFactory factory)
        => Register(factory, [factory.AssetType], _ => true);

    public void Register(
        IAssetEditorViewFactory factory,
        IEnumerable<XAssetType> assetTypes,
        Func<string?, bool> acceptsName)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(assetTypes);
        ArgumentNullException.ThrowIfNull(acceptsName);
        XAssetType[] registeredTypes = assetTypes.Distinct().ToArray();
        if (registeredTypes.Length == 0)
        {
            throw new ArgumentException(
                "An editor view factory must register at least one serialized asset type.",
                nameof(assetTypes));
        }

        XAssetType? undefinedType = registeredTypes
            .Cast<XAssetType?>()
            .FirstOrDefault(assetType => !Enum.IsDefined(assetType!.Value));
        if (undefinedType is not null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assetTypes),
                $"Editor view factory type '{undefinedType}' is not a defined serialized XAssetType.");
        }

        XAssetType? duplicateType = registeredTypes
            .Cast<XAssetType?>()
            .FirstOrDefault(assetType => _factories.ContainsKey(assetType!.Value));
        if (duplicateType is not null)
        {
            throw new InvalidOperationException(
                $"An editor view factory is already registered for serialized type '{duplicateType}'.");
        }

        var registration = new FactoryRegistration(factory, acceptsName);
        foreach (XAssetType assetType in registeredTypes)
            _factories.Add(assetType, registration);
    }

    public bool TryGetFactory(
        XAssetType assetType,
        string? assetName,
        out IAssetEditorViewFactory? factory)
    {
        if (_factories.TryGetValue(
                assetType,
                out FactoryRegistration? registration) &&
            registration.AcceptsName(assetName))
        {
            factory = registration.Factory;
            return true;
        }

        factory = null;
        return false;
    }

    public IAssetEditorViewFactory RequireFactory(
        XAssetType assetType,
        string? assetName) =>
        TryGetFactory(assetType, assetName, out IAssetEditorViewFactory? factory)
            ? factory!
            : throw new KeyNotFoundException(
                $"No Desktop editor view factory is registered for serialized type '{assetType}' and name '{assetName}'.");

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return RequireFactory(
                surface.Entry.AssetType,
                surface.Entry.OriginalName ?? surface.Entry.NormalizedName)
            .Create(surface)
            ?? throw new InvalidDataException(
                $"Desktop editor view factory for '{surface.Entry.AssetType}' returned no view host.");
    }

    private sealed record FactoryRegistration(
        IAssetEditorViewFactory Factory,
        Func<string?, bool> AcceptsName);
}
