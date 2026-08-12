using Avalonia.Controls;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents.AssetReferences;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.Editors.AssetReferences;

/// <summary>
/// Reusable Desktop modal service for typed asset-reference inspector rows.
/// The selected scalar name is returned through the row's existing commit
/// callback; the service retains no asset-pool object.
/// </summary>
public sealed class AssetReferencePickerService
{
    private readonly WorkspaceAssetReferenceCatalog _catalog;
    private readonly IMenuPreviewMaterialResolver _materialResolver;

    public AssetReferencePickerService(
        WorkspaceAssetReferenceCatalog catalog,
        IMenuPreviewMaterialResolver materialResolver)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _materialResolver = materialResolver ??
            throw new ArgumentNullException(nameof(materialResolver));
    }

    public bool IsResolved(XAssetType assetType, string? assetName) =>
        string.IsNullOrWhiteSpace(assetName) ||
        _catalog.Find(assetType, assetName)?.IsResolved == true;

    public async Task ShowAsync(
        Window owner,
        InspectorAssetReferencePropertyRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(row);

        AssetReferencePickerResult? result = await SelectCoreAsync(
            owner,
            row.AssetType,
            row.AssetName);
        if (result is not null)
            _ = row.AcceptSelection(result.Name, result.IsMissing);
    }

    public async Task<string?> SelectNameAsync(
        Window owner,
        XAssetType assetType,
        string? currentName = null)
    {
        AssetReferencePickerResult? result = await SelectCoreAsync(
            owner,
            assetType,
            currentName);
        return result?.Name;
    }

    private async Task<AssetReferencePickerResult?> SelectCoreAsync(
        Window owner,
        XAssetType assetType,
        string? currentName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var viewModel = new AssetReferencePickerViewModel(
            _catalog,
            assetType,
            currentName,
            _materialResolver);
        var dialog = new AssetReferencePickerWindow(viewModel);
        return await dialog.ShowDialog<AssetReferencePickerResult?>(owner);
    }
}

public sealed class AssetReferenceSelectionRequestedEventArgs : EventArgs
{
    public AssetReferenceSelectionRequestedEventArgs(
        InspectorAssetReferencePropertyRowViewModel row)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
    }

    public InspectorAssetReferencePropertyRowViewModel Row { get; }
}
