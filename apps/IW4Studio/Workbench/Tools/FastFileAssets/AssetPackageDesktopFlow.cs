using Avalonia.Controls;
using Avalonia.Platform.Storage;
using IW4.Linker.Contracts;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

internal static class AssetPackageDesktopFlow
{
    private static readonly FilePickerFileType PackageFileType = new("IW4 asset package")
    {
        Patterns = ["*.iw4pkg"]
    };

    internal static async Task ExportAsync(
        Window owner,
        FastFileEditingSession session,
        IReadOnlyList<WorkspaceAssetCatalogEntry> selections,
        Action<string> report)
    {
        CancellationToken token = session.CancellationToken;
        AssetPackageExportPreview preview = await Task.Run(
            () => session.PreviewAssetPackageExport(selections, token), token);
        var dialog = new AssetPackagePreviewWindow(preview.Rows, isImport: false, apply: null);
        if (!await dialog.ShowDialog<bool>(owner))
            return;

        IStorageFile? destination = await owner.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export asset package",
                SuggestedFileName = SuggestedName(selections[0]),
                DefaultExtension = "iw4pkg",
                FileTypeChoices = [PackageFileType]
            });
        if (destination is null)
            return;

        string path = destination.TryGetLocalPath() ?? throw new NotSupportedException(
            "Asset package export requires a local destination.");
        await Task.Run(() => preview.WriteTo(path), token);
        report($"Exported {selections.Count} selected asset(s) to {destination.Name}.");
    }

    internal static async Task ImportAsync(
        Window owner,
        FastFileEditingSession session,
        Func<AssetPackageImportPreview,
            IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice>, int> apply,
        Action<string> report)
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import asset package",
                AllowMultiple = false,
                FileTypeFilter = [PackageFileType, FilePickerFileTypes.All]
            });
        IStorageFile? source = files.FirstOrDefault();
        if (source is null)
            return;

        CancellationToken token = session.CancellationToken;
        AssetPackageImportPreview preview;
        await using (var input = await source.OpenReadAsync())
            preview = await Task.Run(() => session.PreviewAssetPackageImport(input, token), token);
        using var openedPackage = preview;
        var dialog = new AssetPackagePreviewWindow(preview.Rows, isImport: true, choices =>
        {
            token.ThrowIfCancellationRequested();
            int changed = apply(preview, choices);
            report(changed == 0 ? $"{source.Name} already matches this fastfile." :
                $"Imported {source.Name}. Changed {changed:N0} target assets.");
            return Task.CompletedTask;
        });
        _ = await dialog.ShowDialog<bool>(owner);
    }

    private static string SuggestedName(WorkspaceAssetCatalogEntry entry)
    {
        string name = Path.GetFileName((entry.OriginalName ?? entry.NormalizedName ?? entry.AssetType.ToString())
            .Replace('\\', '/'));
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return $"{(string.IsNullOrWhiteSpace(safe) ? entry.AssetType.ToString() : safe)}.iw4pkg";
    }
}
