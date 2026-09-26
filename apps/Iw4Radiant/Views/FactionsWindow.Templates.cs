using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Iw4Radiant.Views;

public partial class FactionsWindow
{
    private string? BlenderKitPath => _bootstrapRoot is null ? null :
        Path.Combine(_bootstrapRoot, "character_templates", "rangers-assault.zip");

    private async void SaveBlenderKit_Click(object? sender, RoutedEventArgs e)
    {
        if (!SaveBlenderKit.IsEnabled || BlenderKitPath is not { } source) return;
        SaveBlenderKit.IsEnabled = false;
        BlenderKitStatus.Text = "";
        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Blender character kit",
                SuggestedFileName = "Rangers-Assault-Kit.zip",
                DefaultExtension = "zip",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("Blender kit archive") { Patterns = ["*.zip"] }]
            });
            if (file is null) return;
            await using Stream input = File.OpenRead(source);
            await using Stream output = await file.OpenWriteAsync();
            output.SetLength(0);
            await input.CopyToAsync(output);
            BlenderKitStatus.Text = "Saved. Unzip the kit and open a .blend file to start.";
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            BlenderKitStatus.Text = $"Could not save the Blender kit: {exception.Message}";
        }
        finally { SaveBlenderKit.IsEnabled = true; }
    }
}
