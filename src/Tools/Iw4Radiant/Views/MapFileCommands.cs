using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

internal sealed class MapFileCommands
{
    private readonly Window _owner;
    private readonly EditorSession _session;
    private readonly EditorDialogs _dialogs;
    private readonly Action _finishGestures, _frameAll;
    private readonly Action<string> _setStatus;
    private bool _allowClose, _closePromptOpen;

    internal MapFileCommands(Window owner, EditorSession session, EditorDialogs dialogs,
        Action finishGestures, Action frameAll, Action<string> setStatus)
    {
        _owner = owner;
        _session = session;
        _dialogs = dialogs;
        _finishGestures = finishGestures;
        _frameAll = frameAll;
        _setStatus = setStatus;
        owner.Closing += OnClosing;
    }

    internal async Task NewAsync()
    {
        if (_dialogs.BlocksInput || !await ConfirmDiscardAsync()) return;
        _session.Replace(MapDocument.Create(), null);
        _frameAll();
    }
    internal async Task OpenAsync()
    {
        if (_dialogs.BlocksInput || !await ConfirmDiscardAsync()) return;
        var files = await _dialogs.ShowModalAsync(() => _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Radiant source map", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Radiant source") { Patterns = ["*.map"] }]
        }));
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            _dialogs.SetBusy(true);
            var document = await Task.Run(() => MapFile.Read(path));
            _session.Replace(document, path);
            _frameAll();
            int preserved = document.Entities.Sum(entity => entity.PreservedPrimitives.Count);
            _setStatus($"Opened {Path.GetFileName(path)}." + (preserved > 0
                ? $" {preserved} unsupported primitives are preserved on save and are not displayed." : ""));
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception)) { _dialogs.SetBusy(false); await _dialogs.MessageAsync("Cannot open map", exception.Message); }
        finally { _dialogs.SetBusy(false); }
    }
    internal async Task<bool> SaveAsync(bool saveAs)
    {
        if (_dialogs.BlocksInput) return false;
        _finishGestures();
        string? path = saveAs ? null : _session.FilePath;
        if (path is null)
        {
            var file = await _dialogs.ShowModalAsync(() => _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Radiant source map", SuggestedFileName = Path.GetFileNameWithoutExtension(_session.FilePath ?? "untitled.map"),
                DefaultExtension = "map", ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("Radiant source") { Patterns = ["*.map"] }]
            }));
            path = file?.TryGetLocalPath();
            if (path is null) return false;
        }
        try
        {
            _dialogs.SetBusy(true);
            await Task.Run(() => MapFile.Write(_session.Document, path));
            _session.MarkSaved(path);
            _setStatus($"Saved {Path.GetFileName(path)}.");
            return true;
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            _dialogs.SetBusy(false);
            await _dialogs.MessageAsync("Cannot save map", exception.Message);
            return false;
        }
        finally { _dialogs.SetBusy(false); }
    }
    private async Task<bool> ConfirmDiscardAsync()
    {
        _finishGestures();
        if (!_session.IsDirty) return true;
        string? choice = await _dialogs.ChoiceAsync("Unsaved map", "Save your changes before continuing?", ["Save", "Discard", "Cancel"]);
        return choice == "Discard" || choice == "Save" && await SaveAsync(false);
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        if (_dialogs.BlocksInput) { e.Cancel = true; return; }
        _finishGestures();
        if (!_session.IsDirty) return;
        e.Cancel = true;
        if (_closePromptOpen) return;
        _closePromptOpen = true;
        try
        {
            if (await ConfirmDiscardAsync()) { _allowClose = true; _owner.Close(); }
        }
        finally { _closePromptOpen = false; }
    }
}
