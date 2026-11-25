using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class FastFileAssetsNavigatorViewModel
{
    internal bool CanAddReference(FastFileContentsRow? row) =>
        !_disposed && row?.CatalogEntry is { } asset && _editor.EditingSession.CanAddReference(asset.Entry);

    internal bool CanOverrideWeapon(FastFileContentsRow? row) =>
        !_disposed && row?.CatalogEntry is { } asset && _editor.EditingSession.CanOverrideDependencyWeapon(asset.Entry);

    internal void AddReference(FastFileContentsRow row)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (row.CatalogEntry is not { } asset)
            throw new InvalidOperationException("Select a dependency asset to reference.");
        WorkspaceAssetCatalogEntry reference = _editor.EditingSession.AddReference(asset.Entry);
        SelectTargetRow(reference, openEditor: false);
        ImportStatusMessage = $"Added {reference.OriginalName} as a reference in {_editor.TargetFileName}.";
    }

    internal void OverrideWeapon(FastFileContentsRow row, string? sourcePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (row.CatalogEntry is not { } asset)
            throw new InvalidOperationException("Select a dependency weapon to override.");
        WorkspaceAssetCatalogEntry owned = _editor.EditingSession.OverrideDependencyWeapon(asset.Entry, sourcePath);
        SelectTargetRow(owned);
        ImportStatusMessage = $"Created an editable override of {asset.Name} in {_editor.TargetFileName}.";
    }
}
