using System.Numerics;
using IW4.Formats.SourceFormat.Material;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;
using Iw4Radiant;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private readonly Dictionary<string, MaterialSource> _authoredWaterMaterials = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, WaterMaterialDefinition> _waterDefinitions =
        new Dictionary<string, WaterMaterialDefinition>(StringComparer.Ordinal);
    private bool _waterDefinitionsDirty = true;

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
        var settings = RadiantSettings.Load();
        Workspace.Materials.InitializeFavorites(settings);
        Inspector.Painter.InitializePresets(settings, Workspace.Models.ResolveModel);
        Workspace.Models.CatalogChanged += Inspector.Painter.ResolveModels;
        bool suppressRelatedModelRestore = false;
        Workspace.Materials.FolderLoaded += async (root, nonBlocking) =>
        {
            settings.MaterialFolder = root;
            settings.Save();
            if (suppressRelatedModelRestore) return;
            if (Path.GetFileName(Path.TrimEndingDirectorySeparator(root)) is "images" or "materials")
                root = Path.GetDirectoryName(root) ?? root;
            if (Directory.Exists(Path.Combine(root, "xmodel"))) await Workspace.Models.LoadFolderAsync(root, nonBlocking);
        };
        Workspace.Models.PlacementRequested += (model, align) => BeginPlacement(model.Name,
            (position, normal) => XModelEditing.Place(_session, model, position, align ? normal : null));
        Workspace.Models.DropRequested += DropModels;
        Workspace.Models.CatalogReset += Inspector.Painter.MarkModelsUnavailable;
        Workspace.Models.FoliageModelRequested += model =>
        {
            Inspector.Painter.AddModel(model);
            Inspector.Painter.StartPainting();
            ShowInspectorSection(Inspector.ShowPainter);
        };
        Inspector.Painter.Changed += RefreshFoliagePainting;
        Inspector.Painter.ModelsRequested += Workspace.ShowModels;
        Workspace.Models.FolderLoaded += root =>
        {
            settings.XModelFolder = root;
            settings.Save();
        };
        Workspace.Prefabs.PlacementRequested += path =>
        {
            PrefabLibrary.Reference(_session.FilePath ?? throw new ArgumentException("Save the map before placing a prefab."), path);
            BeginPlacement(Path.GetFileNameWithoutExtension(path), (position, _) => _session.Prefabs.Place(_session, path, position));
        };
        _session.Scene.ResolveModel = Workspace.Models.ResolveModel;
        _session.Scene.ResolveMaterial = ResolveMaterial;
        RefreshFoliagePainting();
        Opened += (_, _) => _ = RestoreAssetFoldersAsync(settings, () => suppressRelatedModelRestore = true,
            () => suppressRelatedModelRestore = false);
    }

    private async Task RestoreAssetFoldersAsync(RadiantSettings settings, Action markExplicitModelsRestored,
        Action clearExplicitModelsRestored)
    {
        try
        {
            if (settings.XModelFolder is { } models && Directory.Exists(models))
                if (await Workspace.Models.LoadFolderAsync(models, nonBlocking: true)) markExplicitModelsRestored();
            if (settings.MaterialFolder is { } material && Directory.Exists(material))
            {
                bool loaded = await Workspace.Materials.LoadFolderAsync(material, nonBlocking: true);
                if (loaded) settings.MaterialFolder = material;
            }
        }
        finally { clearExplicitModelsRestored(); }
    }

    private MaterialSource? ResolveMaterial(string name)
    {
        if (!WaterMaterialAuthoring.IsAuthoredMaterialName(name))
            return Workspace.Materials.ResolveMaterial(name) ?? Workspace.Models.ResolveMaterial(name);
        if (_waterDefinitionsDirty)
        {
            _waterDefinitionsDirty = false;
            try
            {
                var properties = new Dictionary<string, string>(_session.Document.World.Properties, StringComparer.Ordinal);
                foreach (MapEntity instance in _session.Document.Entities.Where(PrefabLibrary.IsPrefab))
                    if (_session.Prefabs.GetPreview(instance, _session.FilePath) is { } preview)
                        WaterMaterialAuthoring.MergeDefinitions(properties, preview.World.Properties);
                _waterDefinitions = WaterMaterialAuthoring.ReadDefinitions(properties);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                                               FormatException or OverflowException)
            {
                _waterDefinitions = new Dictionary<string, WaterMaterialDefinition>(StringComparer.Ordinal);
            }
            foreach (string cached in _authoredWaterMaterials.Keys.Where(key => !_waterDefinitions.ContainsKey(key)).ToArray())
                _authoredWaterMaterials.Remove(cached);
        }
        return ResolveMaterial(name, _waterDefinitions);
    }

    private MaterialSource? ResolveMaterial(string name,
        IReadOnlyDictionary<string, WaterMaterialDefinition> definitions)
    {
        if (!definitions.TryGetValue(name, out WaterMaterialDefinition? definition))
            return WaterMaterialAuthoring.IsAuthoredMaterialName(name)
                ? null
                : Workspace.Materials.ResolveMaterial(name) ?? Workspace.Models.ResolveMaterial(name);
        if (_authoredWaterMaterials.TryGetValue(name, out MaterialSource? authored)) return authored;
        MaterialSource? source = Workspace.Materials.ResolveMaterial(definition.SourceMaterial) ??
            Workspace.Models.ResolveMaterial(definition.SourceMaterial);
        if (source?.Water is not { } water) return null;
        authored = new MaterialSource(definition.Name, source.ImagePath, source.IsSky, source.SamplerState)
        {
            TechniqueSet = source.TechniqueSet,
            Water = WaterMaterialAuthoring.CreateWater(water, definition),
            Ocean = definition.Ocean,
            OceanFoamImagePath = source.OceanFoamImagePath,
            WaterColor = new Vector4(definition.Red, definition.Green, definition.Blue, source.WaterColor.W),
            EnvMapParms = new Vector4(definition.FresnelMinimum, definition.FresnelMaximum,
                definition.FresnelExponent, source.EnvMapParms.W),
            Surface = source.Surface,
            GameFlags = source.GameFlags,
            SurfaceTypeBits = source.SurfaceTypeBits
        };
        _authoredWaterMaterials.Add(name, authored);
        return authored;
    }

    private void RefreshAssets()
    {
        _authoredWaterMaterials.Clear();
        _waterDefinitionsDirty = true;
        if (_session.HasPlacement) _session.CancelPlacement();
        else _session.Refresh();
        Workspace.Camera.ReloadTextures();
    }

    private void BeginPlacement(string label, Action<Vector3, Vector3?> place)
    {
        Inspector.Painter.StopPainting();
        SetTool(EditorTool.Select);
        _session.BeginPlacement(label, place);
        SetStatus($"Place {label} · Click a surface or grid · Shift repeats · Esc cancels");
        Workspace.FocusActiveView();
    }

    private void RefreshFoliagePainting()
    {
        var painter = Inspector.Painter;
        var camera = Workspace.Camera;
        PainterButton.IsChecked = painter.IsPainting;
        camera.FoliageModels = painter.Models;
        camera.FoliageRadius = painter.BrushRadius;
        camera.FoliageDensity = painter.BrushDensity;
        camera.FoliageSpacing = painter.BrushSpacing;
        camera.FoliageMinimumScale = painter.MinimumBrushScale;
        camera.FoliageMaximumScale = painter.MaximumBrushScale;
        camera.FoliageRandomYaw = painter.UsesRandomYaw;
        camera.FoliageAlignSurface = painter.AlignsToSurface;
        if (painter.IsPainting)
        {
            if (_session.Tool != EditorTool.Select || _session.HasPlacement)
            {
                _activatingFoliage = true;
                try { SetTool(EditorTool.Select); }
                finally { _activatingFoliage = false; }
            }
            SetStatus("Paint foliage in the camera · drag over map surfaces · Esc cancels a stroke");
        }
        camera.FoliagePaintingEnabled = painter.IsPainting;
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
