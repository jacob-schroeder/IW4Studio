using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using IW4.Formats.SourceFormat.Character;

namespace Iw4Radiant.Views;

public partial class FactionsWindow
{
    private readonly HashSet<string> _createdAssetFolders = new(StringComparer.Ordinal);
    private MapFactionSettings? _appliedSettings;
    private bool _importing, _createdCharacterDirectory;

    private async void ImportBlender_Click(object? sender, RoutedEventArgs e)
    {
        if (_importing || !EditableVariant) return;
        if (_mapFilePath is null || !File.Exists(_mapFilePath))
        {
            ImportStatus.Text = "Save the map first so its character assets have a home beside it.";
            return;
        }
        if (_bootstrapRoot is null || _linkerPath is null || !File.Exists(_linkerPath))
        {
            ImportStatus.Text = "The bundled player assets or D3dbspLinker are unavailable. Restore them to import a character.";
            return;
        }
        bool targetAxis = _axis;
        FactionAppearance? before = targetAxis ? _axisAssaultA : _alliesAssaultA;
        bool editingBefore = targetAxis ? _editingAxis : _editingAllies;
        SetImportBusy(true);
        try
        {
            string? body = await PickGlbAsync("Choose full-body GLB (head included)");
            if (_closed || body is null) return;
            string? hands = await PickGlbAsync("Choose matching first-person hands GLB");
            if (_closed || hands is null) return;
            string characters = MapFactionAuthoring.GetCharacterAssetsDirectory(_mapFilePath);
            string prefix = UniquePrefix(characters, body);
            string output = Path.Combine(characters, prefix);
            ImportStatus.Text = "Optimizing body and hands for PS3; creating detail levels…";
            try
            {
                if (!Directory.Exists(characters))
                {
                    Directory.CreateDirectory(characters);
                    _createdCharacterDirectory = true;
                }
                await RunImporterAsync(body, hands, _bootstrapRoot, output, prefix, _linkerPath);
                if (!File.Exists(Path.Combine(output, "xmodel_native", prefix + "_body.json")) ||
                    !File.Exists(Path.Combine(output, "xmodel_native", prefix + "_viewhands.json")))
                    throw new InvalidDataException("The character importer did not create both native models.");
                _createdAssetFolders.Add(prefix);
                var appearance = new FactionAppearance(prefix + "_body", null,
                    prefix + "_viewhands", HeadIncluded: true, CustomAssetFolder: prefix);
                if (targetAxis) { _axisAssaultA = appearance; _editingAxis = false; }
                else { _alliesAssaultA = appearance; _editingAllies = false; }
                ImportStatus.Text = "Imported body and hands, optimized for PS3. Review the detail levels, then Apply to save.";
                ShowReplacementState();
                RequestPreview(clear: true);
            }
            catch
            {
                if (targetAxis) { _axisAssaultA = before; _editingAxis = editingBefore; }
                else { _alliesAssaultA = before; _editingAllies = editingBefore; }
                _createdAssetFolders.Remove(prefix);
                if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
                throw;
            }
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception) || exception is Win32Exception)
        {
            string details = exception.Message.Trim();
            string summary = details.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim() ?? exception.GetType().Name;
            if (summary.Length > 140) summary = summary[..139] + "…";
            ImportStatus.Text = summary == details
                ? $"Import failed: {summary}"
                : $"Import failed: {summary}\n\nDetails (select to copy):\n{details}";
        }
        finally { SetImportBusy(false); }
    }

    private void SetImportBusy(bool busy)
    {
        _importing = busy;
        ImportBlender.IsEnabled = ApplyAction.IsEnabled = CancelAction.IsEnabled = !busy;
        AlliesButton.IsEnabled = AxisButton.IsEnabled = !busy;
        AlliesFaction.IsEnabled = AxisFaction.IsEnabled = !busy;
        Variants.IsEnabled = Search.IsEnabled = !busy;
        RestoreStock.IsEnabled = ReplaceAppearance.IsEnabled = !busy;
        ReplacementBody.IsEnabled = ReplacementHead.IsEnabled = ReplacementHands.IsEnabled = !busy;
        HandsPreview.IsEnabled = SaveBlenderKit.IsEnabled = !busy;
        PreviewLod.IsEnabled = !busy && PreviewLod.ItemsSource is IEnumerable<LodChoice> levels && levels.Skip(1).Any();
    }

    private async Task<string?> PickGlbAsync(string title)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Blender GLB") { Patterns = ["*.glb"] }]
        });
        if (files.Count == 0) return null;
        string path = files[0].TryGetLocalPath() ??
            throw new NotSupportedException("Choose a local GLB file.");
        if (!Path.GetExtension(path).Equals(".glb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a .glb file exported from the Blender character kit.");
        return path;
    }

    private static string UniquePrefix(string characters, string bodyPath)
    {
        string stem = Path.GetFileNameWithoutExtension(bodyPath).ToLowerInvariant();
        string safe = new(stem.Select(character => character is >= 'a' and <= 'z' or >= '0' and <= '9'
            ? character : '_').ToArray());
        safe = safe.Trim('_');
        if (safe.Length == 0) safe = "character";
        if (safe.Length > 40) safe = safe[..40].TrimEnd('_');
        while (true)
        {
            string prefix = safe + "_" + Guid.NewGuid().ToString("N")[..8];
            if (RangersAssaultAppearance.IsCustomAssetFolder(prefix) &&
                !Directory.Exists(Path.Combine(characters, prefix)) &&
                !File.Exists(Path.Combine(characters, prefix))) return prefix;
        }
    }

    private static async Task RunImporterAsync(string body, string hands, string bootstrap,
        string output, string prefix, string linker)
    {
        bool managed = Path.GetExtension(linker).Equals(".dll", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo
        {
            FileName = managed ? "dotnet" : linker,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(linker) ?? "."
        };
        if (managed) start.ArgumentList.Add(linker);
        foreach (string argument in new[] { "import-character", body, hands, bootstrap, output, prefix })
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("D3dbspLinker could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(managed
                ? "Cannot start dotnet. Install the .NET runtime to use D3dbspLinker."
                : "Cannot start D3dbspLinker on this computer.", exception);
        }
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string outputText = await standardOutput;
        string errorText = await standardError;
        if (process.ExitCode != 0)
            throw new InvalidDataException(string.IsNullOrWhiteSpace(errorText)
                ? $"D3dbspLinker exited with code {process.ExitCode}. {outputText.Trim()}"
                : errorText.Trim());
    }

    private void CleanupImportedFolders()
    {
        if (_mapFilePath is null) return;
        string characters = MapFactionAuthoring.GetCharacterAssetsDirectory(_mapFilePath);
        var keep = new HashSet<string>(StringComparer.Ordinal);
        if (_appliedSettings?.AlliesAssaultA?.CustomAssetFolder is { } allies) keep.Add(allies);
        if (_appliedSettings?.AxisAssaultA?.CustomAssetFolder is { } axis) keep.Add(axis);
        foreach (string prefix in _createdAssetFolders)
        {
            if (keep.Contains(prefix)) continue;
            string directory = Path.Combine(characters, prefix);
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
        }
        _createdAssetFolders.Clear();
        if (_createdCharacterDirectory)
        {
            try
            {
                if (Directory.Exists(characters) && !Directory.EnumerateFileSystemEntries(characters).Any())
                    Directory.Delete(characters);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
