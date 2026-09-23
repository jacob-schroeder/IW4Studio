using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class PrefabBrowser : UserControl
{
    private string? _folder;
    private string[] _files = [], _filtered = [];
    private bool _updating, _running;
    private int _previewRequest, _folderRequest;

    public PrefabBrowser() => InitializeComponent();
    internal event Action<string>? PlacementRequested;
    internal event Action<string>? PainterPrefabRequested;

    internal void InitializeActions(Window owner, EditorSession session, EditorDialogs dialogs, Action finishGestures,
        Func<string, Task> openMap)
    {
        FolderButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            var folders = await dialogs.ShowModalAsync(() => owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "Choose a prefab folder", AllowMultiple = false }));
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) await ReadFolderAsync(path);
        };
        SearchBox.TextChanged += (_, _) => Filter();
        PrefabList.SelectionChanged += async (_, _) => await ShowPreviewAsync();
        PlaceButton.Click += async (_, _) => await RunAsync(() =>
        {
            if (CurrentFile() is { } path) PlacementRequested?.Invoke(path);
            return Task.CompletedTask;
        });
        AddToBrushButton.Click += async (_, _) => await RunAsync(() =>
        {
            if (CurrentFile() is { } path) PainterPrefabRequested?.Invoke(path);
            return Task.CompletedTask;
        });
        OpenSourceButton.Click += async (_, _) => await RunAsync(async () =>
        { if (CurrentFile() is { } path) await openMap(path); });
        EditInstanceButton.Click += async (_, _) => await RunAsync(async () =>
        { if (Instance(session) is { } instance) await openMap(session.Prefabs.GetSourcePath(instance, session.FilePath)); });
        ReloadButton.Click += async (_, _) => await RunAsync(async () =>
        {
            session.Prefabs.Reload(session.Document, session.FilePath);
            session.Refresh();
            await ShowPreviewAsync();
        });
        ExplodeButton.Click += async (_, _) => await RunAsync(() =>
        {
            if (Instance(session) is { } instance) session.Prefabs.Explode(session, instance);
            return Task.CompletedTask;
        });
        SaveSelectionButton.Click += async (_, _) => await RunAsync(async () =>
        {
            MapDocument prefab = PrefabLibrary.SelectionDocument(session);
            string? path = await SavePathAsync("Save selection as prefab", "prefab");
            if (path is null) return;
            session.Prefabs.ValidateSave(prefab, path, session.FilePath);
            await WriteFileAsync(prefab, path);
            session.Prefabs.Reload(session.Document, session.FilePath);
            session.Refresh();
            await ReadFolderAsync(Path.GetDirectoryName(path) ?? ".");
            SelectFile(path);
        });
        UniqueButton.Click += async (_, _) => await RunAsync(async () =>
        {
            if (Instance(session) is not { } instance || session.FilePath is not { } mapPath) return;
            string source = session.Prefabs.GetSourcePath(instance, mapPath);
            MapDocument prefab;
            dialogs.SetBusy(true);
            try { prefab = await Task.Run(() => MapFile.Read(source)); }
            finally { dialogs.SetBusy(false); }
            string? path = await SavePathAsync("Save unique prefab source", Path.GetFileNameWithoutExtension(source) + "_unique");
            if (path is null) return;
            if (Path.GetFullPath(path).Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a new file to make this instance unique.");
            string reference = PrefabLibrary.Reference(mapPath, path);
            session.Prefabs.ValidateSave(prefab, path, source);
            await WriteFileAsync(prefab, path);
            session.Edit(() => instance.Properties["model"] = reference);
            session.Prefabs.Reload(session.Document, session.FilePath);
            session.Refresh();
            await ReadFolderAsync(Path.GetDirectoryName(path) ?? ".");
            SelectFile(path);
        });
        RefreshSelection(session);

        async Task RunAsync(Func<Task> action)
        {
            if (_running || dialogs.BlocksInput) return;
            _running = true;
            try { finishGestures(); await action(); }
            catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
            { await dialogs.MessageAsync("Prefab", exception.Message); }
            finally { _running = false; }
            RefreshSelection(session);
        }

        async Task WriteFileAsync(MapDocument document, string path)
        {
            dialogs.SetBusy(true);
            try { await Task.Run(() => MapFile.Write(document, path)); }
            finally { dialogs.SetBusy(false); }
        }

        async Task<string?> SavePathAsync(string title, string name)
        {
            if (session.FilePath is null) throw new ArgumentException("Save the current map first, then save prefabs inside its map_source folder or beside it.");
            string folder = _folder ?? PrefabLibrary.SourceRoot(session.FilePath);
            var file = await dialogs.ShowModalAsync(async () =>
            {
                IStorageFolder? location = await owner.StorageProvider.TryGetFolderFromPathAsync(folder);
                return await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = title, SuggestedFileName = name, DefaultExtension = "map", ShowOverwritePrompt = true,
                    SuggestedStartLocation = location,
                    FileTypeChoices = [new FilePickerFileType("Radiant prefab source") { Patterns = ["*.map"] }]
                });
            });
            return file?.TryGetLocalPath();
        }

        async Task ReadFolderAsync(string folder)
        {
            int request = ++_folderRequest;
            try
            {
                string[] files = await Task.Run(() => Directory.EnumerateFiles(folder, "*", new EnumerationOptions
                    { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                    .Where(path => Path.GetExtension(path).Equals(".map", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray());
                if (request != _folderRequest) return;
                _folder = Path.GetFullPath(folder); _files = files;
                FolderText.Text = _folder;
                Filter();
            }
            catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
            { await dialogs.MessageAsync("Prefab folder", exception.Message); }
        }

        void Filter()
        {
            string? current = CurrentFile();
            string query = SearchBox.Text?.Trim() ?? "";
            _updating = true;
            _filtered = _files.Where(path => Path.GetRelativePath(_folder ?? ".", path).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            PrefabList.ItemsSource = _filtered.Select(path => Path.GetRelativePath(_folder ?? ".", path)).ToArray();
            int index = current is null ? -1 : Array.IndexOf(_filtered, current);
            PrefabList.SelectedIndex = index >= 0 ? index : _filtered.Length > 0 ? 0 : -1;
            _updating = false;
            _ = ShowPreviewAsync();
        }

        async Task ShowPreviewAsync()
        {
            if (_updating) return;
            int request = ++_previewRequest;
            string? path = CurrentFile();
            PlaceButton.IsEnabled = AddToBrushButton.IsEnabled = false;
            OpenSourceButton.IsEnabled = path is not null;
            if (path is null) { Preview.Show(null); PreviewText.Text = "Choose a folder containing reusable .map prefabs."; return; }
            PreviewText.Text = "Loading preview…";
            try
            {
                MapDocument document = await Task.Run(() =>
                {
                    var library = new PrefabLibrary();
                    string parent = Path.Combine(PrefabLibrary.SourceRoot(path), "__prefab_preview__.map");
                    var instance = new MapEntity();
                    instance.Properties["classname"] = "misc_prefab";
                    instance.Properties["model"] = PrefabLibrary.Reference(parent, path);
                    return library.GetPreview(instance, parent) ?? throw new FormatException(library.Error(instance, parent));
                });
                if (request != _previewRequest) return;
                Preview.Show(document);
                PreviewText.Text = $"{document.Brushes.Count()} brushes · {document.Terrains.Count()} patches · {document.Entities.Count - 1} entities";
                PlaceButton.IsEnabled = AddToBrushButton.IsEnabled = true;
            }
            catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
            {
                if (request != _previewRequest) return;
                Preview.Show(null); PreviewText.Text = exception.Message;
            }
        }
    }

    internal void RefreshSelection(EditorSession session)
    {
        bool selected = Instance(session) is { };
        EditInstanceButton.IsEnabled = UniqueButton.IsEnabled = ExplodeButton.IsEnabled = selected;
        SaveSelectionButton.IsEnabled = session.Selection.Count > 0;
        InstanceText.Text = Instance(session) is { } instance ? "Instance: " + instance.Properties.GetValueOrDefault("model", "") : "Select a placed prefab to edit its source, make a separate copy, or turn it into map objects.";
    }

    private string? CurrentFile() => (uint)PrefabList.SelectedIndex < _filtered.Length ? _filtered[PrefabList.SelectedIndex] : null;
    private void SelectFile(string path) => PrefabList.SelectedIndex = Array.IndexOf(_filtered, path);
    private static MapEntity? Instance(EditorSession session) => session.Selection.Count == 1 &&
        session.Selection.Active is MapEntity entity && PrefabLibrary.IsPrefab(entity) ? entity : null;
}
