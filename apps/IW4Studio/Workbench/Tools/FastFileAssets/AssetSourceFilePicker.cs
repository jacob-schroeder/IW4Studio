using Avalonia.Platform.Storage;
using IW4.Game.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

internal static class AssetSourceFilePicker
{
    public static async Task<IStorageFile?> PickSourceAsync(
        IStorageProvider storage,
        XAssetType type)
    {
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = $"Import {type}",
                AllowMultiple = false,
                FileTypeFilter = [FileType(type)]
            });
        return files.FirstOrDefault();
    }

    public static Task<IStorageFile?> PickDestinationAsync(
        IStorageProvider storage,
        XAssetType type,
        string fileName)
    {
        string extension = Path.GetExtension(fileName).TrimStart('.');
        string? defaultExtension = AssetSourceTransfer.GetFileSuffix(type).Length == 0 || extension.Length == 0
            ? null
            : extension;
        return storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export {type}",
            SuggestedFileName = defaultExtension is null ? fileName :
                SaveFilePickerName.WithoutDefaultExtension(fileName, defaultExtension),
            DefaultExtension = defaultExtension,
            ShowOverwritePrompt = true,
            FileTypeChoices = [FileType(type)]
        });
    }

    private static FilePickerFileType FileType(XAssetType type)
    {
        string suffix = AssetSourceTransfer.GetFileSuffix(type);
        return suffix.Length == 0
            ? FilePickerFileTypes.All
            : new FilePickerFileType($"{type} source ({suffix})")
            {
                Patterns = [$"*{suffix}", $"*{suffix.ToUpperInvariant()}"]
            };
    }
}
