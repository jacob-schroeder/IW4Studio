using Avalonia.Controls;
using Avalonia.Platform.Storage;
using IW4.Game.Assets.Menu;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

internal static class AssetPackageDesktopFlow
{
    private static readonly FilePickerFileType PackageFileType = new("IW4 asset package")
    {
        Patterns = ["*.iw4pkg"]
    };

    internal static Task ExportAsync(
        Window owner,
        FastFileEditingSession session,
        IReadOnlyList<WorkspaceAssetCatalogEntry> selections,
        Action<string> report)
    {
        var first = selections[0];
        return ExportAsync(owner, first.OriginalName ?? first.NormalizedName ?? first.AssetType.ToString(),
            selections.Count, token => session.PreviewAssetPackageExport(selections, token), session.CancellationToken, report);
    }

    internal static Task ExportMenuAsync(Window owner, FastFileEditingSession session, MenuDefAsset menu,
        Action<string> report) =>
        ExportAsync(owner, menu.Window.Name ?? "Menu", 1, token => session.PreviewMenuPackageExport(menu, token),
            session.CancellationToken, report);

    private static async Task ExportAsync(Window owner, string name, int selectionCount,
        Func<CancellationToken, AssetPackageExportPreview> prepare, CancellationToken token, Action<string> report)
    {
        AssetPackageExportPreview preview = await Task.Run(() => prepare(token), token);
        var dialog = new AssetPackagePreviewWindow(preview.Rows, isImport: false, apply: null);
        if (!await dialog.ShowDialog<bool>(owner))
            return;

        IStorageFile? destination = await owner.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export asset package",
                SuggestedFileName = SuggestedName(name),
                DefaultExtension = "iw4pkg",
                FileTypeChoices = [PackageFileType]
            });
        if (destination is null)
            return;

        string path = destination.TryGetLocalPath() ?? throw new NotSupportedException(
            "Asset package export requires a local destination.");
        await Task.Run(() => preview.WriteTo(path), token);
        report(selectionCount == 1 ? $"Exported {name} to {destination.Name}." :
            $"Exported {selectionCount:N0} assets to {destination.Name}.");
    }

    internal static async Task<string?> ImportAsync(
        Window owner,
        FastFileEditingSession session,
        Func<AssetPackageImportPreview,
            IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice>, int> apply,
        Action<string> report,
        bool requireSingleMenu = false)
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = requireSingleMenu ? "Import a menu package" : "Import asset package",
                AllowMultiple = false,
                FileTypeFilter = [PackageFileType, FilePickerFileTypes.All]
            });
        IStorageFile? source = files.FirstOrDefault();
        if (source is null)
            return null;

        CancellationToken token = session.CancellationToken;
        AssetPackageImportPreview preview;
        await using (var input = await source.OpenReadAsync())
            preview = await Task.Run(() => session.PreviewAssetPackageImport(input, token), token);
        using var openedPackage = preview;
        string? menuName = preview.SelectedRoots is [{ Type: XAssetType.Menu, Intent: LinkRootIntent.Owned } menu]
            ? menu.WireName : null;
        if (requireSingleMenu && menuName is null)
            throw new InvalidDataException("Choose a package exported from a single menu. To import a whole MenuFile or multiple assets, use Import package in Contents.");
        var dialog = new AssetPackagePreviewWindow(preview.Rows, isImport: true, choices =>
        {
            token.ThrowIfCancellationRequested();
            int changed = apply(preview, choices);
            report(changed == 0 ? $"No assets changed when importing {source.Name}." :
                $"Imported {source.Name}. Changed {changed:N0} target assets.");
            return Task.CompletedTask;
        }, preview.CanLoadDependencies
            ? () => Task.Run(() => session.LoadAssetPackageDependencies(preview, token), token)
            : null, menuToRegister: requireSingleMenu ? menuName : null);
        return await dialog.ShowDialog<bool>(owner) ? menuName : null;
    }

    private static string SuggestedName(string assetName)
    {
        string name = Path.GetFileName(assetName.Replace('\\', '/'));
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return $"{(string.IsNullOrWhiteSpace(safe) ? "asset" : safe)}.iw4pkg";
    }
}
