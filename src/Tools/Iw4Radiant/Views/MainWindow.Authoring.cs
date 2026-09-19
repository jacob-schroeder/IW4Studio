using System.Numerics;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private void InitializeAuthoring()
    {
        Inspector.PlacementRequested += className => BeginPlacement(className,
            (position, _) => GameplayEntityEditing.Place(_session, className, position));
        Inspector.ModelBrowserRequested += Workspace.ShowModels;
        Inspector.PrefabBrowserRequested += Workspace.ShowPrefabs;
        Workspace.Models.InitializeActions(this, _session, _dialogs, FinishGestures, SetStatus);
        Workspace.Prefabs.InitializeActions(this, _session, _dialogs, FinishGestures, _files.OpenPathAsync);
        Workspace.Materials.CatalogChanged += RefreshAssets;
        Workspace.Models.CatalogChanged += RefreshAssets;
        Workspace.Materials.FolderLoaded += async root =>
        {
            if (Path.GetFileName(Path.TrimEndingDirectorySeparator(root)) is "images" or "materials")
                root = Path.GetDirectoryName(root) ?? root;
            if (Directory.Exists(Path.Combine(root, "xmodel"))) await Workspace.Models.LoadFolderAsync(root);
        };
        Workspace.Models.PlacementRequested += (model, align) => BeginPlacement(model.Name,
            (position, normal) => XModelEditing.Place(_session, model, position, align ? normal : null));
        Workspace.Models.DropRequested += DropModels;
        Workspace.Prefabs.PlacementRequested += path =>
        {
            PrefabLibrary.Reference(_session.FilePath ?? throw new ArgumentException("Save the map before placing a prefab."), path);
            BeginPlacement(Path.GetFileNameWithoutExtension(path), (position, _) => _session.Prefabs.Place(_session, path, position));
        };
        _session.Scene.ResolveModel = Workspace.Models.ResolveModel;
        _session.Scene.ResolveMaterial = ResolveMaterial;
    }

    private MaterialSource? ResolveMaterial(string name) =>
        Workspace.Materials.ResolveMaterial(name) ?? Workspace.Models.ResolveMaterial(name);

    private void RefreshAssets()
    {
        if (_session.HasPlacement) _session.CancelPlacement();
        else _session.Refresh();
        Workspace.Camera.ReloadTextures();
    }

    private void BeginPlacement(string label, Action<Vector3, Vector3?> place)
    {
        SetTool(EditorTool.Select);
        _session.BeginPlacement(label, place);
        SetStatus($"Place {label} · Click a surface or grid · Shift repeats · Esc cancels");
        Workspace.FocusActiveView();
    }

    private async void DropModels()
    {
        try
        {
            int count = XModelEditing.DropToSurface(_session, Workspace.Models.ResolveModel, ResolveMaterial);
            SetStatus(count == 0 ? "No surface was found below the selected models." : $"Dropped {count} models onto surfaces.");
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        { await _dialogs.MessageAsync("Drop models", exception.Message); }
    }
}
